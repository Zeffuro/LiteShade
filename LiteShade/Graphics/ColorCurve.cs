using System;
using System.Numerics;
using LiteShade.Configuration;

namespace LiteShade.Graphics;

internal sealed class ColorCurve
{
    private readonly float[] _samples;
    public ToneCurve? Source { get; }
    public bool IsIdentity { get; }

    public static ColorCurve Identity { get; } = new(ToneCurve.Identity);

    public ColorCurve(ToneCurve curve)
    {
        if (!curve.IsValid) curve = ToneCurve.Identity;
        Source = curve;
        IsIdentity = curve.IsIdentity;
        _samples = new float[256];
        Sample(curve, _samples);
    }

    private ColorCurve(float[] samples)
    {
        _samples = samples;
        IsIdentity = true;
        for (var i = 0; i < samples.Length; i++)
            if (samples[i] != i / 255f) IsIdentity = false;
    }

    public float Evaluate(float value)
    {
        var position = value * 255;
        var index = Math.Min((int)position, 254);
        return float.Lerp(_samples[index], _samples[index + 1], position - index);
    }

    public static ColorCurve Lerp(ColorCurve from, ColorCurve to, float amount)
    {
        if (amount <= 0 || ReferenceEquals(from, to)) return from;
        if (amount >= 1) return to;
        if (from.IsIdentity && to.IsIdentity) return Identity;
        var samples = new float[256];
        for (var i = 0; i < samples.Length; i++) samples[i] = float.Lerp(from._samples[i], to._samples[i], amount);
        return new ColorCurve(samples);
    }

    public static void Sample(ToneCurve curve, Span<float> samples)
    {
        if (!curve.IsValid) curve = ToneCurve.Identity;
        if (curve.IsIdentity)
        {
            for (var i = 0; i < samples.Length; i++) samples[i] = i / (float)(samples.Length - 1);
            return;
        }

        var points = curve.Points.AsSpan();
        Span<float> slopes = stackalloc float[points.Length - 1];
        Span<float> tangents = stackalloc float[points.Length];
        for (var i = 0; i < slopes.Length; i++)
            slopes[i] = (points[i + 1].Y - points[i].Y) / (points[i + 1].X - points[i].X);

        tangents[0] = slopes[0];
        tangents[^1] = slopes[^1];
        for (var i = 1; i < tangents.Length - 1; i++)
            tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) * 0.5f;

        for (var i = 0; i < slopes.Length; i++)
        {
            if (slopes[i] == 0)
            {
                tangents[i] = tangents[i + 1] = 0;
                continue;
            }

            var a = tangents[i] / slopes[i];
            var b = tangents[i + 1] / slopes[i];
            var length = MathF.Sqrt(a * a + b * b);
            if (length <= 3) continue;
            tangents[i] *= 3 / length;
            tangents[i + 1] *= 3 / length;
        }

        var segment = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var x = i / (float)(samples.Length - 1);
            while (segment < slopes.Length - 1 && x > points[segment + 1].X) segment++;
            var left = points[segment];
            var right = points[segment + 1];
            var width = right.X - left.X;
            var t = (x - left.X) / width;
            var t2 = t * t;
            var t3 = t2 * t;
            samples[i] = Math.Clamp((2 * t3 - 3 * t2 + 1) * left.Y + (t3 - 2 * t2 + t) * width * tangents[segment]
                + (-2 * t3 + 3 * t2) * right.Y + (t3 - t2) * width * tangents[segment + 1], 0, 1);
        }
    }

    public static bool BuildLut(ColorCurve curve, ColorCurve? from, float blend, float strength, Vector4 native, Span<byte> output)
    {
        var low = native.X;
        var high = native.Y;
        if (high - low < 0.01f)
        {
            var middle = (low + high) * 0.5f;
            low = middle - 0.005f;
            high = middle + 0.005f;
        }

        var scale = 0.5f / MathF.Max(0.01f, high - low) + 0.5f;
        for (var i = 0; i < 256; i++)
        {
            var x = i * (1f / 255f);
            var value = x < low ? x * 0.5f : x > high ? x * 0.5f + 0.5f : (x - low) * scale + low * 0.5f;
            var distance = value - 0.5f;
            value = Math.Clamp(value + (MathF.Sqrt(0.5f - distance * distance) - 0.5f) * native.Z, 0, 1);
            var signed = (value + value) - 1;
            distance = MathF.Abs(signed) - 0.5f;
            value = Math.Clamp(value + ((MathF.Sqrt(0.5f - distance * distance) - 0.5f) * (signed >= 0 ? 1 : -1)) * native.W, 0, 1);
            if (!float.IsFinite(value)) return false;

            var adjusted = curve.Evaluate(value);
            if (from is not null) adjusted = float.Lerp(from.Evaluate(value), adjusted, blend);
            value = float.Lerp(value, adjusted, strength);
            if (!float.IsFinite(value)) return false;
            output[i] = (byte)Math.Clamp(value * 255 + 0.5f, 0, 255);
        }

        return true;
    }
}
