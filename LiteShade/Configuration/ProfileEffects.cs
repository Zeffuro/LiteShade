namespace LiteShade.Configuration;

internal static class ProfileEffects
{
    public static ColorProfile Copy(ColorProfile source, ColorProfile target, Effect effects)
    {
        var copy = target.Copy();
        if ((effects & Effect.ColourAdjustments) != 0)
        {
            copy.Strength = source.Strength;
            copy.Tint = source.Tint;
            copy.Warmth = source.Warmth;
            copy.Saturation = source.Saturation;
            copy.Contrast = source.Contrast;
            copy.Exposure = source.Exposure;
            copy.Midtones = source.Midtones;
            copy.BlackLevel = source.BlackLevel;
            copy.WhiteLevel = source.WhiteLevel;
            copy.OutputBlackLevel = source.OutputBlackLevel;
            copy.OutputWhiteLevel = source.OutputWhiteLevel;
            copy.RedChannel = source.RedChannel;
            copy.GreenChannel = source.GreenChannel;
            copy.BlueChannel = source.BlueChannel;
        }

        if ((effects & Effect.ShadowHighlight) != 0)
        {
            copy.ShadowColor = source.ShadowColor;
            copy.ShadowStrength = source.ShadowStrength;
            copy.ShadowExposure = source.ShadowExposure;
            copy.HighlightColor = source.HighlightColor;
            copy.HighlightStrength = source.HighlightStrength;
            copy.HighlightExposure = source.HighlightExposure;
            copy.TintBalance = source.TintBalance;
            copy.TintBlending = source.TintBlending;
        }

        if ((effects & Effect.GPoseFilter) != 0)
        {
            copy.GameFilterId = source.GameFilterId;
        }

        if ((effects & Effect.DepthOfField) != 0)
        {
            copy.DepthOfField = source.DepthOfField;
            copy.Focus = source.Focus;
            copy.FocusDistance = source.FocusDistance;
            copy.FNumber = source.FNumber;
        }

        if ((effects & Effect.Vignette) != 0)
        {
            copy.Vignette = source.Vignette;
            copy.VignetteAmount = source.VignetteAmount;
            copy.VignetteRadius = source.VignetteRadius;
            copy.VignetteShape = source.VignetteShape;
            copy.VignetteColor = source.VignetteColor;
        }

        return copy;
    }
}
