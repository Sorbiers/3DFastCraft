using System.Numerics;

namespace FastCraft3D.Geometry;

public enum ThreadKind
{
    /// <summary>A threaded rod: the outside thread of a bolt without its head.</summary>
    Rod,

    /// <summary>A nut: a hexagon or a round body with the inside thread through it.</summary>
    Nut,

    /// <summary>The inside thread's hole as a solid, to Subtract from a part.</summary>
    HoleCutter
}

public enum NutBody
{
    Hexagon,
    Round
}

/// <param name="Name">M3, M8 and so on.</param>
/// <param name="Pitch">The ISO coarse pitch.</param>
/// <param name="AcrossFlats">The spanner size of an ISO 4032 nut.</param>
/// <param name="NutHeight">The height of an ISO 4032 nut.</param>
public readonly record struct MetricSize(string Name, float Diameter, float Pitch, float AcrossFlats, float NutHeight);

/// <param name="Diameter">The nominal major diameter - 8 for M8. The clearance is taken off it, not added to it.</param>
/// <param name="Length">Along Z, for a rod or a hole cutter.</param>
/// <param name="Clearance">
/// The gap on the diameter between a rod and a nut of the same size. Half of it comes off the rod
/// and half goes on the nut, so either part also screws onto a bought one of the same size.
/// </param>
/// <param name="AcrossFlats">For a hexagon nut the spanner size; for a round one its outside diameter.</param>
/// <param name="NutHeight">Along Z, for a nut.</param>
public readonly record struct ThreadOptions(
    ThreadKind Kind, float Diameter, float Pitch, float Length, float Clearance,
    NutBody Body, float AcrossFlats, float NutHeight)
{
    public const float MinimumDiameter = 1.5f;
    public const float MaximumDiameter = 150f;
    public const float MinimumPitch = 0.2f;
    public const float MinimumLength = 1f;
    public const float MaximumLength = 300f;
    public const float MaximumClearance = 1f;

    /// <summary>The thinnest a nut is left at its flats: two lines from a 0.4 mm nozzle.</summary>
    public const float MinimumWall = 0.8f;

    /// <summary>
    /// Written out, because a record struct's new() skips these and hands back a thread with no
    /// diameter and no pitch. M8, since it is about the smallest size that prints well on filament.
    /// </summary>
    public static ThreadOptions Default =>
        new(ThreadKind.Rod, 8f, 1.25f, 20f, 0.2f, NutBody.Hexagon, 13f, 6.8f);

    /// <summary>
    /// The coarsest pitch a diameter takes. Much coarser and the teeth are cut so deep that the
    /// core left inside them is thinner than they are tall.
    /// </summary>
    public float MaximumPitch => Diameter / 3f;

    /// <summary>How deep the thread is cut: five eighths of the fundamental triangle, as ISO 68-1 has it.</summary>
    public float Depth => Threads.DepthPerPitch * Pitch;

    public float Height => Kind == ThreadKind.Nut ? NutHeight : Length;

    public float MinimumAcrossFlats => Diameter + Clearance / 2f + 2f * MinimumWall;

    /// <summary>The rod's outside diameter, over the crests.</summary>
    public float RodOutside => Diameter - Clearance / 2f;

    /// <summary>The rod's core, at the roots.</summary>
    public float RodCore => Diameter - 2f * Depth - Clearance / 2f;

    /// <summary>The narrowest the nut's hole is, over the crests of its thread.</summary>
    public float NutBore => Diameter - 2f * Depth + Clearance / 2f;

    /// <summary>
    /// Whether a rod and a nut of this size catch on each other at all. With a clearance of twice
    /// the depth, the rod's crests pass inside the nut's and it slides straight through.
    /// </summary>
    public bool Engages => RodOutside > NutBore;

    public string SizeName =>
        Threads.Find(Diameter, Pitch) is { } metric ? metric.Name : $"{Diameter:0.##} x {Pitch:0.##}";

    public string Name => Kind switch
    {
        ThreadKind.Rod => $"{SizeName} rod",
        ThreadKind.Nut => $"{SizeName} nut",
        _ => $"{SizeName} thread cutter"
    };

    /// <summary>The same thread with every number brought inside what it can be built from.</summary>
    public ThreadOptions Sane()
    {
        float diameter = Math.Clamp(Finite(Diameter, 8f), MinimumDiameter, MaximumDiameter);

        var sane = this with
        {
            Diameter = diameter,
            Pitch = Math.Clamp(Finite(Pitch, 1f), MinimumPitch, diameter / 3f),
            Length = Math.Clamp(Finite(Length, 20f), MinimumLength, MaximumLength),
            NutHeight = Math.Clamp(Finite(NutHeight, 5f), MinimumLength, MaximumLength),
            Clearance = Math.Clamp(Finite(Clearance, 0.2f), 0f, MaximumClearance)
        };

        float flats = Finite(AcrossFlats, 0f);
        return sane with { AcrossFlats = Math.Clamp(flats, sane.MinimumAcrossFlats, sane.MinimumAcrossFlats + 300f) };

        static float Finite(float value, float otherwise) => float.IsFinite(value) ? value : otherwise;
    }
}

/// <summary>
/// Screw threads: a threaded rod, a nut, and the solid of a threaded hole to cut into a part.
///
/// The thread is built as its own surface rather than cut. Subtracting a helix from a cylinder is
/// exactly the fine detail against a curved surface that the boolean tears on, and it would have
/// to be run again for every number typed into the panel. Built directly it is closed by
/// construction and quick enough to preview as it is typed.
///
/// The surface is a radius over angle and height. Unrolled, the corners of the profile - the ends
/// of the crest and root flats - are straight parallel lines, and the ends of the part and the
/// lead-in steps are level lines across them. Every cell is cut exactly along those lines, so the
/// profile's corners are real edges rather than being smoothed over by whichever triangles happen
/// to straddle them; a root corner smoothed over stands proud of the profile and fouls the nut.
/// Each vertex is named by the lines it sits on, and two cells that name the same one share it -
/// no welding by distance, which is what lets near-coincident points merge wrongly.
///
/// The profile is ISO 68-1's basic one: 60 degree flanks, a flat crest an eighth of the pitch wide
/// and a flat root a quarter wide, and right-handed. Both ends are led in: the thread's depth
/// fades to nothing over the last pitch, so it starts cleanly rather than on a knife-edge.
/// </summary>
public static class Threads
{
    /// <summary>The depth of the basic profile per unit of pitch: 5/8 of a fundamental triangle √3/2 tall.</summary>
    public const float DepthPerPitch = 0.54126588f;

    /// <summary>ISO 261 coarse pitches, with ISO 4032 nut sizes.</summary>
    public static readonly MetricSize[] Metric =
    [
        new("M3", 3f, 0.5f, 5.5f, 2.4f),
        new("M4", 4f, 0.7f, 7f, 3.2f),
        new("M5", 5f, 0.8f, 8f, 4.7f),
        new("M6", 6f, 1f, 10f, 5.2f),
        new("M8", 8f, 1.25f, 13f, 6.8f),
        new("M10", 10f, 1.5f, 16f, 8.4f),
        new("M12", 12f, 1.75f, 18f, 10.8f),
        new("M16", 16f, 2f, 24f, 14.8f),
        new("M20", 20f, 2.5f, 30f, 18f)
    ];

    /// <summary>The metric size with this diameter and pitch, if there is one.</summary>
    public static MetricSize? Find(float diameter, float pitch)
    {
        foreach (var m in Metric)
            if (MathF.Abs(m.Diameter - diameter) < 1e-3f && MathF.Abs(m.Pitch - pitch) < 1e-3f)
                return m;

        return null;
    }

    /// <summary>The part, centred on its own origin with its axis along Z.</summary>
    public static Mesh Build(ThreadOptions options)
    {
        var o = options.Sane();

        double radius = o.Diameter / 2.0;
        double pitch = o.Pitch;
        double depth = DepthPerPitch * pitch;
        double gap = o.Clearance / 4.0;
        double height = o.Height;
        int segments = Segments(radius);

        // Lead in over a pitch, or less on a part too short to spare it at both ends.
        double lead = Math.Min(pitch, height / 3.0);
        double Fade(double z) => Math.Clamp(Math.Min(z, height - z) / lead, 0.0, 1.0);

        if (o.Kind == ThreadKind.Rod)
        {
            var rod = new HelicalSurface(pitch, height, lead, segments,
                (t, z) => radius - gap - depth + depth * (1.0 - Share(t)) * Fade(z));
            rod.Build(outward: true);
            rod.Disc(top: false);
            rod.Disc(top: true);
            return rod.ToMesh();
        }

        // A faceted hole is smaller than the round one it stands for: its flats cut inside the circle
        // through its corners. Pushing the corners out by the sag puts the flats on the circle, so a
        // nut is never tighter than its numbers, whatever the segment count.
        double outwards = 1.0 / Math.Cos(Math.PI / segments);
        var hole = new HelicalSurface(pitch, height, lead, segments,
            (t, z) => (radius + gap - depth * Share(t) * Fade(z)) * outwards);

        if (o.Kind == ThreadKind.HoleCutter)
        {
            hole.Build(outward: true);
            hole.Disc(top: false);
            hole.Disc(top: true);
            return hole.ToMesh();
        }

        hole.Build(outward: false);
        hole.Body(o.Body, o.AcrossFlats / 2.0);
        return hole.ToMesh();
    }

    /// <summary>
    /// Round the circumference: enough that a facet's flat sags no more than a hundredth of a
    /// millimetre inside the circle, a multiple of twelve so the corners of a hexagon and the four
    /// quarters each land on one, and never so few that a small thread looks polygonal.
    /// </summary>
    private static int Segments(double radius)
    {
        double needed = Math.PI * Math.Sqrt(radius / (2 * 0.01));
        return Math.Clamp((int)Math.Ceiling(needed / 12.0) * 12, 48, 144);
    }

    /// <summary>
    /// How far down the profile the surface is at this point along the thread, from 0 on the crest
    /// to 1 in the root. <paramref name="t"/> is in pitches, with the crest starting on the whole numbers.
    /// </summary>
    private static double Share(double t)
    {
        t -= Math.Floor(t);
        if (t <= Crest) return 0.0;
        if (t <= Crest + Flank) return (t - Crest) / Flank;
        if (t <= Crest + Flank + Root) return 1.0;
        return (1.0 - t) / Flank;
    }

    private const double Crest = 1.0 / 8.0;
    private const double Root = 1.0 / 4.0;
    private const double Flank = 5.0 / 16.0;

    /// <summary>Where along each pitch the profile turns a corner: into a flank, the root, a flank, the crest.</summary>
    private static readonly double[] Corners = [0.0, Crest, Crest + Flank, Crest + Flank + Root];

    /// <summary>
    /// The thread's surface over angle and height, and the ends that close it.
    ///
    /// The helical lines are numbered upwards: line j is corner j % 4 of turn j / 4, and it reaches
    /// the height of line j + 4 by the time it has gone once round. So column N is column 0 with
    /// every line four further on, and the surface closes on itself without a seam.
    /// </summary>
    private sealed class HelicalSurface
    {
        private const int NoLine = int.MinValue;
        private const int Steps = 8;

        private readonly double pitch;
        private readonly double height;
        private readonly int segments;
        private readonly double[] levels;
        private readonly double snap;
        private readonly Func<double, double, double> radius;

        private readonly Dictionary<(int, int, int), int> ids = new();
        private readonly List<Vector3> positions = new();
        private readonly List<int> indices = new();

        /// <summary>The vertices on each end, and the one on each column of it.</summary>
        private readonly List<(double Theta, int Id, int Sector)> bottom = new(), top = new();
        private readonly int[] bottomColumns, topColumns;

        /// <param name="lead">How far the lead-in runs from each end.</param>
        /// <param name="radius">The radius at a phase along the thread, in pitches, and a height.</param>
        public HelicalSurface(double pitch, double height, double lead, int segments, Func<double, double, double> radius)
        {
            this.pitch = pitch;
            this.height = height;
            this.segments = segments;
            this.radius = radius;
            bottomColumns = new int[segments];
            topColumns = new int[segments];

            // The lead-in fades linearly, but the radius it scales changes along the flank too, so
            // the surface over a cell is curved. Cutting it into eighths of the lead keeps the
            // triangles within a few thousandths of it; cut once, a cell sagged a tenth of a
            // millimetre across a flank - half the clearance.
            var list = new List<double> { 0.0 };
            for (int k = 1; k <= Steps; k++) list.Add(lead * k / Steps);
            for (int k = Steps; k >= 1; k--) list.Add(height - lead * k / Steps);
            list.Add(height);
            levels = [.. list];

            // A corner this near a level is put on it. Left a hair apart, the two would be separate
            // vertices a thousandth of a millimetre from each other, which the health check
            // welds into triangles folded flat. Kept under half the rise of a line across one
            // column, so no line can be put on the same level at both of its ends.
            snap = Math.Min(1e-3, pitch / (4.0 * segments));
        }

        /// <param name="Column">Which column it lies on, counting the far side of the last strip as N; or -1.</param>
        /// <param name="Line">Which helical line it lies on, or <see cref="NoLine"/>.</param>
        /// <param name="Level">Which level it lies on, or -1.</param>
        private readonly record struct Node(int Column, int Line, int Level, double Z);

        private double Offset(int line)
        {
            int turn = (int)Math.Floor(line / 4.0);
            return pitch * (turn + Corners[line - 4 * turn]);
        }

        private (int Column, int Line) Canonical(int column, int line) =>
            column == segments ? (0, line + 4) : (column, line);

        /// <summary>Where a helical line crosses a column.</summary>
        private Node Corner(int column, int line)
        {
            var (c, j) = Canonical(column, line);
            double z = Offset(j) + pitch * c / segments;

            for (int l = 0; l < levels.Length; l++)
                if (Math.Abs(z - levels[l]) < snap)
                    return new Node(column, line, l, levels[l]);

            return new Node(column, line, -1, z);
        }

        /// <summary>The surface, one strip between neighbouring columns at a time.</summary>
        /// <param name="outward">False for a hole, whose surface faces the axis.</param>
        public void Build(bool outward)
        {
            int lowest = -8;
            int highest = 4 * ((int)Math.Ceiling(height / pitch) + 2);

            for (int i = 0; i < segments; i++)
                for (int j = lowest; j < highest; j++)
                {
                    // A parallelogram between two lines, anticlockwise when unrolled with the angle
                    // to the right and height up - which faces out once it is rolled up.
                    var a = Corner(i, j);
                    var b = Corner(i + 1, j);
                    var c = Corner(i + 1, j + 1);
                    var d = Corner(i, j + 1);
                    if (c.Z <= 0 || a.Z >= height) continue;

                    for (int band = 0; band + 1 < levels.Length; band++)
                    {
                        if (c.Z <= levels[band] || a.Z >= levels[band + 1]) continue;

                        var piece = Clip(Clip([a, b, c, d], band, above: true), band + 1, above: false);
                        if (piece.Count >= 3) Fan(piece, i, outward);
                    }
                }
        }

        /// <summary>
        /// The part of a convex piece on one side of a level. A corner on the level is kept and makes
        /// no crossing, so the neighbour that shares it makes none either.
        /// </summary>
        private List<Node> Clip(List<Node> piece, int level, bool above)
        {
            var kept = new List<Node>(piece.Count + 1);
            double at = levels[level];

            for (int k = 0; k < piece.Count; k++)
            {
                var u = piece[k];
                var v = piece[(k + 1) % piece.Count];
                double du = above ? u.Z - at : at - u.Z;
                double dv = above ? v.Z - at : at - v.Z;

                if (du >= 0) kept.Add(u);
                if ((du < 0 && dv > 0) || (du > 0 && dv < 0)) kept.Add(Crossing(u, v, level));
            }

            return kept;
        }

        private Node Crossing(Node u, Node v, int level)
        {
            if (u.Column >= 0 && u.Column == v.Column) return new Node(u.Column, NoLine, level, levels[level]);
            if (u.Line != NoLine && u.Line == v.Line) return new Node(-1, u.Line, level, levels[level]);
            throw new InvalidOperationException("A level crossed an edge that is neither a column nor a line.");
        }

        private void Fan(List<Node> piece, int strip, bool outward)
        {
            int first = Id(piece[0], strip);
            int previous = Id(piece[1], strip);

            for (int k = 2; k < piece.Count; k++)
            {
                int next = Id(piece[k], strip);
                if (outward) Triangle(first, previous, next);
                else Triangle(first, next, previous);
                previous = next;
            }
        }

        /// <summary>
        /// The vertex a node names, made the first time it is asked for. The position comes from the
        /// name alone, so a vertex reached from two strips is the same vertex, not two close together.
        /// </summary>
        private int Id(Node node, int strip)
        {
            (int, int, int) key;
            double theta, z;
            int sector;

            if (node.Line != NoLine && node.Column >= 0)
            {
                var (c, j) = Canonical(node.Column, node.Line);
                key = (0, c, j);
                theta = Math.Tau * c / segments;
                z = node.Z;
                sector = c;
            }
            else if (node.Column >= 0)
            {
                int c = node.Column % segments;
                key = (1, c, node.Level);
                theta = Math.Tau * c / segments;
                z = levels[node.Level];
                sector = c;
            }
            else
            {
                key = (2, node.Level, node.Line);
                z = levels[node.Level];
                theta = Math.Tau * (z - Offset(node.Line)) / pitch;
                sector = strip;
            }

            if (ids.TryGetValue(key, out int id)) return id;

            id = positions.Count;
            ids[key] = id;

            double r = radius(z / pitch - theta / Math.Tau, z);
            positions.Add(new Vector3((float)(r * Math.Cos(theta)), (float)(r * Math.Sin(theta)), (float)(z - height / 2)));

            if (node.Level == 0 || node.Level == levels.Length - 1)
            {
                bool isTop = node.Level != 0;
                (isTop ? top : bottom).Add((theta, id, sector));
                if (key.Item1 != 2) (isTop ? topColumns : bottomColumns)[sector] = id;
            }

            return id;
        }

        private void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        private int Vertex(double x, double y, double z)
        {
            positions.Add(new Vector3((float)x, (float)y, (float)(z - height / 2)));
            return positions.Count - 1;
        }

        /// <summary>The end's vertices in order round the axis, anticlockwise from above.</summary>
        private List<(double Theta, int Id, int Sector)> Ring(bool isTop)
        {
            var ring = isTop ? top : bottom;
            ring.Sort((p, q) => p.Theta.CompareTo(q.Theta));
            return ring;
        }

        /// <summary>
        /// A flat end filled from the axis. The lead-in has brought every vertex on it to the same
        /// radius, so a fan from the middle is always a proper disc.
        /// </summary>
        public void Disc(bool top)
        {
            var ring = Ring(top);
            int middle = Vertex(0, 0, top ? height : 0);

            for (int k = 0; k < ring.Count; k++)
            {
                int a = ring[k].Id, b = ring[(k + 1) % ring.Count].Id;
                if (top) Triangle(middle, a, b);
                else Triangle(middle, b, a);
            }
        }

        /// <summary>
        /// A nut's body round the hole: its outside wall and two flat faces. The outside has a
        /// corner on every column, so each wedge of a face runs from one outside corner across to
        /// the hole's vertices in the same wedge and is filled from that corner.
        /// </summary>
        /// <param name="halfWidth">Half the width across the flats, or the radius of a round body.</param>
        public void Body(NutBody body, double halfWidth)
        {
            var below = new int[segments];
            var above = new int[segments];
            int perSide = segments / 6;

            for (int i = 0; i < segments; i++)
            {
                double theta = Math.Tau * i / segments;

                // Corners at every sixth of a turn, so the flats face the quarters and the width
                // along Y is the width across the flats.
                double r = body == NutBody.Round
                    ? halfWidth
                    : halfWidth / Math.Cos(Math.Tau * (i % perSide) / segments - Math.PI / 6);

                below[i] = Vertex(r * Math.Cos(theta), r * Math.Sin(theta), 0);
                above[i] = Vertex(r * Math.Cos(theta), r * Math.Sin(theta), height);
            }

            for (int i = 0; i < segments; i++)
            {
                int n = (i + 1) % segments;
                Triangle(below[i], below[n], above[n]);
                Triangle(below[i], above[n], above[i]);
            }

            Face(Ring(false), below, bottomColumns, top: false);
            Face(Ring(true), above, topColumns, top: true);
        }

        private void Face(List<(double Theta, int Id, int Sector)> ring, int[] outside, int[] columns, bool top)
        {
            for (int k = 0; k < ring.Count; k++)
            {
                var a = ring[k];
                int b = ring[(k + 1) % ring.Count].Id;
                int corner = outside[a.Sector];

                if (top) Triangle(corner, b, a.Id);
                else Triangle(corner, a.Id, b);
            }

            for (int s = 0; s < segments; s++)
            {
                int n = (s + 1) % segments;
                if (top) Triangle(outside[s], outside[n], columns[n]);
                else Triangle(outside[s], columns[n], outside[n]);
            }
        }

        public Mesh ToMesh() => new(positions, indices);
    }
}
