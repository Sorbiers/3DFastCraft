using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>An axis-aligned rectangle in a face's own 2D frame, in millimetres.</summary>
public readonly record struct Rect2(float MinU, float MinV, float MaxU, float MaxV)
{
    public float Width => MaxU - MinU;
    public float Height => MaxV - MinV;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect2 FromSize(float u, float v, float width, float height) =>
        new(u, v, u + width, v + height);

    public Rect2 ClippedTo(Rect2 bounds) => new(
        Math.Max(MinU, bounds.MinU), Math.Max(MinV, bounds.MinV),
        Math.Min(MaxU, bounds.MaxU), Math.Min(MaxV, bounds.MaxV));

    public bool Contains(float u, float v) => u >= MinU && u <= MaxU && v >= MinV && v <= MaxV;
}

/// <summary>
/// Builds the solid that gets subtracted to leave the pattern behind.
///
/// Rectangular grooves are allowed to overlap - a brick pattern's perpend joints have to run into
/// its courses, or the corners of every brick stay joined. Simply concatenating overlapping boxes
/// would not do: BSP CSG takes its input as a solid, and the walls where two boxes overlap sit
/// inside the material being removed, so they survive the subtraction as internal faces and the
/// result is no longer manifold.
///
/// So the rectangles are resolved into one region first. Their edges are collected into a
/// compressed grid, each cell is tested once for being covered, and the solid is built from the
/// cell set: a top and a bottom per covered cell, and a wall only where a covered cell meets an
/// uncovered one. Overlaps disappear by construction, no internal faces are ever emitted, and
/// because every quad spans exactly one cell the edges all match - no T-junctions to repair.
///
/// Curved grooves cannot go through that grid - one flowing line would fill it with thousands of
/// coordinates - so each is extruded along its own path instead. Those may not overlap, and the
/// pattern that uses them keeps them apart.
/// </summary>
public static class GrooveSolid
{
    /// <summary>
    /// How far the cutter stands proud of the surface it cuts into. Its top face must not be
    /// coplanar with the face being engraved: coplanar input is the one thing BSP CSG is bad at,
    /// and here it would be coplanar over the whole face at once.
    /// </summary>
    public const float Lift = 0.02f;

    /// <summary>
    /// The solid formed by extruding <paramref name="grooves"/> from just above the face down to
    /// <paramref name="depth"/> below it. Empty when nothing is covered.
    /// </summary>
    public static Mesh Build(GrooveSet grooves, FacePatch face, float depth)
    {
        var mesh = new Mesh();
        if (grooves.IsEmpty || depth <= 0) return mesh;

        AddRectangles(mesh, grooves.Rectangles, face, depth);

        foreach (var ribbon in grooves.Ribbons)
            AddRibbon(mesh, ribbon, face, depth);

        return mesh.Welded();
    }

    /// <summary>Convenience for the rectangle-only patterns, and for the tests.</summary>
    public static Mesh Build(IReadOnlyList<Rect2> grooves, FacePatch face, float depth) =>
        Build(GrooveSet.Of(grooves), face, depth);

    // --- Rectangles, resolved through a cell grid --------------------------------------

    private static void AddRectangles(
        Mesh mesh, IReadOnlyList<Rect2> grooves, FacePatch face, float depth)
    {
        var rectangles = grooves.Where(r => !r.IsEmpty).ToList();
        if (rectangles.Count == 0) return;

        float[] us = Coordinates(rectangles, r => r.MinU, r => r.MaxU);
        float[] vs = Coordinates(rectangles, r => r.MinV, r => r.MaxV);
        if (us.Length < 2 || vs.Length < 2) return;

        bool[,] covered = CoverCells(rectangles, us, vs);

        for (int i = 0; i < us.Length - 1; i++)
        {
            for (int j = 0; j < vs.Length - 1; j++)
            {
                if (!covered[i, j]) continue;

                // Wound anticlockwise seen from outside the face, so the top faces outward.
                var a = new Vector2(us[i], vs[j]);
                var b = new Vector2(us[i + 1], vs[j]);
                var c = new Vector2(us[i + 1], vs[j + 1]);
                var d = new Vector2(us[i], vs[j + 1]);

                AddQuad(mesh, face, a, b, c, d, Lift);   // top
                AddQuad(mesh, face, d, c, b, a, -depth); // bottom, facing the other way

                // A wall only where the neighbour is not covered - between two covered cells
                // there is no surface, which is exactly how the overlaps vanish.
                if (!IsCovered(covered, i, j - 1)) AddWall(mesh, face, a, b, depth);
                if (!IsCovered(covered, i + 1, j)) AddWall(mesh, face, b, c, depth);
                if (!IsCovered(covered, i, j + 1)) AddWall(mesh, face, c, d, depth);
                if (!IsCovered(covered, i - 1, j)) AddWall(mesh, face, d, a, depth);
            }
        }
    }

    /// <summary>Every distinct edge coordinate, which is where the cell grid can change state.</summary>
    private static float[] Coordinates(
        List<Rect2> rectangles, Func<Rect2, float> low, Func<Rect2, float> high)
    {
        var values = new List<float>(rectangles.Count * 2);
        foreach (var r in rectangles)
        {
            values.Add(low(r));
            values.Add(high(r));
        }

        values.Sort();

        // Coordinates closer together than this would make a cell too thin to matter and would
        // only cost CSG time; collapsing them keeps the grid honest.
        var distinct = new List<float> { values[0] };
        foreach (float value in values)
            if (value - distinct[^1] > 1e-4f)
                distinct.Add(value);

        return distinct.ToArray();
    }

    private static bool[,] CoverCells(List<Rect2> rectangles, float[] us, float[] vs)
    {
        var covered = new bool[us.Length - 1, vs.Length - 1];

        foreach (var rectangle in rectangles)
        {
            // Only the cells this rectangle actually spans, so a pattern of many small grooves
            // stays linear in the number of grooves rather than sweeping the whole grid each time.
            int i0 = Span(us, rectangle.MinU), i1 = Span(us, rectangle.MaxU);
            int j0 = Span(vs, rectangle.MinV), j1 = Span(vs, rectangle.MaxV);

            for (int i = i0; i < i1; i++)
                for (int j = j0; j < j1; j++)
                    covered[i, j] = true;
        }

        return covered;
    }

    /// <summary>Index of the last coordinate at or below <paramref name="value"/>.</summary>
    private static int Span(float[] coordinates, float value)
    {
        int index = Array.BinarySearch(coordinates, value);
        if (index >= 0) return index;

        return Math.Clamp(~index - 1, 0, coordinates.Length - 1);
    }

    private static bool IsCovered(bool[,] covered, int i, int j) =>
        i >= 0 && j >= 0 && i < covered.GetLength(0) && j < covered.GetLength(1) && covered[i, j];

    // --- Ribbons, extruded along their own path ----------------------------------------

    /// <summary>
    /// One curved groove. The path is offset to either side to give the cut its width, and the
    /// strip between the two offsets is roofed, floored and walled.
    ///
    /// Corner normals average the two segments meeting there rather than mitring them. A mitre
    /// holds the width exact through a bend but runs off to infinity as the bend sharpens;
    /// averaging pinches the width slightly instead, which at these sizes nothing will notice.
    /// </summary>
    private static void AddRibbon(Mesh mesh, Polyline2 ribbon, FacePatch face, float depth)
    {
        if (!Edges(ribbon, out var points, out var left, out var right)) return;

        int count = points.Count;
        int segments = ribbon.Closed ? count : count - 1;

        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % count;

            AddQuad(mesh, face, right[i], right[j], left[j], left[i], Lift);
            AddQuad(mesh, face, left[i], left[j], right[j], right[i], -depth);

            // The strip's two long edges are its only boundary, so the walls go there.
            AddWall(mesh, face, right[i], right[j], depth);
            AddWall(mesh, face, left[j], left[i], depth);
        }

        if (!ribbon.Closed)
        {
            AddWall(mesh, face, left[0], right[0], depth);
            AddWall(mesh, face, right[count - 1], left[count - 1], depth);
        }
    }

    /// <summary>
    /// The two sides of a ribbon. Shared with the preview so that what is drawn on the face is
    /// built from the same path as what gets cut out of it.
    /// </summary>
    private static bool Edges(
        Polyline2 ribbon, out List<Vector2> points, out Vector2[] left, out Vector2[] right)
    {
        points = Trimmed(ribbon.Points);
        left = right = [];

        if (points.Count < 2 || ribbon.Width <= 0) return false;

        float half = ribbon.Width * 0.5f;
        left = new Vector2[points.Count];
        right = new Vector2[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            var normal = NormalAt(points, i, ribbon.Closed);
            left[i] = points[i] + normal * half;
            right[i] = points[i] - normal * half;
        }

        return true;
    }

    /// <summary>Drops repeated points, which would otherwise give a segment no direction.</summary>
    private static List<Vector2> Trimmed(IReadOnlyList<Vector2> points)
    {
        var kept = new List<Vector2>(points.Count);

        foreach (var point in points)
            if (kept.Count == 0 || (point - kept[^1]).LengthSquared() > 1e-10f)
                kept.Add(point);

        return kept;
    }

    private static Vector2 NormalAt(List<Vector2> points, int index, bool closed)
    {
        int count = points.Count;

        var before = index > 0 ? points[index] - points[index - 1]
            : closed ? points[0] - points[count - 1] : Vector2.Zero;
        var after = index < count - 1 ? points[index + 1] - points[index]
            : closed ? points[0] - points[count - 1] : Vector2.Zero;

        var direction = Normalised(before) + Normalised(after);
        if (direction.LengthSquared() < 1e-12f) direction = Normalised(before + after);
        if (direction.LengthSquared() < 1e-12f) direction = Vector2.UnitX;

        direction = Vector2.Normalize(direction);
        return new Vector2(-direction.Y, direction.X);
    }

    private static Vector2 Normalised(Vector2 v) =>
        v.LengthSquared() < 1e-12f ? Vector2.Zero : Vector2.Normalize(v);

    /// <summary>
    /// Just the tops of the grooves, laid on the face at <paramref name="height"/>. This is the
    /// preview: it comes from the same rectangles and ribbons the cutter is built from, so what
    /// is shown is exactly what will be taken away.
    ///
    /// Overlaps are left alone here - two rectangles drawn over each other look the same as one,
    /// and only the cutter has to care about them.
    /// </summary>
    public static Mesh Surface(GrooveSet grooves, FacePatch face, float height)
    {
        var mesh = new Mesh();

        foreach (var r in grooves.Rectangles)
        {
            if (r.IsEmpty) continue;

            AddQuad(mesh, face,
                new Vector2(r.MinU, r.MinV), new Vector2(r.MaxU, r.MinV),
                new Vector2(r.MaxU, r.MaxV), new Vector2(r.MinU, r.MaxV), height);
        }

        foreach (var ribbon in grooves.Ribbons)
        {
            if (!Edges(ribbon, out var points, out var left, out var right)) continue;

            int segments = ribbon.Closed ? points.Count : points.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % points.Count;
                AddQuad(mesh, face, right[i], right[j], left[j], left[i], height);
            }
        }

        return mesh;
    }

    // --- Shared -------------------------------------------------------------------------

    private static void AddQuad(
        Mesh mesh, FacePatch face, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float height)
    {
        mesh.AddTriangle(face.ToLocal(a, height), face.ToLocal(b, height), face.ToLocal(c, height));
        mesh.AddTriangle(face.ToLocal(a, height), face.ToLocal(c, height), face.ToLocal(d, height));
    }

    /// <summary>
    /// The wall under one top edge. Walking the edge in the top face's own winding leaves the
    /// material on the left, so this ordering faces the wall outward.
    /// </summary>
    private static void AddWall(Mesh mesh, FacePatch face, Vector2 a, Vector2 b, float depth)
    {
        Vector3 topA = face.ToLocal(a, Lift), topB = face.ToLocal(b, Lift);
        Vector3 lowA = face.ToLocal(a, -depth), lowB = face.ToLocal(b, -depth);

        mesh.AddTriangle(topA, lowA, lowB);
        mesh.AddTriangle(topA, lowB, topB);
    }
}
