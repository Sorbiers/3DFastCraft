using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Splits a mesh into its separate pieces - the shells that share no vertices with each other.
///
/// This is what Ungroup runs on, and it is useful well beyond undoing a Group: a downloaded STL
/// is very often several loose parts in one file, and separating them is the only way to lay
/// them out on the plate individually.
/// </summary>
public static class MeshComponents
{
    /// <summary>
    /// Returns one mesh per connected piece, in the order the pieces first appear. A mesh that
    /// is already a single piece comes back as a one-element list holding an equivalent copy.
    /// </summary>
    public static List<Mesh> Split(Mesh mesh)
    {
        int vertexCount = mesh.VertexCount;
        if (vertexCount == 0 || mesh.TriangleCount == 0) return [];

        // Union-find over vertices: two triangles belong together when they share a vertex.
        var parent = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++) parent[i] = i;

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            Union(parent, a, b);
            Union(parent, b, c);
        }

        // Bucket the triangles by the root of their first corner, keeping first-seen order so
        // the resulting part numbering is stable rather than dictated by hashing.
        var order = new List<int>();
        var buckets = new Dictionary<int, List<int>>();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int root = Find(parent, mesh.Indices[t]);
            if (!buckets.TryGetValue(root, out var triangles))
            {
                buckets[root] = triangles = new List<int>();
                order.Add(root);
            }
            triangles.Add(t);
        }

        var parts = new List<Mesh>(order.Count);
        foreach (int root in order)
        {
            var remap = new Dictionary<int, int>();
            var positions = new List<Vector3>();
            var indices = new List<int>();

            foreach (int t in buckets[root])
            {
                for (int k = 0; k < 3; k++)
                {
                    int original = mesh.Indices[t + k];
                    if (!remap.TryGetValue(original, out int local))
                    {
                        local = positions.Count;
                        remap[original] = local;
                        positions.Add(mesh.Positions[original]);
                    }
                    indices.Add(local);
                }
            }

            parts.Add(new Mesh(positions, indices));
        }

        return parts;
    }

    /// <summary>How many separate pieces a mesh has, without building them.</summary>
    public static int Count(Mesh mesh)
    {
        int vertexCount = mesh.VertexCount;
        if (vertexCount == 0 || mesh.TriangleCount == 0) return 0;

        var parent = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++) parent[i] = i;

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            Union(parent, a, b);
            Union(parent, b, c);
        }

        var roots = new HashSet<int>();
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            roots.Add(Find(parent, mesh.Indices[t]));

        return roots.Count;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]]; // path halving keeps this near-flat
            i = parent[i];
        }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a), rootB = Find(parent, b);
        if (rootA != rootB) parent[rootB] = rootA;
    }
}
