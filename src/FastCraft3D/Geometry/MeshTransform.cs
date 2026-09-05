using System.Numerics;

namespace FastCraft3D.Geometry;

public enum Axis
{
    X,
    Y,
    Z
}

public static class MeshTransform
{
    /// <summary>
    /// Applies a transform to every vertex.
    ///
    /// A mirror (or any negative-scale) matrix has a negative determinant, which reverses
    /// the handedness of every triangle: normals end up pointing inwards and the STL exports
    /// inside-out. Detecting that here and flipping the winding is the single fix for the
    /// whole app, so no caller has to remember it.
    /// </summary>
    public static Mesh Transformed(Mesh mesh, Matrix4x4 matrix)
    {
        var positions = new List<Vector3>(mesh.Positions.Count);
        foreach (var p in mesh.Positions)
            positions.Add(Vector3.Transform(p, matrix));

        var result = new Mesh(positions, mesh.Indices);
        if (matrix.GetDeterminant() < 0)
            result.FlipWinding();
        return result;
    }

    public static Mesh Mirrored(Mesh mesh, Axis axis)
    {
        var scale = axis switch
        {
            Axis.X => new Vector3(-1, 1, 1),
            Axis.Y => new Vector3(1, -1, 1),
            _ => new Vector3(1, 1, -1)
        };
        return Transformed(mesh, Matrix4x4.CreateScale(scale));
    }

    /// <summary>Drops the mesh so its lowest point rests on the build plate (Z = 0).</summary>
    public static Mesh AlignedToPlate(Mesh mesh)
    {
        var bounds = mesh.ComputeBounds();
        if (bounds.IsEmpty || MathF.Abs(bounds.Min.Z) < 1e-6f) return mesh.Clone();
        return Transformed(mesh, Matrix4x4.CreateTranslation(0, 0, -bounds.Min.Z));
    }

    /// <summary>
    /// The rotation that carries one direction onto another, used to point a shape along an
    /// arbitrary axis - such as swinging the split plane's half-space box onto its normal.
    /// </summary>
    public static Matrix4x4 RotationBetween(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);
        float alignment = Vector3.Dot(from, to);

        if (alignment > 0.99999f) return Matrix4x4.Identity;

        if (alignment < -0.99999f)
        {
            // Exactly opposed: the cross product is degenerate, so any perpendicular axis will
            // do for the half turn. Pick one that is definitely not parallel to "from".
            Vector3 fallback = MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            return Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, fallback)), MathF.PI);
        }

        Vector3 axis = Vector3.Normalize(Vector3.Cross(from, to));
        return Matrix4x4.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(alignment, -1f, 1f)));
    }

    /// <summary>
    /// Builds the object-to-world matrix. Scale, then rotate (Z-Y-X intrinsic, degrees),
    /// then translate - the order the properties panel presents them in.
    /// </summary>
    public static Matrix4x4 Compose(Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        const float toRad = MathF.PI / 180f;
        return Matrix4x4.CreateScale(scale)
             * Matrix4x4.CreateRotationX(rotationDegrees.X * toRad)
             * Matrix4x4.CreateRotationY(rotationDegrees.Y * toRad)
             * Matrix4x4.CreateRotationZ(rotationDegrees.Z * toRad)
             * Matrix4x4.CreateTranslation(position);
    }
}
