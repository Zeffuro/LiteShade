using System;
using System.Collections.Generic;
using System.Linq;
using LiteShade.Configuration.Sharing;
using LiteShade.Profiles;

namespace LiteShade.Configuration;

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
            DefaultProfileId = profileId ?? config.DefaultProfileId,
            AutomaticProfiles = includeRules && config.AutomaticProfiles,
            Profiles = profiles,
            Rules = rules,
        }.ToText();
    }

    public static ProfileExport Parse(string input) => ProfileExport.FromText(input);

    public static Guid Import(SystemConfiguration target, ProfileExport source, bool includeRules, bool enableRules, bool replace)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Profiles is null || target.Rules is null || target.Profiles.Any(profile => profile is null))
        {
            throw new InvalidOperationException("Profiles are unavailable.");
        }

        ProfileExport.CheckCounts((replace ? 0 : target.Profiles.Count) + source.Profiles.Count,
            (replace ? 0 : target.Rules.Count) + (includeRules ? source.Rules.Count : 0));

        var importedProfiles = new List<ColorProfile>(source.Profiles.Count);
        var profileIds = new Dictionary<Guid, Guid>();
        var names = new HashSet<string>(replace ? [] : target.Profiles.Select(profile => profile.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var profile in source.Profiles)
        {
            var imported = ProfileExport.CopyProfile(profile);
            imported.Id = Guid.NewGuid();
            imported.Name = UniqueName(imported.Name, names);
            profileIds.Add(profile.Id, imported.Id);
            importedProfiles.Add(imported);
        }

        var importedRules = new List<ProfileRule>();
        if (includeRules)
        {
            foreach (var rule in source.Rules)
            {
                var imported = ProfileExport.CopyRule(rule);
                imported.Id = Guid.NewGuid();
                imported.ProfileId = profileIds[rule.ProfileId];
                if (!enableRules)
                {
                    imported.Enabled = false;
                }

                importedRules.Add(imported);
            }
        }

        if (replace)
        {
            target.Profiles = importedProfiles;
            target.Rules = importedRules;
            target.DefaultProfileId = profileIds[source.DefaultProfileId];
            target.AutomaticProfiles = includeRules && enableRules && source.AutomaticProfiles;
            return target.DefaultProfileId;
        }

        target.Profiles.AddRange(importedProfiles);
        target.Rules.AddRange(importedRules);
        return importedProfiles[0].Id;
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
}
