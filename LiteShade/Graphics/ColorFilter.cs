using System;
using System.Collections.Generic;
using System.Linq;
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

internal sealed unsafe class ColorFilter : IDisposable
{
    public readonly record struct Settings(ColorMatrix Matrix, float Midtones, ColorCurve Curve, float CurveStrength,
        Vector3 MidtoneTint, Vector3 Shadows, Vector3 Highlights, Vector2 TintParameters, uint GameFilterId,
        PauseOptions Pauses, PauseOptions ShadowHighlightPauses, PauseOptions GameFilterPauses,
        ColorCurve? CurveFrom = null, float CurveBlend = 1)
    {
        public bool HasCurve => CurveStrength != 0 && (!Curve.IsIdentity || CurveFrom is { IsIdentity: false });
    }

    private readonly ProfileService _profiles;

    private readonly Hook<Manager.Delegates.RenderView>? _renderHook;
    private readonly Hook<PostEffectColorFilterDarkBlend.Delegates.Draw>? _drawHook;
    private readonly ColorLut? _lut;

    private readonly PostEffectColorFilterDarkBlend.PostEffectColorFilterDarkBlendVirtualTable* _vtable;

    private PostEffectManager* _activeManager;
    private PostEffectColorFilterDarkBlend* _activePart;
    private ColorMatrix _matrix;
    private ColorMatrix _darkMatrix;
    private Vector3 _darkParameters;

    private bool _applied;
    private PauseFade _colourPause;
    private PauseFade _shadowPause;
    private volatile string _status = FilterStatus.WaitingForScene;
    private volatile Effect _pausedEffects;
    private volatile Effect _fadingEffects;

    public string Status => _status;
    public string StatusFor(Effect effect) => (_fadingEffects & effect) != 0 && (_status == FilterStatus.Active || _status == "Paused")
        ? (_pausedEffects & effect) != 0 ? "Fading out" : "Fading in"
        : (_pausedEffects & effect) != 0 ? "Paused" : _status;
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
            _lut = new ColorLut();
            _lut.Enable();

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
            _lut?.Dispose();
        }
    }

    private void RenderView(Manager* renderManager, bool enabled, Manager.RenderViews view)
    {
        if (view != Manager.RenderViews.Main || !IFramework.Get().IsInFrameworkUpdateThread)
        {
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var (settings, _, _) = _profiles.RenderSettings;
        _pausedEffects = 0;
        _fadingEffects = 0;
        if (!enabled || settings is null || (settings.Value.Matrix.IsIdentity && settings.Value.Midtones == 0
            && !settings.Value.HasCurve && settings.Value.GameFilterId == 0
            && settings.Value.MidtoneTint == Vector3.Zero && settings.Value.Shadows == Vector3.One && settings.Value.Highlights == Vector3.One))
        {
            _colourPause = _shadowPause = default;
            _status = !enabled ? FilterStatus.PostEffectsDisabled
                : settings is null ? FilterStatus.Disabled : FilterStatus.Neutral;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var (matrix, midtones, toneCurve, curveStrength, midtoneTint, shadows, highlights, tintParameters,
            gameFilterId, pauses, shadowPauses, filterPauses, curveFrom, curveBlend) = settings.Value;
        var conditions = ICondition.Get();
        if (!IClientState.Get().IsLoggedIn || conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51])
        {
            _colourPause = _shadowPause = default;
            _status = FilterStatus.WaitingForGameplay;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var graphics = GraphicsConfig.Instance();
        if (graphics == null)
        {
            _colourPause = _shadowPause = default;
            _status = FilterStatus.WaitingForGraphics;
            _renderHook!.Original(renderManager, enabled, view);
            return;
        }

        var activePauses = EffectPauseState.GetActive(graphics, conditions);

        var colourPaused = (pauses & activePauses) != 0;
        var shadowPaused = (shadowPauses & activePauses) != 0;
        var seconds = _profiles.TransitionSeconds;
        var colourAmount = _colourPause.GetAmount(colourPaused, seconds);
        var shadowAmount = _shadowPause.GetAmount(shadowPaused, seconds);
        matrix = matrix.WithStrength(colourAmount);
        midtones *= colourAmount;
        curveStrength *= colourAmount;
        if (shadowAmount != 1)
        {
            midtoneTint *= shadowAmount;
            shadows = shadowAmount == 0 ? Vector3.One : Vector3.Lerp(Vector3.One, shadows, shadowAmount);
            highlights = shadowAmount == 0 ? Vector3.One : Vector3.Lerp(Vector3.One, highlights, shadowAmount);
        }

        if (colourPaused)
        {
            _pausedEffects |= Effect.ColourAdjustments;
        }

        if (shadowPaused)
        {
            _pausedEffects |= Effect.ShadowHighlight;
        }

        if (colourPaused ? colourAmount > 0 : colourAmount < 1) _fadingEffects |= Effect.ColourAdjustments;
        if (shadowPaused ? shadowAmount > 0 : shadowAmount < 1) _fadingEffects |= Effect.ShadowHighlight;

        if ((filterPauses & activePauses) != 0)
        {
            gameFilterId = 0;
            _pausedEffects |= Effect.GPoseFilter;
        }
        if (matrix.IsIdentity && midtones == 0 && (curveStrength == 0 || (toneCurve.IsIdentity && curveFrom is not { IsIdentity: false }))
            && midtoneTint == Vector3.Zero && shadows == Vector3.One && highlights == Vector3.One && gameFilterId == 0)
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

        var splitTint = midtoneTint != Vector3.Zero || shadows != Vector3.One || highlights != Vector3.One;
        var (normalMatrix, darkMatrix) = splitTint
            ? ColorMatrix.SplitTone(combined, shadows, highlights, midtoneTint, tintParameters)
            : (combined, ColorMatrix.Identity);
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
        _darkParameters = splitTint ? new Vector3(tintParameters, 1) : new Vector3(0, 1, 1);
        _applied = false;
        var originalCurve = manager->ColorFilterCurve;
        var curve = gameFilter?.Curve ?? (Vector4)originalCurve;
        if (midtones != 0)
        {
            curve.Z = Math.Clamp(curve.Z + midtones, -1f, 1f);
        }

        if (gameFilter is not null || midtones != 0)
        {
            manager->ColorFilterCurve = curve;
        }

        manager->Flags |= PostEffectFlags.ColorFilterDarkBlend;
        try
        {
            _lut!.Begin(manager, part, toneCurve, curveFrom, curveBlend, curveStrength);
            _renderHook!.Original(renderManager, enabled, view);
            _status = _lut.Error ?? (_applied ? FilterStatus.Active : FilterStatus.PassNotDrawn);
        }
        finally
        {
            _lut!.End();
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
        var manager = IFramework.Get().IsInFrameworkUpdateThread ? PostEffectManager.Instance() : null;
        if (manager == null || manager != _activeManager || part != _activePart)
        {
            _drawHook!.Original(part);
            return;
        }

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
