using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// A picture turned into outlines to stamp, as 3D Builder embossed a PNG: where there is ink is
/// inside, where there is none is out. What comes out is what an SVG drawing gives - outlines and
/// the holes in them - so Emboss handles a picture exactly as it handles a drawing.
///
/// Marching squares on the ink itself rather than on pixels made black or white first: the edge
/// is put where the ink crosses the threshold between two samples, so a smooth curve in the
/// picture comes out a smooth curve rather than a staircase of pixel edges.
/// </summary>
public static class PictureTrace
{
    /// <summary>
    /// Where to split ink from none: Otsu's, the level that best separates the picture's two
    /// halves. A logo on white and a scan with a grey background both come out right without
    /// asking; the panel lets it be moved when they do not.
    /// </summary>
    public static float Threshold(Greyscale ink)
    {
        const int bins = 64;
        var counts = new double[bins];
        foreach (float v in ink.Samples) counts[Math.Clamp((int)(v * bins), 0, bins - 1)]++;

        double total = ink.Samples.Length, sum = 0;
        for (int i = 0; i < bins; i++) sum += i * counts[i];

        // A clean picture - ink and paper and nothing between - scores every split between the two
        // alike, and the first of them sits hard against the paper; the middle of the run is the
        // level halfway between.
        double below = 0, belowSum = 0, best = -1;
        int first = bins / 2, last = bins / 2;
        for (int i = 0; i < bins - 1; i++)
        {
            below += counts[i];
            belowSum += i * counts[i];
            double above = total - below;
            if (below == 0 || above == 0) continue;

            double difference = belowSum / below - (sum - belowSum) / above;
            double spread = below * above * difference * difference;
            if (spread > best * (1 + 1e-9))
            {
                best = spread;
                first = last = i + 1;
            }
            else if (spread >= best * (1 - 1e-9))
            {
                last = i + 1;
            }
        }

        return Math.Clamp((first + last) / 2f / bins, 0.05f, 0.95f);
    }

    /// <summary>
    /// The outlines of the ink, the whole lot <paramref name="heightMm"/> tall with its middle at
    /// the origin, Y up. Specks smaller than a few samples are left out, and every outline is
    /// straightened to within a third of a sample, which is far finer than a nozzle draws.
    /// </summary>
    public static List<TextShape> Outlines(Greyscale ink, float threshold, float heightMm)
    {
        int w = ink.Width, h = ink.Height;
        if (w < 2 || h < 2 || heightMm <= 0) return [];

        // A border of no ink all round, so ink running off the edge of the picture still closes.
        int pw = w + 2, ph = h + 2;
        float At(int i, int j) => i <= 0 || j <= 0 || i > w || j > h ? 0f : ink.Samples[(j - 1) * w + (i - 1)];

        // The crossing on each edge between two grid points, by its number: even along X, odd along Y.
        var point = new Dictionary<int, Vector2>();
        var links = new Dictionary<int, List<int>>();

        int Along(int i, int j) => (j * pw + i) * 2;
        int Down(int i, int j) => (j * pw + i) * 2 + 1;

        Vector2 Crossing(int edge)
        {
            if (point.TryGetValue(edge, out var p)) return p;
            int cell = edge / 2, i = cell % pw, j = cell / pw;
            bool along = edge % 2 == 0;
            float a = At(i, j), b = along ? At(i + 1, j) : At(i, j + 1);
            float t = MathF.Abs(b - a) < 1e-6f ? 0.5f : Math.Clamp((threshold - a) / (b - a), 0.001f, 0.999f);
            p = along ? new Vector2(i - 0.5f + t, j - 0.5f) : new Vector2(i - 0.5f, j - 0.5f + t);
            point[edge] = p;
            return p;
        }

        void Link(int a, int b)
        {
            Crossing(a);
            Crossing(b);
            if (!links.TryGetValue(a, out var la)) links[a] = la = [];
            if (!links.TryGetValue(b, out var lb)) links[b] = lb = [];
            la.Add(b);
            lb.Add(a);
        }

        for (int j = 0; j < ph - 1; j++)
            for (int i = 0; i < pw - 1; i++)
            {
                float a = At(i, j), b = At(i + 1, j), c = At(i + 1, j + 1), d = At(i, j + 1);
                int code = (a >= threshold ? 1 : 0) | (b >= threshold ? 2 : 0) | (c >= threshold ? 4 : 0) | (d >= threshold ? 8 : 0);
                if (code is 0 or 15) continue;

                int top = Along(i, j), bottom = Along(i, j + 1), left = Down(i, j), right = Down(i + 1, j);
                bool middle = (a + b + c + d) / 4f >= threshold;

                switch (code)
                {
                    case 1: case 14: Link(left, top); break;
                    case 2: case 13: Link(top, right); break;
                    case 3: case 12: Link(left, right); break;
                    case 4: case 11: Link(right, bottom); break;
                    case 6: case 9: Link(top, bottom); break;
                    case 7: case 8: Link(left, bottom); break;
                    case 5:
                        if (middle) { Link(top, right); Link(bottom, left); }
                        else { Link(left, top); Link(right, bottom); }
                        break;
                    case 10:
                        if (middle) { Link(left, top); Link(right, bottom); }
                        else { Link(top, right); Link(bottom, left); }
                        break;
                }
            }

        // Each crossing has two neighbours: walk them round into loops.
        var loops = new List<List<Vector2>>();
        var done = new HashSet<int>();
        foreach (int start in links.Keys)
        {
            if (done.Contains(start)) continue;

            var loop = new List<Vector2>();
            int previous = -1, at = start;
            while (done.Add(at))
            {
                loop.Add(point[at]);
                var next = links[at];
                int go = next[0] == previous && next.Count > 1 ? next[1] : next[0];
                previous = at;
                at = go;
            }

            if (loop.Count >= 3 && MathF.Abs(Polygon2.SignedArea(loop)) >= 3f)
                loops.Add(Straightened(loop, 0.35f));
        }

        loops.RemoveAll(l => l.Count < 3);
        if (loops.Count == 0) return [];

        // Y up, the whole lot the height asked for, its middle on the origin - as a drawing comes in.
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var loop in loops)
            for (int k = 0; k < loop.Count; k++)
            {
                loop[k] = new Vector2(loop[k].X, -loop[k].Y);
                min = Vector2.Min(min, loop[k]);
                max = Vector2.Max(max, loop[k]);
            }

        float scale = heightMm / MathF.Max(max.Y - min.Y, 1e-3f);
        var centre = (min + max) * 0.5f;
        foreach (var loop in loops)
            for (int k = 0; k < loop.Count; k++)
                loop[k] = (loop[k] - centre) * scale;

        return Polygon2.Nest(loops).Select(s => new TextShape(s.Outline, s.Holes)).ToList();
    }

    /// <summary>A closed loop with the points that lie within <paramref name="tolerance"/> of a straight run taken out.</summary>
    private static List<Vector2> Straightened(List<Vector2> loop, float tolerance)
    {
        // Split at the point farthest from the first, and straighten each half as an open run.
        int far = 0;
        for (int i = 1; i < loop.Count; i++)
            if (Vector2.DistanceSquared(loop[i], loop[0]) > Vector2.DistanceSquared(loop[far], loop[0])) far = i;

        var keep = new bool[loop.Count];
        keep[0] = keep[far] = true;
        Run(0, far);
        Run(far, loop.Count);

        var kept = new List<Vector2>();
        for (int i = 0; i < loop.Count; i++)
            if (keep[i]) kept.Add(loop[i]);
        return kept;

        void Run(int from, int to)
        {
            var stack = new Stack<(int, int)>();
            stack.Push((from, to));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b - a < 2) continue;

                var p = loop[a];
                var q = loop[b % loop.Count];
                var line = q - p;
                float length = line.Length();

                int worst = -1;
                float most = tolerance;
                for (int i = a + 1; i < b; i++)
                {
                    var r = loop[i] - p;
                    float off = length < 1e-6f ? r.Length() : MathF.Abs(line.X * r.Y - line.Y * r.X) / length;
                    if (off > most)
                    {
                        most = off;
                        worst = i;
                    }
                }

                if (worst < 0) continue;
                keep[worst] = true;
                stack.Push((a, worst));
                stack.Push((worst, b));
            }
        }
    }
}
