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
    public Dictionary<Effect, PauseOptions> EffectPauses { get; set; } = [];
    public float TransitionSeconds { get; set; } = 0.35f;
    public Guid DefaultProfileId { get; set; }
    public List<ColorProfile> Profiles { get; set; } = [];
    public List<ProfileRule> Rules { get; set; } = [];
    public HashSet<uint> FavoriteGameFilters { get; set; } = [];
    public HashSet<Guid> FavoriteProfiles { get; set; } = [];
    public ProfilePack Pack { get; set; } = new();
    public List<InstalledPack> InstalledPacks { get; set; } = [];

    public void EnsureInitialized()
    {
        Profiles ??= [];
        Rules ??= [];
        FavoriteGameFilters ??= [];
        FavoriteProfiles ??= [];
        EffectPauses ??= [];
        Pack ??= new();
        Pack.Normalize();
        InstalledPacks ??= [];
        FavoriteGameFilters.Remove(0);
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

        FavoriteProfiles.IntersectWith(ids);

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

        var packIds = new HashSet<Guid>();
        InstalledPacks.RemoveAll(pack => pack is null || pack.Pack is null || pack.Pack.Id == Guid.Empty
            || !packIds.Add(pack.Pack.Id));
        foreach (var pack in InstalledPacks)
        {
            pack.Pack.Normalize();
            pack.ProfileIds ??= [];
            pack.RuleIds ??= [];
            pack.ProfileIds = pack.ProfileIds.Where(pair => pair.Key != Guid.Empty && ids.Contains(pair.Value))
                .ToDictionary();
            pack.RuleIds = pack.RuleIds.Where(pair => pair.Key != Guid.Empty && ruleIds.Contains(pair.Value))
                .ToDictionary();
        }

        TransitionSeconds = float.IsFinite(TransitionSeconds) ? Math.Clamp(TransitionSeconds, 0f, 3f) : 0.35f;
        Version = Math.Max(Version, CurrentVersion);
    }

    public ColorProfile GetDefaultProfile() => Profiles.First(profile => profile.Id == DefaultProfileId);

    public void Reset()
    {
        Version = CurrentVersion;
        Enabled = false;
        HasSeenWelcome = false;
        AutomaticProfiles = false;
        EffectPauses = [];
        TransitionSeconds = 0.35f;
        Profiles = [ColorProfile.CreateNeutral()];
        Rules = [];
        FavoriteGameFilters = [];
        FavoriteProfiles = [];
        Pack = new();
        InstalledPacks = [];
        DefaultProfileId = Profiles[0].Id;
    }

    public PauseOptions GetPauses(Effect effect) => EffectPauses.GetValueOrDefault(effect);
}
