using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using LiteShade.Configuration;
using LiteShade.Helpers;
using LiteShade.Profiles;

namespace LiteShade.Graphics;

internal sealed unsafe class Vignette : IDisposable
{
    public readonly record struct Settings(float Amount, float Radius, float Shape, uint Color, PauseOptions Pauses);

    private readonly ProfileService _profiles;

    private readonly IFramework _framework = IFramework.Get();
    private readonly IClientState _client = IClientState.Get();
    private readonly ICondition _conditions = ICondition.Get();

    private readonly Hook<Manager.Delegates.RenderView>? _hook;

    private volatile string _status = "Disabled";

    public string Status => _status;

    public Vignette(ProfileService profiles)
    {
        _profiles = profiles;
        try
        {
            _hook = IGameInteropProvider.Get().HookFromAddress<Manager.Delegates.RenderView>(
                Manager.MemberFunctionPointers.RenderView, RenderView);
            _hook.Enable();
        }
        catch (Exception exception)
        {
            Dispose();
            _status = "Native vignette unavailable";
            IPluginLog.Get().Error(exception, "Could not initialize native vignette.");
        }
    }

    public void Dispose() => _hook?.Dispose();

    private void RenderView(Manager* renderManager, bool enabled, Manager.RenderViews view)
    {
        if (view != Manager.RenderViews.Main || !_framework.IsInFrameworkUpdateThread)
        {
            _hook!.Original(renderManager, enabled, view);
            return;
        }

        var (_, _, settings) = _profiles.RenderSettings;
        if (!enabled || settings is not { } vignette)
        {
            _status = enabled ? "Disabled" : FilterStatus.PostEffectsDisabled;
            _hook!.Original(renderManager, enabled, view);
            return;
        }

        if (!CanApply(vignette.Pauses))
        {
            _hook!.Original(renderManager, enabled, view);
            return;
        }

        var manager = PostEffectManager.Instance();
        if (!Experimental.IsVignetteReady(manager, PostEffectResources.Instance()))
        {
            _status = "Waiting for vignette resources";
            _hook!.Original(renderManager, enabled, view);
            return;
        }

        var original = manager->Vignetting;
        var nativeEnabled = (manager->Flags & PostEffectFlags.Vignetting) != 0;
        var radiusSquared = vignette.Radius * vignette.Radius;

        manager->Vignetting = new PostEffectVignettingParameters
        {
            AspectRatioBlend = vignette.Shape,
            RadiusSquared = radiusSquared,
            Falloff = vignette.Amount / (1 - radiusSquared),
            Color = new Vector3(vignette.Color & 0xFF, (vignette.Color >> 8) & 0xFF, (vignette.Color >> 16) & 0xFF) / 255f,
        };
        manager->Flags |= PostEffectFlags.Vignetting;

        try
        {
            _hook!.Original(renderManager, enabled, view);
            _status = "Active";
        }
        finally
        {
            if (PostEffectManager.Instance() == manager)
            {
                manager->Vignetting = original;
                if (!nativeEnabled)
                {
                    manager->Flags &= ~PostEffectFlags.Vignetting;
                }
            }
        }
    }

    private bool CanApply(PauseOptions pauses)
    {
        if (!_client.IsLoggedIn || _conditions[ConditionFlag.BetweenAreas] || _conditions[ConditionFlag.BetweenAreas51])
        {
            _status = FilterStatus.WaitingForGameplay;
            return false;
        }

        var graphics = GraphicsConfig.Instance();
        if (graphics == null)
        {
            _status = FilterStatus.WaitingForGraphics;
            return false;
        }

        if ((pauses & EffectPauseState.GetActive(graphics, _conditions)) != 0)
        {
            _status = "Paused";
            return false;
        }

        return true;
    }
}
