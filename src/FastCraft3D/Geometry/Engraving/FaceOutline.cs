using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// The outline of a flat face in its own 2D frame: the loops round its edge, nested into shapes
/// with holes, and the two questions asked of them - is a point inside, and how near an edge.
/// </summary>
public sealed class FaceOutline
{
    private FaceOutline(List<List<Vector2>> loops)
    {
        Loops = loops;
        Shapes = loops.Count > 0 ? Polygon2.Nest(loops) : [];
    }

    public List<List<Vector2>> Loops { get; }

    public List<(List<Vector2> Outline, List<List<Vector2>> Holes)> Shapes { get; }

    /// <summary>
    /// Chained from the face's boundary edges. Chained by position rather than by corner number,
    /// since a mesh that was never welded gives the same corner a number per triangle.
    /// </summary>
    public static FaceOutline Of(FacePatch face)
    {
        var next = new Dictionary<(long, long, long), List<Vector3>>();
        foreach (var (a, b) in face.Boundary)
        {
            var from = face.Mesh.Positions[a];
            var to = face.Mesh.Positions[b];
            if (Vector3.DistanceSquared(from, to) < 1e-12f) continue;

            var key = Key(from);
            if (!next.TryGetValue(key, out var ends)) next[key] = ends = [];
            ends.Add(to);
        }

        var loops = new List<List<Vector2>>();

        foreach (var start in next.Keys.ToList())
        {
            while (next.TryGetValue(start, out var ends) && ends.Count > 0)
            {
                var loop = new List<Vector2>();
                var at = start;

                // Walked until it comes back round, or runs out: a face whose outline does not
                // close is not one to lay anything out on, and a broken loop is simply dropped.
                for (int guard = 0; guard < face.Boundary.Count + 1; guard++)
                {
                    if (!next.TryGetValue(at, out var onward) || onward.Count == 0) break;

                    var to = onward[^1];
                    onward.RemoveAt(onward.Count - 1);
                    loop.Add(face.ToUv(to));

                    at = Key(to);
                    if (at == start) break;
                }

                if (at == start && loop.Count >= 3) loops.Add(Tidied(loop));
            }
        }

        return new FaceOutline(loops.Where(l => l.Count >= 3).ToList());

        static (long, long, long) Key(Vector3 p) =>
            ((long)MathF.Round(p.X * 1e4f), (long)MathF.Round(p.Y * 1e4f), (long)MathF.Round(p.Z * 1e4f));
    }

    /// <summary>
    /// Corners closer together than a hundredth of a millimetre are merged. A scan cut flat has
    /// thousands of them along its edge, and each is a piece of wall to build round later.
    /// </summary>
    private static List<Vector2> Tidied(List<Vector2> loop)
    {
        var kept = new List<Vector2>(loop.Count);
        foreach (var p in loop)
            if (kept.Count == 0 || Vector2.DistanceSquared(kept[^1], p) > 1e-4f)
                kept.Add(p);

        if (kept.Count > 1 && Vector2.DistanceSquared(kept[0], kept[^1]) <= 1e-4f) kept.RemoveAt(kept.Count - 1);
        return kept;
    }

    public bool Contains(Vector2 p)
    {
        foreach (var (outline, holes) in Shapes)
            if (Polygon2.Contains(outline, p) && !holes.Any(h => Polygon2.Contains(h, p)))
                return true;

        return false;
    }

    public float DistanceToEdge(Vector2 p)
    {
        float nearest = float.MaxValue;

        foreach (var loop in Loops)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
                Vector2 ab = b - a;
                float t = ab.LengthSquared() < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
                nearest = MathF.Min(nearest, Vector2.Distance(p, a + ab * t));
            }
        }

        return nearest;
    }

    /// <summary>Whether a circle this size fits wholly inside the face, clear of every edge and hole.</summary>
    public bool Holds(Vector2 centre, float radius) => Contains(centre) && DistanceToEdge(centre) >= radius;
}
