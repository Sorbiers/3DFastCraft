using System.Numerics;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Geometry;

/// <summary>
/// A flat face made into a thin solid of its own, standing on the face it was taken from.
///
/// The face's own triangles are its underside, exactly where the face is, and a copy of them
/// lifted along the normal is its top, joined round the outline. Lifted outward, so the copy sits
/// on the part rather than inside it: a pad, a label plate, the outline of an opening to build a
/// frame on. Then laid again from its corners, since a face that came out of a boolean is usually
/// cut into slivers, and there is no reason for the copy to keep them.
///
/// Grown or shrunk, the outline is drawn again in the face's own plane instead: every edge moved
/// out by the amount, and every hole in by it, which is a frame round an opening or a gasket.
/// </summary>
public static class SurfaceClone
{
    public const float DefaultThicknessMm = 0.1f;

    /// <summary>
    /// The face as a solid <paramref name="thickness"/> thick, its outline moved out by
    /// <paramref name="grow"/> (in, below zero), or null when its outline does not make one.
    /// </summary>
    public static Mesh? Extrude(FacePatch face, float thickness = DefaultThicknessMm, float grow = 0f)
    {
        if (face.Triangles.Count == 0 || face.Boundary.Count < 3 || !(thickness > 0)) return null;

        return MathF.Abs(grow) < 1e-4f ? AsItIs(face, thickness) : Grown(face, thickness, grow);
    }

    private static Mesh? AsItIs(FacePatch face, float thickness)
    {
        var source = face.Mesh;
        var lift = Vector3.Normalize(face.Normal) * thickness;

        var points = new List<Vector3>();
        var below = new Dictionary<int, int>();
        var above = new Dictionary<int, int>();

        int Below(int v)
        {
            if (below.TryGetValue(v, out int i)) return i;
            below[v] = i = points.Count;
            points.Add(source.Positions[v]);
            return i;
        }

        int Above(int v)
        {
            if (above.TryGetValue(v, out int i)) return i;
            above[v] = i = points.Count;
            points.Add(source.Positions[v] + lift);
            return i;
        }

        var indices = new List<int>();
        foreach (int t in face.Triangles)
        {
            int a = source.Indices[t], b = source.Indices[t + 1], c = source.Indices[t + 2];

            // Underneath faces into the part it came from, so it is wound the other way round.
            indices.AddRange([Below(a), Below(c), Below(b)]);
            indices.AddRange([Above(a), Above(b), Above(c)]);
        }

        // The outline runs the face's way round, so each side faces out from it.
        foreach (var (a, b) in face.Boundary)
        {
            indices.AddRange([Below(a), Below(b), Above(b)]);
            indices.AddRange([Below(a), Above(b), Above(a)]);
        }

        var solid = new Mesh(points, indices);
        if (!solid.CheckHealth().IsWatertight) return null;

        var tidy = OutlineRebuild.Rebuild(solid);
        return tidy.Changed ? tidy.Mesh : solid;
    }

    /// <summary>
    /// The outline drawn again in the face's plane, moved out by <paramref name="grow"/>, filled
    /// and lifted. A hole that the growing closes is simply gone; an outline that shrinks to
    /// nothing, or that would cross itself, is refused.
    /// </summary>
    private static Mesh? Grown(FacePatch face, float thickness, float grow)
    {
        var shapes = FaceOutline.Of(face).Shapes;
        if (shapes.Count != 1) return null;

        var (outline, holes) = shapes[0];
        if (Moved(outline, grow) is not { } outer) return null;

        var inner = new List<IReadOnlyList<Vector2>>();
        foreach (var hole in holes)
            if (Moved(hole, -grow) is { } kept) inner.Add(kept);

        var (laid, triangles) = Polygon2.Triangulate(outer, inner);
        if (triangles.Count < 3) return null;

        var points = new List<Vector3>();
        var below = new Dictionary<Vector2, int>();
        var above = new Dictionary<Vector2, int>();

        int At(Dictionary<Vector2, int> level, Vector2 uv, float height)
        {
            if (level.TryGetValue(uv, out int i)) return i;
            level[uv] = i = points.Count;
            points.Add(face.ToLocal(uv, height));
            return i;
        }

        // Anticlockwise in the face's frame faces along its normal, which is the top's way.
        var indices = new List<int>();
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Vector2 a = laid[triangles[i]], b = laid[triangles[i + 1]], c = laid[triangles[i + 2]];
            indices.AddRange([At(above, a, thickness), At(above, b, thickness), At(above, c, thickness)]);
            indices.AddRange([At(below, a, 0f), At(below, c, 0f), At(below, b, 0f)]);
        }

        // Walls: the outline anticlockwise and the holes clockwise, so each faces out of the solid.
        void Wall(IReadOnlyList<Vector2> loop)
        {
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                indices.AddRange([At(below, a, 0f), At(below, b, 0f), At(above, b, thickness)]);
                indices.AddRange([At(below, a, 0f), At(above, b, thickness), At(above, a, thickness)]);
            }
        }

        Wall(Polygon2.SignedArea(outer) > 0 ? outer : outer.AsEnumerable().Reverse().ToList());
        foreach (var hole in inner) Wall(Polygon2.SignedArea(hole) < 0 ? hole : hole.AsEnumerable().Reverse().ToList());

        var solid = new Mesh(points, indices);
        return solid.CheckHealth().IsWatertight && solid.ComputeSignedVolume() > 0 ? solid : null;
    }

    /// <summary>
    /// A loop with every edge moved away from what it encloses by <paramref name="by"/> (towards it,
    /// below zero), corners mitred. Null when it shrinks to nothing or would cross itself - a
    /// mitre cannot tell an edge vanishing from an edge moving, so the result is checked rather
    /// than trusted.
    /// </summary>
    private static List<Vector2>? Moved(IReadOnlyList<Vector2> loop, float by)
    {
        if (loop.Count < 3) return null;

        float area = Polygon2.SignedArea(loop);
        if (MathF.Abs(area) < 1e-9f) return null;

        // Worked anticlockwise, where the outside of each edge is on its right.
        var ring = area > 0 ? loop.ToList() : loop.Reverse().ToList();
        var moved = new List<Vector2>(ring.Count);

        for (int i = 0; i < ring.Count; i++)
        {
            var previous = ring[(i - 1 + ring.Count) % ring.Count];
            var here = ring[i];
            var next = ring[(i + 1) % ring.Count];

            var n1 = Outward(here - previous);
            var n2 = Outward(next - here);
            var mitre = n1 + n2;
            if (mitre.LengthSquared() < 1e-12f) mitre = n1;
            mitre = Vector2.Normalize(mitre);

            // A very sharp corner would throw its point far out; four times the amount is a cap.
            float along = MathF.Max(Vector2.Dot(mitre, n1), 0.25f);
            moved.Add(here + mitre * (by / along));
        }

        // Shrunk past its middle, a square comes out as the same square turned half round, area
        // and winding and all - so neither of those says it has gone. An edge that now runs
        // backwards does.
        for (int i = 0; i < ring.Count; i++)
        {
            var was = ring[(i + 1) % ring.Count] - ring[i];
            var now = moved[(i + 1) % ring.Count] - moved[i];
            if (Vector2.Dot(was, now) <= 0) return null;
        }

        float after = Polygon2.SignedArea(moved);
        if (after <= 1e-6f || Crosses(moved)) return null;

        return moved;
    }

    private static Vector2 Outward(Vector2 edge)
    {
        float length = edge.Length();
        return length < 1e-12f ? Vector2.Zero : new Vector2(edge.Y, -edge.X) / length;
    }

    /// <summary>Whether any two edges of the loop that are not neighbours cross.</summary>
    private static bool Crosses(List<Vector2> loop)
    {
        int n = loop.Count;
        for (int i = 0; i < n; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (Intersect(a, b, loop[j], loop[(j + 1) % n])) return true;
            }
        }

        return false;
    }

    private static bool Intersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        static float Side(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        float d1 = Side(q1, q2, p1), d2 = Side(q1, q2, p2), d3 = Side(p1, p2, q1), d4 = Side(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
