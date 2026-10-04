using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics.PostEffect;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using LiteShade.Configuration;
using LiteShade.Helpers;
using LiteShade.Profiles;

namespace LiteShade.Graphics;

internal sealed unsafe class ColorFilter : IDisposable
{
    public readonly record struct Settings(ColorMatrix Matrix, Vector3 Shadows, Vector3 Highlights, uint GameFilterId, PauseOptions Pauses,
        PauseOptions ShadowHighlightPauses, PauseOptions GameFilterPauses);

    private readonly ProfileService _profiles;

    private readonly IFramework _framework = IFramework.Get();
    private readonly ICondition _conditions = ICondition.Get();
    private readonly IClientState _client = IClientState.Get();

    private readonly Hook<Manager.Delegates.RenderView>? _renderHook;
    private readonly Hook<PostEffectColorFilterDarkBlend.Delegates.Draw>? _drawHook;

    private readonly PostEffectColorFilterDarkBlend.PostEffectColorFilterDarkBlendVirtualTable* _vtable;

    private PostEffectManager* _activeManager;
    private PostEffectColorFilterDarkBlend* _activePart;
    private ColorMatrix _matrix;
    private ColorMatrix _darkMatrix;
    private Vector3 _darkParameters;

    private bool _applied;
    private volatile string _status = FilterStatus.WaitingForScene;
    private volatile Effect _pausedEffects;

    public string Status => _status;
    public string StatusFor(Effect effect) => (_pausedEffects & effect) != 0 ? "Paused" : _status;
    public IReadOnlyList<GameFilter> GameFilters { get; } = [];
    public string? GameFiltersError { get; }
    private readonly Dictionary<uint, GameFilter> _gameFilters = [];

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
            var interop = IGameInteropProvider.Get();
            _vtable = PostEffectColorFilterDarkBlend.StaticVirtualTablePointer;
            _drawHook = interop.HookFromAddress<PostEffectColorFilterDarkBlend.Delegates.Draw>(_vtable->Draw, DrawFilter);
            _renderHook = interop.HookFromAddress<Manager.Delegates.RenderView>(Manager.MemberFunctionPointers.RenderView, RenderView);

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
        _pausedEffects = 0;
        if (!enabled || settings is null || (settings.Value.Matrix.IsIdentity && settings.Value.GameFilterId == 0
            && settings.Value.Shadows == Vector3.One && settings.Value.Highlights == Vector3.One))
        {
            _status = !enabled ? FilterStatus.PostEffectsDisabled
                : settings is null ? FilterStatus.Disabled : FilterStatus.Neutral;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var (matrix, shadows, highlights, gameFilterId, pauses, shadowPauses, filterPauses) = settings.Value;
        if (!_client.IsLoggedIn || _conditions[ConditionFlag.BetweenAreas] || _conditions[ConditionFlag.BetweenAreas51])
        {
            _status = FilterStatus.WaitingForGameplay;
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

        var activePauses = PauseOptions.None;
        var inGPose = GameMain.IsInGPose();
        if (inGPose) activePauses |= PauseOptions.GPose;
        if (!inGPose && (_conditions[ConditionFlag.WatchingCutscene] || _conditions[ConditionFlag.WatchingCutscene78]
            || _conditions[ConditionFlag.OccupiedInCutSceneEvent])) activePauses |= PauseOptions.Cutscenes;
        if (graphics->PortraitMode || graphics->PortraitPreview) activePauses |= PauseOptions.Portraits;
        if (GameMain.IsInIdleCam()) activePauses |= PauseOptions.IdleCamera;

        if ((pauses & activePauses) != 0)
        {
            matrix = ColorMatrix.Identity;
            _pausedEffects |= Effect.ColourAdjustments;
        }

        if ((shadowPauses & activePauses) != 0)
        {
            shadows = highlights = Vector3.One;
            _pausedEffects |= Effect.ShadowHighlight;
        }

        if ((filterPauses & activePauses) != 0)
        {
            gameFilterId = 0;
            _pausedEffects |= Effect.GPoseFilter;
        }
        if (matrix.IsIdentity && shadows == Vector3.One && highlights == Vector3.One && gameFilterId == 0)
        {
            _status = "Paused";
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        _gameFilters.TryGetValue(gameFilterId, out var gameFilter);
        if (gameFilterId != 0 && gameFilter is null)
        {
            _status = "Game filter unavailable";
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var manager = PostEffectManager.Instance();
        var part = Experimental.GetReadyPart(manager, _vtable);
        if (part == null)
        {
            _status = FilterStatus.WaitingForFilter;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        if (gameFilter is null && (Vector4)manager->ColorFilterCurve != new Vector4(0, 1, 0, 0))
        {
            _status = FilterStatus.ColourCurve;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var nativeFilterEnabled = (manager->Flags & PostEffectFlags.ColorFilterDarkBlend) != 0;
        var combined = gameFilter is null ? matrix : matrix.Multiply(gameFilter.Matrix);
        if (gameFilter is null && nativeFilterEnabled)
        {
            var filter = manager->ColorFilter;
            var nativeMatrix = new ColorMatrix(filter.Matrix[0], filter.Matrix[1], filter.Matrix[2]);
            var nativeDarkMatrix = new ColorMatrix(filter.DarkMatrix[0], filter.DarkMatrix[1], filter.DarkMatrix[2]);
            if (filter.DarkParameters.X != 0 || !float.IsFinite(filter.DarkParameters.Y) || filter.DarkParameters.Y < 0
                || !float.IsFinite(filter.DarkParameters.Z))
            {
                _status = FilterStatus.ShadowEffect;
                _renderHook!.Original(renderManager, enabled, view);
                return;
            }

            if (!nativeMatrix.IsFinite || !nativeDarkMatrix.IsFinite
                || !float.IsFinite(filter.Strength) || filter.Strength < 0 || filter.Strength > 1)
            {
                _status = FilterStatus.SceneOutOfRange;
                _renderHook!.Original(renderManager, enabled, view);
                return;
            }

            combined = combined.Multiply(nativeMatrix.WithStrength(filter.Strength));
            if (!combined.IsFinite)
            {
                _status = FilterStatus.CombinedOutOfRange;
                _renderHook!.Original(renderManager, enabled, view);
                return;
            }
        }

        var splitTint = shadows != Vector3.One || highlights != Vector3.One;
        var normalMatrix = splitTint ? combined.ScaleRows(highlights) : combined;
        var darkMatrix = splitTint ? ColorMatrix.ShadowCorrection(shadows, highlights) : ColorMatrix.Identity;
        if (!normalMatrix.IsFinite || !darkMatrix.IsFinite)
        {
            _status = FilterStatus.CombinedOutOfRange;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        _activeManager = manager;
        _activePart = part;
        _matrix = normalMatrix;
        _darkMatrix = darkMatrix;
        _darkParameters = splitTint ? new Vector3(0.5f, 0.5f, 1) : new Vector3(0, 1, 1);
        _applied = false;
        var originalCurve = manager->ColorFilterCurve;
        if (gameFilter is not null)
        {
            manager->ColorFilterCurve = gameFilter.Curve;
        }

        manager->Flags |= PostEffectFlags.ColorFilterDarkBlend;
        try
        {
            _renderHook!.Original(renderManager, enabled, view);
            _status = _applied ? FilterStatus.Active : FilterStatus.PassNotDrawn;
        }
        finally
        {
            if (PostEffectManager.Instance() == manager)
            {
                manager->ColorFilterCurve = originalCurve;
                if (!nativeFilterEnabled)
                {
                    manager->Flags &= ~PostEffectFlags.ColorFilterDarkBlend;
                }
            }

            _activeManager = null;
            _activePart = null;
        }
    }

    private void DrawFilter(PostEffectColorFilterDarkBlend* part)
    {
        if (!_framework.IsInFrameworkUpdateThread || _activeManager == null || PostEffectManager.Instance() != _activeManager || part != _activePart)
        {
            _drawHook!.Original(part);
            return;
        }

        var manager = _activeManager;
        var original = manager->ColorFilter;
        var filter = new PostEffectManager.ColorFilterParameters
        {
            DarkParameters = _darkParameters,
            Strength = 1,
        };
        filter.Matrix[0] = _matrix.Red;
        filter.Matrix[1] = _matrix.Green;
        filter.Matrix[2] = _matrix.Blue;
        filter.DarkMatrix[0] = _darkMatrix.Red;
        filter.DarkMatrix[1] = _darkMatrix.Green;
        filter.DarkMatrix[2] = _darkMatrix.Blue;
        manager->ColorFilter = filter;
        try
        {
            _drawHook!.Original(part);
            _applied = true;
        }
        finally
        {
            if (PostEffectManager.Instance() == manager)
            {
                manager->ColorFilter = original;
            }
        }
    }
}
