using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// The walls round an upright part, seen from above: the closed outline of its cross-section, a
/// corner at every place the wall turns, and how high the wall stands.
///
/// A texture laid on one wall of a box stops at the corner, and the next wall's stops at the
/// same corner a few tenths of a millimetre away from it, with a blade of two end walls standing
/// between the two patterns that do not match. Every cure that keeps the walls apart has that
/// seam somewhere. Taking the walls as one - unrolled into a single strip as long as the way
/// round - has none: a wall of any number of flat faces unrolls exactly, without stretching,
/// because it is a prism and a prism is developable. The pattern is laid on the strip, which
/// closes on itself like the strip round a barrel, and each point of it is put back on the wall it
/// came from. Fewer than all of the walls unroll the same way, into a strip with two ends - see
/// <see cref="WallRun"/> and <see cref="WallsSurface"/>.
/// </summary>
public sealed class WallLoop
{
    /// <summary>Under this a turn is not a corner: the two walls are one wall.</summary>
    private const float LeastTurnDegrees = 1f;

    /// <summary>The tallest a corner's mitre is let grow, as a multiple of the offset, on a very sharp corner.</summary>
    private const float MostMitre = 4f;

    private readonly Vector2[] corners;
    private readonly Vector2[] along;
    private readonly Vector2[] outward;
    private readonly float[] arcs;

    private WallLoop(Vector2[] corners, float low, float high, float cutZ, Vector2 pick)
    {
        this.corners = corners;
        int n = corners.Length;
        along = new Vector2[n];
        outward = new Vector2[n];
        arcs = new float[n + 1];

        for (int i = 0; i < n; i++)
        {
            var edge = corners[(i + 1) % n] - corners[i];
            float length = edge.Length();
            along[i] = edge / length;

            // The material is on the left of the way round, so the outside is on the right.
            outward[i] = new Vector2(along[i].Y, -along[i].X);
            arcs[i + 1] = arcs[i] + length;
        }

        Perimeter = arcs[n];
        Low = low;
        High = high;
        CutZ = cutZ;
        StartArc = Nearest(pick, null).Arc;
    }

    /// <summary>The corners, in order round the part with the material on the left.</summary>
    public IReadOnlyList<Vector2> Corners => corners;

    /// <summary>How far it is all the way round, in millimetres.</summary>
    public float Perimeter { get; }

    /// <summary>The height of the foot of the wall that was picked, and of its top.</summary>
    public float Low { get; }

    public float High { get; }

    /// <summary>
    /// The height the outline was taken at: one where the walls go all the way round with nothing
    /// in the way, so the middle of any wall at this height is on that wall's face.
    /// </summary>
    public float CutZ { get; }

    /// <summary>Where the click landed, as a distance along the way round.</summary>
    public float StartArc { get; }

    public int Count => corners.Length;

    /// <summary>The corner that starts wall <paramref name="i"/>, in plan.</summary>
    public Vector2 CornerAt(int i) => corners[i];

    /// <summary>Which way wall <paramref name="i"/> runs, and which way its outside faces, both unit vectors in plan.</summary>
    public (Vector2 Along, Vector2 Outward) WallAt(int i) => (along[i], outward[i]);

    /// <summary>Where wall <paramref name="i"/> starts, as a distance along the way round.</summary>
    public float ArcOfCorner(int i) => arcs[i];

    /// <summary>How long wall <paramref name="i"/> is.</summary>
    public float LengthOf(int i) => arcs[i + 1] - arcs[i];

    /// <summary>
    /// The point of the wall <paramref name="arc"/> along the way round, pushed out of it by
    /// <paramref name="height"/>. At a corner the push is along the mitre - the way the two
    /// walls' outsides meet - and as long as it takes to stand that far off both, so the surface
    /// stood off the walls is one surface and not two with a gap between.
    /// </summary>
    public Vector2 At(float arc, float height) => At(arc, height, 0, corners.Length);

    /// <summary>
    /// The same on a run of <paramref name="count"/> walls from <paramref name="first"/>. A corner
    /// inside the run turns on the mitre; at its two ends there is no wall beyond to share a mitre
    /// with, so the strip stands off square to the end wall and stops flush with its edge, as a
    /// texture on a single face does. On the mitre there it would lean out past the next wall's
    /// face by as much as it stands off.
    /// </summary>
    public Vector2 At(float arc, float height, int first, int count)
    {
        int n = corners.Length;
        arc -= Perimeter * MathF.Floor(arc / Perimeter);

        int wall = Math.Clamp(Array.BinarySearch(arcs, 0, n, arc) is var found && found < 0 ? ~found - 1 : found, 0, n - 1);
        float into = arc - arcs[wall], toEnd = arcs[wall + 1] - arc;

        if (into < CornerReachMm) return corners[wall] + Off(wall, first, count) * height;
        if (toEnd < CornerReachMm) return corners[(wall + 1) % n] + Off((wall + 1) % n, first, count) * height;

        return corners[wall] + along[wall] * into + outward[wall] * height;
    }

    /// <summary>The way a corner of a run stands off: on the mitre inside it, square to the end wall at either end.</summary>
    private Vector2 Off(int corner, int first, int count)
    {
        int n = corners.Length;
        if (count < n)
        {
            if (corner == first) return outward[first];
            if (corner == (first + count) % n) return outward[(first + count + n - 1) % n];
        }

        return Mitre(corner);
    }

    /// <summary>
    /// How close to a corner counts as at it: far under anything laid down, and far over what
    /// arithmetic on a sample line put there can have strayed.
    /// </summary>
    private const float CornerReachMm = 1e-3f;

    /// <summary>The way a corner's surface stands off it, long enough to be the given distance from both walls.</summary>
    private Vector2 Mitre(int corner)
    {
        int n = corners.Length;
        var before = outward[(corner + n - 1) % n];
        var after = outward[corner];

        var middle = before + after;
        if (middle.LengthSquared() < 1e-9f) return after;

        middle = Vector2.Normalize(middle);
        return middle / MathF.Max(Vector2.Dot(middle, before), 1f / MostMitre);
    }

    /// <summary>
    /// The wall nearest a point in plan, and how far the point is from it. Given the way a picked
    /// face looks, only a wall looking the same way will do, so a click right on a corner goes to
    /// the wall that was clicked rather than to whichever of the two came first; -1 when none does.
    /// </summary>
    public int WallNear(Vector2 point, Vector2? facing, out float distance)
    {
        var (wall, _, d) = Nearest(point, facing);
        distance = d;
        return wall;
    }

    private (int Wall, float Arc, float Distance) Nearest(Vector2 point, Vector2? facing)
    {
        float best = float.MaxValue, arc = 0f;
        int wall = -1;

        for (int i = 0; i < corners.Length; i++)
        {
            if (facing is { } f && Vector2.Dot(outward[i], f) < 0.98f) continue;

            float t = Math.Clamp(Vector2.Dot(point - corners[i], along[i]), 0f, LengthOf(i));
            float d = Vector2.DistanceSquared(point, corners[i] + along[i] * t);
            if (d < best) (best, arc, wall) = (d, arcs[i] + t, i);
        }

        return (wall, arc, wall < 0 ? float.MaxValue : MathF.Sqrt(best));
    }

    /// <summary>
    /// The flat face wall <paramref name="i"/> is part of, found at its middle at
    /// <see cref="CutZ"/> - or null when there is none.
    /// </summary>
    public FacePatch? FaceOf(Mesh world, int i)
    {
        var middle = corners[i] + along[i] * (LengthOf(i) / 2f);
        return FacePatch.Find(world, new Vector3(middle.X, middle.Y, CutZ), new Vector3(outward[i].X, outward[i].Y, 0f));
    }

    /// <summary>
    /// The walls round the face that was picked, or null with the reason when they cannot be had.
    /// The part is cut across at several heights of the picked wall and the cleanest cut kept: the
    /// fewest corners, and of those the longest way round. A cut through a window either turns in
    /// at its reveals and back out, which is more corners, or - through a part with an opening
    /// right across it - comes apart in two, each half shorter than the whole. Taking the first cut
    /// that closed, as this did, took the half.
    /// </summary>
    public static WallLoop? Around(Mesh world, FacePatch face, Vector3 pick, out string? why)
    {
        why = null;

        if (MathF.Abs(face.Normal.Z) > 0.2f)
        {
            why = "That is not an upright wall - pick one of the sides.";
            return null;
        }

        float low = float.MaxValue, high = float.MinValue;
        foreach (int t in face.Triangles)
            for (int k = 0; k < 3; k++)
            {
                float z = world.Positions[world.Indices[t + k]].Z;
                low = MathF.Min(low, z);
                high = MathF.Max(high, z);
            }

        if (high - low < 0.5f)
        {
            why = "That wall is too low to texture.";
            return null;
        }

        var plan = new Vector2(pick.X, pick.Y);
        WallLoop? best = null;

        float[] fractions = [0.5f, 0.3f, 0.7f, 0.15f, 0.85f, 0.05f, 0.95f];
        foreach (float f in fractions)
        {
            float z = ClearOfVertices(world, low + (high - low) * f);
            if (Cross(world, z, plan) is not { Count: >= 3 } outline) continue;

            var loop = new WallLoop([.. outline], low, high, z, plan);
            if (best is null || loop.Count < best.Count
                || (loop.Count == best.Count && loop.Perimeter > best.Perimeter + 1e-3f))
                best = loop;
        }

        if (best is null)
            why = "The walls do not go all the way round the part at any height - pick a face of a closed upright shape.";

        return best;
    }

    /// <summary>The height nudged off any vertex of the mesh: a plane through a corner of the mesh cuts that corner twice.</summary>
    private static float ClearOfVertices(Mesh mesh, float z)
    {
        foreach (var p in mesh.Positions)
            if (MathF.Abs(p.Z - z) < 1e-3f) return z + 0.0137f;

        return z;
    }

    /// <summary>
    /// The outline of the part at one height, as the loop nearest the pick: its triangles cut by
    /// the plane, each cut a short step along the wall with the material on its left, joined end
    /// to end. Null when the nearest does not close, which a window or a door in the wall leaves it.
    /// </summary>
    private static List<Vector2>? Cross(Mesh mesh, float z, Vector2 near)
    {
        var steps = new List<(Vector2 From, Vector2 To)>();
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            var flat = new Vector2(normal.X, normal.Y);
            if (flat.LengthSquared() < 1e-12f) continue;

            var cut = new List<Vector2>(2);
            Edge(a, b);
            Edge(b, c);
            Edge(c, a);
            if (cut.Count != 2) continue;

            // Walking the way that leaves this triangle's outside on the right.
            var way = cut[1] - cut[0];
            if (Vector2.Dot(new Vector2(way.Y, -way.X), flat) < 0) (cut[0], cut[1]) = (cut[1], cut[0]);
            if ((cut[1] - cut[0]).LengthSquared() > 1e-10f) steps.Add((cut[0], cut[1]));

            void Edge(Vector3 p, Vector3 q)
            {
                if ((p.Z < z) == (q.Z < z)) return;
                float f = (z - p.Z) / (q.Z - p.Z);
                cut.Add(new Vector2(p.X + f * (q.X - p.X), p.Y + f * (q.Y - p.Y)));
            }
        }

        if (steps.Count < 3) return null;

        // Joined end to end, by where they start.
        static (int, int) Key(Vector2 v) => ((int)MathF.Round(v.X * 500f), (int)MathF.Round(v.Y * 500f));
        var starts = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < steps.Count; i++)
        {
            var key = Key(steps[i].From);
            if (!starts.TryGetValue(key, out var list)) starts[key] = list = [];
            list.Add(i);
        }

        // The step nearest the pick, and the loop it is in.
        int first = -1;
        float closest = float.MaxValue;
        for (int i = 0; i < steps.Count; i++)
        {
            float d = Distance(near, steps[i].From, steps[i].To);
            if (d < closest) (closest, first) = (d, i);
        }

        if (closest > 0.5f) return null;

        var loop = new List<Vector2> { steps[first].From };
        var used = new HashSet<int> { first };
        int at = first;

        while (true)
        {
            var end = steps[at].To;
            if (!starts.TryGetValue(Key(end), out var next)) return null;

            int following = next.FirstOrDefault(i => !used.Contains(i) || i == first, -1);
            if (following < 0) return null;
            if (following == first) break;

            used.Add(following);
            loop.Add(end);
            at = following;
            if (loop.Count > steps.Count) return null;
        }

        return Simplified(loop);
    }

    private static float Distance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-12f), 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>The loop without the points on a straight run: the two steps either side of one are one wall.</summary>
    private static List<Vector2> Simplified(List<Vector2> loop)
    {
        float least = MathF.Cos(LeastTurnDegrees * MathF.PI / 180f);
        var kept = new List<Vector2>();

        for (int i = 0; i < loop.Count; i++)
        {
            var before = loop[i] - loop[(i + loop.Count - 1) % loop.Count];
            var after = loop[(i + 1) % loop.Count] - loop[i];
            if (before.LengthSquared() < 1e-10f || after.LengthSquared() < 1e-10f) continue;

            if (Vector2.Dot(Vector2.Normalize(before), Vector2.Normalize(after)) < least) kept.Add(loop[i]);
        }

        return kept;
    }
}
