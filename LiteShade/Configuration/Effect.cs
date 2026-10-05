using System;

namespace LiteShade.Configuration;

[Flags]
public enum Effect
{
    ColourAdjustments = 1,
    ShadowHighlight = 2,
    GPoseFilter = 4,
    DepthOfField = 8,
    Vignette = 16,
}

internal static class Effects
{
    public static readonly Effect[] All = Enum.GetValues<Effect>();

    public static string Label(this Effect effect) => effect switch
    {
        Effect.ColourAdjustments => "Colour adjustments",
        Effect.ShadowHighlight => "Shadow/highlight",
        Effect.GPoseFilter => "GPose filter",
        Effect.DepthOfField => "Depth of field",
        Effect.Vignette => "Vignette",
        _ => effect.ToString(),
    };
}
