namespace FastCraft3D.Geometry.Csg;

public enum BooleanOp
{
    Union,
    Subtract,
    Intersect
}

/// <summary>
/// Constructive solid geometry over closed triangle meshes.
///
/// One engine backs three user-facing features: the boolean menu, the plane cut (which is
/// a subtract/intersect against an oversized half-space box, so cut faces come out capped
/// automatically), and the guarantee that results stay watertight and therefore printable.
/// </summary>
public static class CsgSolid
{
    public static Mesh Union(Mesh a, Mesh b, bool parallel = true, CancellationToken token = default)
        => Apply(a, b, BooleanOp.Union, parallel, token);

    public static Mesh Subtract(Mesh a, Mesh b, bool parallel = true, CancellationToken token = default)
        => Apply(a, b, BooleanOp.Subtract, parallel, token);

    public static Mesh Intersect(Mesh a, Mesh b, bool parallel = true, CancellationToken token = default)
        => Apply(a, b, BooleanOp.Intersect, parallel, token);

    /// <summary>
    /// Runs a boolean operation. Blocks the calling thread while a dedicated big-stack
    /// thread does the work, so callers on the UI thread must wrap this in Task.Run.
    ///
    /// Cancelling throws rather than returning half a result: a BSP abandoned part-way through
    /// is not a solid, and there is no meaningful thing to hand back. Nothing has been touched
    /// at that point, because the caller only reaches the scene once this returns.
    /// </summary>
    public static Mesh Apply(Mesh a, Mesh b, BooleanOp op, bool parallel = true,
                             CancellationToken token = default)
        => CsgRunner.Run(() => Execute(a, b, op, parallel, token));

    private static Mesh Execute(Mesh meshA, Mesh meshB, BooleanOp op, bool parallel,
                                CancellationToken token)
    {
        var a = new CsgNode();
        a.Build(ToPolygons(meshA), parallel, token);
        var b = new CsgNode();
        b.Build(ToPolygons(meshB), parallel, token);

        switch (op)
        {
            case BooleanOp.Union:
                a.ClipTo(b, parallel, token);
                b.ClipTo(a, parallel, token);
                b.Invert(token);
                b.ClipTo(a, parallel, token);
                b.Invert(token);
                a.Build(b.AllPolygons(), parallel, token);
                break;

            case BooleanOp.Subtract:
                a.Invert(token);
                a.ClipTo(b, parallel, token);
                b.ClipTo(a, parallel, token);
                b.Invert(token);
                b.ClipTo(a, parallel, token);
                b.Invert(token);
                a.Build(b.AllPolygons(), parallel, token);
                a.Invert(token);
                break;

            case BooleanOp.Intersect:
                a.Invert(token);
                b.ClipTo(a, parallel, token);
                b.Invert(token);
                a.ClipTo(b, parallel, token);
                b.ClipTo(a, parallel, token);
                a.Build(b.AllPolygons(), parallel, token);
                a.Invert(token);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(op));
        }

        return ToMesh(a.AllPolygons());
    }

    public static List<CsgPolygon> ToPolygons(Mesh mesh)
    {
        var polygons = new List<CsgPolygon>(mesh.TriangleCount);
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            Vec3d a = Vec3d.From(mesh.Positions[mesh.Indices[i]]);
            Vec3d b = Vec3d.From(mesh.Positions[mesh.Indices[i + 1]]);
            Vec3d c = Vec3d.From(mesh.Positions[mesh.Indices[i + 2]]);

            // Zero-area triangles carry no plane and would poison the tree.
            if (CsgPlane.TryFromPoints(a, b, c, out var plane))
                polygons.Add(new CsgPolygon([a, b, c], plane));
        }
        return polygons;
    }

    public static Mesh ToMesh(List<CsgPolygon> polygons)
    {
        var mesh = new Mesh();
        foreach (var polygon in polygons)
        {
            var v = polygon.Vertices;
            // Split results are convex, so a triangle fan is safe.
            for (int i = 2; i < v.Length; i++)
                mesh.AddTriangle(v[0].ToVector3(), v[i - 1].ToVector3(), v[i].ToVector3());
        }
        // Fanning emits unshared vertices; weld so the result is a proper indexed solid,
        // then close the T-junctions the BSP split leaves behind so the solid is manifold.
        return MeshRepair.Repair(mesh.Welded());
    }
}
