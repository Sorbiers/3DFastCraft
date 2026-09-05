using FastCraft3D.Geometry;
using HelixToolkit.SharpDX.Core;
using Media3D = System.Windows.Media.Media3D;
using Numerics = System.Numerics;
using SharpDXVector3 = SharpDX.Vector3;

namespace FastCraft3D.Render;

/// <summary>
/// The only place where editable geometry meets renderer types.
///
/// Keeping the conversion here is what lets Geometry, Model and Io stay free of SharpDX, so
/// the viewport can be replaced without touching the modelling core.
/// </summary>
public static class MeshConverter
{
    public static MeshGeometry3D ToGeometry(Mesh mesh, float creaseAngleDegrees = ShadingNormals.DefaultCreaseAngleDegrees)
    {
        var (positions, normals, indices) = ShadingNormals.Build(mesh, creaseAngleDegrees);

        var positionCollection = new Vector3Collection(positions.Length);
        foreach (var p in positions)
            positionCollection.Add(new SharpDXVector3(p.X, p.Y, p.Z));

        var normalCollection = new Vector3Collection(normals.Length);
        foreach (var n in normals)
            normalCollection.Add(new SharpDXVector3(n.X, n.Y, n.Z));

        var indexCollection = new IntCollection(indices.Length);
        indexCollection.AddRange(indices);

        return new MeshGeometry3D
        {
            Positions = positionCollection,
            Normals = normalCollection,
            Indices = indexCollection
        };
    }

    /// <summary>
    /// Both System.Numerics and WPF's Media3D use the row-vector convention (point * matrix)
    /// with translation in the fourth row, so the components map straight across.
    /// </summary>
    public static Media3D.Matrix3D ToMatrix3D(Numerics.Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    public static Media3D.Transform3D ToTransform(Numerics.Matrix4x4 m) =>
        new Media3D.MatrixTransform3D(ToMatrix3D(m));
}
