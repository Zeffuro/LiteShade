using System;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace LiteShade.Configuration;

public enum FocusMode
{
    Target,
    Camera,
    Manual,
}

public sealed record ColorProfile
{
    [JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public string Folder { get; set; } = string.Empty;
    public float Strength { get; set; } = 1f;
    public float Tint { get; set; }
    public float Warmth { get; set; }
    public float Saturation { get; set; } = 1f;
    public float Contrast { get; set; } = 1f;
    public float Exposure { get; set; }
    public float Midtones { get; set; }
    public float BlackLevel { get; set; }
    public float WhiteLevel { get; set; } = 1f;
    public float OutputBlackLevel { get; set; }
    public float OutputWhiteLevel { get; set; } = 1f;
    public Vector3 RedChannel { get; set; } = Vector3.UnitX;
    public Vector3 GreenChannel { get; set; } = Vector3.UnitY;
    public Vector3 BlueChannel { get; set; } = Vector3.UnitZ;
    public uint ShadowColor { get; set; } = 0xFFFFFFFF;
    public float ShadowStrength { get; set; }
    public float ShadowExposure { get; set; }
    public uint HighlightColor { get; set; } = 0xFFFFFFFF;
    public float HighlightStrength { get; set; }
    public float HighlightExposure { get; set; }
    public float TintBalance { get; set; }
    public float TintBlending { get; set; } = 0.5f;
    public uint GameFilterId { get; set; }
    public bool Vignette { get; set; }
    public float VignetteAmount { get; set; } = 0.35f;
    public float VignetteRadius { get; set; } = 0.6f;
    public float VignetteShape { get; set; } = 0.5f;
    public uint VignetteColor { get; set; } = 0xFF000000;
    public bool DepthOfField { get; set; }
    public FocusMode Focus { get; set; }
    public float FocusDistance { get; set; } = 5f;
    public float FNumber { get; set; } = 4f;

    public static ColorProfile CreateNeutral() => new() { Name = "Neutral" };

    public ColorProfile Copy() => this with { };

    public ColorProfile Duplicate()
    {
        var copy = Copy();
        copy.Id = Guid.NewGuid();
        copy.Name += " copy";
        return copy;
    }

    public void Normalize()
    {
        Name = NormalizeName(Name, "Unnamed profile");
        Folder = NormalizeName(Folder, string.Empty);

        Strength = Clamp(Strength, 0f, 1f, 1f);
        Tint = Clamp(Tint, -1f, 1f, 0f);
        Warmth = Clamp(Warmth, -1f, 1f, 0f);
        Saturation = Clamp(Saturation, 0f, 2f, 1f);
        Contrast = Clamp(Contrast, 0.5f, 1.5f, 1f);
        Exposure = Clamp(Exposure, -2f, 2f, 0f);
        Midtones = Clamp(Midtones, -1f, 1f, 0f);
        BlackLevel = Clamp(BlackLevel, 0f, 0.95f, 0f);
        WhiteLevel = Clamp(WhiteLevel, BlackLevel + 0.05f, 1f, 1f);
        OutputBlackLevel = Clamp(OutputBlackLevel, 0f, 0.95f, 0f);
        OutputWhiteLevel = Clamp(OutputWhiteLevel, OutputBlackLevel + 0.05f, 1f, 1f);
        RedChannel = ClampChannel(RedChannel, Vector3.UnitX);
        GreenChannel = ClampChannel(GreenChannel, Vector3.UnitY);
        BlueChannel = ClampChannel(BlueChannel, Vector3.UnitZ);
        ShadowColor |= 0xFF000000;
        HighlightColor |= 0xFF000000;
        ShadowStrength = Clamp(ShadowStrength, 0f, 1f, 0f);
        HighlightStrength = Clamp(HighlightStrength, 0f, 1f, 0f);
        ShadowExposure = Clamp(ShadowExposure, -2f, 2f, 0f);
        HighlightExposure = Clamp(HighlightExposure, -2f, 2f, 0f);
        TintBalance = Clamp(TintBalance, -1f, 1f, 0f);
        TintBlending = Clamp(TintBlending, 0f, 1f, 0.5f);
        FocusDistance = Clamp(FocusDistance, 0.5f, 200f, 5f);
        FNumber = Clamp(FNumber, 0.5f, 32f, 4f);
        VignetteAmount = Clamp(VignetteAmount, 0f, 1f, 0.35f);
        VignetteRadius = Clamp(VignetteRadius, 0f, 0.95f, 0.6f);
        VignetteShape = Clamp(VignetteShape, 0f, 1f, 0.5f);
        VignetteColor |= 0xFF000000;
        if (!Enum.IsDefined(Focus))
        {
            Focus = FocusMode.Target;
        }
    }

    private static float Clamp(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static Vector3 ClampChannel(Vector3 value, Vector3 fallback) => new(
        Clamp(value.X, -2f, 2f, fallback.X),
        Clamp(value.Y, -2f, 2f, fallback.Y),
        Clamp(value.Z, -2f, 2f, fallback.Z));

    internal static string NormalizeName(string? name, string fallback)
    {
        name = Regex.Replace(name ?? string.Empty, @"[\p{Cc}\p{Cf}]|#{2,}", " ").Trim();
        return name.Length == 0 ? fallback : name[..Math.Min(name.Length, 80)];
    }
}
