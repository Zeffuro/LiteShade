using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using LiteShade.Profiles;

namespace LiteShade.Configuration;

[Serializable]
public sealed class SystemConfiguration : IPluginConfiguration
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public bool Enabled { get; set; }
    public bool HasSeenWelcome { get; set; }
    public bool AutomaticProfiles { get; set; }
    public PauseOptions Pauses { get; set; }
    public Guid DefaultProfileId { get; set; }
    public List<ColorProfile> Profiles { get; set; } = [];
    public List<ProfileRule> Rules { get; set; } = [];

    public void EnsureInitialized()
    {
        Profiles ??= [];
        Rules ??= [];
        Profiles.RemoveAll(profile => profile is null);
        Rules.RemoveAll(rule => rule is null);

        if (Profiles.Count == 0)
        {
            Profiles.Add(ColorProfile.CreateNeutral());
        }

        var ids = new HashSet<Guid>();
        foreach (var profile in Profiles)
        {
            if (profile.Id == Guid.Empty || !ids.Add(profile.Id))
            {
                profile.Id = Guid.NewGuid();
                ids.Add(profile.Id);
            }

            profile.Normalize();
        }

        if (!ids.Contains(DefaultProfileId))
        {
            DefaultProfileId = Profiles[0].Id;
        }

        var ruleIds = new HashSet<Guid>();
        foreach (var rule in Rules)
        {
            if (rule.Id == Guid.Empty || !ruleIds.Add(rule.Id))
            {
                rule.Id = Guid.NewGuid();
                ruleIds.Add(rule.Id);
            }

            rule.Normalize();
        }

        Version = Math.Max(Version, CurrentVersion);
    }

    public ColorProfile GetDefaultProfile() => Profiles.First(profile => profile.Id == DefaultProfileId);

    public void Reset()
    {
        Version = CurrentVersion;
        Enabled = false;
        HasSeenWelcome = false;
        AutomaticProfiles = false;
        Pauses = PauseOptions.None;
        Profiles = [ColorProfile.CreateNeutral()];
        Rules = [];
        DefaultProfileId = Profiles[0].Id;
    }
}
