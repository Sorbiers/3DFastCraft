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

        var rest = new List<Mesh>();
        var middle = solid;

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
