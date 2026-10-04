using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteShade.Configuration.Sharing;

internal sealed class ProfileImport(ProfileExport source)
{
    public ProfileExport Source { get; } = source;
    public HashSet<Guid> Selected { get; } = source.Profiles.Select(profile => profile.Id).ToHashSet();
    public Dictionary<Guid, string> Names { get; } = source.Profiles.ToDictionary(profile => profile.Id, profile => profile.Name);

    public int RuleCount => Source.Rules.Count(rule => Selected.Contains(rule.ProfileId));

    public Guid DefaultProfileId => Selected.Contains(Source.DefaultProfileId)
        ? Source.DefaultProfileId
        : Source.Profiles.FirstOrDefault(profile => Selected.Contains(profile.Id))?.Id ?? Guid.Empty;

    public string DefaultProfileName => Names.TryGetValue(DefaultProfileId, out var name)
        ? ColorProfile.NormalizeName(name, "Unnamed profile")
        : "None selected";

    public ProfileExport Prepare(bool includeRules)
    {
        var profiles = Source.Profiles.Where(profile => Selected.Contains(profile.Id)).Select(profile =>
        {
            var copy = profile.Copy();
            copy.Name = Names[profile.Id];
            return copy;
        }).ToList();

        return new ProfileExport
        {
            Version = Source.Version,
            Pack = Source.Pack?.Copy(),
            MinimumPluginVersion = Source.MinimumPluginVersion,
            DefaultProfileId = DefaultProfileId,
            AutomaticProfiles = Source.AutomaticProfiles,
            Profiles = profiles,
            Rules = includeRules ? Source.Rules.Where(rule => Selected.Contains(rule.ProfileId)).ToList() : [],
        }.Copy();
    }
}
