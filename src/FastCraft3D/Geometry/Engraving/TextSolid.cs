using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <param name="Outline">The outer loop, in the flat layout, in millimetres.</param>
/// <param name="Holes">Loops enclosed by it.</param>
public readonly record struct TextShape(
    IReadOnlyList<Vector2> Outline, IReadOnlyList<IReadOnlyList<Vector2>> Holes);

/// <summary>
/// Builds the solid that lettering is cut from or raised with.
///
/// It is built flat first, in the layout's own millimetres, and only then put onto the shape.
/// Keeping those apart is what lets the same letters go flat onto a face or round a cylinder:
/// the outlines never learn about the surface, and the surface never learns about letters.
///
/// On anything curved the flat solid is divided up before it is laid on, because a straight edge
/// laid round a barrel cuts the corner. Dividing it uniformly keeps it watertight - every shared
/// edge is split the same way from both sides - which mattered more than being clever about
/// which edges actually needed it.
///
/// The same solid serves both jobs: subtracted it engraves, unioned it embosses. The difference
/// is only which side of the surface it stands on and which boolean is asked for.
/// </summary>
public static class TextSolid
{
    /// <summary>
    /// A bevel wider than this share of a stroke would eat the stroke, so it is held back. Thin
    /// strokes are what fail first, and a letter that closes up is worse than one with no bevel.
    /// </summary>
    private const float BevelSafety = 0.35f;

    /// <summary>
    /// The sharpest corner a bevel is followed round. Below it the corner is left blunt rather
    /// than shot off into a spike four times the bevel's own width.
    /// </summary>
    private const float MiterLimit = 0.25f;

    /// <summary>
    /// How far the lettering may stray from the surface before a step is broken in half. Well
    /// under a printed layer, so nothing of it survives slicing, and loose enough that a barrel
    /// does not ask for a step every tenth of a millimetre.
    /// </summary>
    private const float SagToleranceMm = 0.04f;

    /// <summary>
    /// Where wrapping stops chasing the curve. Lettering this fine is already far past what any
    /// nozzle will lay down, and going further only makes the boolean that follows slower.
    /// </summary>
    private const int WrapTriangleBudget = 40_000;

    /// <param name="from">Height the solid starts at, out of the surface.</param>
    /// <param name="to">Height it ends at. Engraving goes down, raising goes up.</param>
    /// <param name="bevelMm">
    /// How far the far end is drawn in, sloping the walls. Zero leaves them upright.
    /// </param>
    public static Mesh Build(
        IReadOnlyList<TextShape> shapes, IPlacementSurface surface, float from, float to, float bevelMm = 0)
    {
        var flat = BuildFlat(shapes, from, to, bevelMm);
        if (flat.TriangleCount == 0) return flat;

        return Warp(flat, surface);
    }

    /// <summary>Convenience for the common case of flat lettering on a picked face.</summary>
    public static Mesh Build(
        IReadOnlyList<TextShape> shapes, FacePatch face, float from, float to, float bevelMm = 0) =>
        Build(shapes, new PlanarSurface(face), from, to, bevelMm);

    /// <summary>
    /// The solid in the layout's own coordinates: x and y across the lettering, z out of it.
    /// </summary>
    private static Mesh BuildFlat(
        IReadOnlyList<TextShape> shapes, float from, float to, float bevelMm)
    {
        var mesh = new Mesh();
        if (shapes.Count == 0 || from == to) return mesh;

        float low = Math.Min(from, to), high = Math.Max(from, to);

        foreach (var shape in shapes)
        {
            var outline = Wound(shape.Outline, anticlockwise: true);
            var holes = shape.Holes.Select(h => Wound(h, anticlockwise: false)).ToList();

            // The bevelled end is drawn in: the outline shrinks and the holes grow, which is what
            // slopes a wall inward on both.
            var narrowOutline = bevelMm > 0 ? Inset(outline, bevelMm) : outline;
            var narrowHoles = bevelMm > 0
                ? holes.Select(h => Inset(h, bevelMm)).ToList()
                : holes;

            AddCap(mesh, narrowOutline, narrowHoles, high, up: true);
            AddCap(mesh, outline, holes, low, up: false);

            AddWalls(mesh, outline, narrowOutline, low, high);
            for (int i = 0; i < holes.Count; i++)
                AddWalls(mesh, holes[i], narrowHoles[i], low, high);
        }

        return mesh.Welded();
    }

    private static void AddCap(
        Mesh mesh, IReadOnlyList<Vector2> outline, IReadOnlyList<IReadOnlyList<Vector2>> holes,
        float height, bool up)
    {
        var (points, triangles) = Polygon2.Triangulate(outline, holes);

        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            var a = points[triangles[i]];
            var b = points[triangles[i + 1]];
            var c = points[triangles[i + 2]];

            if (up)
                mesh.AddTriangle(Flat(a, height), Flat(b, height), Flat(c, height));
            else
                mesh.AddTriangle(Flat(c, height), Flat(b, height), Flat(a, height));
        }
    }

    /// <summary>
    /// The wall between one loop at the base and the same loop, possibly drawn in, at the top.
    /// Both have the same number of points, so each pair joins straight across.
    /// </summary>
    private static void AddWalls(
        Mesh mesh, IReadOnlyList<Vector2> low, IReadOnlyList<Vector2> high,
        float bottom, float top)
    {
        if (low.Count != high.Count) return;

        for (int i = 0; i < low.Count; i++)
        {
            int j = (i + 1) % low.Count;
            if ((low[j] - low[i]).LengthSquared() < 1e-12f) continue;

            mesh.AddTriangle(Flat(high[i], top), Flat(low[i], bottom), Flat(low[j], bottom));
            mesh.AddTriangle(Flat(high[i], top), Flat(low[j], bottom), Flat(high[j], top));
        }
    }

    /// <summary>
    /// Draws a loop in by the given amount, along each corner's own direction.
    ///
    /// A proper offset would have to notice a stroke closing up on itself; this notices only the
    /// symptom - the loop turning inside out - and gives up on that shape's bevel when it does.
    /// A letter with no bevel is a great deal better than a letter with its stem swallowed.
    /// </summary>
    private static IReadOnlyList<Vector2> Inset(IReadOnlyList<Vector2> loop, float amount)
    {
        if (loop.Count < 3) return loop;

        float before = Polygon2.SignedArea(loop);
        var moved = new List<Vector2>(loop.Count);

        for (int i = 0; i < loop.Count; i++)
        {
            var previous = loop[(i - 1 + loop.Count) % loop.Count];
            var here = loop[i];
            var next = loop[(i + 1) % loop.Count];

            moved.Add(here + Miter(Inward(here - previous), Inward(next - here), amount));
        }

        float after = Polygon2.SignedArea(moved);

        // The move must not turn the loop inside out. Checking only the winding is not enough and
        // was the first thing tried: a stroke narrower than the bevel turns inside out through its
        // own middle, and that is a point reflection, which leaves the winding exactly as it was.
        // A 2 mm square drawn in by 5 mm came back as a 5 mm square, still anticlockwise, and
        // perfectly happy. So the size has to be checked as well as the sign.
        //
        // Which way the size should go depends on which loop this is. An outline is wound
        // anticlockwise and must come out smaller; a hole is wound the other way, so the same
        // inward step grows it, which is exactly what slopes its wall the same direction. A hole
        // growing cannot swallow itself, so only the outline needs the guard against losing most
        // of a thin stroke.
        bool sane = before > 0
            ? after > 0 && after < before && after > before * (1 - BevelSafety * 2)
            : after < 0 && after < before;

        return sane ? moved : loop;
    }

    /// <summary>
    /// How far a corner moves so that both of its edges end up <paramref name="amount"/> in.
    ///
    /// Moving the corner itself by that amount is the obvious thing and is wrong: on a right
    /// angle it only brings each edge in by 0.71 mm of the millimetre asked for, because the
    /// corner travels along the bisector while the edges answer to their own normals. The corner
    /// has to go further, by exactly the cosine of half the turn - and on a needle-sharp corner
    /// that runs away to infinity, so it is capped, and such a corner comes out a little blunt.
    /// </summary>
    private static Vector2 Miter(Vector2 first, Vector2 second, float amount)
    {
        var sum = first + second;
        if (sum.LengthSquared() < 1e-12f) return Vector2.Zero;

        var bisector = Vector2.Normalize(sum);
        var reference = first.LengthSquared() > 1e-12f ? first : second;

        float cosine = Math.Max(Vector2.Dot(bisector, reference), MiterLimit);
        return bisector * (amount / cosine);
    }

    /// <summary>The inward normal of an edge, for a loop wound anticlockwise.</summary>
    private static Vector2 Inward(Vector2 edge)
    {
        if (edge.LengthSquared() < 1e-12f) return Vector2.Zero;

        var unit = Vector2.Normalize(edge);
        return new Vector2(-unit.Y, unit.X);
    }

    private static IReadOnlyList<Vector2> Wound(IReadOnlyList<Vector2> loop, bool anticlockwise)
    {
        if (Polygon2.SignedArea(loop) > 0 == anticlockwise) return loop;

        var flipped = new List<Vector2>(loop);
        flipped.Reverse();
        return flipped;
    }

    private static Vector3 Flat(Vector2 uv, float height) => new(uv.X, uv.Y, height);

    /// <summary>
    /// Lays the flat solid onto the surface, breaking up whatever would not follow it.
    ///
    /// Only the steps that actually stray get broken up. Splitting everything was simpler and is
    /// what this did first; the trouble is that it quadruples the whole solid to fix a handful of
    /// edges, and one short word round a barrel came out at a hundred and fifty thousand
    /// triangles and took twenty-five seconds to cut. Nothing bends along a barrel's axis, so
    /// those steps were being split for no reason at all.
    /// </summary>
    private static Mesh Warp(Mesh flat, IPlacementSurface surface)
    {
        var mesh = MeshSubdivision.SplitEdgesWhere(flat, Strays, maxTriangles: WrapTriangleBudget);

        var points = mesh.Positions
            .Select(p => surface.At(new Vector2(p.X, p.Y), p.Z))
            .ToList();

        return new Mesh(points, mesh.Indices).Welded();

        // Only the spread across the lettering is laid on the surface; its thickness is not, so
        // the step is measured in the layout and the height ignored.
        bool Strays(Vector3 a, Vector3 b) =>
            surface.Sag(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) > SagToleranceMm;
    }
}
