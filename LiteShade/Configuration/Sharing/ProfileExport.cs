using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiteShade.Profiles;

namespace LiteShade.Configuration.Sharing;

internal sealed class ProfileExport
{
    private const string Prefix = "LiteShade2:";
    private const string LegacyPrefix = "LiteShade1:";
    private const int MaxInputCharacters = 1024 * 1024;
    private const int MaxJsonBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 32,
        IgnoreReadOnlyProperties = true,
        IncludeFields = true,
    };

    [JsonRequired]
    public int Version { get; set; } = 2;

    public ProfilePack? Pack { get; set; }
    public string? MinimumPluginVersion { get; set; }

    public static string CurrentPluginVersion => typeof(ProfileExport).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";

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

        var text = (Version == 1 ? LegacyPrefix : Prefix) + Convert.ToBase64String(output.ToArray());
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

        var prefix = text.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix : LegacyPrefix;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException("Profile data is not valid LiteShade data.");
        }

        try
        {
            var json = Decompress(Convert.FromBase64String(text[prefix.Length..]));
            var profileExport = JsonSerializer.Deserialize<ProfileExport>(json, JsonOptions)
                ?? throw new FormatException("Profile data is not valid.");
            if (profileExport.Version != (prefix == Prefix ? 2 : 1))
            {
                throw new FormatException("Profile data has an unsupported version.");
            }

            return profileExport.Copy();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
        {
            throw new FormatException("Profile data is not valid.", exception);
        }
    }

    public ProfileExport Copy()
    {
        if (Version is not (1 or 2))
        {
            throw new FormatException("Profile data has an unsupported version.");
        }

        if (Version == 2 && (Pack is null || MinimumPluginVersion is null))
        {
            throw new FormatException("Profile data is missing pack information.");
        }

        if (MinimumPluginVersion is not null)
        {
            if (!System.Version.TryParse(MinimumPluginVersion, out var required))
            {
                throw new FormatException("Profile data has an invalid minimum plugin version.");
            }

            required = new System.Version(required.Major, required.Minor, Math.Max(0, required.Build), Math.Max(0, required.Revision));
            if (required > System.Version.Parse(CurrentPluginVersion))
            {
                throw new FormatException($"This pack requires LiteShade {MinimumPluginVersion} or newer.");
            }
        }

        var pack = Pack?.Copy();
        if (pack is not null)
        {
            if (pack.Id == Guid.Empty || pack.Revision < 1)
            {
                throw new FormatException("Profile data has invalid pack information.");
            }

            pack.Normalize();
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
            Version = Version,
            Pack = pack,
            MinimumPluginVersion = MinimumPluginVersion,
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

        if (!float.IsFinite(profile.BlackLevel) || !float.IsFinite(profile.WhiteLevel)
            || profile.BlackLevel is < 0f or > 0.95f || profile.WhiteLevel is < 0.05f or > 1f
            || profile.WhiteLevel < profile.BlackLevel + 0.05f
            || !float.IsFinite(profile.OutputBlackLevel) || !float.IsFinite(profile.OutputWhiteLevel)
            || profile.OutputBlackLevel is < 0f or > 0.95f || profile.OutputWhiteLevel is < 0.05f or > 1f
            || profile.OutputWhiteLevel < profile.OutputBlackLevel + 0.05f
            || !ValidChannel(profile.RedChannel) || !ValidChannel(profile.GreenChannel) || !ValidChannel(profile.BlueChannel))
        {
            throw new FormatException("Profile data contains invalid levels or channel mixing.");
        }

        if (!float.IsFinite(profile.Midtones) || profile.Midtones is < -1f or > 1f)
        {
            throw new FormatException("Profile data contains invalid midtones.");
        }

        if (!profile.Curve.IsValid)
        {
            throw new FormatException("Profile data contains an invalid curve.");
        }

        if (!float.IsFinite(profile.MidtoneStrength) || profile.MidtoneStrength is < 0f or > 1f)
        {
            throw new FormatException("Profile data contains invalid midtone tint strength.");
        }

        if (!float.IsFinite(profile.ShadowExposure) || profile.ShadowExposure is < -2f or > 2f
            || !float.IsFinite(profile.HighlightExposure) || profile.HighlightExposure is < -2f or > 2f)
        {
            throw new FormatException("Profile data contains invalid shadow or highlight brightness.");
        }

        if (!float.IsFinite(profile.TintBalance) || profile.TintBalance is < -1f or > 1f
            || !float.IsFinite(profile.TintBlending) || profile.TintBlending is < 0f or > 1f)
        {
            throw new FormatException("Profile data contains invalid tint balance or blending.");
        }

        var copy = profile.Copy();
        copy.Normalize();
        return copy;
    }

    private static bool ValidChannel(Vector3 channel)
        => float.IsFinite(channel.X) && channel.X is >= -2f and <= 2f
            && float.IsFinite(channel.Y) && channel.Y is >= -2f and <= 2f
            && float.IsFinite(channel.Z) && channel.Z is >= -2f and <= 2f;

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
