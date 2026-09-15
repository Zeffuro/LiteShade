using System;
using System.Text.RegularExpressions;

namespace LiteShade.Configuration;

public enum FocusMode
{
    Target,
    Camera,
    Manual,
}

public sealed class ColorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public float Strength { get; set; } = 1f;
    public float Tint { get; set; }
    public float Warmth { get; set; }
    public float Saturation { get; set; } = 1f;
    public float Contrast { get; set; } = 1f;
    public float Exposure { get; set; }
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

    public ColorProfile Copy() => (ColorProfile)MemberwiseClone();

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

        Strength = Clamp(Strength, 0f, 1f, 1f);
        Tint = Clamp(Tint, -1f, 1f, 0f);
        Warmth = Clamp(Warmth, -1f, 1f, 0f);
        Saturation = Clamp(Saturation, 0f, 2f, 1f);
        Contrast = Clamp(Contrast, 0.5f, 1.5f, 1f);
        Exposure = Clamp(Exposure, -2f, 2f, 0f);
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

    internal static string NormalizeName(string? name, string fallback)
    {
        name = Regex.Replace(name ?? string.Empty, @"[\p{Cc}\p{Cf}]|#{2,}", " ").Trim();
        return name.Length == 0 ? fallback : name[..Math.Min(name.Length, 80)];
    }
}
