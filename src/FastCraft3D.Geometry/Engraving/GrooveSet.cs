using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// A groove that follows a curve. Wood grain cannot be built from rectangles: approximating one
/// flowing line with a few hundred axis-aligned boxes would multiply the cutter's cell grid past
/// any use, so a curved groove is extruded along its path instead.
/// </summary>
/// <param name="Points">The centre line, in the face's 2D frame.</param>
/// <param name="Width">How wide the cut is, across the path.</param>
/// <param name="Closed">True for a ring, such as the growth rings around a knot.</param>
public sealed record Polyline2(IReadOnlyList<Vector2> Points, float Width, bool Closed = false)
{
    public int SegmentCount => Closed ? Points.Count : Points.Count - 1;

    public bool IsUsable => Points.Count >= 2 && Width > 0;
}

/// <summary>
/// Everything one pattern wants cut out of a face.
///
/// The two kinds are built by different means and must not be mixed on the same face: the
/// rectangles are resolved into a single region and so may overlap each other freely, while
/// ribbons are extruded individually and rely on never crossing. No pattern uses both.
/// </summary>
public sealed record GrooveSet(IReadOnlyList<Rect2> Rectangles, IReadOnlyList<Polyline2> Ribbons)
{
    public static GrooveSet Empty { get; } = new([], []);

    public static GrooveSet Of(IReadOnlyList<Rect2> rectangles) => new(rectangles, []);

    public static GrooveSet Of(IReadOnlyList<Polyline2> ribbons) => new([], ribbons);

    public int Count => Rectangles.Count + Ribbons.Count;

    public bool IsEmpty => Count == 0;
}
