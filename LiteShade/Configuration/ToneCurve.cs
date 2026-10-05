using System;
using System.Collections.Immutable;
using System.Numerics;

namespace LiteShade.Configuration;

public readonly record struct ToneCurve(ImmutableArray<Vector2> Points)
{
    public const int MaxPoints = 16;
    private const float Spacing = 0.01f;

    public static ToneCurve Identity { get; } = new([Vector2.Zero, new Vector2(0.25f), new Vector2(0.5f), new Vector2(0.75f), Vector2.One]);

    [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsIdentity
    {
        get
        {
            if (Points.IsDefaultOrEmpty) return false;
            foreach (var point in Points)
                if (point.X != point.Y) return false;
            return true;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid
    {
        get
        {
            if (Points.IsDefaultOrEmpty || Points.Length is < 2 or > MaxPoints || Points[0] != Vector2.Zero || Points[^1] != Vector2.One)
                return false;
            for (var i = 0; i < Points.Length; i++)
                if (!ValidPoint(Points[i]) || (i > 0 && Points[i].X - Points[i - 1].X < Spacing - 0.000001f)) return false;
            return true;
        }
    }

    public ToneCurve WithPoint(int index, Vector2 point)
    {
        if (!IsValid || !float.IsFinite(point.X) || !float.IsFinite(point.Y) || index <= 0 || index >= Points.Length - 1) return this;
        var min = Points[index - 1].X + Spacing;
        var max = Points[index + 1].X - Spacing;
        point.X = Math.Clamp(point.X, Math.Min(min, max), max);
        point.Y = Math.Clamp(point.Y, 0, 1);
        return new ToneCurve(Points.SetItem(index, point));
    }

    public ToneCurve AddPoint(Vector2 point)
    {
        if (!IsValid || !ValidPoint(point) || Points.Length == MaxPoints) return this;
        var index = 0;
        foreach (var existing in Points)
        {
            if (MathF.Abs(existing.X - point.X) < Spacing) return this;
            if (existing.X < point.X) index++;
        }
        return new ToneCurve(Points.Insert(index, point));
    }

    public ToneCurve RemovePoint(int index)
        => IsValid && index > 0 && index < Points.Length - 1 ? new ToneCurve(Points.RemoveAt(index)) : this;

    public bool Equals(ToneCurve other) => Points.AsSpan().SequenceEqual(other.Points.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in Points.AsSpan()) hash.Add(point);
        return hash.ToHashCode();
    }

    private static bool ValidPoint(Vector2 point)
        => float.IsFinite(point.X) && float.IsFinite(point.Y) && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1;
}
