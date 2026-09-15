using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Dalamud.Plugin.Services;
using LiteShade.Configuration;
using LiteShade.Graphics;
using LiteShade.Helpers;

namespace LiteShade.Profiles;

internal sealed class ProfileService : IDisposable
{
    private readonly record struct Effects(ColorFilter.Settings Color, DepthOfField.Settings? DepthOfField, Vignette.Settings? Vignette);

    private readonly object _sync = new();
    private readonly SystemConfiguration _config;
    private readonly IFramework _framework = IFramework.Get();

    private Dictionary<Guid, Effects> _effects = [];
    private HashSet<Guid> _profileIds = [];
    private ProfileRule[] _rules = [];

    private Guid _defaultId;
    private bool _enabled;
    private bool _automatic;

    private Guid? _overrideProfileId;
    private float _transitionSeconds;
    private ColorMatrix _transitionFrom;
    private long _transitionStarted;
    private float _transitionDuration;
    private ProfileContext _context;
    private ProfileSelection _selection;

    private Effects _current = new(new ColorFilter.Settings(ColorMatrix.Identity, 0, PauseOptions.None), null, null);
    private Effects? _preview;

    private bool _disposed;

    public ProfileContext Context
    {
        get
        {
            lock (_sync)
            {
                return _context;
            }
        }
    }

    public ProfileSelection Selection
    {
        get
        {
            lock (_sync)
            {
                return _selection;
            }
        }
    }

    public Guid? OverrideProfileId
    {
        get
        {
            lock (_sync)
            {
                return _overrideProfileId;
            }
        }
    }

    public (ColorFilter.Settings? Color, DepthOfField.Settings? DepthOfField, Vignette.Settings? Vignette) RenderSettings
    {
        get
        {
            lock (_sync)
            {
                if (_disposed || !_context.IsLoggedIn || _context.IsTransitioning || (!_enabled && _preview is null))
                {
                    return (null, null, null);
                }

                var effects = _preview ?? _current with { Color = _current.Color with { Matrix = CurrentMatrix() } };
                return (effects.Color, effects.DepthOfField, effects.Vignette);
            }
        }
    }

    public ProfileService(SystemConfiguration config)
    {
        _config = config;
        Refresh();
        _framework.Update += OnFrameworkUpdate;
    }

    public void Refresh()
    {
        lock (_sync)
        {
            _enabled = _config.Enabled;
            _automatic = _config.AutomaticProfiles;
            _transitionSeconds = _config.TransitionSeconds;
            _defaultId = _config.DefaultProfileId;
            _effects = _config.Profiles.ToDictionary(profile => profile.Id, GetEffects);
            _profileIds = _effects.Keys.ToHashSet();
            _rules = _config.Rules.Select(rule => rule.Copy()).ToArray();
            if (_overrideProfileId is { } id && !_profileIds.Contains(id))
            {
                _overrideProfileId = null;
            }

            UpdateSelection();
        }
    }

    public void SetPreview(ColorProfile? profile)
    {
        profile?.Normalize();
        lock (_sync)
        {
            _preview = profile is null ? null : GetEffects(profile);
            _transitionDuration = 0;
        }
    }

    public bool SetOverride(Guid? profileId)
    {
        lock (_sync)
        {
            if (_disposed || (profileId is { } id && !_profileIds.Contains(id)))
            {
                return false;
            }

            _overrideProfileId = profileId;
            UpdateSelection(true);
            return true;
        }
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        lock (_sync)
        {
            _disposed = true;
            _preview = null;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var context = ProfileContext.Read();
        lock (_sync)
        {
            if (_disposed || context == _context)
            {
                return;
            }

            var transition = _context.IsLoggedIn && !_context.IsTransitioning
                && context.TerritoryId == _context.TerritoryId;
            _context = context;
            UpdateSelection(transition);
        }
    }

    private void UpdateSelection(bool transition = false)
    {
        var selection = _overrideProfileId is { } id
            ? new ProfileSelection(id, null)
            : RuleResolver.Resolve(_rules, _profileIds, _defaultId, _context, _automatic);
        if (selection.ProfileId != _selection.ProfileId)
        {
            _transitionFrom = CurrentMatrix();
            _transitionStarted = Stopwatch.GetTimestamp();
            _transitionDuration = transition && _enabled && _context.IsLoggedIn && !_context.IsTransitioning && _preview is null
                && _current.Color.GameFilterId == _effects[selection.ProfileId].Color.GameFilterId
                ? _transitionSeconds : 0;
        }
        else if (!transition)
        {
            _transitionDuration = 0;
        }

        _selection = selection;
        _current = _effects[_selection.ProfileId];
    }

    private ColorMatrix CurrentMatrix()
    {
        if (_transitionDuration <= 0)
        {
            return _current.Color.Matrix;
        }

        var amount = (float)Stopwatch.GetElapsedTime(_transitionStarted).TotalSeconds / _transitionDuration;
        if (amount >= 1)
        {
            _transitionDuration = 0;
            return _current.Color.Matrix;
        }

        return ColorMatrix.Lerp(_transitionFrom, _current.Color.Matrix, amount);
    }

    private Effects GetEffects(ColorProfile profile)
        => new(new ColorFilter.Settings(ColorMatrix.FromProfile(profile), profile.GameFilterId, _config.ColorPauses),
            profile.DepthOfField ? new DepthOfField.Settings(profile.Focus, profile.FocusDistance, profile.FNumber, _config.DepthOfFieldPauses) : null,
            profile.Vignette && profile.VignetteAmount > 0
                ? new Vignette.Settings(profile.VignetteAmount, profile.VignetteRadius, profile.VignetteShape, profile.VignetteColor, _config.VignettePauses)
                : null);
}
