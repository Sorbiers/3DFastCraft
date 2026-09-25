using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Builds a raised pattern into a flat face directly, instead of standing it up as a separate
/// solid and unioning the two.
///
/// The boolean was never doing anything clever here. A brick sits on a flat face, inside its
/// outline, not touching its neighbours; the answer is known before any tree is built. What the
/// boolean adds is its failure mode - every plane in the pattern is infinite, so it slices
/// geometry on the far side of the model that has nothing to do with this face, and a face split
/// by a plane its neighbour missed is a face that no longer shares an edge. On a box that is
/// survivable three times over. The fourth wall meets three walls' worth of that damage and
/// comes apart, which is exactly the wall people notice, because a house has four.
///
/// So the face is retiled instead. Its own triangles are thrown away and replaced by: a frame
/// joining the original outline to the patterned area, the pattern laid across that area at two
/// levels, and a wall wherever the two levels meet. Every vertex on the outline is one the
/// surrounding mesh already had, so nothing else in the model is touched, let alone split. The
/// result is watertight by construction rather than by repair, and there is no fourth wall
/// problem because there is no accumulation.
///
/// The area itself is decomposed one of two ways. Rectangles go through a grid of cells, which
/// is the cheaper of the two and is what brick and siding need; anything with a curve in it -
/// grain, a ring, an imported drawing - goes through <see cref="FaceTiling"/> instead. Both hand
/// back the same pair of things: the tiles, and the points along the four sides of the area
/// where the frame has to meet them.
///
/// It only applies to a face that really is a filled rectangle - which a box's wall is, before
/// and after being patterned. Anything else hands the work back and takes the boolean.
/// </summary>
public static class FaceRelief
{
    /// <summary>
    /// Above this the retiling is finer than the boolean would have been and no longer worth it.
    /// A wall of five-millimetre bricks is a couple of thousand.
    /// </summary>
    public const int MaximumCells = 200_000;

    /// <summary>How far off the face's rectangle a boundary vertex may sit and still be on it.</summary>
    private const float OnEdge = 1e-3f;

    /// <summary>
    /// The mesh with <paramref name="pattern"/> standing <paramref name="rise"/> proud of
    /// <paramref name="face"/>, or null when this face is not one that can be retiled.
    /// </summary>
    /// <param name="area">
    /// The rectangle the pattern is confined to. It must leave a margin inside the face, since
    /// that margin becomes the frame that keeps the original outline intact.
    /// </param>
    public static Mesh? Apply(
        Mesh mesh, FacePatch face, Rect2 area, GrooveSet pattern, float rise)
    {
        if (!Ready(mesh, face, area, rise, out var sides, out var holes)) return null;
        if (!TryPattern(face, area, pattern, holes, rise, out var inside, out var inner)) return null;

        return Assemble(mesh, face, sides, holes, inner, inside);
    }

    /// <summary>
    /// The same for outlines that are not a pattern at all - lettering, or a drawing read in
    /// from a file. They must not overlap each other and must fit inside
    /// <paramref name="area"/>; anything else is handed back for the boolean to deal with.
    /// </summary>
    public static Mesh? Apply(
        Mesh mesh, FacePatch face, Rect2 area,
        IReadOnlyList<IReadOnlyList<Vector2>> outlines, float rise)
    {
        if (!Ready(mesh, face, area, rise, out var sides, out var holes)) return null;
        if (face.Fences.Count > 0 || holes.Count > 0) return null;
        if (!FaceTiling.TryFill(face, area, outlines, rise, out var inside, out var inner)) return null;

        return Assemble(mesh, face, sides, holes, inner, inside);
    }

    /// <summary>
    /// A window or a door already cut through the wall: a rectangular hole in the face, with the
    /// coordinates its own boundary vertices sit on so the retiling can land on them exactly.
    /// </summary>
    private readonly record struct Hole(Rect2 Bounds, List<float> Us, List<float> Vs);

    /// <summary>Whether this is a face that can be retiled, its outline, and any holes in it.</summary>
    private static bool Ready(
        Mesh mesh, FacePatch face, Rect2 area, float rise,
        out List<int>[] sides, out List<Hole> holes)
    {
        sides = [];
        holes = [];

        if (rise <= 0 || area.IsEmpty || !ReferenceEquals(face.Mesh, mesh)) return false;
        if (!TryLoops(face, out var loops)) return false;
        if (!TryHoles(face, area, loops, out var outer, out holes)) return false;
        if (!FillsItsRectangle(face, holes)) return false;

        return TrySides(face, outer, out sides);
    }

    private static Mesh Assemble(
        Mesh mesh, FacePatch face, List<int>[] sides, List<Hole> holes,
        List<Vector2>[] inner, Mesh inside)
    {
        var built = WithoutFace(mesh, face);
        AddFrame(built, mesh, face, sides, inner);
        Add(built, inside);

        return built.Welded();
    }

    /// <summary>
    /// Whether the face fills its own bounding rectangle. A gable end or a round cap does not,
    /// and retiling one would flatten the part of the outline the rectangle does not follow.
    /// </summary>
    private static bool FillsItsRectangle(FacePatch face, List<Hole> holes)
    {
        var size = face.Size;
        if (size.X <= 0 || size.Y <= 0) return false;

        float flat = size.X * size.Y;
        float missing = holes.Sum(h => h.Bounds.Width * h.Bounds.Height);

        return Math.Abs(face.Area - (flat - missing)) <= flat * 1e-3f;
    }

    // --- Which decomposition -----------------------------------------------------------------

    private static bool TryPattern(
        FacePatch face, Rect2 area, GrooveSet pattern, List<Hole> holes, float rise,
        out Mesh inside, out List<Vector2>[] inner)
    {
        if (pattern.Ribbons.Count == 0)
            return TryCells(face, area, pattern.Rectangles, holes, rise, out inside, out inner);

        inside = new Mesh();
        inner = [];

        // A curve laid round a window is a harder question than a grid of cells, and not one the
        // house needed answering.
        if (holes.Count > 0) return false;

        // A fence is a line the pattern must stop short of, and the cell grid clears the cells
        // that cross one. Trapezoids have no such notion, so a face carrying fences - a cylinder
        // facet, a wedge's slope - is handed back rather than patterned over the edge.
        if (face.Fences.Count > 0) return false;

        var shapes = pattern.Ribbons.SelectMany(FaceTiling.Outline).ToList();
        shapes.AddRange(pattern.Rectangles.Where(piece => !piece.IsEmpty).Select(Corners));

        return FaceTiling.TryFill(face, area, shapes, rise, out inside, out inner);
    }

    private static List<Vector2> Corners(Rect2 piece) =>
    [
        new(piece.MinU, piece.MinV), new(piece.MaxU, piece.MinV),
        new(piece.MaxU, piece.MaxV), new(piece.MinU, piece.MaxV)
    ];

    /// <summary>
    /// The rectangle decomposition: a grid whose lines are the pattern's own edges, with a quad
    /// per cell at whichever level that cell belongs to and a wall where the two meet.
    /// </summary>
    private static bool TryCells(
        FacePatch face, Rect2 area, IReadOnlyList<Rect2> rectangles, List<Hole> holes, float rise,
        out Mesh inside, out List<Vector2>[] inner)
    {
        inside = new Mesh();
        inner = [];

        var pieces = rectangles.Where(piece => !piece.IsEmpty).ToList();
        if (pieces.Count == 0) return false;

        // The area's own edges join the pattern's, and so do every hole's, so the grid reaches
        // both the frame and the window reveals exactly.
        var spanning = new List<Rect2>(pieces) { area };
        spanning.AddRange(holes.Select(h => h.Bounds));

        float[] us = GrooveSolid.Coordinates(spanning, r => r.MinU, r => r.MaxU, holes.SelectMany(h => h.Us));
        float[] vs = GrooveSolid.Coordinates(spanning, r => r.MinV, r => r.MaxV, holes.SelectMany(h => h.Vs));

        if (us.Length < 2 || vs.Length < 2) return false;
        if ((long)(us.Length - 1) * (vs.Length - 1) > MaximumCells) return false;

        bool[,] covered = GrooveSolid.CoverCells(pieces, us, vs);
        GrooveSolid.Fence(covered, us, vs, face);

        // Where a window is there is no face to lay at all - the reveal the boolean already cut
        // is the surface there, and anything laid over it would close the opening.
        bool[,] empty = GrooveSolid.CoverCells(holes.Select(h => h.Bounds).ToList(), us, vs);

        AddCells(inside, face, us, vs, covered, empty, rise);

        inner =
        [
            us.Select(u => new Vector2(u, vs[0])).ToList(),
            vs.Select(v => new Vector2(us[^1], v)).ToList(),
            us.Reverse().Select(u => new Vector2(u, vs[^1])).ToList(),
            vs.Reverse().Select(v => new Vector2(us[0], v)).ToList()
        ];

        return true;
    }

    // --- The outline the surrounding mesh shares -----------------------------------------

    /// <summary>
    /// Every boundary loop of the face, in winding order.
    ///
    /// A plain wall has one. A wall with windows cut through it has one for the outline and one
    /// more for each opening, and the openings are what used to make this hand the face back.
    /// </summary>
    private static bool TryLoops(FacePatch face, out List<List<int>> loops)
    {
        loops = [];

        var next = new Dictionary<int, int>(face.Boundary.Count);
        foreach (var (a, b) in face.Boundary)
            if (!next.TryAdd(a, b)) return false; // the outline pinches on itself

        if (next.Count == 0) return false;

        var walked = new HashSet<int>();

        foreach (var (from, _) in face.Boundary)
        {
            if (walked.Contains(from)) continue;

            var loop = new List<int>();
            int at = from;

            do
            {
                loop.Add(at);
                walked.Add(at);
                if (!next.TryGetValue(at, out at)) return false;
            }
            while (at != from && loop.Count <= next.Count);

            if (at != from) return false;
            loops.Add(loop);
        }

        return loops.Count > 0;
    }

    /// <summary>
    /// Sorts the loops into the outline and the holes.
    ///
    /// A hole has to be a rectangle standing clear inside the patterned area: the tiling lays
    /// cells on a grid, and a hole whose edges do not fall on grid lines cannot be left empty
    /// exactly. Windows and doors are rectangles, which is the case worth having. Anything else
    /// is handed back.
    /// </summary>
    private static bool TryHoles(
        FacePatch face, Rect2 area, List<List<int>> loops, out List<int> outer, out List<Hole> holes)
    {
        outer = [];
        holes = [];

        // The outline is the loop that runs round everything else.
        var uv = loops.Select(l => l.Select(v => face.ToUv(face.Mesh.Positions[v])).ToList()).ToList();
        int widest = 0;

        for (int i = 1; i < uv.Count; i++)
            if (MathF.Abs(Polygon2.SignedArea(uv[i])) > MathF.Abs(Polygon2.SignedArea(uv[widest])))
                widest = i;

        outer = loops[widest];

        for (int i = 0; i < loops.Count; i++)
        {
            if (i == widest) continue;

            var points = uv[i];
            var bounds = new Rect2(
                points.Min(p => p.X), points.Min(p => p.Y),
                points.Max(p => p.X), points.Max(p => p.Y));

            if (bounds.IsEmpty) return false;

            // Rectangular, and clear of the frame ring by a whisker.
            foreach (var p in points)
                if (!OnRectangleEdge(p, bounds)) return false;

            if (bounds.MinU < area.MinU + OnEdge || bounds.MaxU > area.MaxU - OnEdge ||
                bounds.MinV < area.MinV + OnEdge || bounds.MaxV > area.MaxV - OnEdge)
                return false;

            // The coordinates its own vertices stand on, so the cells meet them exactly rather
            // than leaving a vertex partway along an edge with nothing on the other side of it.
            holes.Add(new Hole(
                bounds,
                points.Where(p => MathF.Abs(p.Y - bounds.MinV) < OnEdge || MathF.Abs(p.Y - bounds.MaxV) < OnEdge)
                      .Select(p => p.X).ToList(),
                points.Where(p => MathF.Abs(p.X - bounds.MinU) < OnEdge || MathF.Abs(p.X - bounds.MaxU) < OnEdge)
                      .Select(p => p.Y).ToList()));
        }

        return true;
    }

    private static bool OnRectangleEdge(Vector2 p, Rect2 r) =>
        MathF.Abs(p.X - r.MinU) < OnEdge || MathF.Abs(p.X - r.MaxU) < OnEdge ||
        MathF.Abs(p.Y - r.MinV) < OnEdge || MathF.Abs(p.Y - r.MaxV) < OnEdge;

    /// <summary>
    /// The outline split at the rectangle's four corners, each side kept in winding order and
    /// carrying both its endpoints. False when a corner is missing or the loop wanders off the
    /// rectangle, either of which means this is not the face it looks like.
    /// </summary>
    private static bool TrySides(FacePatch face, List<int> loop, out List<int>[] sides)
    {
        sides = [];

        var corners = new[]
        {
            new Vector2(face.Min.X, face.Min.Y),
            new Vector2(face.Max.X, face.Min.Y),
            new Vector2(face.Max.X, face.Max.Y),
            new Vector2(face.Min.X, face.Max.Y)
        };

        var uv = loop.Select(v => face.ToUv(face.Mesh.Positions[v])).ToList();

        foreach (var point in uv)
            if (!OnRectangle(point, face)) return false;

        var found = new int[4];
        for (int k = 0; k < 4; k++)
        {
            found[k] = uv.FindIndex(p => (p - corners[k]).Length() < OnEdge);
            if (found[k] < 0) return false;
        }

        // Read the loop from its first corner, so the four sides come out in winding order.
        int origin = found[0];
        var order = new int[4];
        for (int k = 0; k < 4; k++)
            order[k] = (found[k] - origin + loop.Count) % loop.Count;

        if (order[1] >= order[2] || order[2] >= order[3]) return false;

        sides = new List<int>[4];
        for (int k = 0; k < 4; k++)
        {
            int from = order[k], to = k == 3 ? loop.Count : order[k + 1];
            var side = new List<int>();

            for (int i = from; i <= to; i++)
                side.Add(loop[(i + origin) % loop.Count]);

            sides[k] = side;
        }

        return true;
    }

    private static bool OnRectangle(Vector2 point, FacePatch face) =>
        point.X >= face.Min.X - OnEdge && point.X <= face.Max.X + OnEdge &&
        point.Y >= face.Min.Y - OnEdge && point.Y <= face.Max.Y + OnEdge &&
        (Math.Abs(point.X - face.Min.X) < OnEdge || Math.Abs(point.X - face.Max.X) < OnEdge ||
         Math.Abs(point.Y - face.Min.Y) < OnEdge || Math.Abs(point.Y - face.Max.Y) < OnEdge);

    // --- Retiling --------------------------------------------------------------------------

    /// <summary>Everything except the face, carried over vertex for vertex.</summary>
    private static Mesh WithoutFace(Mesh mesh, FacePatch face)
    {
        var dropped = new HashSet<int>(face.Triangles);
        var built = new Mesh();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            if (dropped.Contains(t)) continue;

            built.AddTriangle(
                mesh.Positions[mesh.Indices[t]],
                mesh.Positions[mesh.Indices[t + 1]],
                mesh.Positions[mesh.Indices[t + 2]]);
        }

        return built;
    }

    private static void Add(Mesh into, Mesh part)
    {
        for (int t = 0; t + 2 < part.Indices.Count; t += 3)
            into.AddTriangle(
                part.Positions[part.Indices[t]],
                part.Positions[part.Indices[t + 1]],
                part.Positions[part.Indices[t + 2]]);
    }

    /// <summary>
    /// The flat ring between the face's real outline and the patterned area.
    ///
    /// Its outer edge uses the mesh's own vertices, unmoved, which is what keeps the face welded
    /// to its neighbours; its inner edge follows whatever the decomposition asked for, so the
    /// tiles meet it exactly.
    /// </summary>
    private static void AddFrame(
        Mesh built, Mesh mesh, FacePatch face, List<int>[] sides, List<Vector2>[] inner)
    {
        Vector2[] along = [Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY];

        for (int k = 0; k < 4; k++)
            AddStrip(built, mesh, face, sides[k], inner[k], along[k]);
    }

    /// <summary>
    /// Fills the band between two polylines that run the same way, advancing whichever side is
    /// behind the other. Both start and end level, so the strip closes at the corners.
    /// </summary>
    private static void AddStrip(
        Mesh built, Mesh mesh, FacePatch face, List<int> outer, List<Vector2> inner, Vector2 along)
    {
        var outerPoints = outer.Select(v => mesh.Positions[v]).ToList();
        var outerUv = outerPoints.Select(face.ToUv).ToList();
        var innerPoints = inner.Select(p => face.ToLocal(p)).ToList();

        int i = 0, j = 0;

        while (i < outerPoints.Count - 1 || j < inner.Count - 1)
        {
            bool takeOuter =
                j >= inner.Count - 1 ||
                (i < outerPoints.Count - 1 &&
                 Vector2.Dot(outerUv[i + 1], along) <= Vector2.Dot(inner[j + 1], along));

            if (takeOuter)
            {
                built.AddTriangle(outerPoints[i], outerPoints[i + 1], innerPoints[j]);
                i++;
            }
            else
            {
                built.AddTriangle(innerPoints[j], outerPoints[i], innerPoints[j + 1]);
                j++;
            }
        }
    }

    /// <summary>
    /// The patterned area: every cell gets a quad, at the face's own level or a brick's height
    /// above it, and a wall wherever the two levels meet.
    /// </summary>
    private static void AddCells(
        Mesh built, FacePatch face, float[] us, float[] vs, bool[,] covered, bool[,] empty, float rise)
    {
        for (int i = 0; i < us.Length - 1; i++)
        {
            for (int j = 0; j < vs.Length - 1; j++)
            {
                if (empty[i, j]) continue;   // a window: the reveal is the surface here

                var a = new Vector2(us[i], vs[j]);
                var b = new Vector2(us[i + 1], vs[j]);
                var c = new Vector2(us[i + 1], vs[j + 1]);
                var d = new Vector2(us[i], vs[j + 1]);

                bool up = covered[i, j];
                GrooveSolid.AddQuad(built, face, a, b, c, d, up ? rise : 0f);

                if (!up) continue;

                // Walking the cell anticlockwise leaves the brick on the left, which faces the
                // walls outward. Between two raised cells there is no step and so no wall - but
                // a brick standing at the edge of a window needs one, because the reveal below
                // it starts at the face and the brick stands proud of it.
                if (!Raised(covered, empty, i, j - 1)) GrooveSolid.AddWall(built, face, a, b, rise, 0f);
                if (!Raised(covered, empty, i + 1, j)) GrooveSolid.AddWall(built, face, b, c, rise, 0f);
                if (!Raised(covered, empty, i, j + 1)) GrooveSolid.AddWall(built, face, c, d, rise, 0f);
                if (!Raised(covered, empty, i - 1, j)) GrooveSolid.AddWall(built, face, d, a, rise, 0f);
            }
        }
    }

    /// <summary>Whether the neighbour is standing at the same height, so no wall goes between.</summary>
    private static bool Raised(bool[,] covered, bool[,] empty, int i, int j) =>
        GrooveSolid.IsCovered(covered, i, j) && !GrooveSolid.IsCovered(empty, i, j);
}
