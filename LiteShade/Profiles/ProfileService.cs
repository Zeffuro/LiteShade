using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using LiteShade.Configuration;
using LiteShade.Graphics;
using LiteShade.Helpers;

namespace LiteShade.Profiles;

internal sealed class ProfileService : IDisposable
{
    private readonly object _sync = new();
    private readonly SystemConfiguration _config;
    private readonly IFramework _framework = IFramework.Get();

    private Dictionary<Guid, (ColorMatrix Matrix, DepthOfField.Settings? DepthOfField)> _effects = [];
    private HashSet<Guid> _profileIds = [];
    private ProfileRule[] _rules = [];

    private Guid _defaultId;
    private bool _enabled;
    private bool _automatic;

    private PauseOptions _pauses;
    private ProfileContext _context;
    private ProfileSelection _selection;

    private (ColorMatrix Matrix, DepthOfField.Settings? DepthOfField) _current = (ColorMatrix.Identity, null);
    private (ColorMatrix Matrix, DepthOfField.Settings? DepthOfField)? _preview;

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

    public (ColorMatrix? Matrix, DepthOfField.Settings? DepthOfField, PauseOptions Pauses) RenderSettings
    {
        get
        {
            lock (_sync)
            {
                if (_disposed || !_context.IsLoggedIn || _context.IsTransitioning || (!_enabled && _preview is null))
                {
                    return (null, null, _pauses);
                }

                var effects = _preview ?? _current;
                return (effects.Matrix, effects.DepthOfField, _pauses);
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
            _pauses = _config.Pauses;
            _defaultId = _config.DefaultProfileId;
            _effects = _config.Profiles.ToDictionary(profile => profile.Id, GetEffects);
            _profileIds = _effects.Keys.ToHashSet();
            _rules = _config.Rules.Select(rule => rule.Copy()).ToArray();
            UpdateSelection();
        }
    }

    public void SetPreview(ColorProfile? profile)
    {
        profile?.Normalize();
        lock (_sync)
        {
            _preview = profile is null ? null : GetEffects(profile);
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

            _context = context;
            UpdateSelection();
        }
    }

    private void UpdateSelection()
    {
        _selection = RuleResolver.Resolve(_rules, _profileIds, _defaultId, _context, _automatic);
        _current = _effects[_selection.ProfileId];
    }

    private static (ColorMatrix, DepthOfField.Settings?) GetEffects(ColorProfile profile)
        => (ColorMatrix.FromProfile(profile), profile.DepthOfField
            ? new DepthOfField.Settings(profile.AutoFocus, profile.FocusDistance, profile.FNumber)
            : null);
}
