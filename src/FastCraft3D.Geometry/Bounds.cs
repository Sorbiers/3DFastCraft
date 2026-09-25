using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Axis-aligned bounding box in millimetres.
///
/// Emptiness is stored as a positive "has value" flag rather than an <c>IsEmpty</c> one, so that
/// the struct's default value is the empty box. With the flag the other way round, C# zeroing a
/// struct produces something that claims to be a real box sitting at the origin, and every
/// accumulating union then silently stretches from the origin to the geometry - which puts the
/// centre of a selection halfway to the origin and makes its diagonal meaningless.
/// </summary>
public readonly struct Bounds
{
    private readonly bool hasValue;

    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public Bounds(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
        hasValue = true;
    }

    /// <summary>The box that contains nothing. Also what <c>default(Bounds)</c> gives you.</summary>
    public static Bounds Empty => default;

    public bool IsEmpty => !hasValue;

    public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;
    public Vector3 Center => IsEmpty ? Vector3.Zero : (Min + Max) * 0.5f;
    public float Diagonal => Size.Length();

    public Bounds Union(Bounds other)
    {
        if (other.IsEmpty) return this;
        if (IsEmpty) return other;
        return new Bounds(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
    }

    public static Bounds FromPoints(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            any = true;
        }
        return any ? new Bounds(min, max) : Empty;
    }

    public override string ToString() =>
        IsEmpty ? "empty" : $"{Size.X:0.##} x {Size.Y:0.##} x {Size.Z:0.##} mm";
}
