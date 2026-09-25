using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <param name="TileMm">Along the course, the middle of one joint to the middle of the next.</param>
/// <param name="CourseMm">Up the face, the same.</param>
/// <param name="ThickMm">
/// How thick the slab is, which is what it stands off the face at its head. A tile is a plate of
/// one thickness, not a wedge: the ramp this replaced tapered every tile to nothing at its head,
/// which is a knife edge no printer will lay and a joint that vanishes where it matters most.
/// </param>
/// <param name="JointMm">The gap left between one slab and the next.</param>
/// <param name="SlopeDegrees">
/// How far each slab is tilted. Four degrees is what reads as a lapped roof at the sizes anyone
/// models one; nought lays the tiles flat. Negative lifts the other edge - the head of a course
/// rather than its tail, the near end of a piece rather than its far one - which is the same
/// tiling seen from the other side of the roof.
/// </param>
/// <param name="Slope">Which way that tilt runs.</param>
/// <param name="Stagger">Whether alternate courses are set over by half a tile.</param>
/// <param name="Ends">
/// Whether a course is cut into tiles at all. Siding is one strip the whole way across - having no
/// ends to stagger is exactly what makes siding siding rather than tiling.
/// </param>
public readonly record struct TileCourses(
    float TileMm, float CourseMm, float ThickMm, float JointMm,
    float SlopeDegrees = 4f, TileSlope Slope = TileSlope.Roll,
    bool Stagger = true, bool Ends = true);

/// <summary>
/// What of the face a tile is allowed to stand on: the openings cut through it, and its outline.
///
/// A window is a hole in the wall, not a hole in the tiling, so a course running across one has to
/// be broken at the reveal. Subtracting a tool from the object would have got that for free, which
/// is the fair case against building the geometry; building it means saying so. What building it
/// buys in return is the rest of this file - no boolean per tile, and no sampling.
///
/// An opening is cleared by its bounding rectangle. A window is a rectangle, and where one is not,
/// clearing its box leaves a little more bare wall rather than a tile hanging over an opening,
/// which is the right way round to be wrong.
/// </summary>
public sealed class TileRoom
{
    /// <summary>
    /// How far clear of an opening a raised tile stops, so it does not butt the reveal.
    ///
    /// A cutter is given the negative of the engraver's overshoot instead, and the difference is
    /// the whole of why a cut used to tear. Stopping short of an opening leaves the material
    /// between the cut and the reveal standing as a rib a fifth of a millimetre wide and as deep
    /// as the cut - a seven to one knife edge the boolean cannot resolve. Running past it instead
    /// takes a hair off the reveal, which nobody will see.
    /// </summary>
    public const float ClearanceMm = 0.2f;

    /// <summary>
    /// How far apart a wrapped piece is tested along its length. Finer than a joint, so a reveal
    /// lands within a joint's width of where it really is.
    /// </summary>
    public const float ScanMm = 0.25f;

    /// <summary>And a cap, so a course right round a big barrel is still tested in a blink.</summary>
    public const int MostScanSteps = 2_000;

    private readonly FacePatch? face;
    private readonly FaceOutline? outline;
    private readonly Vector2 middle;

    private readonly IPlacementSurface? wrapped;
    private readonly SolidLookup? solid;

    /// <summary>
    /// Wrapped round a barrel there is no face patch to ask - the layout goes round the whole
    /// object - so where the wall is has to be asked of the solid itself: a point a hair inside
    /// the nominal surface has material under it if it is inside the mesh.
    ///
    /// That is a ray cast rather than a polygon test, so it is asked along a piece rather than of
    /// the piece as a whole, and a course is broken wherever the support stops. The step is a
    /// quarter of a millimetre, so a reveal can come out that much out of place. On a face the
    /// outline answers exactly and none of this is used.
    /// </summary>
    private TileRoom(IPlacementSurface wrapped, Mesh solid)
    {
        this.wrapped = wrapped;
        this.solid = new SolidLookup(solid);

        Holes = [];
    }

    private TileRoom(FacePatch face, Vector2 middle, float clearanceMm)
    {
        this.face = face;
        this.middle = middle;

        outline = FaceOutline.Of(face);

        var cleared = new List<Rect2>();

        foreach (var (_, holes) in outline.Shapes)
            foreach (var hole in holes)
            {
                if (hole.Count < 3) continue;

                cleared.Add(new Rect2(
                    hole.Min(p => p.X) - middle.X - clearanceMm,
                    hole.Min(p => p.Y) - middle.Y - clearanceMm,
                    hole.Max(p => p.X) - middle.X + clearanceMm,
                    hole.Max(p => p.Y) - middle.Y + clearanceMm));
            }

        Holes = cleared;
    }

    /// <summary>The openings, in the layout's own frame.</summary>
    public IReadOnlyList<Rect2> Holes { get; }

    /// <summary>
    /// The room a laid texture has, or null when it is not going onto a face at all - wrapped
    /// round a barrel there is no patch to ask, so a barrel with a window in it still gets tiled
    /// over. Worth fixing the day anybody puts one there.
    /// </summary>
    public static TileRoom? Of(
        IPlacementSurface? surface, Mesh? solid = null, float clearanceMm = ClearanceMm)
    {
        if (surface is PlanarSurface flat) return new TileRoom(flat.Face, flat.Middle, clearanceMm);

        return surface is not null && solid is not null && solid.TriangleCount > 0
            ? new TileRoom(surface, solid)
            : null;
    }

    /// <summary>
    /// Whether a piece has to be tested along its length rather than judged whole. Wrapped, the
    /// answer comes a point at a time, so a course that crosses an opening is broken at it rather
    /// than thrown away.
    /// </summary>
    public bool Scans => outline is null;

    /// <summary>Whether the whole of this piece has wall under it.</summary>
    public bool Holds(Rect2 piece)
    {
        Vector2[] corners =
        [
            new(piece.MinU, piece.MinV), new(piece.MaxU, piece.MinV),
            new(piece.MaxU, piece.MaxV), new(piece.MinU, piece.MaxV)
        ];

        foreach (var corner in corners)
            if (!Supports(corner))
                return false;

        // And the middle, since wrapped the corners alone would step over an opening narrower
        // than the piece is wide.
        return Supports(new Vector2(
            (piece.MinU + piece.MaxU) / 2f, (piece.MinV + piece.MaxV) / 2f));
    }

    /// <summary>Whether there is material under this point of the layout.</summary>
    public bool Supports(Vector2 at)
    {
        if (outline is not null && face is not null)
        {
            var on = at + middle;
            return outline.Contains(on) && face.Clears(on, 0f);
        }

        // A hair inside the nominal surface, so a point on a wall reads as material and one over
        // an opening does not.
        return solid is not null && wrapped is not null &&
               solid.Contains(wrapped.At(at, -TileSolid.SinkMm / 2f));
    }
}

/// <summary>Which way a piece is tilted.</summary>
public enum TileSlope
{
    /// <summary>
    /// Up the course: the tail stands off the face and the head tucks under the course above,
    /// which is how a roof is laid and how siding laps.
    /// </summary>
    Roll,

    /// <summary>
    /// Along the piece: one end stands off and the other lies down, so a course reads as a row
    /// of shingles leaning the same way rather than as a lapped one.
    /// </summary>
    Pitch
}

/// <summary>
/// Roof tiles and lap siding built as what they are: flat rectangular slabs, each rolled a few
/// degrees so its tail stands further off the face than its head, laid in staggered courses.
///
/// This replaced a sampled height field, and the reason is worth writing down because the field
/// looked like the cheaper answer. A tile is a discrete thing - a rectangle with a plate on it -
/// and a height field is a continuous function read off a grid, so every fault the field had was a
/// sampling fault rather than a tile fault. Two of them shipped. The grid repeated in step with
/// the profile and cancelled the lap out, so tiles came back as flat columns with no courses in
/// them; and a grid line landing exactly on a joint edge sat on the knife edge of the modulo that
/// decides which side of the joint it is on, so half a tile folded along its diagonal. Neither can
/// happen here, because nothing is sampled: a slab is eight corners and twelve triangles.
///
/// It is also a great deal smaller. The field wanted 9,420 triangles on a 77 mm cube at a 10 mm
/// pitch; the same tiles as slabs are about 2,200.
///
/// Every slab is a closed box and no two of them touch - there is a joint between - so the whole
/// field is closed by construction and goes to the boolean as an ordinary solid.
/// </summary>
public static class TileSolid
{
    /// <summary>
    /// How far the footings sit inside the object. Enough that the slabs genuinely cross the face
    /// rather than meeting it, which is the one thing the boolean cannot be asked to do. The same
    /// hair, for the same reason, as <see cref="GrooveSolid.Sink"/>.
    /// </summary>
    public const float SinkMm = 0.3f;

    /// <summary>Below this a slab is not a slab, and the printer will not lay it either.</summary>
    public const float LeastMm = TextureOptions.LeastPadMm;

    /// <summary>Past this the tilt is a fin rather than a tile.</summary>
    public const float MostSlopeDegrees = 45f;

    /// <summary>And a cap, so a silly pitch is refused rather than asking for the memory.</summary>
    public const int MostTiles = 20_000;

    /// <summary>How far a slab's top may chord off a curved surface before it is split.</summary>
    private const float MostSagMm = 0.05f;

    /// <summary>
    /// How much further one edge of this piece stands off the face than the other.
    ///
    /// Measured on the piece rather than on a whole tile, so a tile cut at a verge keeps the plane
    /// of the ones beside it instead of standing up to the same height over a shorter run.
    /// </summary>
    public static float RiseOn(in TileCourses courses, Rect2 piece)
    {
        float extent = courses.Slope == TileSlope.Pitch ? piece.Width : piece.Height;

        // The sign says which edge rises, not how far: taken as read it would drive the low edge
        // below the face and give the slab a negative thickness at one end.
        float rise = extent * MathF.Tan(
            Math.Min(MathF.Abs(courses.SlopeDegrees), MostSlopeDegrees) * MathF.PI / 180f);

        // A piece that rises further than a course is deep is a fin, not a tile - and it is also
        // what a strip of siding pitched a few degrees would otherwise become, since a strip runs
        // the whole way across and a few degrees of that is a wedge the height of the wall.
        return MathF.Min(rise, MathF.Max(courses.CourseMm, 0.1f));
    }

    /// <summary>What a whole piece stands off the face at its furthest, for the panel to report.</summary>
    public static float ReliefOf(in TileCourses courses)
    {
        float extent = MathF.Max(
            courses.Slope == TileSlope.Pitch && courses.Ends
                ? courses.TileMm - courses.JointMm
                : courses.CourseMm - courses.JointMm,
            0.1f);

        return courses.ThickMm + RiseOn(courses, new Rect2(0f, 0f, extent, extent));
    }

    /// <summary>
    /// Where every slab sits on the face.
    ///
    /// Anchored on the middle of the face rather than walked from an edge, so that the courses of
    /// one call and the joints of the next agree about where a course begins - the same rule the
    /// profiles had to learn.
    /// </summary>
    /// <param name="cutting">
    /// Whether these are a cutter rather than something to stand on the face. A cutter may hang
    /// over an edge or an opening - that is the point of it - so it is not asked to sit wholly on
    /// wall the way a raised piece is.
    /// </param>
    /// <param name="shiftMm">
    /// How far the whole pattern is moved over the face, which is what the panel's Across and Up
    /// mean for something that fills the face rather than being stamped on it. It moves the
    /// lattice, not the field: the pieces still stop at the same edges, they are simply cut
    /// differently there.
    /// </param>
    public static List<Rect2> Pieces(
        in TileCourses courses, float acrossMm, float upMm, TileRoom? room = null,
        bool cutting = false, Vector2 shiftMm = default)
    {
        var made = new List<Rect2>();

        float tile = MathF.Max(courses.TileMm, LeastMm + courses.JointMm);
        float course = MathF.Max(courses.CourseMm, LeastMm + courses.JointMm);
        float half = courses.JointMm / 2f;

        if (acrossMm <= 0 || upMm <= 0) return made;

        var field = new Rect2(-acrossMm / 2f, -upMm / 2f, acrossMm / 2f, upMm / 2f);

        // The course boundaries, and one course clear of the face either way so a part course at
        // the edge is a cut tile rather than a gap.
        float[] bottoms = ReliefField.Lattice(
            -course / 2f + shiftMm.Y, course, -upMm / 2f - course, upMm / 2f + course);

        for (int row = 0; row < bottoms.Length; row++)
        {
            float low = bottoms[row] + half, high = bottoms[row] + course - half;

            // Which way the stagger falls has to follow the course's own index, not its place in
            // this list, or the face's size decides the bond.
            int index = (int)MathF.Round((bottoms[row] - shiftMm.Y + course / 2f) / course);
            bool over = courses.Stagger && ((index % 2) + 2) % 2 == 1;

            if (!courses.Ends)
            {
                Keep(made, new Rect2(field.MinU, low, field.MaxU, high), field, room, cutting, half * 2f);
                continue;
            }

            float shift = over ? tile / 2f : 0f;
            float[] starts = ReliefField.Lattice(
                shift + shiftMm.X, tile, -acrossMm / 2f - tile, acrossMm / 2f + tile);

            foreach (float start in starts)
            {
                if (made.Count >= MostTiles) return made;

                Keep(made, new Rect2(start + half, low, start + tile - half, high), field, room, cutting, half * 2f);
            }
        }

        return made;
    }

    /// <summary>
    /// Adds a slab, cut off at the edges of the field and dropped when what is left is too small
    /// to be one.
    ///
    /// Cut off rather than thrown away, because that is what a tiler does at a verge: a course
    /// ends in a cut tile, not in a gap the size of a whole one.
    /// </summary>
    private static void Keep(
        List<Rect2> made, Rect2 piece, Rect2 field, TileRoom? room, bool cutting, float gapMm)
    {
        var cut = piece.ClippedTo(field);
        if (cut.IsEmpty) return;

        if (room is { Scans: true })
        {
            Scan(made, cut, room);
            return;
        }

        List<Rect2> parts = room is null ? [cut] : Without(cut, room.Holes, gapMm);

        foreach (var part in parts)
        {
            // A thousandth of slack, because a width worked out as one subtraction of two large
            // numbers loses its last bit as the pieces get further from the middle of the face.
            if (part.Width < LeastMm - 1e-3f || part.Height < LeastMm - 1e-3f) continue;

            // And the outline itself, which catches a gable end or a face with a corner off it as
            // well as anything the bounding boxes above did not already take out. Not asked of a
            // cutter: one hanging over an opening removes nothing, and one that stopped short of
            // the edge would leave a rib of material standing there.
            if (!cutting && room is not null && !room.Holds(part)) continue;

            made.Add(part);
        }
    }

    /// <summary>
    /// Walks a piece along its length and keeps the stretches that have wall under them.
    ///
    /// For the wrapped case, where the answer comes a point at a time rather than from an outline.
    /// A course crossing a window comes back as the stretch before it and the stretch after, to a
    /// quarter of a millimetre.
    /// </summary>
    private static void Scan(List<Rect2> made, Rect2 piece, TileRoom room)
    {
        int steps = Math.Clamp(
            (int)MathF.Ceiling(piece.Width / TileRoom.ScanMm), 1, TileRoom.MostScanSteps);

        float step = piece.Width / steps;
        int from = -1;

        for (int i = 0; i <= steps; i++)
        {
            bool held = i < steps && room.Holds(new Rect2(
                piece.MinU + i * step, piece.MinV, piece.MinU + (i + 1) * step, piece.MaxV));

            if (held)
            {
                if (from < 0) from = i;
                continue;
            }

            if (from < 0) continue;

            var run = new Rect2(piece.MinU + from * step, piece.MinV, piece.MinU + i * step, piece.MaxV);
            from = -1;

            if (run.Width >= LeastMm - 1e-3f && run.Height >= LeastMm - 1e-3f) made.Add(run);
        }
    }

    /// <summary>
    /// What is left of a piece once the openings are taken out of it: nothing, itself, or the
    /// strips around the hole. A course of siding running across a window comes back as the piece
    /// to its left and the piece to its right, which is what a siding fitter would have.
    /// </summary>
    /// <param name="gapMm">
    /// What to leave between two parts that would otherwise meet. A piece bitten out of one
    /// corner comes back as an upright part beside the opening and a short part over it, and those
    /// two share a wall exactly - which is the one thing the boolean cannot be asked to union, and
    /// which welding turns into an edge with four triangles on it. Round a window it reads as the
    /// butt joint a fitter would leave there anyway.
    /// </param>
    public static List<Rect2> Without(Rect2 piece, IReadOnlyList<Rect2> holes, float gapMm = 0f)
    {
        var parts = new List<Rect2> { piece };

        foreach (var hole in holes)
        {
            var left = new List<Rect2>(parts.Count + 3);

            foreach (var part in parts) Split(part, hole, left, gapMm);

            parts = left;
            if (parts.Count == 0) break;
        }

        return parts;
    }

    private static void Split(Rect2 part, Rect2 hole, List<Rect2> into, float gapMm)
    {
        // Clear of the opening altogether, so it survives whole.
        if (hole.MaxU <= part.MinU || hole.MinU >= part.MaxU ||
            hole.MaxV <= part.MinV || hole.MinV >= part.MaxV)
        {
            into.Add(part);
            return;
        }

        bool beside = hole.MinU > part.MinU, after = hole.MaxU < part.MaxU;

        if (beside) into.Add(new Rect2(part.MinU, part.MinV, hole.MinU, part.MaxV));
        if (after) into.Add(new Rect2(hole.MaxU, part.MinV, part.MaxU, part.MaxV));

        // And the strips above and below, which only span what the two beside it did not - held
        // off them by the gap, since a part that met one of them wall to wall is what tore.
        float low = MathF.Max(part.MinU, hole.MinU) + (beside ? gapMm : 0f);
        float high = MathF.Min(part.MaxU, hole.MaxU) - (after ? gapMm : 0f);

        if (high - low < 1e-4f) return;

        if (hole.MinV > part.MinV) into.Add(new Rect2(low, part.MinV, high, hole.MinV));
        if (hole.MaxV < part.MaxV) into.Add(new Rect2(low, hole.MaxV, high, part.MaxV));
    }

    /// <summary>
    /// The solid, ready to be unioned onto the object - or subtracted from it, which
    /// <paramref name="sunk"/> mirrors it about the face for.
    /// </summary>
    /// <param name="room">
    /// Where there is wall to stand on. Left out, a face works it out for itself from its own
    /// outline; wrapped there is no face to ask, so the caller has to hand over the object.
    /// </param>
    public static Mesh Build(
        IPlacementSurface surface, in TileCourses courses, float acrossMm, float upMm,
        bool sunk = false, TileRoom? room = null, Vector2 shiftMm = default)
    {
        var mesh = new Mesh();

        foreach (var piece in Pieces(
            courses, acrossMm, upMm, room ?? TileRoom.Of(surface), sunk, shiftMm))
            AddSlab(mesh, surface, courses, piece, sunk);

        return mesh.Welded();
    }

    /// <summary>
    /// One slab: a box of one thickness whose top is tilted, standing furthest off the face at
    /// whichever edge <see cref="TileCourses.Slope"/> names.
    /// </summary>
    private static void AddSlab(
        Mesh mesh, IPlacementSurface surface, in TileCourses courses, Rect2 piece, bool sunk)
    {
        float[] us = Spans(surface, piece);

        float thick = MathF.Max(courses.ThickMm, 0.05f);
        float rise = RiseOn(courses, piece);
        bool pitched = courses.Slope == TileSlope.Pitch;
        bool other = courses.SlopeDegrees < 0f;

        // Sunk, the slab is its own mirror about the face: the boolean then takes it away and
        // leaves the tile cut into the surface rather than standing off it.
        float turn = sunk ? -1f : 1f;
        float foot = -turn * SinkMm;

        // A plane over the piece rather than a height per edge. Read at a corner it gives that
        // corner exactly, so nothing here turns on which side of a boundary a coordinate falls -
        // the question that has already cost this tool twice.
        float Height(float u, float v)
        {
            float along = pitched
                ? (piece.Width > 1e-6f ? (u - piece.MinU) / piece.Width : 0f)
                : (piece.Height > 1e-6f ? (piece.MaxV - v) / piece.Height : 0f);

            along = Math.Clamp(along, 0f, 1f);

            return turn * (thick + rise * (other ? 1f - along : along));
        }

        Vector3 Top(float u, float v) => surface.At(new Vector2(u, v), Height(u, v));

        Vector3 Bottom(float u, float v) => surface.At(new Vector2(u, v), foot);

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (sunk)
            {
                // Mirrored, the box is inside out, which a boolean reads as the whole of space
                // except the box. Turning each face round puts its normals back outward.
                mesh.AddTriangle(a, c, b);
                mesh.AddTriangle(a, d, c);
            }
            else
            {
                mesh.AddTriangle(a, b, c);
                mesh.AddTriangle(a, c, d);
            }
        }

        for (int i = 0; i + 1 < us.Length; i++)
        {
            float u0 = us[i], u1 = us[i + 1];

            // Wound anticlockwise seen from outside the face, so the top looks outward.
            Quad(Top(u0, piece.MinV), Top(u1, piece.MinV),
                 Top(u1, piece.MaxV), Top(u0, piece.MaxV));

            Quad(Bottom(u0, piece.MaxV), Bottom(u1, piece.MaxV),
                 Bottom(u1, piece.MinV), Bottom(u0, piece.MinV));

            // The tail wall and the head wall, which follow the split along with the top.
            Quad(Top(u0, piece.MinV), Bottom(u0, piece.MinV),
                 Bottom(u1, piece.MinV), Top(u1, piece.MinV));

            Quad(Top(u1, piece.MaxV), Bottom(u1, piece.MaxV),
                 Bottom(u0, piece.MaxV), Top(u0, piece.MaxV));
        }

        // And the two ends, which do not.
        float left = us[0], right = us[^1];

        Quad(Top(left, piece.MaxV), Bottom(left, piece.MaxV),
             Bottom(left, piece.MinV), Top(left, piece.MinV));

        Quad(Top(right, piece.MinV), Bottom(right, piece.MinV),
             Bottom(right, piece.MaxV), Top(right, piece.MaxV));
    }

    /// <summary>
    /// Where a slab has to be split across its width to follow a curve.
    ///
    /// Flat, a slab is one quad however wide it is. Round a barrel its top is a chord, and a tile
    /// wide enough to matter would stand off the surface in the middle and sink into it at the
    /// ends. Asked of the surface rather than assumed, so a flat face pays nothing.
    /// </summary>
    private static float[] Spans(IPlacementSurface surface, Rect2 piece)
    {
        float middle = (piece.MinV + piece.MaxV) / 2f;
        float sag = surface.Sag(new Vector2(piece.MinU, middle), new Vector2(piece.MaxU, middle));

        int count = Math.Clamp((int)MathF.Ceiling(sag / MostSagMm), 1, 64);
        var us = new float[count + 1];

        for (int i = 0; i <= count; i++) us[i] = piece.MinU + piece.Width * i / count;

        return us;
    }
}
