using System;

namespace LiteShade.Configuration;

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
    public bool DepthOfField { get; set; }
    public bool AutoFocus { get; set; } = true;
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
        Name = string.IsNullOrWhiteSpace(Name) ? "Unnamed profile" : Name.Trim();
        if (Name.Length > 80)
        {
            Name = Name[..80];
        }

        Strength = Clamp(Strength, 0f, 1f, 1f);
        Tint = Clamp(Tint, -1f, 1f, 0f);
        Warmth = Clamp(Warmth, -1f, 1f, 0f);
        Saturation = Clamp(Saturation, 0f, 2f, 1f);
        Contrast = Clamp(Contrast, 0.5f, 1.5f, 1f);
        Exposure = Clamp(Exposure, -2f, 2f, 0f);
        FocusDistance = Clamp(FocusDistance, 0.5f, 200f, 5f);
        FNumber = Clamp(FNumber, 0.7f, 32f, 4f);
    }

    private static float Clamp(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
