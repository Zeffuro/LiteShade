using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiteShade.Profiles;

namespace LiteShade.Configuration.Sharing;

internal sealed class ProfileExport
{
    private const string Prefix = "LiteShade1:";
    private const int MaxInputCharacters = 1024 * 1024;
    private const int MaxJsonBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        IgnoreReadOnlyProperties = true,
    };

    [JsonRequired]
    public int Version { get; set; } = 1;

    [JsonRequired]
    public Guid DefaultProfileId { get; set; }

    [JsonRequired]
    public bool AutomaticProfiles { get; set; }

    [JsonRequired]
    public List<ColorProfile> Profiles { get; set; } = [];

    [JsonRequired]
    public List<ProfileRule> Rules { get; set; } = [];

    public string ToText()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(Copy(), JsonOptions);
        if (json.Length > MaxJsonBytes)
        {
            throw new InvalidOperationException("Profile data is too large to export.");
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(json, 0, json.Length);
        }

        var text = Prefix + Convert.ToBase64String(output.ToArray());
        if (text.Length > MaxInputCharacters)
        {
            throw new InvalidOperationException("Profile data is too large to export.");
        }

        return text;
    }

    public static ProfileExport FromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Profile data is empty.");
        }

        text = text.Trim();
        if (text.Length > MaxInputCharacters)
        {
            throw new FormatException("Profile data is too large.");
        }

        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Profile data is not valid LiteShade data.");
        }

        try
        {
            var json = Decompress(Convert.FromBase64String(text[Prefix.Length..]));
            var profileExport = JsonSerializer.Deserialize<ProfileExport>(json, JsonOptions)
                ?? throw new FormatException("Profile data is not valid.");
            return profileExport.Copy();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
        {
            throw new FormatException("Profile data is not valid.", exception);
        }
    }

    public ProfileExport Copy()
    {
        if (Version != 1)
        {
            throw new FormatException("Profile data has an unsupported version.");
        }

        if (Profiles is null || Rules is null || Profiles.Count == 0)
        {
            throw new FormatException("Profile data has no profiles.");
        }

        CheckCounts(Profiles.Count, Rules.Count);

        var profiles = Profiles.Select(CopyProfile).ToList();
        var profileIds = new HashSet<Guid>();
        foreach (var profile in profiles)
        {
            if (profile.Id == Guid.Empty || !profileIds.Add(profile.Id))
            {
                throw new FormatException("Profile data contains duplicate or invalid profile IDs.");
            }
        }

        if (!profileIds.Contains(DefaultProfileId))
        {
            throw new FormatException("Profile data has a missing default profile.");
        }

        var rules = Rules.Select(CopyRule).ToList();
        var ruleIds = new HashSet<Guid>();
        foreach (var rule in rules)
        {
            if (rule.Id == Guid.Empty || !ruleIds.Add(rule.Id))
            {
                throw new FormatException("Profile data contains duplicate or invalid rule IDs.");
            }

            if (!rule.IsValid || !profileIds.Contains(rule.ProfileId))
            {
                throw new FormatException("Profile data has an invalid rule.");
            }
        }

        return new ProfileExport
        {
            DefaultProfileId = DefaultProfileId,
            AutomaticProfiles = AutomaticProfiles,
            Profiles = profiles,
            Rules = rules,
        };
    }

    public static ColorProfile CopyProfile(ColorProfile profile)
    {
        if (profile is null)
        {
            throw new FormatException("Profile data contains an empty profile.");
        }

        var copy = profile.Copy();
        copy.Normalize();
        return copy;
    }

    public static void CheckCounts(int profiles, int rules)
    {
        if (profiles > 512 || rules > 4096)
        {
            throw new FormatException("A shared setup can contain up to 512 profiles and 4096 rules.");
        }
    }

    public static ProfileRule CopyRule(ProfileRule rule)
    {
        if (rule is null)
        {
            throw new FormatException("Profile data contains an empty rule.");
        }

        var copy = rule.Copy();
        copy.Normalize();
        return copy;
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = gzip.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length > MaxJsonBytes - read)
            {
                throw new FormatException("Profile data is too large.");
            }

            output.Write(buffer, 0, read);
        }
    }
}
