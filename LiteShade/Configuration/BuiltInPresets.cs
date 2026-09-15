using System.Collections.Generic;

namespace LiteShade.Configuration;

internal static class BuiltInPresets
{
    public static IReadOnlyList<BuiltInPreset> All { get; } =
    [
        new("Clear", "Less green, a little more colour.", Tint: 0.06f, Saturation: 1.25f),
        new("Warm", "Warmer light and softer contrast.", Warmth: 0.40f, Saturation: 1.16f, Contrast: 0.98f),
        new("Cool", "Cooler tones with a crisp finish.", Warmth: -0.40f, Saturation: 1.16f, Contrast: 1.02f),
        new("Vivid", "Richer colour and deeper shadows.", Tint: 0.10f, Saturation: 1.35f, Contrast: 1.04f),
    ];
}

internal sealed record BuiltInPreset(
    string Name,
    string Description,
    float Tint = 0f,
    float Warmth = 0f,
    float Saturation = 1f,
    float Contrast = 1f)
{
    public ColorProfile CreateProfile() => new()
    {
        Name = Name,
        Tint = Tint,
        Warmth = Warmth,
        Saturation = Saturation,
        Contrast = Contrast,
    };
}
