using System.Numerics;

namespace FastCraft3D.Geometry.Csg;

/// <summary>
/// A boolean between a large solid and a small tool, done only where the tool can reach.
///
/// The engine is quadratic in the triangle count, and nearly all of that is building a tree over
/// the solid - work that is wasted when the tool is a pour hole or a registration key that could
/// not touch nine tenths of it. So the solid is taken apart along the six walls of the tool's own
/// box, the boolean runs on the small piece in the middle, and the pieces go back together.
///
/// Taking it apart leaves the cuts open on purpose. Both sides of a cut work its ring out from
/// the same edge, the same way round, so the pieces still fit exactly when they are put back;
/// capping them would leave a pair of faces inside the solid at every wall. The piece the
/// boolean is given is the one exception - a boolean needs a closed solid, not a patch - and its
/// lids are dropped again on the way out.
/// </summary>
public static class LocalCsg
{
    /// <summary>
    /// How far beyond the tool the working piece is cut, in millimetres.
    ///
    /// Enough that the tool never reaches a lid: a boolean that altered one would leave a ring
    /// that no longer matches the piece it has to join back onto.
    /// </summary>
    private const float Margin = 1.5f;

    /// <summary>How near a wall a triangle must lie to count as one of the lids.</summary>
    private const float OnWall = 1e-3f;

    /// <summary>
    /// How much of the solid's box the tool may fill and still be worth working locally.
    ///
    /// The saving comes from the boolean seeing a small piece instead of the whole model, and it
    /// is paid for with twelve plane clips. A tool that fills most of the solid saves nothing and
    /// pays anyway.
    /// </summary>
    private const float SmallEnough = 0.3f;

    /// <summary>Below this the whole boolean is quick regardless, and simpler.</summary>
    private const int WorthIt = 5_000;

    /// <summary>
    /// A boolean, worked locally when that will help and wholly when it will not.
    ///
    /// Only subtract and union: both leave everything outside the tool exactly as it was, which is
    /// the whole premise. An intersection does the opposite - outside the tool nothing survives -
    /// so there is no untouched rest to put back, and it goes to the full engine.
    /// </summary>
    public static Mesh Apply(Mesh solid, Mesh tool, BooleanOp op, CancellationToken token = default)
    {
        if (op == BooleanOp.Intersect || !Worthwhile(solid, tool))
            return CsgSolid.Apply(solid, tool, op, token: token);

        return op == BooleanOp.Subtract ? Subtract(solid, tool, token) : Union(solid, tool, token);
    }

    /// <summary>
    /// Whether the tool is small enough, against a solid big enough, for the local route to pay.
    ///
    /// Measured on the boxes rather than the triangle counts: what the boolean costs here is set
    /// by how much of the solid has to go into the tree, and that is a question about where the
    /// tool reaches, not how finely the solid is tessellated. Drilling a 10 mm hole in a 300,000
    /// triangle mould half is the case this exists for - a minute and a half of tree-building for
    /// a thousandth of the model.
    ///
    /// One axis is enough. A pour hole runs the full height of the block and is small in the other
    /// two, and cutting the solid on those two alone leaves the boolean a fraction of what it had.
    /// </summary>
    public static bool Worthwhile(Mesh solid, Mesh tool)
    {
        if (solid.TriangleCount < WorthIt) return false;

        var big = solid.ComputeBounds();
        var small = tool.ComputeBounds();
        if (big.IsEmpty || small.IsEmpty) return false;

        Vector3 whole = big.Max - big.Min;
        Vector3 part = small.Max - small.Min + new Vector3(2f * Margin);

        return part.X <= whole.X * SmallEnough
            || part.Y <= whole.Y * SmallEnough
            || part.Z <= whole.Z * SmallEnough;
    }

    public static Mesh Subtract(Mesh solid, Mesh tool, CancellationToken token = default) =>
        Apply(solid, tool, subtract: true, token);

    public static Mesh Union(Mesh solid, Mesh tool, CancellationToken token = default) =>
        Apply(solid, tool, subtract: false, token);

    private static Mesh Apply(Mesh solid, Mesh tool, bool subtract, CancellationToken token)
    {
        var reach = tool.ComputeBounds();
        if (reach.IsEmpty || solid.TriangleCount == 0) return solid;

        Vector3 min = reach.Min - new Vector3(Margin);
        Vector3 max = reach.Max + new Vector3(Margin);

        // The six half-spaces the working piece lies in: a point belongs to it when it is on the
        // keeping side of every one of them.
        var walls = new (Vector3 Facing, float At)[]
        {
            (Vector3.UnitX, min.X), (-Vector3.UnitX, -max.X),
            (Vector3.UnitY, min.Y), (-Vector3.UnitY, -max.Y),
            (Vector3.UnitZ, min.Z), (-Vector3.UnitZ, -max.Z)
        };

        // Only the triangles that reach into the box are cut apart; every other triangle goes back
        // exactly as it was.
        //
        // A plane has no edges. Cutting the whole solid along the six walls sliced it right through,
        // far beyond the tool - one 1 mm peg socket in a rounded box split about four thousand
        // triangles all the way round the part. The shape did not change, every piece lay on the old
        // surface, but the shading works from the corners, and the new corners sat in the middle of
        // what had been smoothly lit triangles: every rounded edge came back striped. Now only the
        // triangles touching the box are split. Where one of those shares an edge with an untouched
        // triangle, the split leaves a T-junction on that edge, which the mend at the end closes -
        // one extra corner on that one edge, not a ring round the part.
        var (near, far) = Partition(solid, min, max);
        if (near.TriangleCount == 0) return solid;

        var rest = new List<Mesh> { far };
        var middle = near;

        foreach (var (facing, at) in walls)
        {
            token.ThrowIfCancellationRequested();

            rest.Add(PlaneClip.Keep(middle, Matrix4x4.Identity, -facing, -at, cap: false));
            middle = PlaneClip.Keep(middle, Matrix4x4.Identity, facing, at, cap: false);
        }

        // The tool is nowhere near the solid, so there is nothing to do and nothing to put back.
        if (middle.TriangleCount == 0) return solid;

        var closed = solid;
        foreach (var (facing, at) in walls)
        {
            token.ThrowIfCancellationRequested();
            closed = PlaneClip.Keep(closed, Matrix4x4.Identity, facing, at);
        }

        // Mended if a lid came out short. Closing the cut means chaining the edges it left into
        // rings, and on a dense surface a plane that passes within a whisker of a corner can pinch
        // one - three or four edges meeting at a point where two should - and the ring it belongs
        // to is dropped rather than guessed at. That leaves a hole the size of one triangle, which
        // matters here because a boolean needs a closed solid. Cheap to mend, because this piece
        // is small by construction.
        if (!closed.CheckHealth().IsWatertight)
            closed = MeshHealer.Heal(closed, token: token).Mesh;

        var worked = subtract
            ? CsgSolid.Subtract(closed, tool, token: token)
            : CsgSolid.Union(closed, tool, token: token);

        rest.Add(WithoutLids(worked, walls));

        // Mended where the pieces meet. The boolean splits polygons on planes that run right
        // across the piece it was given, so the ring it hands back has corners along it that the
        // piece next door does not - a T-junction at every one, and a boundary edge for each.
        return MeshHealer.Heal(Mesh.Combine(rest), token: token).Mesh;
    }

    /// <summary>
    /// The triangles whose box overlaps the working box, and the ones clear of it.
    ///
    /// The ones clear of it keep their shared corners exactly as they were, so the part of the
    /// surface nothing reaches is not merely the same shape but the same triangles.
    /// </summary>
    private static (Mesh Near, Mesh Far) Partition(Mesh solid, Vector3 min, Vector3 max)
    {
        var near = new Mesh();
        var farIndices = new List<int>();

        for (int t = 0; t + 2 < solid.Indices.Count; t += 3)
        {
            int ia = solid.Indices[t], ib = solid.Indices[t + 1], ic = solid.Indices[t + 2];
            Vector3 a = solid.Positions[ia], b = solid.Positions[ib], c = solid.Positions[ic];

            Vector3 low = Vector3.Min(a, Vector3.Min(b, c));
            Vector3 high = Vector3.Max(a, Vector3.Max(b, c));

            bool touches = high.X >= min.X && low.X <= max.X
                && high.Y >= min.Y && low.Y <= max.Y
                && high.Z >= min.Z && low.Z <= max.Z;

            if (touches)
            {
                near.AddTriangle(a, b, c);
            }
            else
            {
                farIndices.Add(ia);
                farIndices.Add(ib);
                farIndices.Add(ic);
            }
        }

        // Compacted to the corners it uses, keeping them shared.
        var remap = new Dictionary<int, int>();
        var positions = new List<Vector3>();
        var indices = new List<int>(farIndices.Count);

        foreach (int i in farIndices)
        {
            if (!remap.TryGetValue(i, out int j))
            {
                j = positions.Count;
                positions.Add(solid.Positions[i]);
                remap[i] = j;
            }

            indices.Add(j);
        }

        return (near, new Mesh(positions, indices));
    }

    /// <summary>
    /// The worked piece with the lids taken off again, leaving it open where it was cut.
    ///
    /// A triangle is a lid when all three of its corners sit on one of the six walls. Nothing
    /// else can: the tool is kept a margin clear of them, so the boolean never puts a face there.
    /// </summary>
    private static Mesh WithoutLids(Mesh mesh, (Vector3 Facing, float At)[] walls)
    {
        var kept = new Mesh();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];

            if (OnAWall(a, b, c, walls)) continue;

            kept.AddTriangle(a, b, c);
        }

        return kept;
    }

    private static bool OnAWall(Vector3 a, Vector3 b, Vector3 c, (Vector3 Facing, float At)[] walls)
    {
        foreach (var (facing, at) in walls)
        {
            if (MathF.Abs(Vector3.Dot(facing, a) - at) <= OnWall
                && MathF.Abs(Vector3.Dot(facing, b) - at) <= OnWall
                && MathF.Abs(Vector3.Dot(facing, c) - at) <= OnWall)
                return true;
        }

        return false;
    }
}
