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
///
/// Retrying was not enough on a curve: a heart wrapped on the default cylinder tore in every one
/// of 32 placements. So the cut goes to Manifold first, which is robust where the BSP engine is
/// unlucky (see <see cref="ManifoldCsg"/>), and the retries are what is left for a PC it cannot
/// run on or a model it will not take.
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
    /// Raised lettering, a raised texture or a plug trimmed to the face it is on, so none of it
    /// stands over a window or a doorway already cut in the face, or past the face's edge.
    /// </summary>
    /// <remarks>
    /// A texture is laid over the face's whole rectangle, and so is lettering placed anywhere on
    /// it. Cut, what falls over an opening cuts only air; raised, it bridged the opening with
    /// bricks. So the solid is kept to the face's own region - its triangles stood up off it -
    /// which leaves any opening out exactly, whatever its shape, and wherever it is: a hole in the
    /// face, or a notch in its edge. Only a flat face: a wrapped one has no single region to stand up.
    /// </remarks>
    public static Mesh OnTheFace(Mesh solid, IPlacementSurface surface, CancellationToken token = default)
    {
        if (surface is not PlanarSurface flat || solid.TriangleCount == 0) return solid;

        var bounds = solid.ComputeBounds();
        float reach = bounds.Diagonal + 1f;
        var region = Region(flat.Face, reach, reach);

        return ManifoldCsg.Intersect(solid, region, token) is { TriangleCount: > 0 } kept && kept.CheckHealth().IsWatertight
            ? kept
            : solid;
    }

    /// <summary>
    /// A raised solid trimmed to what it stands on: the face, or each of the walls it goes round.
    /// </summary>
    /// <param name="depthMm">How far it stands off, at the most.</param>
    public static Mesh KeptOn(Mesh solid, IPlacementSurface surface, float depthMm, CancellationToken token = default) =>
        surface is WallsSurface walls
            ? OnTheWalls(solid, walls.Run, depthMm, token)
            : OnTheFace(solid, surface, token);

    /// <summary>
    /// A raised texture laid round the walls trimmed to them, so none of it stands over a window or
    /// a doorway: each wall's own face stood up off itself as <see cref="OnTheFace"/> does, and at
    /// each outside corner inside the strip the wedge between the two walls' regions, which the
    /// texture goes round and neither wall's region reaches.
    /// </summary>
    /// <param name="depthMm">How far the texture stands off the walls, at the most.</param>
    public static Mesh OnTheWalls(Mesh solid, WallRun run, float depthMm, CancellationToken token = default)
    {
        if (solid.TriangleCount == 0) return solid;

        var loop = run.Loop;
        float above = depthMm + 1f;
        var regions = new List<Mesh>();
        int n = loop.Count, k = 0;

        foreach (int i in run.Walls)
        {
            regions.Add(Region(run.Faces[k++], 1f, above));

            // The outside of a corner, where the offsets of the two walls leave a gap. The strip's
            // own ends stop square at the edge of the end wall and leave none.
            if (i == run.First && !run.Closed) continue;

            var (before, outBefore) = loop.WallAt((i + n - 1) % n);
            var (along, outward) = loop.WallAt(i);
            if (before.X * along.Y - before.Y * along.X > 1e-3f)
            {
                var a = loop.CornerAt(i);
                var wedge = new List<Vector2> { a, a + outBefore * above, loop.At(loop.ArcOfCorner(i), above), a + outward * above };
                regions.Add(PrismOf(wedge, run.Low, run.High));
            }
        }

        var region = ManifoldCsg.UnionAll(regions, token);
        return region is { TriangleCount: > 0 } && ManifoldCsg.Intersect(solid, region, token) is { TriangleCount: > 0 } kept
               && kept.CheckHealth().IsWatertight
            ? kept
            : solid;
    }

    /// <summary>A prism standing on a plan between two heights, wound outward.</summary>
    private static Mesh PrismOf(List<Vector2> plan, float low, float high)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        int n = plan.Count;

        // Wound so the plan is anticlockwise, which puts the faces outward.
        float area = 0;
        for (int i = 0; i < n; i++) area += plan[i].X * plan[(i + 1) % n].Y - plan[(i + 1) % n].X * plan[i].Y;
        if (area < 0) plan = [.. plan.AsEnumerable().Reverse()];

        foreach (var p in plan) positions.Add(new Vector3(p.X, p.Y, low));
        foreach (var p in plan) positions.Add(new Vector3(p.X, p.Y, high));

        for (int i = 1; i + 1 < n; i++)
        {
            indices.AddRange([0, i + 1, i]);
            indices.AddRange([n, n + i, n + i + 1]);
        }

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            indices.AddRange([i, j, n + j, i, n + j, n + i]);
        }

        return new Mesh(positions, indices);
    }

    /// <summary>The face stood up off itself, <paramref name="below"/> into the part and <paramref name="above"/> out of it.</summary>
    private static Mesh Region(FacePatch face, float below, float above)
    {
        var mesh = face.Mesh;
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var low = new Dictionary<int, int>();
        var high = new Dictionary<int, int>();

        // Onto the face's plane first: a face found as "flat enough" is not always flat to the
        // last digit, and the region's floor and roof have to be.
        int At(Dictionary<int, int> map, int v, float height)
        {
            if (map.TryGetValue(v, out int at)) return at;
            at = positions.Count;
            positions.Add(face.ToLocal(face.ToUv(mesh.Positions[v]), height));
            map[v] = at;
            return at;
        }

        foreach (int t in face.Triangles)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            indices.AddRange([At(high, a, above), At(high, b, above), At(high, c, above)]);
            indices.AddRange([At(low, a, -below), At(low, c, -below), At(low, b, -below)]);
        }

        foreach (var (a, b) in face.Boundary)
        {
            int ab = At(low, a, -below), bb = At(low, b, -below), bt = At(high, b, above), at = At(high, a, above);
            indices.AddRange([ab, bb, bt, ab, bt, at]);
        }

        return new Mesh(positions, indices);
    }

    /// <param name="Body">The object with the lettering in it.</param>
    /// <param name="Lettering">
    /// The solid that put it there: raised, the letters themselves; cut, the plug that exactly
    /// fills the recess, because it is the very shape the recess was cut with.
    /// </param>
    public readonly record struct Lettered(Mesh Body, Mesh Lettering);

    /// <summary>
    /// The solid the lettering is made of. Raised it starts a clearance inside the object and
    /// stands proud; cut it starts a clearance outside and sinks in.
    /// </summary>
    private static Mesh Solid(
        IReadOnlyList<TextShape> shapes, IPlacementSurface surface,
        bool raised, float depthMm, float bevelMm, float clear) =>
        raised
            ? TextSolid.Build(shapes, surface, -clear, depthMm, bevelMm)
            : TextSolid.Build(shapes, surface, clear, -depthMm, bevelMm);

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
        bool raised, float depthMm, float bevelMm = 0, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (Retiled(world, shapes, surface, raised, depthMm, bevelMm) is { } laid) return laid;

        return Worked(world, shapes, surface, raised, depthMm, bevelMm, token)?.Body;
    }

    /// <summary>
    /// The lettering as a part of its own, and the body that carries it - which is what printing
    /// the letters in a second filament needs, since a slicer assigns a material to a part and a
    /// part is a solid, not a patch of a surface.
    ///
    /// Raised, nothing is cut at all: the letters are the solid that would have been unioned on,
    /// and the body is untouched. They dip the surface's own clearance into it, so the two are in
    /// contact rather than balanced on a shared face, and every slicer takes that.
    ///
    /// Cut, the body is engraved exactly as before and the plug handed back is the very solid
    /// that cut it - including whichever retry finally worked - so the two mate by construction
    /// rather than by two builds agreeing with each other. The plug stands the clearance proud of
    /// the face, which is a hundredth of a millimetre and well under a layer.
    /// </summary>
    public static Lettered? Separate(
        Mesh world, IReadOnlyList<TextShape> shapes, IPlacementSurface surface,
        bool raised, float depthMm, float bevelMm = 0, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (!raised)
        {
            // The plug is the cut's own cutter, kept to the face so it does not stand over an opening.
            var cut = Worked(world, shapes, surface, false, depthMm, bevelMm, token);
            return cut is { } made ? made with { Lettering = KeptOn(made.Lettering, surface, depthMm, token) } : null;
        }

        var letters = KeptOn(Solid(shapes, surface, true, depthMm, bevelMm, surface.ClearanceMm), surface, depthMm, token).Welded();
        return letters.TriangleCount == 0 ? null : new Lettered(world, letters);
    }

    /// <summary>
    /// The boolean and its retries, handing back both what came out and the solid that did it.
    /// </summary>
    private static Lettered? Worked(
        Mesh world, IReadOnlyList<TextShape> shapes, IPlacementSurface surface,
        bool raised, float depthMm, float bevelMm, CancellationToken token)
    {
        var first = Solid(shapes, surface, raised, depthMm, bevelMm, surface.ClearanceMm);
        if (first.TriangleCount == 0) return null;

        // Raised, kept off any opening in the face. Cut is left whole: over an opening it cuts
        // only air, and a groove is meant to run off the face's edge.
        if (raised) first = KeptOn(first, surface, depthMm, token);

        var robust = raised
            ? ManifoldCsg.Union(world, first, token)
            : ManifoldCsg.Subtract(world, first, token);

        if (robust is { TriangleCount: > 0 } && robust.CheckHealth().IsWatertight)
            return new Lettered(robust, first.Welded());

        Lettered? best = null;

        foreach (var (factor, slide) in Nudges)
        {
            token.ThrowIfCancellationRequested();

            var moved = new SurfacePlacement(new Vector2(slide, 0), 0).Apply(shapes);
            var solid = Solid(moved, surface, raised, depthMm, bevelMm, surface.ClearanceMm * factor);

            if (solid.TriangleCount == 0) return null;
            if (raised) solid = KeptOn(solid, surface, depthMm, token);

            var cut = raised
                ? CsgSolid.Union(world, solid, token: token)
                : CsgSolid.Subtract(world, solid, token: token);

            foreach (var candidate in new[] { cut, cut.Welded(CoarseWeldMm) })
            {
                var result = MeshHealer.Heal(candidate, token: token).Mesh;
                if (result.CheckHealth().IsWatertight) return new Lettered(result, solid.Welded());

                // Kept so the caller has something to report on, and so a run that never
                // succeeds still says what went wrong rather than nothing at all.
                best ??= new Lettered(result, solid.Welded());
            }
        }

        return best;
    }
}
