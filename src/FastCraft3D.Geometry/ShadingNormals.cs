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
        var sliver = new bool[triangleCount];
        for (int t = 0; t < triangleCount; t++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t * 3]];
            Vector3 b = mesh.Positions[mesh.Indices[t * 3 + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t * 3 + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            weighted[t] = n;
            unit[t] = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitZ;

            // How tall the triangle is across its longest side. A boolean leaves strips a few
            // thousandths of a millimetre tall, whose cross product is mostly rounding error: each
            // got a normal of its own pointing nowhere in particular, was lit as if it faced away,
            // and showed as a dark line drawn across a flat wall.
            float longest = MathF.Max((b - a).Length(), MathF.Max((c - b).Length(), (a - c).Length()));
            float height = longest > 0f ? n.Length() / longest : 0f;
            sliver[t] = height < 1e-3f || height < longest * 1e-3f;
        }

        var incident = new List<int>[mesh.VertexCount];
        for (int t = 0; t < triangleCount; t++)
            for (int k = 0; k < 3; k++)
            {
                int v = mesh.Indices[t * 3 + k];
                (incident[v] ??= new List<int>()).Add(t);
            }

        var along = Along(mesh, unit, sliver);

        var positions = new List<Vector3>(mesh.VertexCount);
        var normals = new List<Vector3>(mesh.VertexCount);
        var indices = new int[mesh.Indices.Count];
        var emitted = new Dictionary<(int Vertex, long Nx, long Ny, long Nz), int>(mesh.VertexCount);

        for (int t = 0; t < triangleCount; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int v = mesh.Indices[t * 3 + k];

                // A sliver's own normal is not to be trusted, and neither is it as a neighbour.
                Vector3 facing = sliver[t] ? along[t] : unit[t];

                Vector3 sum = Vector3.Zero;
                foreach (int other in incident[v]!)
                    if (!sliver[other] && Vector3.Dot(facing, unit[other]) >= cosCrease)
                        sum += weighted[other];

                Vector3 normal = sum.LengthSquared() > 1e-20f ? Vector3.Normalize(sum) : facing;

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

            // A sliver wound against the face it lies along is turned round for drawing. With the
            // inside of parts left undrawn it was dropped as inside, and the gap it left - a pixel
            // here and there along its length - showed the background as a dashed line.
            if (sliver[t] && Vector3.Dot(unit[t], along[t]) < 0f)
                (indices[t * 3 + 1], indices[t * 3 + 2]) = (indices[t * 3 + 2], indices[t * 3 + 1]);
        }

        return (positions.ToArray(), normals.ToArray(), indices);
    }

    /// <summary>
    /// The way each sliver should face: the way the face it lies along does - the one across its
    /// longest side, through any other slivers lying alongside it.
    ///
    /// Taken first from the biggest face at each corner, which put a sliver that ran into a
    /// corner of the wall half in the light of the face round that corner, and the dark line
    /// stayed - fainter, and shaded from one end to the other.
    /// </summary>
    private static Vector3[] Along(Mesh mesh, Vector3[] unit, bool[] sliver)
    {
        var along = (Vector3[])unit.Clone();
        if (!sliver.Any(s => s)) return along;

        var byEdge = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t < unit.Length; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t * 3 + k], b = mesh.Indices[t * 3 + (k + 1) % 3];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (!byEdge.TryGetValue(key, out var list)) byEdge[key] = list = new List<int>(2);
                list.Add(t);
            }

        for (int t = 0; t < unit.Length; t++)
        {
            if (!sliver[t]) continue;

            int at = t, from = -1;
            for (int step = 0; step < 6 && sliver[at]; step++)
            {
                int next = Across(at, from);
                if (next < 0) break;
                from = at;
                at = next;
            }

            if (!sliver[at]) along[t] = unit[at];
        }

        return along;

        int Across(int t, int not)
        {
            // The longest side, which is the one the face it lies along shares.
            int best = 0;
            float longest = -1f;
            for (int k = 0; k < 3; k++)
            {
                float length = Vector3.DistanceSquared(mesh.Positions[mesh.Indices[t * 3 + k]], mesh.Positions[mesh.Indices[t * 3 + (k + 1) % 3]]);
                if (length > longest) { longest = length; best = k; }
            }

            int a = mesh.Indices[t * 3 + best], b = mesh.Indices[t * 3 + (best + 1) % 3];
            foreach (int other in byEdge[(Math.Min(a, b), Math.Max(a, b))])
                if (other != t && other != not) return other;
            return -1;
        }
    }
}
