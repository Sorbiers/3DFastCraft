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
/// models one; nought lays the tiles flat.
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
        float rise = extent * MathF.Tan(Math.Clamp(courses.SlopeDegrees, 0f, MostSlopeDegrees)
                                        * MathF.PI / 180f);

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
    public static List<Rect2> Pieces(in TileCourses courses, float acrossMm, float upMm)
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
            -course / 2f, course, -upMm / 2f - course, upMm / 2f + course);

        for (int row = 0; row < bottoms.Length; row++)
        {
            float low = bottoms[row] + half, high = bottoms[row] + course - half;

            // Which way the stagger falls has to follow the course's own index, not its place in
            // this list, or the face's size decides the bond.
            int index = (int)MathF.Round((bottoms[row] + course / 2f) / course);
            bool over = courses.Stagger && ((index % 2) + 2) % 2 == 1;

            if (!courses.Ends)
            {
                Keep(made, new Rect2(field.MinU, low, field.MaxU, high), field);
                continue;
            }

            float shift = over ? tile / 2f : 0f;
            float[] starts = ReliefField.Lattice(
                shift, tile, -acrossMm / 2f - tile, acrossMm / 2f + tile);

            foreach (float start in starts)
            {
                if (made.Count >= MostTiles) return made;

                Keep(made, new Rect2(start + half, low, start + tile - half, high), field);
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
    private static void Keep(List<Rect2> made, Rect2 piece, Rect2 field)
    {
        var cut = piece.ClippedTo(field);

        // A thousandth of slack, because a width worked out as one subtraction of two large
        // numbers loses its last bit as the pieces get further from the middle of the face.
        if (cut.Width < LeastMm - 1e-3f || cut.Height < LeastMm - 1e-3f) return;

        made.Add(cut);
    }

    /// <summary>
    /// The solid, ready to be unioned onto the object - or subtracted from it, which
    /// <paramref name="sunk"/> mirrors it about the face for.
    /// </summary>
    public static Mesh Build(
        IPlacementSurface surface, in TileCourses courses, float acrossMm, float upMm,
        bool sunk = false)
    {
        var mesh = new Mesh();

        foreach (var piece in Pieces(courses, acrossMm, upMm))
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

            return turn * (thick + rise * Math.Clamp(along, 0f, 1f));
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
