using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Cuts lettering into a solid, or raises it off one.
///
/// The whole of this class exists because the boolean is occasionally unlucky rather than wrong.
/// Lettering wrapped round a barrel came back with a single torn edge at one particular size and
/// was perfectly clean a couple of millimetres either side of it: a letter's wall had landed on
/// one of the flats the barrel is really made of, and the two met exactly rather than crossing.
///
/// So a failure is retried from somewhere slightly else - a little further off the surface, and a
/// hundredth of a millimetre along it. Neither is visible and neither changes what is asked for:
/// the depth of the cut is measured from the surface, not from where the cutter starts, and a
/// hundredth of a millimetre is a fortieth of the finest nozzle anyone prints with.
///
/// Retrying is the standard answer to this in a BSP engine and is much the cheaper of the two on
/// offer - the alternative being exact arithmetic throughout.
/// </summary>
public static class TextCutter
{
    /// <summary>
    /// Lettering standing proud of a plain flat face, built into the face rather than unioned
    /// onto it - so it cannot tear, and cannot split anything else in the model.
    ///
    /// It only takes the straightforward case: raised, upright walls, a face that is a filled
    /// rectangle, and every outline clear of its edge. A bevel means sloping walls, which is a
    /// solid rather than a step; anything wrapped is not flat; and lettering that runs off the
    /// edge has to cut into whatever is round the corner. All of those still go to the boolean.
    /// </summary>
    private static Mesh? Retiled(
        Mesh world, IReadOnlyList<TextShape> shapes, IPlacementSurface surface,
        bool raised, float depthMm, float bevelMm)
    {
        if (!raised || bevelMm > 0 || surface is not PlanarSurface flat) return null;

        var face = flat.Face;
        float inset = Engraver.RaisedInset;
        var area = new Rect2(
            face.Min.X + inset, face.Min.Y + inset, face.Max.X - inset, face.Max.Y - inset);

        var outlines = new List<IReadOnlyList<Vector2>>();
        foreach (var shape in shapes)
        {
            outlines.Add(shape.Outline.Select(p => flat.Middle + p).ToList());

            foreach (var hole in shape.Holes)
                outlines.Add(hole.Select(p => flat.Middle + p).ToList());
        }

        var laid = FaceRelief.Apply(world, face, area, outlines, depthMm);
        return laid is not null && laid.CheckHealth().IsWatertight ? laid : null;
    }

    /// <summary>
    /// How far off the surface to stand, as a multiple of the surface's own clearance, and how
    /// far to slide along it in millimetres.
    ///
    /// Deliberately not round numbers or multiples of each other: the point is to land somewhere
    /// else entirely, not to graze the same edge from slightly further away.
    /// </summary>
    private static readonly (float Clear, float Slide)[] Nudges =
        [(1f, 0f), (1.7f, 0.011f), (0.53f, -0.017f), (3.1f, 0.029f)];

    /// <summary>
    /// A coarser weld, tried only when the ordinary result is not printable.
    ///
    /// A letter's wall crossing the model at a shallow angle leaves the boolean with a choice to
    /// make at a scale where floating point has no opinion, and what came back was a pair of
    /// vertices a thousandth of a millimetre apart with four triangles meeting along the hair
    /// between them. Joining those mends it. Joining them everywhere does not: on lettering that
    /// came through cleanly the same weld fuses surfaces that were meant to stay apart and turns
    /// a sound result into a torn one. So it is offered as a candidate and kept only if it is
    /// actually printable, which is the rule every candidate here answers to.
    /// </summary>
    private const float CoarseWeldMm = 0.002f;

    /// <param name="world">The object, in world space.</param>
    /// <param name="raised">Whether the lettering stands off the object rather than sinking in.</param>
    /// <param name="depthMm">How deep it is cut, or how far it stands proud.</param>
    /// <returns>
    /// Null when there was nothing to letter with. Otherwise the best attempt - which the caller
    /// still has to check, because it may be that none of them came out printable.
    /// </returns>
    public static Mesh? Apply(
        Mesh world, IReadOnlyList<TextShape> shapes, IPlacementSurface surface,
        bool raised, float depthMm, float bevelMm = 0)
    {
        if (Retiled(world, shapes, surface, raised, depthMm, bevelMm) is { } laid) return laid;

        Mesh? best = null;

        foreach (var (factor, slide) in Nudges)
        {
            float clear = surface.ClearanceMm * factor;
            var moved = new SurfacePlacement(new Vector2(slide, 0), 0).Apply(shapes);

            var solid = raised
                ? TextSolid.Build(moved, surface, -clear, depthMm, bevelMm)
                : TextSolid.Build(moved, surface, clear, -depthMm, bevelMm);

            if (solid.TriangleCount == 0) return null;

            var cut = raised ? CsgSolid.Union(world, solid) : CsgSolid.Subtract(world, solid);

            foreach (var candidate in new[] { cut, cut.Welded(CoarseWeldMm) })
            {
                var result = MeshHealer.Heal(candidate).Mesh;
                if (result.CheckHealth().IsWatertight) return result;

                // Kept so the caller has something to report on, and so a run that never
                // succeeds still says what went wrong rather than nothing at all.
                best ??= result;
            }
        }

        return best;
    }
}
