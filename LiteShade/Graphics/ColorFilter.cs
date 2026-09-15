using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using LiteShade.Configuration;
using LiteShade.Helpers;
using LiteShade.Profiles;

namespace LiteShade.Graphics;

internal sealed unsafe class ColorFilter : IDisposable
{
    public readonly record struct Settings(ColorMatrix Matrix, uint GameFilterId, PauseOptions Pauses);

    private readonly ProfileService _profiles;

    private readonly IFramework _framework = IFramework.Get();
    private readonly ICondition _conditions = ICondition.Get();
    private readonly IClientState _client = IClientState.Get();

    private readonly Hook<Experimental.RenderViewDelegate>? _renderHook;
    private readonly Hook<Experimental.DrawFilterDelegate>? _drawHook;
    private readonly Experimental.PostEffectManager** _manager;

    private readonly nint _drawAddress;

    private Experimental.PostEffectManager* _activeManager;
    private Experimental.FilterPart* _activePart;
    private ColorMatrix _matrix;

    private bool _applied;
    private volatile string _status = FilterStatus.WaitingForScene;

    public string Status => _status;
    public IReadOnlyList<GameFilter> GameFilters { get; } = [];
    public string? GameFiltersError { get; }
    private readonly Dictionary<uint, GameFilter> _gameFilters = [];

    // TODO: Swap to FFXIVClientStructs when in main Dalamud.
    public ColorFilter(ProfileService profiles)
    {
        _profiles = profiles;
        try
        {
            GameFilters = GameFilter.Load();
            _gameFilters = GameFilters.ToDictionary(filter => filter.Id);
        }
        catch (Exception exception)
        {
            GameFiltersError = "Game filters unavailable";
            IPluginLog.Get().Error(exception, "Could not load game filters.");
        }

        try
        {
            var scanner = ISigScanner.Get();
            var interop = IGameInteropProvider.Get();
            var vtable = (nint*)scanner.GetStaticAddressFromSig(Experimental.FilterVTableSignature, Experimental.FilterVTableOffset);

            _drawAddress = vtable[5];
            _manager = (Experimental.PostEffectManager**)scanner.GetStaticAddressFromSig(Experimental.ManagerSignature);

            _drawHook = interop.HookFromAddress<Experimental.DrawFilterDelegate>(_drawAddress, DrawFilter);
            _renderHook = interop.HookFromAddress<Experimental.RenderViewDelegate>(scanner.ScanText(Experimental.RenderViewSignature), RenderView);

            _drawHook.Enable();
            _renderHook.Enable();
        }
        catch (Exception exception)
        {
            Dispose();
            _status = FilterStatus.Unavailable;
            IPluginLog.Get().Error(exception, "Could not initialize the native colour filter.");
        }
    }

    public void Dispose()
    {
        try
        {
            _renderHook?.Dispose();
        }
        finally
        {
            _drawHook?.Dispose();
        }
    }

    private void RenderView(Manager* renderManager, bool enabled, Manager.RenderViews view)
    {
        if (view != Manager.RenderViews.Main || !_framework.IsInFrameworkUpdateThread)
        {
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var (settings, _, _) = _profiles.RenderSettings;
        if (!enabled || settings is null || (settings.Value.Matrix.IsIdentity && settings.Value.GameFilterId == 0))
        {
            _status = !enabled ? FilterStatus.PostEffectsDisabled
                : settings is null ? FilterStatus.Disabled : FilterStatus.Neutral;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var (matrix, gameFilterId, pauses) = settings.Value;
        _gameFilters.TryGetValue(gameFilterId, out var gameFilter);
        if (gameFilterId != 0 && gameFilter is null)
        {
            _status = "Game filter unavailable";
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        if (!_client.IsLoggedIn || _conditions[ConditionFlag.BetweenAreas] || _conditions[ConditionFlag.BetweenAreas51])
        {
            _status = FilterStatus.WaitingForGameplay;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var inGPose = (pauses & (PauseOptions.GPose | PauseOptions.Cutscenes)) != 0 && GameMain.IsInGPose();
        if (!inGPose && (pauses & PauseOptions.Cutscenes) != 0 &&
            (_conditions[ConditionFlag.WatchingCutscene] || _conditions[ConditionFlag.WatchingCutscene78] || _conditions[ConditionFlag.OccupiedInCutSceneEvent]))
        {
            _status = FilterStatus.Cutscene;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var graphics = GraphicsConfig.Instance();
        if (graphics == null)
        {
            _status = FilterStatus.WaitingForGraphics;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        if ((pauses & PauseOptions.Portraits) != 0 && (graphics->PortraitMode || graphics->PortraitPreview))
        {
            _status = FilterStatus.Portrait;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        if ((pauses & PauseOptions.GPose) != 0 && inGPose)
        {
            _status = FilterStatus.GPose;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        if ((pauses & PauseOptions.IdleCamera) != 0 && GameMain.IsInIdleCam())
        {
            _status = FilterStatus.IdleCamera;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var manager = *_manager;
        var part = Experimental.GetReadyPart(manager, _drawAddress);
        if (part == null)
        {
            _status = FilterStatus.WaitingForFilter;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        if (gameFilter is null && manager->Curve != new Vector4(0, 1, 0, 0))
        {
            _status = FilterStatus.ColourCurve;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var nativeFilterEnabled = (manager->Flags & Experimental.PostEffectFlags.ColorFilterDarkBlend) != 0;
        var combined = gameFilter is null ? matrix : matrix.Multiply(gameFilter.Matrix);
        if (gameFilter is null && nativeFilterEnabled)
        {
            var filter = manager->Filter;
            if (filter.DarkParameters.X != 0 || !float.IsFinite(filter.DarkParameters.Y) || filter.DarkParameters.Y < 0
                || !float.IsFinite(filter.DarkParameters.Z))
            {
                _status = FilterStatus.ShadowEffect;
                _renderHook!.Original(renderManager, enabled, view);
                return;
            }

            if (!filter.Matrix.IsFinite || !filter.DarkMatrix.IsFinite
                || !float.IsFinite(filter.Strength) || filter.Strength < 0 || filter.Strength > 1)
            {
                _status = FilterStatus.SceneOutOfRange;
                _renderHook!.Original(renderManager, enabled, view);
                return;
            }

            combined = combined.Multiply(filter.Matrix.WithStrength(filter.Strength));
            if (!combined.IsFinite)
            {
                _status = FilterStatus.CombinedOutOfRange;
                _renderHook!.Original(renderManager, enabled, view);
                return;
            }
        }

        _activeManager = manager;
        _activePart = part;
        _matrix = combined;
        _applied = false;
        var originalCurve = manager->Curve;
        if (gameFilter is not null)
        {
            manager->Curve = gameFilter.Curve;
        }

        manager->Flags |= Experimental.PostEffectFlags.ColorFilterDarkBlend;
        try
        {
            _renderHook!.Original(renderManager, enabled, view);
            _status = _applied ? FilterStatus.Active : FilterStatus.PassNotDrawn;
        }
        finally
        {
            if (*_manager == manager)
            {
                manager->Curve = originalCurve;
                if (!nativeFilterEnabled)
                {
                    manager->Flags &= ~Experimental.PostEffectFlags.ColorFilterDarkBlend;
                }
            }

            _activeManager = null;
            _activePart = null;
        }
    }

    private void DrawFilter(Experimental.FilterPart* part)
    {
        if (!_framework.IsInFrameworkUpdateThread || _activeManager == null || *_manager != _activeManager || part != _activePart)
        {
            _drawHook!.Original(part);
            return;
        }

        var manager = _activeManager;
        var original = manager->Filter;
        manager->Filter = new Experimental.FilterParameters
        {
            Matrix = _matrix,
            DarkMatrix = ColorMatrix.Identity,
            DarkParameters = new Vector3(0, 1, 1),
            Strength = 1,
        };
        try
        {
            _drawHook!.Original(part);
            _applied = true;
        }
        finally
        {
            if (*_manager == manager)
            {
                manager->Filter = original;
            }
        }
    }
}
