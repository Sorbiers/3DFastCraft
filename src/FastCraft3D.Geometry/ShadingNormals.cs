using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Derives render-ready vertex normals from a welded solid.
///
/// The editable mesh stores shared vertices only - that is what CSG and the watertightness
/// check need. Display needs something different: a sphere should look smooth while a cube
/// keeps crisp edges. Both fall out of one rule - average the normals of neighbouring faces
/// only when they meet at less than the crease angle - so no shape needs special handling,
/// and boolean results automatically get sharp cut edges against smooth original surfaces.
/// </summary>
public static class ShadingNormals
{
    public const float DefaultCreaseAngleDegrees = 35f;

    public static (Vector3[] Positions, Vector3[] Normals, int[] Indices) Build(
        Mesh mesh, float creaseAngleDegrees = DefaultCreaseAngleDegrees)
    {
        int triangleCount = mesh.TriangleCount;
        float cosCrease = MathF.Cos(creaseAngleDegrees * MathF.PI / 180f);

        // Area-weighted face normals: the un-normalised cross product is twice the area, so
        // large faces dominate the average, which is what looks right on uneven tessellation.
        var weighted = new Vector3[triangleCount];
        var unit = new Vector3[triangleCount];
        for (int t = 0; t < triangleCount; t++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t * 3]];
            Vector3 b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            weighted[t] = n;
            unit[t] = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitZ;
        }

        var incident = new List<int>[mesh.VertexCount];
        for (int t = 0; t < triangleCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = mesh.Indices[t * 3 + k];
                (incident[v] ??= new List<int>()).Add(t);
            }

        var positions = new List<Vector3>(mesh.VertexCount);
        var normals = new List<Vector3>(mesh.VertexCount);
        var indices = new int[mesh.Indices.Count];
        var emitted = new Dictionary<(int Vertex, long Nx, long Ny, long Nz), int>(mesh.VertexCount);

        for (int t = 0; t < triangleCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = mesh.Indices[t * 3 + k];

                Vector3 sum = Vector3.Zero;
                foreach (int other in incident[v]!)
                    if (Vector3.Dot(unit[t], unit[other]) >= cosCrease)
                        sum += weighted[other];

                Vector3 normal = sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : unit[t];

                // Quantise so vertices that agree to within display precision are shared.
                var key = (v,
                    (long)MathF.Round(normal.X * 4096f),
                    (long)MathF.Round(normal.Y * 4096f),
                    (long)MathF.Round(normal.Z * 4096f));

                if (!emitted.TryGetValue(key, out int index))
                {
                    index = positions.Count;
                    positions.Add(mesh.Positions[v]);
                    normals.Add(normal);
                    emitted[key] = index;
                }

                indices[t * 3 + k] = index;
            }
        }

        return (positions.ToArray(), normals.ToArray(), indices);
    }
}
