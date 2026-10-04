using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LiteShade.Configuration;

public sealed class ProfilePack
{
    [JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "My profiles";
    public string Author { get; set; } = string.Empty;
    [JsonRequired]
    public int Revision { get; set; } = 1;

    public ProfilePack Copy() => new() { Id = Id, Name = Name, Author = Author, Revision = Revision };

    public void Normalize()
    {
        if (Id == Guid.Empty)
        {
            Id = Guid.NewGuid();
        }

        Name = ColorProfile.NormalizeName(Name, "My profiles");
        Author = string.IsNullOrWhiteSpace(Author) ? string.Empty : ColorProfile.NormalizeName(Author, string.Empty);
        Revision = Math.Max(1, Revision);
    }
}

public sealed class InstalledPack
{
    public ProfilePack Pack { get; set; } = new();
    public Dictionary<Guid, Guid> ProfileIds { get; set; } = [];
    public Dictionary<Guid, Guid> RuleIds { get; set; } = [];

    public InstalledPack Copy() => new()
    {
        Pack = Pack.Copy(),
        ProfileIds = new Dictionary<Guid, Guid>(ProfileIds),
        RuleIds = new Dictionary<Guid, Guid>(RuleIds),
    };
}
