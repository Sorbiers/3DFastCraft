using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry.Engraving;

/// <param name="Mesh">The engraved mesh.</param>
/// <param name="Grooves">How many groove rectangles the pattern produced.</param>
/// <param name="CutterTriangles">Size of the solid that was subtracted, for reporting.</param>
public readonly record struct EngraveResult(Mesh Mesh, int Grooves, int CutterTriangles);

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
    /// Runs the whole operation. Blocks while a big-stack thread does the boolean work, so a
    /// caller on the UI thread must wrap this in Task.Run.
    /// </summary>
    public static EngraveResult Engrave(Mesh mesh, FacePatch face, EngraveOptions options)
    {
        options = options.Sane();

        var grooves = Grooves(face, options);
        if (grooves.IsEmpty) return new EngraveResult(mesh, 0, 0);

        var cutter = GrooveSolid.Build(grooves, face, options.Depth);
        if (cutter.TriangleCount == 0) return new EngraveResult(mesh, grooves.Count, 0);

        return new EngraveResult(
            CsgSolid.Subtract(mesh, cutter), grooves.Count, cutter.TriangleCount);
    }

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
    /// The grooves for a face, held inside its rectangle. The preview draws these, and the cutter
    /// is built from exactly the same set, so what is shown is what gets cut.
    /// </summary>
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
        GroovePattern.Count(options, PatternArea(face));

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
