using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry.Engraving;

/// <param name="Mesh">The engraved mesh.</param>
/// <param name="Grooves">How many groove rectangles the pattern produced.</param>
/// <param name="CutterTriangles">Size of the solid that was subtracted, for reporting.</param>
/// <param name="Health">What the result came out like.</param>
/// <param name="Used">
/// The settings that actually cut it, which are not always the ones asked for: a cut that will
/// not go through is retried from a slightly different pattern, and the caller has to be able to
/// say so rather than quietly showing one thing and building another.
/// </param>
public readonly record struct EngraveResult(
    Mesh Mesh, int Grooves, int CutterTriangles, MeshHealth Health, EngraveOptions Used)
{
    /// <summary>
    /// Whether the result is worth keeping. A pattern cut into a facet of a curved surface can
    /// come back torn - the cutter is a thicket of thin walls in a slab a fraction of a
    /// millimetre deep, and the boolean does not always survive it - and handing that to someone
    /// as a finished model is worse than not cutting it.
    /// </summary>
    public bool IsPrintable => Health.IsWatertight && Mesh.TriangleCount > 0;
}

/// <summary>
/// Cuts a pattern into one flat face of a mesh.
///
/// The whole operation is a single subtraction: build the grooves as one solid standing on the
/// face, and take it away. Nothing here re-implements geometry the boolean engine already does,
/// which is why the result comes back watertight and repaired for free.
///
/// The pattern is confined to the face's own rectangle in 2D, before any of it becomes geometry.
/// Trimming it afterwards with a second boolean - intersecting the cutter with a prism of the
/// face - reads better and handles odd outlines exactly, but it does not survive contact with
/// the BSP engine: the intersection leaves slivers along every trim edge, and although the
/// trimmed cutter passes a watertightness check itself, subtracting it tears the result open.
/// Clipping rectangles costs one line and cannot produce a sliver at all.
///
/// Cutting it in two passes - the courses, then the perpends - was tried and is worse, which is
/// worth writing down because it sounds as though it ought to help. Each pass is then a set of
/// separate slabs rather than one connected web, but the perpends run through the courses, so
/// the second pass arrives with its floor in the same plane as the first pass's floor, and a
/// shared plane is precisely what tears. Measured on sixty-four walls at awkward sizes: one pass
/// tore four, two passes at the same depth tore sixteen of the thirty-eight that got that far.
/// Giving the second pass a slightly deeper floor cures the shared plane and replaces it with a
/// paper-thin ledge at every crossing - so much extra geometry that the sweep had not finished
/// after twenty minutes, against two for one pass.
///
/// Raising the pattern instead of cutting it does help, and for the reason two passes did not:
/// the bricks are genuinely separate, with mortar between them, so there is no web to begin
/// with. Same sweep, raised: two of sixty-four.
/// </summary>
public static class Engraver
{
    /// <summary>
    /// How far the pattern runs past the edge of the face, so a groove reaching the boundary
    /// breaks through it cleanly instead of leaving a wall a hair thick.
    ///
    /// On anything convex the overshoot hangs in mid-air and removes nothing. Where a face meets
    /// another at an inside corner it does trim this much off the neighbour, which is why it is
    /// a fraction of a millimetre rather than the width of a groove.
    /// </summary>
    public const float EdgeOvershoot = 0.5f;

    /// <summary>
    /// What to change when a cut comes out torn: how far to slide the pattern, and by how much to
    /// stretch it.
    ///
    /// Sliding alone is not enough, and the reason is worth writing down. A cut tears where two
    /// faces meet exactly instead of crossing, and that can happen two ways. Against the model -
    /// a groove landing on a notch an earlier pattern left - is cured by sliding, since the two
    /// move apart. Against itself is not: a perpend joint touching the corner of another leaves
    /// the material between them pinched to nothing along a line, and sliding the whole pattern
    /// carries that coincidence along with it, unchanged, however far it goes.
    ///
    /// So the later attempts stretch the pattern a little as well, which is the only thing that
    /// moves its own parts relative to each other.
    ///
    /// The distances are measured in groove widths, not millimetres, and that matters as much as
    /// the stretching. A pattern repeats every couple of millimetres, and so do the notches left
    /// by the pattern before it - they are the same pattern, on the face next door. Sliding by a
    /// few microns against something that comes round again every two millimetres lands in very
    /// nearly the same place; escaping it takes a fair fraction of a groove. A tenth of a
    /// millimetre on a five millimetre brick is not something anyone will see, and the tool says
    /// which settings it actually used.
    /// </summary>
    private static readonly (float Across, float Along, float Size, float Width)[] Nudges =
    [
        (0f, 0f, 1f, 1f),
        (0.41f, 0.23f, 1f, 1f),
        (-0.67f, 0.44f, 1.004f, 1f),
        (1.29f, -0.83f, 0.997f, 1.011f),
        (-1.73f, 1.31f, 1.009f, 0.986f)
    ];

    /// <summary>
    /// Runs the whole operation. Blocks while a big-stack thread does the boolean work, so a
    /// caller on the UI thread must wrap this in Task.Run.
    /// </summary>
    public static EngraveResult Engrave(Mesh mesh, FacePatch face, EngraveOptions options,
                                       CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        options = options.Sane();

        EngraveResult? best = null;

        foreach (var (across, along, size, width) in Nudges)
        {
            token.ThrowIfCancellationRequested();

            var moved = options with
            {
                OffsetU = options.OffsetU + across * options.GrooveWidth,
                OffsetV = options.OffsetV + along * options.GrooveWidth,
                Size = options.Size * size,
                GrooveWidth = options.GrooveWidth * width
            };

            var pattern = Pattern(face, moved);
            if (pattern.IsEmpty) return Nothing(mesh, moved);

            var solid = moved.Raised
                ? GrooveSolid.Raised(pattern, face, moved.Depth)
                : GrooveSolid.Build(pattern, face, moved.Depth);

            if (solid.TriangleCount == 0) return Nothing(mesh, moved, pattern.Count);

            // A raised pattern on a plain flat face needs no boolean at all - the face is
            // simply retiled around it, and comes back watertight by construction. Everything
            // else, and anything the retiler declines, goes through the engine.
            var retiled = moved.Raised
                ? FaceRelief.Apply(mesh, face, RaisedArea(face), pattern, moved.Depth)
                : null;

            // Repair before judging it. The automatic pass inside the boolean handles the
            // ordinary leftovers; this catches the rest, and declines when it cannot help, so
            // what is measured here is the best this attempt is going to get.
            var worked = retiled ?? (moved.Raised
                ? CsgSolid.Union(mesh, solid, token: token)
                : CsgSolid.Subtract(mesh, solid, token: token));

            var cut = MeshHealer.Heal(worked, token: token).Mesh;
            var result = new EngraveResult(
                cut, pattern.Count, solid.TriangleCount, cut.CheckHealth(), moved);

            if (result.IsPrintable) return result;

            // Kept so a run that never succeeds still says what went wrong.
            best ??= result;
        }

        return best!.Value;
    }

    private static EngraveResult Nothing(Mesh mesh, EngraveOptions options, int grooves = 0) =>
        new(mesh, grooves, 0, mesh.CheckHealth(), options);

    /// <summary>
    /// The rectangle the pattern covers: the face's own extent, grown by the overshoot.
    ///
    /// A face that is not a rectangle - a gable end, a round cap - is covered corner to corner,
    /// and the parts of the cutter hanging past the real outline simply meet no material. The
    /// pattern therefore stops exactly on the sloping edge without anything having to clip it.
    /// </summary>
    public static Rect2 PatternArea(FacePatch face) => new(
        face.Min.X - EdgeOvershoot, face.Min.Y - EdgeOvershoot,
        face.Max.X + EdgeOvershoot, face.Max.Y + EdgeOvershoot);

    /// <summary>
    /// The rectangle a raised pattern covers: the face itself, with nothing added.
    ///
    /// A cutter may overshoot because the part hanging past the outline meets no material and
    /// removes nothing. Something raised has no such licence - what hangs past the edge is real
    /// and would be left standing in mid-air.
    /// </summary>
    public static Rect2 RaisedArea(FacePatch face) => new(
        face.Min.X + RaisedInset, face.Min.Y + RaisedInset,
        face.Max.X - RaisedInset, face.Max.Y - RaisedInset);

    /// <summary>
    /// How far a raised pattern stops short of the edge of its face.
    ///
    /// A brick reaching exactly to the corner has its side in the plane of the face next door,
    /// and once that face is patterned too, its bricks arrive to meet it exactly - which is the
    /// one thing to avoid.
    ///
    /// It is also what the face is retiled around: the strip it leaves is the frame joining
    /// the pattern to the face's own outline, so it has to be there and has to be positive. A
    /// twentieth of a millimetre is enough for both jobs and is not something anyone will find
    /// on a printed wall.
    /// </summary>
    public const float RaisedInset = GrooveSolid.Sink;

    /// <summary>
    /// The grooves for a face, held inside its rectangle. The preview draws these, and the cutter
    /// is built from exactly the same set, so what is shown is what gets cut.
    /// </summary>
    /// <summary>
    /// What the pattern puts on the face: the joints to cut away, or the bricks to stand proud.
    /// </summary>
    public static GrooveSet Pattern(FacePatch face, EngraveOptions options)
    {
        if (!options.Raised) return Grooves(face, options);

        var area = RaisedArea(face);
        var raised = GroovePattern.Raised(options.Sane(), area);

        // A piece the edge has cut down to a sliver is thrown away rather than kept. It would be
        // a wall a few hundredths of a millimetre thick standing on the face - nothing anyone
        // could print or see, and exactly the sort of thing the boolean comes apart on.
        float least = options.Sane().GrooveWidth;

        return raised with
        {
            Rectangles = raised.Rectangles
                .Select(piece => piece.ClippedTo(area))
                .Where(piece => !piece.IsEmpty
                             && piece.MaxU - piece.MinU >= least
                             && piece.MaxV - piece.MinV >= least)
                .ToList()
        };
    }

    public static GrooveSet Grooves(FacePatch face, EngraveOptions options)
    {
        var area = PatternArea(face);
        var set = GroovePattern.Build(options.Sane(), area);

        // Ribbons are already generated inside the area; only the rectangles run past it.
        return set with
        {
            Rectangles = set.Rectangles
                .Select(groove => groove.ClippedTo(area))
                .Where(groove => !groove.IsEmpty)
                .ToList()
        };
    }

    /// <summary>How many grooves the pattern would cut, without cutting them.</summary>
    public static int CountGrooves(FacePatch face, EngraveOptions options) =>
        options.Raised
            ? GroovePattern.Raised(options.Sane(), RaisedArea(face)).Count
            : GroovePattern.Count(options, PatternArea(face));

    /// <summary>
    /// How much material stands behind the face, so the dialog can say when a depth would cut
    /// clean through. Measured along the face normal across the whole mesh, which is exact for a
    /// wall and a safe lower bound for anything shaped more interestingly.
    /// </summary>
    public static float MaterialBehind(Mesh mesh, FacePatch face)
    {
        if (mesh.VertexCount == 0) return 0;

        float surface = Vector3.Dot(face.Normal, face.Origin);
        float deepest = mesh.Positions.Min(p => Vector3.Dot(face.Normal, p));

        return Math.Max(surface - deepest, 0);
    }
}
