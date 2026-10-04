using System;
using System.Numerics;
using System.Runtime.InteropServices;
using LiteShade.Configuration;

namespace LiteShade.Graphics;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ColorMatrix(Vector4 red, Vector4 green, Vector4 blue)
{
    public readonly Vector4 Red = red;
    public readonly Vector4 Green = green;
    public readonly Vector4 Blue = blue;

    public static ColorMatrix Identity => new(Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ);

    public bool IsIdentity => Red == Vector4.UnitX && Green == Vector4.UnitY && Blue == Vector4.UnitZ;

    public bool IsFinite => Finite(Red) && Finite(Green) && Finite(Blue);

    public static ColorMatrix FromProfile(ColorProfile profile)
    {
        var tint = profile.Tint * 0.18f;
        var warmth = profile.Warmth * 0.18f;
        var gains = new Vector3(1f + tint + warmth, 1f - tint, 1f + tint - warmth);

        // Luminance weights from shader/sm5/posteffect/ColorFilter_DarkBlend.shcd.
        var luminance = new Vector3(0.29891f, 0.58661f, 0.11448f);
        gains /= Vector3.Dot(gains, luminance);
        gains *= MathF.Pow(2f, profile.Exposure * 0.5f) * profile.Contrast;

        var grey = new Vector4(luminance * (1f - profile.Saturation), 0f);
        var offset = 0.5f * (1f - profile.Contrast);
        var red = (grey + Vector4.UnitX * profile.Saturation) * gains.X;
        var green = (grey + Vector4.UnitY * profile.Saturation) * gains.Y;
        var blue = (grey + Vector4.UnitZ * profile.Saturation) * gains.Z;
        red.W = green.W = blue.W = offset;

        var grade = new ColorMatrix(red, green, blue);
        var mixer = new ColorMatrix(new Vector4(profile.RedChannel, 0f),
            new Vector4(profile.GreenChannel, 0f), new Vector4(profile.BlueChannel, 0f));
        var mixed = mixer.Multiply(grade);
        var levelScale = (profile.OutputWhiteLevel - profile.OutputBlackLevel) / (profile.WhiteLevel - profile.BlackLevel);
        var levelOffset = new Vector4(0f, 0f, 0f, profile.OutputBlackLevel - profile.BlackLevel * levelScale);
        return new ColorMatrix(mixed.Red * levelScale + levelOffset,
            mixed.Green * levelScale + levelOffset, mixed.Blue * levelScale + levelOffset)
            .WithStrength(profile.Strength);
    }

    public ColorMatrix WithStrength(float strength) => strength switch
    {
        0 => Identity,
        1 => this,
        _ => new ColorMatrix(
            Vector4.Lerp(Vector4.UnitX, Red, strength),
            Vector4.Lerp(Vector4.UnitY, Green, strength),
            Vector4.Lerp(Vector4.UnitZ, Blue, strength)),
    };

    public ColorMatrix Multiply(ColorMatrix right) => new(
        MultiplyRow(Red, right),
        MultiplyRow(Green, right),
        MultiplyRow(Blue, right));

    public ColorMatrix ScaleRows(Vector3 gains) => new(Red * gains.X, Green * gains.Y, Blue * gains.Z);

    public static Vector3 TintGain(uint colour, float amount)
    {
        if (amount == 0 || (colour & 0xFFFFFF) == 0xFFFFFF)
        {
            return Vector3.One;
        }

        var tint = new Vector3(colour & 0xFF, (colour >> 8) & 0xFF, (colour >> 16) & 0xFF) / 255f + new Vector3(0.5f);
        tint /= Vector3.Dot(tint, new Vector3(0.29891f, 0.58661f, 0.11448f));
        return Vector3.Lerp(Vector3.One, tint, amount);
    }

    public static ColorMatrix ShadowCorrection(Vector3 shadows, Vector3 highlights)
    {
        var gains = shadows / highlights * (256f / 255f);
        var offset = -gains * (0.5f / 256f);
        return new ColorMatrix(new Vector4(gains.X, 0, 0, offset.X),
            new Vector4(0, gains.Y, 0, offset.Y), new Vector4(0, 0, gains.Z, offset.Z));
    }

    public static ColorMatrix Lerp(ColorMatrix from, ColorMatrix to, float amount) => new(
        Vector4.Lerp(from.Red, to.Red, amount),
        Vector4.Lerp(from.Green, to.Green, amount),
        Vector4.Lerp(from.Blue, to.Blue, amount));

    private static Vector4 MultiplyRow(Vector4 row, ColorMatrix right)
        => row.X * right.Red + row.Y * right.Green + row.Z * right.Blue + new Vector4(0, 0, 0, row.W);

    private static bool Finite(Vector4 row)
        => float.IsFinite(row.X) && float.IsFinite(row.Y) && float.IsFinite(row.Z) && float.IsFinite(row.W);
}
