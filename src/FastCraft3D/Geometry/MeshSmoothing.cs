using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// Rounds off a mesh by nudging each vertex toward the average of its neighbours.
///
/// Done plainly, that shrinks the model: every pass pulls the surface inward, and a sphere
/// smoothed a dozen times is visibly smaller than it started. Taubin's answer is to follow each
/// inward pass with a slightly stronger outward one. The two together leave the low frequencies -
/// the shape - almost untouched while still removing the high ones, which is the faceting.
///
/// Smoothing can only move the vertices it is given, so a shape with few of them has nothing to
/// round. <see cref="MeshSubdivision"/> is what fixes that: split every triangle into four
/// first, without moving anything, and the same smoothing turns a cube into a rounded cube.
/// </summary>
public static class MeshSmoothing
{
    /// <summary>How far each vertex moves toward its neighbours on the inward pass.</summary>
    public const float Lambda = 0.5f;

    /// <summary>
    /// The outward pass. Slightly stronger than <see cref="Lambda"/> and opposite in sign, which
    /// is what stops the shape shrinking; equal and opposite would simply undo the first pass.
    /// </summary>
    public const float Mu = -0.53f;

    /// <param name="passes">Each pass is one inward step and one outward one.</param>
    /// <param name="moveRim">
    /// Whether the rim of an open mesh may move. Off by default: letting it drift pulls an
    /// opening out of shape and moves the very edges that most need to stay where they are. A
    /// closed model has no rim, so this changes nothing for one.
    /// </param>
    public static Mesh Smooth(Mesh mesh, int passes = 5, bool moveRim = false)
    {
        if (mesh.VertexCount == 0 || passes <= 0) return mesh;

        var neighbours = Neighbours(mesh);
        var pinned = moveRim ? new bool[mesh.VertexCount] : BoundaryVertices(mesh);
        var points = new Vector3[mesh.VertexCount];
        mesh.Positions.CopyTo(points);

        for (int pass = 0; pass < passes; pass++)
        {
            points = Step(points, neighbours, pinned, Lambda);
            points = Step(points, neighbours, pinned, Mu);
        }

        return new Mesh(points, mesh.Indices);
    }

    /// <summary>
    /// One step toward - or away from - the average of each vertex's neighbours.
    ///
    /// Every vertex is read from the same snapshot rather than from the array being written, so
    /// the result does not depend on which order the vertices happen to be stored in.
    /// </summary>
    private static Vector3[] Step(
        Vector3[] points, List<int>[] neighbours, bool[] pinned, float factor)
    {
        var moved = new Vector3[points.Length];

        for (int i = 0; i < points.Length; i++)
        {
            var around = neighbours[i];
            if (pinned[i] || around.Count == 0)
            {
                moved[i] = points[i];
                continue;
            }

            var average = Vector3.Zero;
            foreach (int j in around) average += points[j];
            average /= around.Count;

            moved[i] = points[i] + (average - points[i]) * factor;
        }

        return moved;
    }

    private static List<int>[] Neighbours(Mesh mesh)
    {
        var lists = new List<int>[mesh.VertexCount];
        for (int i = 0; i < lists.Length; i++) lists[i] = new List<int>(6);

        var seen = new HashSet<(int, int)>();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k], b = mesh.Indices[t + (k + 1) % 3];
                if (a == b) continue;

                var key = a < b ? (a, b) : (b, a);
                if (!seen.Add(key)) continue;

                lists[a].Add(b);
                lists[b].Add(a);
            }
        }

        return lists;
    }

    /// <summary>
    /// Vertices on the rim of an open mesh, which are held still.
    ///
    /// A closed model has none of these. On one with holes, letting the rim drift would pull the
    /// opening out of shape and move the very edges that most need to stay where they are.
    /// </summary>
    private static bool[] BoundaryVertices(Mesh mesh)
    {
        var counts = new Dictionary<(int, int), int>(mesh.Indices.Count);

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k], b = mesh.Indices[t + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var pinned = new bool[mesh.VertexCount];
        foreach (var ((a, b), count) in counts)
        {
            if (count != 1) continue;
            pinned[a] = true;
            pinned[b] = true;
        }

        return pinned;
    }
}
