using System;
using System.Collections.Generic;
using System.Linq;
using LiteShade.Profiles;

namespace LiteShade.Configuration.Sharing;

internal sealed class SharedSetupSnapshot
{
    private readonly List<ColorProfile> _profiles;
    private readonly List<ProfileRule> _rules;
    private readonly Guid _defaultProfileId;
    private readonly bool _automaticProfiles;
    private readonly HashSet<Guid> _favoriteProfiles;
    private readonly HashSet<uint> _favoriteGameFilters;
    private readonly List<InstalledPack> _installedPacks;

    private SharedSetupSnapshot(SystemConfiguration config)
    {
        _profiles = config.Profiles.Select(profile => profile.Copy()).ToList();
        _rules = config.Rules.Select(rule => rule.Copy()).ToList();
        _defaultProfileId = config.DefaultProfileId;
        _automaticProfiles = config.AutomaticProfiles;
        _favoriteProfiles = new HashSet<Guid>(config.FavoriteProfiles);
        _favoriteGameFilters = new HashSet<uint>(config.FavoriteGameFilters);
        _installedPacks = config.InstalledPacks.Select(pack => pack.Copy()).ToList();
    }

    public static SharedSetupSnapshot Capture(SystemConfiguration config) => new(config);

    public void Restore(SystemConfiguration config)
    {
        config.Profiles = _profiles.Select(profile => profile.Copy()).ToList();
        config.Rules = _rules.Select(rule => rule.Copy()).ToList();
        config.DefaultProfileId = _defaultProfileId;
        config.AutomaticProfiles = _automaticProfiles;
        config.FavoriteProfiles = new HashSet<Guid>(_favoriteProfiles);
        config.FavoriteGameFilters = new HashSet<uint>(_favoriteGameFilters);
        config.InstalledPacks = _installedPacks.Select(pack => pack.Copy()).ToList();
    }
}
