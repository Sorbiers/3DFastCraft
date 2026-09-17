using System.Numerics;

namespace FastCraft3D.Geometry.Drawings;

public enum DrawingView
{
    Front,
    Top,
    Right,
    Isometric
}

/// <summary>A line on a view, in model millimetres, X to the right and Y up.</summary>
public readonly record struct Line2(Vector2 A, Vector2 B);

/// <summary>One view of the model: the lines seen, the lines behind something, and how far it reaches.</summary>
public sealed record ViewLines(DrawingView View, List<Line2> Visible, List<Line2> Hidden, Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;

    public static ViewLines Empty(DrawingView view) => new(view, [], [], Vector2.Zero, Vector2.Zero);
}

/// <summary>
/// The lines a view of the model is drawn with, as a draughtsman draws them: the outline and every
/// sharp edge, solid where it can be seen and dashed where something is in front of it.
///
/// Which edges: where two faces meet at an angle, where a face has no neighbour, and the
/// silhouette - where a face turned towards the viewer meets one turned away, which is the only
/// edge a cylinder has from the side. Every edge of the triangles would draw the tessellation, not
/// the part.
///
/// What hides them: the model is drawn into a depth buffer, one pixel a few hundredths of a
/// millimetre on a small part, and each edge is walked along and compared with it. An edge on a
/// surface is as deep as that surface, so it counts as seen when anything round it is no nearer
/// than it is; a line behind a face has the face in front of it on every side. A hidden line that
/// lies along a seen one - the back edges of a box, straight behind the front ones - is left out,
/// or every outline would be dashed over itself.
/// </summary>
public static class ViewDrawing
{
    /// <summary>Where two faces meet at more than this, the edge is drawn. A 32-sided cylinder turns 11 degrees a face and is left smooth.</summary>
    public const float CreaseDegrees = 30f;

    /// <summary>Pixels across the longer side of the depth buffer.</summary>
    public const int Resolution = 1600;

    /// <summary>
    /// The view's own axes: to the right and up on the paper, and away from the viewer. Front looks
    /// along +Y, the top view down, the right view from +X; the isometric from the front right,
    /// above, as the app's own isometric view does.
    /// </summary>
    public static (Vector3 Right, Vector3 Up, Vector3 Away) Axes(DrawingView view)
    {
        switch (view)
        {
            case DrawingView.Front: return (Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
            case DrawingView.Top: return (Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ);
            case DrawingView.Right: return (Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitX);
            default:
            {
                var away = Vector3.Normalize(new Vector3(-1f, 1f, -1f));
                var up = Vector3.Normalize(Vector3.UnitZ - away * Vector3.Dot(Vector3.UnitZ, away));
                return (Vector3.Cross(away, up), up, away);
            }
        }
    }

    public static ViewLines Build(IReadOnlyList<Mesh> meshes, DrawingView view,
                                  float creaseDegrees = CreaseDegrees, int resolution = Resolution,
                                  CancellationToken token = default)
    {
        var (right, up, away) = Axes(view);
        var solids = meshes.Where(m => m.TriangleCount > 0).Select(m => m.Welded()).ToList();
        if (solids.Count == 0) return ViewLines.Empty(view);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var mesh in solids)
            foreach (var p in mesh.Positions)
            {
                var flat = new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up));
                min = Vector2.Min(min, flat);
                max = Vector2.Max(max, flat);
            }

        var span = max - min;
        float pixel = MathF.Max(MathF.Max(span.X, span.Y) / resolution, 1e-4f);
        var buffer = new DepthBuffer(min, pixel, (int)MathF.Ceiling(span.X / pixel) + 3, (int)MathF.Ceiling(span.Y / pixel) + 3);

        foreach (var mesh in solids)
        {
            token.ThrowIfCancellationRequested();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                buffer.Fill(Project(mesh.Positions[mesh.Indices[t]]),
                            Project(mesh.Positions[mesh.Indices[t + 1]]),
                            Project(mesh.Positions[mesh.Indices[t + 2]]));
        }

        var edges = new List<(Vector3 A, Vector3 B)>();
        foreach (var mesh in solids) edges.AddRange(Edges(mesh, away, creaseDegrees));

        // Every edge walked once and its samples marked seen or not. A sample on a line an earlier
        // edge has already drawn is left out, so edges that lie along each other - the back of a
        // box straight behind its front - are drawn once; and a hidden sample on any seen line is
        // left out, or every outline would be dashed over itself.
        float slack = 4f * pixel;
        var walks = new List<(Vector3 A, Vector3 B, bool[] Seen, int[] At)>(edges.Count);
        var drawnSeen = new bool[buffer.Width * buffer.Height];

        foreach (var (a, b) in edges)
        {
            token.ThrowIfCancellationRequested();
            var pa = Project(a);
            var pb = Project(b);
            float length = Vector2.Distance(new(pa.X, pa.Y), new(pb.X, pb.Y)) / pixel;
            if (length < 0.25f) continue;

            int samples = Math.Max(2, (int)MathF.Ceiling(length * 2f));
            var seen = new bool[samples];
            var at = new int[samples];
            for (int i = 0; i < samples; i++)
            {
                var point = Vector3.Lerp(pa, pb, (i + 0.5f) / samples);
                at[i] = buffer.Index(point.X, point.Y);
                seen[i] = buffer.Farthest(point.X, point.Y) >= point.Z - slack;
            }

            walks.Add((pa, pb, seen, at));
        }

        var visible = new List<Line2>();
        foreach (var (pa, pb, seen, at) in walks)
        {
            var drawn = new bool[seen.Length];
            for (int i = 0; i < seen.Length; i++) drawn[i] = seen[i] && !Marked(drawnSeen, at[i]);

            Runs(i => drawn[i], seen.Length, pa, pb, visible);
            for (int i = 0; i < seen.Length; i++) if (drawn[i]) buffer.Mark(drawnSeen, at[i]);
        }

        var hidden = new List<Line2>();
        var drawnHidden = new bool[buffer.Width * buffer.Height];
        foreach (var (pa, pb, seen, at) in walks)
        {
            var drawn = new bool[seen.Length];
            for (int i = 0; i < seen.Length; i++)
                drawn[i] = !seen[i] && !Marked(drawnSeen, at[i]) && !Marked(drawnHidden, at[i]);

            Runs(i => drawn[i], seen.Length, pa, pb, hidden);
            for (int i = 0; i < seen.Length; i++) if (drawn[i]) buffer.Mark(drawnHidden, at[i]);
        }

        return new ViewLines(view, visible, hidden, min, max);

        Vector3 Project(Vector3 p) => new(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, away));

        static bool Marked(bool[] marks, int index) => index >= 0 && marks[index];
    }

    /// <summary>The stretches along a walked edge where a test holds, as lines.</summary>
    private static void Runs(Func<int, bool> holds, int samples, Vector3 a, Vector3 b, List<Line2> into)
    {
        int start = -1;
        for (int i = 0; i <= samples; i++)
        {
            bool on = i < samples && holds(i);
            if (on && start < 0) start = i;
            if (on || start < 0) continue;

            // From the start of the first sample's stretch to the end of the last's.
            float from = start == 0 ? 0f : (float)start / samples;
            float to = i == samples ? 1f : (float)i / samples;
            var p = Vector3.Lerp(a, b, from);
            var q = Vector3.Lerp(a, b, to);
            into.Add(new Line2(new Vector2(p.X, p.Y), new Vector2(q.X, q.Y)));
            start = -1;
        }
    }

    /// <summary>The edges worth drawing from this side: sharp, open, or on the silhouette.</summary>
    private static List<(Vector3 A, Vector3 B)> Edges(Mesh mesh, Vector3 away, float creaseDegrees)
    {
        float crease = MathF.Cos(creaseDegrees * MathF.PI / 180f);
        var faces = new Dictionary<(int, int), (Vector3 First, Vector3? Second, int Count)>(mesh.Indices.Count);

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            var normal = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            if (normal.LengthSquared() < 1e-20f) continue;
            normal = Vector3.Normalize(normal);

            Add(a, b);
            Add(b, c);
            Add(c, a);

            void Add(int i, int j)
            {
                var key = i < j ? (i, j) : (j, i);
                faces[key] = faces.TryGetValue(key, out var had)
                    ? (had.First, had.Second ?? normal, had.Count + 1)
                    : (normal, null, 1);
            }
        }

        var edges = new List<(Vector3, Vector3)>();
        foreach (var ((i, j), (first, second, count)) in faces)
        {
            bool draw = count != 2 || second is not { } other
                || Vector3.Dot(first, other) < crease
                || MathF.Sign(Vector3.Dot(first, away)) != MathF.Sign(Vector3.Dot(other, away));

            if (draw) edges.Add((mesh.Positions[i], mesh.Positions[j]));
        }

        return edges;
    }

    /// <summary>The nearest depth drawn at each pixel of a view.</summary>
    private sealed class DepthBuffer(Vector2 origin, float pixel, int width, int height)
    {
        private readonly float[] depth = CreateEmpty(width * height);

        public int Width => width;
        public int Height => height;

        private static float[] CreateEmpty(int size)
        {
            var values = new float[size];
            Array.Fill(values, float.PositiveInfinity);
            return values;
        }

        private (float X, float Y) Pixel(float x, float y) => ((x - origin.X) / pixel + 1f, (y - origin.Y) / pixel + 1f);

        public void Fill(Vector3 a, Vector3 b, Vector3 c)
        {
            var (ax, ay) = Pixel(a.X, a.Y);
            var (bx, by) = Pixel(b.X, b.Y);
            var (cx, cy) = Pixel(c.X, c.Y);

            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (MathF.Abs(area) < 1e-9f) return;

            int left = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
            int right = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
            int bottom = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
            int top = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));

            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float wa = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                    float wb = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                    float wc = 1f - wa - wb;
                    if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;

                    float z = wa * a.Z + wb * b.Z + wc * c.Z;
                    ref float held = ref depth[y * width + x];
                    if (z < held) held = z;
                }
        }

        /// <summary>The deepest of the nearest depths round a point: how far back something there could be and still be seen.</summary>
        public float Farthest(float x, float y)
        {
            var (px, py) = Pixel(x, y);
            int cx = (int)MathF.Floor(px), cy = (int)MathF.Floor(py);
            float farthest = float.NegativeInfinity;

            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = cx + dx, iy = cy + dy;
                    if (ix < 0 || iy < 0 || ix >= width || iy >= height) return float.PositiveInfinity;
                    farthest = MathF.Max(farthest, depth[iy * width + ix]);
                }

            return farthest;
        }

        /// <summary>The pixel a point falls in, or -1 off the buffer.</summary>
        public int Index(float x, float y)
        {
            var (px, py) = Pixel(x, y);
            int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py);
            return ix >= 0 && iy >= 0 && ix < width && iy < height ? iy * width + ix : -1;
        }

        /// <summary>A pixel and those round it, marked as drawn on.</summary>
        public void Mark(bool[] marks, int index)
        {
            if (index < 0) return;
            int cx = index % width, cy = index / width;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ix = cx + dx, iy = cy + dy;
                    if (ix >= 0 && iy >= 0 && ix < width && iy < height) marks[iy * width + ix] = true;
                }
        }
    }
}
