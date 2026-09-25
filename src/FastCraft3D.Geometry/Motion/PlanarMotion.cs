using System.Numerics;

namespace FastCraft3D.Geometry.Motion;

/// <summary>How a body may move: not at all, turning about a point, or sliding along a line.</summary>
public enum Joint
{
    Fixed,
    Revolute,
    Prismatic
}

/// <summary>One outline of a body, in one layer. Outlines only run into outlines in the same layer.</summary>
/// <param name="Layer">Which slice of the mechanism it is in: a gear's teeth, the lock above them.</param>
/// <param name="Points">A closed loop in the plane, where it is when the body is at nought.</param>
public sealed record PlanarLoop(int Layer, Vector2[] Points);

/// <summary>A part of a mechanism, seen from above.</summary>
/// <param name="Name">What it is called in a report.</param>
/// <param name="Joint">How it may move.</param>
/// <param name="Centre">The point it turns about, for a revolute body.</param>
/// <param name="Direction">The way it slides, for a prismatic one; a unit vector.</param>
/// <param name="Loops">Its outlines.</param>
/// <param name="Reach">
/// The most it may be pushed in one step - radians if it turns, millimetres if it slides - or
/// nought for what the run is given.
/// </param>
/// <param name="Returns">
/// Drawn back towards where it started whenever nothing holds it there: a cam's follower on its
/// spring, which would otherwise stay wherever it was last pushed.
/// </param>
public sealed record PlanarBody(
    string Name, Joint Joint, Vector2 Centre, Vector2 Direction, IReadOnlyList<PlanarLoop> Loops,
    double Reach = 0, bool Returns = false)
{
    public static PlanarBody Turning(string name, Vector2 centre, params PlanarLoop[] loops) =>
        new(name, Joint.Revolute, centre, Vector2.UnitX, loops);

    public static PlanarBody Sliding(string name, Vector2 direction, params PlanarLoop[] loops) =>
        new(name, Joint.Prismatic, Vector2.Zero, Vector2.Normalize(direction), loops);

    public static PlanarBody Standing(string name, params PlanarLoop[] loops) =>
        new(name, Joint.Fixed, Vector2.Zero, Vector2.UnitX, loops);
}

/// <summary>One step of a run: where every body is, and, if it jammed there, which two bodies met.</summary>
public sealed record MotionStep(double[] At, string? Jam, string? Against);

/// <param name="JammedAt">How far the driver had turned when nothing could move, in degrees; null if it never jammed.</param>
/// <param name="Jam">Which two bodies met at the jam.</param>
/// <param name="Least">Each body's least coordinate over the run: an angle in radians, or a slide in millimetres.</param>
/// <param name="Most">Each body's greatest.</param>
/// <param name="Final">Where each body ended.</param>
public sealed record MotionRun(
    double? JammedAt,
    string? Jam,
    IReadOnlyList<double> Least,
    IReadOnlyList<double> Most,
    IReadOnlyList<double> Final)
{
    public bool Jammed => JammedAt is not null;
}

/// <summary>
/// Turns a mechanism's driver in small steps and moves everything else only when it is pushed,
/// as far as clears it and no further: friction without inertia, which is what a printed toy
/// turned slowly by hand is. A step that no push either way clears is a jam.
///
/// It is the reciprocating frame's test generalised. That frame passed every static test and
/// jammed both ways round the moment it was turned; what caught it was a drive simulator in the
/// flat - turn the gear a little, let the frame slide only when pushed, report a jam. Here a
/// pushed body can turn as well as slide, which is what a Geneva wheel or a pawl does: the same
/// search, on an angle.
///
/// It says nothing about anything that coasts. A mechanism that only works because nothing moves
/// unless pushed is one a lock should be holding, and the lock is what the frame's own check
/// measures.
/// </summary>
public static class PlanarMotion
{
    /// <param name="bodies">The mechanism. Fixed bodies never move; the rest move only when pushed.</param>
    /// <param name="driver">The body that is turned: revolute.</param>
    /// <param name="turns">How far to turn it, in whole turns; negative turns it the other way.</param>
    /// <param name="start">Where each body starts: an angle or a slide, one per body. Null starts them all at nought.</param>
    /// <param name="stepDegrees">How far the driver turns each step.</param>
    /// <param name="reach">The most a pushed body may move in one step: radians if it turns, millimetres if it slides.</param>
    public static MotionRun Drive(
        IReadOnlyList<PlanarBody> bodies, int driver, double turns,
        IReadOnlyList<double>? start = null, double stepDegrees = 0.5, double reach = 2.0)
    {
        var first = start?.ToArray() ?? new double[bodies.Count];
        var least = first.ToArray();
        var most = first.ToArray();
        var at = first;
        int s = 0;

        foreach (var step in Steps(bodies, driver, turns, start, stepDegrees, reach))
        {
            s++;
            at = step.At;
            if (step.Jam is not null)
                return new(s * stepDegrees * Math.Sign(turns), $"{step.Jam} and {step.Against}", least, most, at);

            for (int b = 0; b < bodies.Count; b++)
            {
                least[b] = Math.Min(least[b], at[b]);
                most[b] = Math.Max(most[b], at[b]);
            }
        }

        return new(null, null, least, most, at);
    }

    /// <summary>
    /// The run a step at a time: where every body is after each turn of the driver, the last one
    /// saying which two bodies met if it jammed. What an animation plays, and what
    /// <see cref="Drive"/> sums up.
    /// </summary>
    public static IEnumerable<MotionStep> Steps(
        IReadOnlyList<PlanarBody> bodies, int driver, double turns,
        IReadOnlyList<double>? start = null, double stepDegrees = 0.5, double reach = 2.0)
    {
        if (bodies[driver].Joint != Joint.Revolute) throw new ArgumentException("The driver has to turn.", nameof(driver));

        var at = start?.ToArray() ?? new double[bodies.Count];
        var home = at.ToArray();
        double step = Math.Sign(turns) * stepDegrees * Math.PI / 180.0;
        int steps = (int)Math.Round(Math.Abs(turns) * 360.0 / stepDegrees);

        for (int s = 1; s <= steps; s++)
        {
            at[driver] += step;
            string? jam = null, against = null;

            // Pushed in order, each held only by what has already moved this step - the driver,
            // what stands still, and the bodies before it. A chain of gears would otherwise jam at
            // once: the middle shaft cannot turn to clear the pinion driving it while the gear it
            // drives is held where it was, and that gear's turn comes after. Then once more round
            // with everything in place, for whatever the later pushes pressed back into.
            for (int pass = 0; pass < 2 && jam is null; pass++)
            for (int b = 0; b < bodies.Count && jam is null; b++)
            {
                if (b == driver || bodies[b].Joint == Joint.Fixed) continue;
                double far = bodies[b].Reach > 0 ? bodies[b].Reach : reach;

                // Everything it may meet stands still while this one is searched, so it is laid out once.
                var field = new Field(bodies, at, b, pass == 0 ? Later(b) : null);
                if (field.Hit(bodies[b], at[b]) is not null)
                {
                    double? ahead = Clear(field, bodies[b], at[b], +1, far), behind = Clear(field, bodies[b], at[b], -1, far);
                    if (ahead is null && behind is null)
                    {
                        jam = bodies[b].Name;
                        against = field.Hit(bodies[b], at[b]);
                        break;
                    }

                    at[b] += ahead is not null && (behind is null || ahead <= behind) ? ahead.Value : -behind!.Value;
                }
                else if (pass == 0 && bodies[b].Returns && Math.Abs(home[b] - at[b]) > 1e-9)
                {
                    // Back towards home as far as it is free to go, and no further than home.
                    int toward = Math.Sign(home[b] - at[b]);
                    double free = FreeIn(field, bodies[b], at[b], toward, Math.Min(far, Math.Abs(home[b] - at[b])));
                    at[b] += toward * free;
                }
            }

            // A body nothing could push is only jammed if the driver itself runs into something.
            if (jam is null && new Field(bodies, at, driver).Hit(bodies[driver], at[driver]) is { } struck)
            {
                jam = bodies[driver].Name;
                against = struck;
            }

            yield return new MotionStep(at.ToArray(), jam, against);
            if (jam is not null) yield break;
        }

        // The moving bodies after b, which have not been pushed yet this step.
        Func<int, bool> Later(int b) => o => o > b && o != driver && bodies[o].Joint != Joint.Fixed;
    }

    /// <summary>How far a body can move one way within a field before it touches it, up to <paramref name="reach"/>.</summary>
    private static double FreeIn(Field field, PlanarBody body, double start, int sign, double reach)
    {
        if (field.Hit(body, start + sign * reach) is null) return reach;

        double low = 0, high = reach;
        for (int i = 0; i < 12; i++)
        {
            double middle = (low + high) / 2.0;
            if (field.Hit(body, start + sign * middle) is not null) high = middle; else low = middle;
        }

        return low;
    }

    /// <summary>How far body <paramref name="b"/> can move one way before it touches anything, up to <paramref name="reach"/>.</summary>
    public static double Free(IReadOnlyList<PlanarBody> bodies, IReadOnlyList<double> at, int b, int sign, double reach)
    {
        var field = new Field(bodies, at, b);
        double start = at[b];
        double d = reach / 400.0;

        while (d <= reach && field.Hit(bodies[b], start + sign * d) is null) d *= 1.5;
        if (d > reach) return reach;

        double low = d / 1.5, high = d;
        for (int i = 0; i < 12; i++)
        {
            double middle = (low + high) / 2.0;
            if (field.Hit(bodies[b], start + sign * middle) is not null) high = middle; else low = middle;
        }

        return low;
    }

    /// <summary>The name of a body that body <paramref name="b"/> runs into, where they stand; null if none.</summary>
    public static string? Hit(IReadOnlyList<PlanarBody> bodies, IReadOnlyList<double> at, int b) =>
        new Field(bodies, at, b).Hit(bodies[b], at[b]);

    /// <summary>The least move one way that clears the body, or null if nothing within reach does.</summary>
    private static double? Clear(Field field, PlanarBody body, double start, int sign, double reach)
    {
        double d = reach / 1000.0;
        while (d <= reach && field.Hit(body, start + sign * d) is not null) d *= 1.6;
        if (d > reach) return null;

        double low = d / 1.6, high = d;
        for (int i = 0; i < 10; i++)
        {
            double middle = (low + high) / 2.0;
            if (field.Hit(body, start + sign * middle) is not null) low = middle; else high = middle;
        }

        return high;
    }

    /// <summary>A body's loops where it stands at <paramref name="coordinate"/>.</summary>
    private static IEnumerable<(int Layer, Vector2[] Points)> Placed(PlanarBody body, double coordinate)
    {
        Func<Vector2, Vector2> move = body.Joint switch
        {
            Joint.Revolute => Turn(body.Centre, coordinate),
            Joint.Prismatic => p => p + body.Direction * (float)coordinate,
            _ => p => p
        };

        return body.Loops.Select(l => (l.Layer, l.Points.Select(move).ToArray()));

        static Func<Vector2, Vector2> Turn(Vector2 centre, double angle)
        {
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            return p =>
            {
                var d = p - centre;
                return centre + new Vector2(cos * d.X - sin * d.Y, sin * d.X + cos * d.Y);
            };
        }
    }

    /// <summary>
    /// Every body but one, where they stand, their edges filed in a grid by layer: what the one
    /// body is tried against as it is moved. Bodies start clear and move in small steps, so one
    /// passing into another shows as edges crossing before it shows as anything else - which is
    /// what makes edges enough, as the frame's test found.
    /// </summary>
    private sealed class Field
    {
        // Two millimetres. An edge is filed in every cell its box covers, and a thinned outline has
        // long straight edges: at half a millimetre one of them covered hundreds of cells, and
        // filing the field three times a degree was nine tenths of the Geneva's second-long build.
        // The size cannot change an answer - two edges that cross share the cell they cross in -
        // only how many pairs are tried: 880 ms at 0.5, 250 at 1, 116 at 2, 132 at 4.
        private const float Cell = 2f;
        private readonly List<(int Layer, string Name, Vector2 A, Vector2 B)> edges = [];
        private readonly Dictionary<(int, int, int), List<int>> cells = [];

        public Field(IReadOnlyList<PlanarBody> bodies, IReadOnlyList<double> at, int except, Func<int, bool>? leaveOut = null)
        {
            for (int o = 0; o < bodies.Count; o++)
            {
                if (o == except || leaveOut?.Invoke(o) == true) continue;

                foreach (var (layer, loop) in Placed(bodies[o], at[o]))
                    for (int i = 0; i < loop.Length; i++)
                    {
                        var a = loop[i];
                        var b = loop[(i + 1) % loop.Length];
                        edges.Add((layer, bodies[o].Name, a, b));
                        foreach (var (x, y) in Covered(a, b))
                        {
                            if (!cells.TryGetValue((layer, x, y), out var list)) cells[(layer, x, y)] = list = [];
                            list.Add(edges.Count - 1);
                        }
                    }
            }
        }

        public string? Hit(PlanarBody body, double coordinate)
        {
            foreach (var (layer, loop) in Placed(body, coordinate))
                for (int i = 0; i < loop.Length; i++)
                {
                    var p = loop[i];
                    var q = loop[(i + 1) % loop.Length];
                    foreach (var (x, y) in Covered(p, q))
                    {
                        if (!cells.TryGetValue((layer, x, y), out var list)) continue;
                        foreach (int k in list)
                            if (Cross(p, q, edges[k].A, edges[k].B)) return edges[k].Name;
                    }
                }

            return null;
        }

        private static IEnumerable<(int, int)> Covered(Vector2 a, Vector2 b)
        {
            int x0 = (int)MathF.Floor(MathF.Min(a.X, b.X) / Cell), x1 = (int)MathF.Floor(MathF.Max(a.X, b.X) / Cell);
            int y0 = (int)MathF.Floor(MathF.Min(a.Y, b.Y) / Cell), y1 = (int)MathF.Floor(MathF.Max(a.Y, b.Y) / Cell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    yield return (x, y);
        }

        private static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            static float Side(Vector2 p, Vector2 q, Vector2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            float d1 = Side(c, d, a), d2 = Side(c, d, b), d3 = Side(a, b, c), d4 = Side(a, b, d);
            return d1 * d2 < 0 && d3 * d4 < 0;
        }
    }
}

/// <summary>
/// A solid cut across at a height, as loops: the outlines the planar check works on, taken from
/// a part as it will print rather than drawn a second time beside it.
/// </summary>
public static class Sections
{
    public static List<Vector2[]> At(Mesh mesh, float z)
    {
        var segments = new List<(Vector2 A, Vector2 B)>();
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var p = new[] { mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i + 1]], mesh.Positions[mesh.Indices[i + 2]] };
            var crossing = new List<Vector2>(2);

            for (int e = 0; e < 3; e++)
            {
                var a = p[e];
                var b = p[(e + 1) % 3];
                if ((a.Z - z) * (b.Z - z) >= 0) continue;

                // Always from the same end of the edge: its two triangles walk it opposite ways,
                // and interpolated each from its own end they gave points a rounding apart - which
                // on a part moved into place fell either side of the grid below, and the outline
                // came back as fragments, each closing straight across the solid.
                if (Less(b, a)) (a, b) = (b, a);
                float t = (z - a.Z) / (b.Z - a.Z);
                var hit = a + (b - a) * t;
                crossing.Add(new Vector2(hit.X, hit.Y));
            }

            if (crossing.Count == 2) segments.Add((crossing[0], crossing[1]));
        }

        // Chained end to end, by where their ends fall on a fine grid.
        static (long, long) Key(Vector2 v) => ((long)MathF.Round(v.X * 1e4f), (long)MathF.Round(v.Y * 1e4f));
        static bool Less(Vector3 a, Vector3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

        var from = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            foreach (var end in new[] { segments[i].A, segments[i].B })
            {
                var key = Key(end);
                if (!from.TryGetValue(key, out var list)) from[key] = list = [];
                list.Add(i);
            }
        }

        var used = new bool[segments.Count];
        var loops = new List<Vector2[]>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;

            var loop = new List<Vector2> { segments[i].A };
            var next = segments[i].B;
            while (Vector2.DistanceSquared(next, loop[0]) > 1e-8f)
            {
                loop.Add(next);
                int found = from.TryGetValue(Key(next), out var candidates) ? candidates.FirstOrDefault(c => !used[c], -1) : -1;
                if (found < 0) break;

                used[found] = true;
                next = Vector2.DistanceSquared(segments[found].A, next) < Vector2.DistanceSquared(segments[found].B, next)
                    ? segments[found].B
                    : segments[found].A;
            }

            if (loop.Count >= 3) loops.Add(loop.ToArray());
        }

        return loops;
    }

    /// <summary>
    /// A loop with points dropped wherever it is straight to within <paramref name="tolerance"/>:
    /// a gear's outline cut across is two thousand points, most of them on curves gentle enough
    /// that a tenth of them draw it as well, and every step of a run tries every edge.
    /// </summary>
    public static Vector2[] Thinned(Vector2[] loop, float tolerance)
    {
        if (loop.Length < 8) return loop;

        var keep = new bool[loop.Length];
        keep[0] = true;
        int far = 1;
        for (int i = 1; i < loop.Length; i++)
            if (Vector2.DistanceSquared(loop[i], loop[0]) > Vector2.DistanceSquared(loop[far], loop[0])) far = i;
        keep[far] = true;

        Mark(0, far);
        Mark(far, loop.Length);
        return loop.Where((_, i) => keep[i]).ToArray();

        void Mark(int from, int to)
        {
            var stack = new Stack<(int, int)>();
            stack.Push((from, to));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b - a < 2) continue;

                var p = loop[a];
                var q = loop[b % loop.Length];
                var d = q - p;
                float length = d.Length();
                int worst = -1;
                float most = tolerance;
                for (int i = a + 1; i < b; i++)
                {
                    float off = length < 1e-9f ? Vector2.Distance(loop[i], p) : MathF.Abs(d.X * (loop[i].Y - p.Y) - d.Y * (loop[i].X - p.X)) / length;
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
