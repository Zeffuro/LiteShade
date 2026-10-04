using System;
using System.Collections.Generic;
using System.Linq;
using LiteShade.Configuration.Sharing;
using LiteShade.Profiles;

namespace LiteShade.Configuration;

internal enum ImportMode
{
    Add,
    Update,
    Replace,
}

internal readonly record struct ImportChanges(int ProfilesAdded, int ProfilesUpdated, int ProfilesRemoved,
    int RulesAdded, int RulesUpdated, int RulesRemoved, int? ExistingRevision);

internal static class ProfileTransfer
{
    public static string Export(SystemConfiguration config, Guid? profileId, bool includeRules)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Profiles is null || config.Rules is null)
        {
            throw new InvalidOperationException("Profiles are unavailable.");
        }

        var profiles = config.Profiles
            .Where(profile => profile is not null && (profileId is null || profile.Id == profileId.Value))
            .ToList();
        if (profiles.Count == 0)
        {
            throw new InvalidOperationException("Profile was not found.");
        }

        var profileIds = profiles.Select(profile => profile.Id).ToHashSet();
        var rules = includeRules
            ? config.Rules.Where(rule => rule is not null && profileIds.Contains(rule.ProfileId)).ToList()
            : [];

        return new ProfileExport
        {
            Pack = config.Pack.Copy(),
            MinimumPluginVersion = ProfileExport.CurrentPluginVersion,
            DefaultProfileId = profileId ?? config.DefaultProfileId,
            AutomaticProfiles = includeRules && config.AutomaticProfiles,
            Profiles = profiles,
            Rules = rules,
        }.ToText();
    }

    public static ProfileExport Parse(string input) => ProfileExport.FromText(input);

    public static InstalledPack? GetInstalledPack(SystemConfiguration target, ProfileExport source)
        => source.Pack is null ? null : target.InstalledPacks.FirstOrDefault(pack => pack.Pack.Id == source.Pack.Id);

    public static ImportMode GetDefaultMode(SystemConfiguration target, ProfileExport source)
        => GetInstalledPack(target, source) is null ? ImportMode.Add : ImportMode.Update;

    public static ImportChanges GetChanges(SystemConfiguration target, ProfileExport source, bool includeRules, ImportMode mode)
        => Prepare(target, source, includeRules, false, mode).Changes;

    public static Guid Import(SystemConfiguration target, ProfileExport source, bool includeRules, bool enableRules, ImportMode mode)
    {
        var prepared = Prepare(target, source, includeRules, enableRules, mode);
        target.Profiles = prepared.Profiles;
        target.Rules = prepared.Rules;
        target.DefaultProfileId = prepared.DefaultProfileId;
        target.AutomaticProfiles = prepared.AutomaticProfiles;
        target.FavoriteProfiles = prepared.FavoriteProfiles;
        target.InstalledPacks = prepared.InstalledPacks;
        return prepared.SelectedProfileId;
    }

    private static PreparedImport Prepare(SystemConfiguration target, ProfileExport source, bool includeRules, bool enableRules, ImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (target.Profiles is null || target.Rules is null || target.InstalledPacks is null
            || target.FavoriteProfiles is null || target.Profiles.Any(profile => profile is null)
            || target.Rules.Any(rule => rule is null))
        {
            throw new InvalidOperationException("Profiles are unavailable.");
        }

        source = source.Copy();
        var existing = GetInstalledPack(target, source);
        if (mode == ImportMode.Update && source.Pack is null)
        {
            throw new InvalidOperationException("This share has no pack identity. Import it as new profiles.");
        }

        var replacing = mode == ImportMode.Replace;
        var profiles = replacing ? [] : target.Profiles.Select(profile => profile.Copy()).ToList();
        var rules = replacing ? [] : target.Rules.Select(rule => rule.Copy()).ToList();
        var installedPacks = replacing ? [] : target.InstalledPacks.Select(pack => pack.Copy()).ToList();
        var installed = source.Pack is null || mode == ImportMode.Add && existing is not null
            ? null : installedPacks.FirstOrDefault(pack => pack.Pack.Id == source.Pack.Id);
        if (source.Pack is not null && installed is null && !(mode == ImportMode.Add && existing is not null))
        {
            installed = new InstalledPack { Pack = source.Pack.Copy() };
            installedPacks.Add(installed);
        }

        var profileIds = new Dictionary<Guid, Guid>();
        var profilesById = profiles.ToDictionary(profile => profile.Id);
        var rulesById = rules.ToDictionary(rule => rule.Id);
        var updatingProfileIds = mode == ImportMode.Update && installed is not null
            ? source.Profiles.Where(profile => installed.ProfileIds.ContainsKey(profile.Id))
                .Select(profile => installed.ProfileIds[profile.Id]).ToHashSet()
            : [];
        var names = profiles.Where(profile => !updatingProfileIds.Contains(profile.Id))
            .Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profilesAdded = 0;
        var profilesUpdated = 0;
        foreach (var profile in source.Profiles)
        {
            var imported = profile.Copy();
            var updatesExisting = mode == ImportMode.Update && installed is not null
                && installed.ProfileIds.TryGetValue(profile.Id, out var localId) && profilesById.ContainsKey(localId);
            imported.Id = updatesExisting ? installed!.ProfileIds[profile.Id] : Guid.NewGuid();
            imported.Name = UniqueName(imported.Name, names);
            profileIds.Add(profile.Id, imported.Id);
            if (updatesExisting)
            {
                profiles[profiles.FindIndex(candidate => candidate.Id == imported.Id)] = imported;
                profilesUpdated++;
            }
            else
            {
                profiles.Add(imported);
                profilesAdded++;
            }

            if (installed is not null)
            {
                installed.ProfileIds[profile.Id] = imported.Id;
            }
        }

        var rulesRemoved = replacing ? target.Rules.Count : 0;
        var rulesAdded = 0;
        var rulesUpdated = 0;
        if (includeRules)
        {
            var incomingRules = new List<ProfileRule>();
            var selectedRuleIds = new HashSet<Guid>();
            if (mode == ImportMode.Update && installed is not null)
            {
                var selectedLocalProfiles = profileIds.Values.ToHashSet();
                var incomingRuleIds = source.Rules.Select(rule => rule.Id).ToHashSet();
                selectedRuleIds = installed.RuleIds.Where(pair => rulesById.TryGetValue(pair.Value, out var rule)
                    && (selectedLocalProfiles.Contains(rule.ProfileId) || incomingRuleIds.Contains(pair.Key)))
                    .Select(pair => pair.Value).ToHashSet();
                var obsolete = installed.RuleIds.Where(pair => !incomingRuleIds.Contains(pair.Key)
                    && rulesById.TryGetValue(pair.Value, out var rule) && selectedLocalProfiles.Contains(rule.ProfileId)).ToList();
                rulesRemoved += obsolete.Count;
                foreach (var pair in obsolete)
                {
                    installed.RuleIds.Remove(pair.Key);
                }
            }

            foreach (var rule in source.Rules)
            {
                var imported = rule.Copy();
                var updatesExisting = mode == ImportMode.Update && installed is not null
                    && installed.RuleIds.TryGetValue(rule.Id, out var localId) && rulesById.ContainsKey(localId);
                imported.Id = updatesExisting ? installed!.RuleIds[rule.Id] : Guid.NewGuid();
                imported.ProfileId = profileIds[rule.ProfileId];
                imported.Enabled = updatesExisting && !enableRules
                    ? rulesById[imported.Id].Enabled : imported.Enabled && enableRules;
                if (updatesExisting)
                {
                    rulesUpdated++;
                }
                else
                {
                    rulesAdded++;
                }

                incomingRules.Add(imported);

                if (installed is not null)
                {
                    installed.RuleIds[rule.Id] = imported.Id;
                }
            }

            rules = PlaceRules(rules, incomingRules, selectedRuleIds);
        }

        ProfileExport.CheckCounts(profiles.Count, rules.Count);
        if (installed is not null)
        {
            installed.Pack = source.Pack!.Copy();
            installed.Pack.Revision = Math.Max(installed.Pack.Revision, existing?.Pack.Revision ?? 1);
        }

        return new PreparedImport
        {
            Profiles = profiles,
            Rules = rules,
            InstalledPacks = installedPacks,
            DefaultProfileId = replacing ? profileIds[source.DefaultProfileId] : target.DefaultProfileId,
            AutomaticProfiles = replacing ? includeRules && enableRules && source.AutomaticProfiles : target.AutomaticProfiles,
            FavoriteProfiles = replacing ? [] : new HashSet<Guid>(target.FavoriteProfiles),
            SelectedProfileId = replacing ? profileIds[source.DefaultProfileId] : profileIds[source.Profiles[0].Id],
            Changes = new ImportChanges(profilesAdded, profilesUpdated, replacing ? target.Profiles.Count : 0,
                rulesAdded, rulesUpdated, rulesRemoved, existing?.Pack.Revision),
        };
    }

    private static List<ProfileRule> PlaceRules(List<ProfileRule> existing, List<ProfileRule> incoming, HashSet<Guid> selectedIds)
    {
        var lastSlot = existing.FindLastIndex(rule => selectedIds.Contains(rule.Id));
        if (lastSlot < 0)
        {
            existing.AddRange(incoming);
            return existing;
        }

        var ordered = new List<ProfileRule>();
        var nextIncoming = 0;
        for (var index = 0; index < existing.Count; index++)
        {
            if (!selectedIds.Contains(existing[index].Id))
            {
                ordered.Add(existing[index]);
            }
            else if (nextIncoming < incoming.Count)
            {
                ordered.Add(incoming[nextIncoming++]);
            }

            if (index == lastSlot)
            {
                ordered.AddRange(incoming.Skip(nextIncoming));
            }
        }

        return ordered;
    }

    private static string UniqueName(string name, HashSet<string> names)
    {
        if (names.Add(name))
        {
            return name;
        }

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" {suffix}";
            var candidate = name[..Math.Min(name.Length, 80 - suffixText.Length)] + suffixText;
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private sealed class PreparedImport
    {
        public required List<ColorProfile> Profiles { get; init; }
        public required List<ProfileRule> Rules { get; init; }
        public required List<InstalledPack> InstalledPacks { get; init; }
        public required HashSet<Guid> FavoriteProfiles { get; init; }
        public Guid DefaultProfileId { get; init; }
        public bool AutomaticProfiles { get; init; }
        public Guid SelectedProfileId { get; init; }
        public ImportChanges Changes { get; init; }
    }
}
