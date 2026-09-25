using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Where the lettering sits on the surface, and which way up it is.
///
/// This is deliberately a move in the flat layout rather than anything to do with the surface.
/// Sliding a word across a face and sliding it round a barrel are then the same operation, and
/// the drag handles have one thing to change however the lettering is being projected.
/// </summary>
/// <param name="OffsetMm">Where the middle of the lettering goes, in layout millimetres.</param>
/// <param name="AngleDegrees">How far it is turned, anticlockwise.</param>
public readonly record struct SurfacePlacement(Vector2 OffsetMm, float AngleDegrees)
{
    public static SurfacePlacement Middle => new(Vector2.Zero, 0);

    public Vector2 Apply(Vector2 point)
    {
        float radians = AngleDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);

        return OffsetMm + new Vector2(
            point.X * cos - point.Y * sin,
            point.X * sin + point.Y * cos);
    }

    public IReadOnlyList<TextShape> Apply(IReadOnlyList<TextShape> shapes)
    {
        if (OffsetMm == Vector2.Zero && AngleDegrees == 0) return shapes;

        return shapes
            .Select(s => new TextShape(
                s.Outline.Select(Apply).ToList(),
                s.Holes.Select(h => (IReadOnlyList<Vector2>)h.Select(Apply).ToList()).ToList()))
            .ToList();
    }

    /// <summary>
    /// Half the width and height of the lettering, which is what the handles are drawn around.
    /// Measured before placing, because the handles turn with the lettering rather than boxing
    /// it in an upright rectangle that grows every time it is turned.
    /// </summary>
    public static Vector2 Extent(IReadOnlyList<TextShape> shapes)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var shape in shapes)
            foreach (var point in shape.Outline)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

        return min.X > max.X ? Vector2.Zero : (max - min) * 0.5f;
    }
}
