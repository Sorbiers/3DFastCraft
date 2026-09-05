using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Finds the edges that read as the shape's outline: where two faces meet at a sharp angle, and
/// where a face has no neighbour at all.
///
/// This is what the white selection outline is drawn from. Drawing every triangle edge instead
/// would be far simpler, but a sphere or a boolean result would turn into a ball of wireframe -
/// the point is to trace the form, not the tessellation.
/// </summary>
public static class FeatureEdges
{
    public const float DefaultCreaseAngleDegrees = 25f;

    public static List<(Vector3 A, Vector3 B)> Build(Mesh mesh, float creaseAngleDegrees = DefaultCreaseAngleDegrees)
    {
        var result = new List<(Vector3, Vector3)>();
        if (mesh.TriangleCount == 0) return result;

        float cosCrease = MathF.Cos(creaseAngleDegrees * MathF.PI / 180f);

        // First normal seen for an edge, and the second if there is one. Anything shared by more
        // than two faces is left alone: it has no single meaningful angle.
        var first = new Dictionary<(int, int), Vector3>(mesh.Indices.Count);
        var second = new Dictionary<(int, int), Vector3>();
        var crowded = new HashSet<(int, int)>();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            Vector3 normal = Vector3.Cross(
                mesh.Positions[b] - mesh.Positions[a],
                mesh.Positions[c] - mesh.Positions[a]);
            if (normal.LengthSquared() < 1e-20f) continue;
            normal = Vector3.Normalize(normal);

            Record(a, b, normal);
            Record(b, c, normal);
            Record(c, a, normal);
        }

        foreach (var (edge, normalA) in first)
        {
            if (crowded.Contains(edge)) continue;

            bool sharp;
            if (!second.TryGetValue(edge, out var normalB))
            {
                sharp = true; // a boundary edge is always part of the outline
            }
            else
            {
                sharp = Vector3.Dot(normalA, normalB) < cosCrease;
            }

            if (sharp)
                result.Add((mesh.Positions[edge.Item1], mesh.Positions[edge.Item2]));
        }

        return result;

        void Record(int x, int y, Vector3 normal)
        {
            var key = x < y ? (x, y) : (y, x);
            if (!first.ContainsKey(key)) first[key] = normal;
            else if (!second.ContainsKey(key)) second[key] = normal;
            else crowded.Add(key);
        }
    }
}
