using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// How far one solid can move straight down before it touches another: for setting a part down
/// on whatever is under it, as Drop to plate sets it on the plate.
/// </summary>
/// <remarks>
/// Two solids moving together first touch where a corner of one meets a face of the other, or
/// where an edge of one crosses an edge of the other. So the distance is the least of three:
/// straight down from each corner of the moving one to an upward face of the other, straight up
/// from each corner of the other to a downward face of the moving one, and down from each edge
/// of the moving one to each edge of the other that it crosses in plan. Corners alone were not
/// enough: two ridges crossed at right angles meet in the middle, where neither has a corner.
///
/// Faces and edges are sorted into a grid over the plan, so each question only looks at what is
/// under it. Parts already a little into each other - dropped with an overlap to be merged - count
/// as touching, rather than as having the other part's underside somewhere below to fall to.
/// </remarks>
public static class VerticalDrop
{
    /// <summary>How far into the other a part may already be and still be resting on it.</summary>
    public const float Sunk = 0.5f;

    /// <summary>
    /// How far <paramref name="moving"/> can go down before it touches <paramref name="still"/>:
    /// nought or less if it is already resting on it, null if nothing of it is underneath.
    /// </summary>
    public static float? Distance(Mesh moving, Mesh still)
    {
        var mb = moving.ComputeBounds();
        var sb = still.ComputeBounds();
        if (mb.IsEmpty || sb.IsEmpty) return null;
        if (mb.Min.X > sb.Max.X || sb.Min.X > mb.Max.X || mb.Min.Y > sb.Max.Y || sb.Min.Y > mb.Max.Y) return null;
        if (sb.Min.Z >= mb.Max.Z) return null;

        float x0 = MathF.Max(mb.Min.X, sb.Min.X) - 1e-3f, y0 = MathF.Max(mb.Min.Y, sb.Min.Y) - 1e-3f;
        float x1 = MathF.Min(mb.Max.X, sb.Max.X) + 1e-3f, y1 = MathF.Min(mb.Max.Y, sb.Max.Y) + 1e-3f;

        var stillFaces = new Plan(still, x0, y0, x1, y1, up: true);
        var movingFaces = new Plan(moving, x0, y0, x1, y1, up: false);
        float? best = null;

        void Take(float d)
        {
            if (d >= -Sunk && (best is null || d < best)) best = d;
        }

        foreach (var p in moving.Positions)
            if (stillFaces.Contains(p))
                foreach (float z in stillFaces.FacesAt(p)) Take(p.Z - z);

        foreach (var p in still.Positions)
            if (movingFaces.Contains(p))
                foreach (float z in movingFaces.FacesAt(p)) Take(z - p.Z);

        // Edge against edge, where they cross in plan.
        foreach (var (a, b) in Edges(moving))
        {
            if (MathF.Max(a.X, b.X) < x0 || MathF.Min(a.X, b.X) > x1 || MathF.Max(a.Y, b.Y) < y0 || MathF.Min(a.Y, b.Y) > y1) continue;

            foreach (var (c, d) in stillFaces.EdgesNear(a, b))
                if (Cross(a, b, c, d) is { } at) Take(at.Moving - at.Still);
        }

        return best;
    }

    /// <summary>Where two edges cross in plan, and how high each is there.</summary>
    private static (float Moving, float Still)? Cross(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var r = new Vector2(b.X - a.X, b.Y - a.Y);
        var s = new Vector2(d.X - c.X, d.Y - c.Y);
        float denominator = r.X * s.Y - r.Y * s.X;
        if (MathF.Abs(denominator) < 1e-9f) return null;

        var q = new Vector2(c.X - a.X, c.Y - a.Y);
        float t = (q.X * s.Y - q.Y * s.X) / denominator;
        float u = (q.X * r.Y - q.Y * r.X) / denominator;
        if (t < 0f || t > 1f || u < 0f || u > 1f) return null;

        return (a.Z + (b.Z - a.Z) * t, c.Z + (d.Z - c.Z) * u);
    }

    private static IEnumerable<(Vector3 A, Vector3 B)> Edges(Mesh mesh)
    {
        var seen = new HashSet<(int, int)>();
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            for (int k = 0; k < 3; k++)
            {
                int i = mesh.Indices[t + k], j = mesh.Indices[t + (k + 1) % 3];
                if (seen.Add(i < j ? (i, j) : (j, i))) yield return (mesh.Positions[i], mesh.Positions[j]);
            }
    }

    /// <summary>
    /// A mesh's faces - only those facing the way asked, up or down - and its edges, sorted into
    /// cells over a piece of the plan.
    /// </summary>
    private sealed class Plan
    {
        private const int Cells = 96;

        private readonly Mesh mesh;
        private readonly float x0, y0, cell;
        private readonly int across, along;
        private readonly List<int>[] faces;
        private readonly List<(Vector3, Vector3)>[] edges;

        public Plan(Mesh mesh, float x0, float y0, float x1, float y1, bool up)
        {
            this.mesh = mesh;
            this.x0 = x0;
            this.y0 = y0;
            cell = MathF.Max(MathF.Max(x1 - x0, y1 - y0) / Cells, 1e-3f);
            across = Math.Max(1, (int)MathF.Ceiling((x1 - x0) / cell));
            along = Math.Max(1, (int)MathF.Ceiling((y1 - y0) / cell));
            faces = new List<int>[across * along];
            edges = new List<(Vector3, Vector3)>[across * along];

            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                var a = mesh.Positions[mesh.Indices[t]];
                var b = mesh.Positions[mesh.Indices[t + 1]];
                var c = mesh.Positions[mesh.Indices[t + 2]];

                float facing = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (up ? facing <= 1e-9f : facing >= -1e-9f) continue;

                foreach (int i in CellsOver(MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Min(a.Y, MathF.Min(b.Y, c.Y)),
                             MathF.Max(a.X, MathF.Max(b.X, c.X)), MathF.Max(a.Y, MathF.Max(b.Y, c.Y))))
                    (faces[i] ??= []).Add(t);
            }

            if (!up) return;
            foreach (var (a, b) in Edges(mesh))
                foreach (int i in CellsOver(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y)))
                    (edges[i] ??= []).Add((a, b));
        }

        public bool Contains(Vector3 p) =>
            p.X >= x0 && p.Y >= y0 && p.X <= x0 + across * cell && p.Y <= y0 + along * cell;

        private IEnumerable<int> CellsOver(float ax, float ay, float bx, float by)
        {
            int i0 = Math.Clamp((int)((ax - x0) / cell), 0, across - 1), i1 = Math.Clamp((int)((bx - x0) / cell), 0, across - 1);
            int j0 = Math.Clamp((int)((ay - y0) / cell), 0, along - 1), j1 = Math.Clamp((int)((by - y0) / cell), 0, along - 1);
            if (bx < x0 || by < y0 || ax > x0 + across * cell || ay > y0 + along * cell) yield break;

            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    yield return j * across + i;
        }

        /// <summary>How high each face over or under a point is, straight up and down through it.</summary>
        public IEnumerable<float> FacesAt(Vector3 p)
        {
            foreach (int k in CellsOver(p.X, p.Y, p.X, p.Y))
            {
                if (faces[k] is not { } here) continue;
                foreach (int t in here)
                {
                    var a = mesh.Positions[mesh.Indices[t]];
                    var b = mesh.Positions[mesh.Indices[t + 1]];
                    var c = mesh.Positions[mesh.Indices[t + 2]];

                    float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    float u = ((b.X - p.X) * (c.Y - p.Y) - (b.Y - p.Y) * (c.X - p.X)) / area;
                    float v = ((c.X - p.X) * (a.Y - p.Y) - (c.Y - p.Y) * (a.X - p.X)) / area;
                    float w = 1f - u - v;
                    if (u < -1e-5f || v < -1e-5f || w < -1e-5f) continue;

                    yield return u * a.Z + v * b.Z + w * c.Z;
                }
            }
        }

        public IEnumerable<(Vector3, Vector3)> EdgesNear(Vector3 a, Vector3 b)
        {
            var seen = new HashSet<(Vector3, Vector3)>();
            foreach (int k in CellsOver(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y)))
                if (edges[k] is { } here)
                    foreach (var e in here)
                        if (seen.Add(e)) yield return e;
        }
    }
}
