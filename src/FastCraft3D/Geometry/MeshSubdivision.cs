using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Splits every triangle into four, giving smoothing something to work with.
///
/// Nothing moves: each edge gains a vertex at its midpoint and each triangle becomes four, so
/// the shape is exactly what it was, only described more finely. That is the point - smoothing
/// can only round a surface by moving the vertices it has, so a cube with eight of them has
/// nothing to round. Subdivide first and the same smoothing turns it into a rounded cube.
///
/// Midpoints are shared between the two triangles either side of an edge, so the mesh stays
/// welded and watertight rather than falling into loose triangles.
/// </summary>
public static class MeshSubdivision
{
    /// <summary>
    /// Enough triangles for smoothing to have something to work on. A shape below this is
    /// subdivided up to it; anything already denser is left alone, since an import does not need
    /// four times the triangles to be smoothed.
    /// </summary>
    public const int ComfortableDensity = 3000;

    /// <summary>Each level multiplies the triangle count by four, so this is already 256 times.</summary>
    public const int MaximumLevels = 4;

    public static Mesh Subdivide(Mesh mesh, int levels = 1)
    {
        var current = mesh;
        for (int i = 0; i < levels && i < MaximumLevels; i++) current = Once(current);
        return current;
    }

    /// <summary>
    /// How many times to split before smoothing, so a simple shape rounds and a dense one is not
    /// needlessly quadrupled.
    /// </summary>
    public static int LevelsFor(Mesh mesh, int target = ComfortableDensity)
    {
        if (mesh.TriangleCount == 0) return 0;

        int levels = 0;
        long count = mesh.TriangleCount;

        while (count < target && levels < MaximumLevels)
        {
            count *= 4;
            levels++;
        }

        return levels;
    }

    private static Mesh Once(Mesh mesh)
    {
        var positions = new List<Vector3>(mesh.Positions);
        var indices = new List<int>(mesh.Indices.Count * 4);
        var midpoints = new Dictionary<(int, int), int>(mesh.Indices.Count);

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];

            int ab = Midpoint(positions, midpoints, a, b);
            int bc = Midpoint(positions, midpoints, b, c);
            int ca = Midpoint(positions, midpoints, c, a);

            // Three corner triangles and the middle one, all wound as the original was.
            indices.AddRange([a, ab, ca]);
            indices.AddRange([ab, b, bc]);
            indices.AddRange([ca, bc, c]);
            indices.AddRange([ab, bc, ca]);
        }

        return new Mesh(positions, indices);
    }

    /// <summary>
    /// The vertex halfway along an edge, made once and shared. Keyed on the edge rather than the
    /// triangle, so the neighbour on the other side gets the same vertex and the surface does not
    /// come apart along every edge it was split on.
    /// </summary>
    private static int Midpoint(
        List<Vector3> positions, Dictionary<(int, int), int> made, int from, int to)
    {
        var key = from < to ? (from, to) : (to, from);
        if (made.TryGetValue(key, out int existing)) return existing;

        int index = positions.Count;
        positions.Add((positions[from] + positions[to]) * 0.5f);
        made[key] = index;

        return index;
    }
}
