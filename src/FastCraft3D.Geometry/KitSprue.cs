using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Clearance">Free space between each part and the runners round it.</param>
/// <param name="Runner">Width of the runners between the parts.</param>
/// <param name="Frame">Width of the frame round the outside, wider than a runner to keep it stiff.</param>
/// <param name="Height">Height of the frame and runners, standing on the plate.</param>
/// <param name="GateWidth">Width of a gate where it meets the part: what is cut.</param>
/// <param name="GateHeight">Height of a gate where it meets the part.</param>
/// <param name="Width">How wide the sprue may grow before a new row of parts is started.</param>
/// <param name="Corners">
/// Gates by the corners rather than in the middle of each side, so the frame holds down the
/// corners that lift first.
/// </param>
public readonly record struct KitSprueOptions(
    float Clearance, float Runner, float Frame, float Height, float GateWidth, float GateHeight, float Width, bool Corners = false)
{
    public static KitSprueOptions Default => new(
        Clearance: 4f, Runner: 2.5f, Frame: 4f, Height: 2f, GateWidth: 1.2f, GateHeight: 0.8f, Width: 180f);
}

/// <param name="Mesh">The frame, runners and gates, one closed solid standing on the plate.</param>
/// <param name="Moves">How far each part moves to its place in the sprue, in the order given.</param>
/// <param name="Gates">How many gates hold the parts.</param>
/// <param name="Loose">The parts no gate reaches, by their place in the order given.</param>
/// <param name="Rows">How many rows the parts are laid out in.</param>
/// <param name="Size">The sprue's width and depth, frame included.</param>
public sealed record KitSprueLayout(Mesh Mesh, IReadOnlyList<Vector3> Moves, int Gates, IReadOnlyList<int> Loose, int Rows, Vector2 Size);

/// <summary>
/// Parts bound into a sprue, as a model kit's are: laid out in rows on the plate, each in a cell
/// of runners inside a frame, held by a gate from each side to be snipped off after printing.
/// Small parts print stood together on a stiff frame rather than each on its own few square
/// millimetres of first layer, and come off the plate as one piece with nothing lost.
///
/// The frame and runners stand on the plate. A gate leaves its runner as a stub as thick as the
/// runner and narrows to the gate proper only for the last stretch, low and thin, where it meets
/// the part at the plate - a gate the whole way would be floppy, and one meeting the part higher
/// up would leave its scar on a face that shows.
///
/// The solid is drawn on a grid of every edge of every bar, cell by cell, each cell as high as
/// the tallest bar over it. All of it is boxes standing on the plate, so that is exact, and it
/// comes out closed without a boolean: the BSP is not to be trusted with dozens of bars meeting.
/// </summary>
public sealed class KitSprue
{
    /// <summary>The thin stretch at the end of a gate, where it is cut.</summary>
    public const float GateLength = 1.5f;

    /// <summary>
    /// How far a gate runs into its part, so the slicer joins them. The two stay separate objects,
    /// and a gate that only touched its part's skin would print beside it, not on to it.
    /// </summary>
    private const float Overlap = 0.4f;

    private readonly IReadOnlyList<Mesh> parts;
    private readonly List<Bounds> bounds;
    private readonly Dictionary<float, List<(Vector2 A, Vector2 B)>[]> sections = [];

    /// <param name="parts">Each part in world space, as it stands.</param>
    public KitSprue(IReadOnlyList<Mesh> parts)
    {
        this.parts = parts;
        bounds = parts.Select(p => p.ComputeBounds()).ToList();
    }

    public KitSprueLayout Lay(KitSprueOptions o)
    {
        float clearance = MathF.Max(o.Clearance, GateLength + 0.5f);
        float runner = MathF.Max(o.Runner, 0.8f);
        float frame = MathF.Max(o.Frame, runner);
        float height = MathF.Max(o.Height, 0.4f);
        float gateHeight = Math.Clamp(o.GateHeight, 0.2f, height);
        float gateWidth = Math.Clamp(o.GateWidth, 0.4f, runner);

        // Rows filled deepest part first, so the first in a row sets its depth and the rest fit
        // under it - the usual way to shelve boxes of different sizes with little left over.
        var order = Enumerable.Range(0, parts.Count)
            .OrderByDescending(i => bounds[i].Size.Y).ThenByDescending(i => bounds[i].Size.X).ToList();

        float CellWidth(int i) => bounds[i].Size.X + 2 * clearance;

        var rows = new List<List<int>>();
        float inside = MathF.Max(o.Width - 2 * frame, 1f);
        foreach (int i in order)
        {
            if (rows.Count > 0 && RowWidth(rows[^1]) + runner + CellWidth(i) <= inside) rows[^1].Add(i);
            else rows.Add([i]);
        }

        float RowWidth(List<int> row) => row.Sum(CellWidth) + (row.Count - 1) * runner;
        float RowDepth(List<int> row) => row.Max(i => bounds[i].Size.Y) + 2 * clearance;

        float width = rows.Count == 0 ? 0 : rows.Max(RowWidth);
        float depth = rows.Sum(RowDepth) + Math.Max(rows.Count - 1, 0) * runner;

        var bars = new List<Bar>();
        float left = -width / 2, right = width / 2, back = depth / 2, front = -depth / 2;

        bars.Add(new(left - frame, right + frame, front - frame, front, height));
        bars.Add(new(left - frame, right + frame, back, back + frame, height));
        bars.Add(new(left - frame, left, front, back, height));
        bars.Add(new(right, right + frame, front, back, height));

        var moves = new Vector3[parts.Count];
        var loose = new List<int>();
        int gates = 0;
        var cut = Sections(gateHeight);

        float top = back;
        foreach (var row in rows)
        {
            float rowDepth = RowDepth(row);
            float y0 = top - rowDepth, y1 = top;

            // A narrower row's spare width is shared out, so its parts sit in the middle of wider
            // cells rather than all of it left over at the end.
            float spare = (width - RowWidth(row)) / row.Count;
            float x = left;

            foreach (int i in row)
            {
                float x0 = x, x1 = x + CellWidth(i) + spare;
                var b = bounds[i];
                var move = new Vector3((x0 + x1) / 2 - b.Center.X, (y0 + y1) / 2 - b.Center.Y, -b.Min.Z);
                moves[i] = move;

                var placed = new Bounds(b.Min + move, b.Max + move);
                var segments = cut[i].Select(s => (s.A + new Vector2(move.X, move.Y), s.B + new Vector2(move.X, move.Y))).ToList();

                var gate = new GateSize(runner, height, gateWidth, gateHeight, o.Corners);
                int held = 0;
                held += Gate(bars, segments, 0, +1, x0, placed.Min.X - x0, placed.Min.Y, placed.Max.Y, gate);
                held += Gate(bars, segments, 0, -1, x1, x1 - placed.Max.X, placed.Min.Y, placed.Max.Y, gate);
                held += Gate(bars, segments, 1, +1, y0, placed.Min.Y - y0, placed.Min.X, placed.Max.X, gate);
                held += Gate(bars, segments, 1, -1, y1, y1 - placed.Max.Y, placed.Min.X, placed.Max.X, gate);

                if (held == 0) loose.Add(i);
                gates += held;

                x = x1;
                if (i != row[^1])
                {
                    bars.Add(new(x, x + runner, y0, y1, height));
                    x += runner;
                }
            }

            top = y0 - runner;
            if (row != rows[^1]) bars.Add(new(left, right, top, y0, height));
        }

        return new KitSprueLayout(Solid(bars), moves, gates, loose, rows.Count, new Vector2(width + 2 * frame, depth + 2 * frame));
    }

    private readonly record struct GateSize(float Runner, float Height, float Width, float Low, bool Corners);

    /// <summary>
    /// The gates from one side of a cell into its part, each a stub from the runner and then the
    /// thin gate the rest of the way. How many it is.
    /// </summary>
    /// <param name="axis">0 when the gates run along X, 1 along Y.</param>
    /// <param name="inward">+1 or -1: which way along the axis the part lies from the runner.</param>
    /// <param name="edge">Where the runner's inner side is, along the axis.</param>
    /// <param name="free">How far it is from there to the part's bounds, under which the stub may not go.</param>
    /// <param name="from">The part's extent across the gates.</param>
    private static int Gate(List<Bar> bars, List<(Vector2 A, Vector2 B)> section, int axis, int inward, float edge, float free,
        float from, float to, GateSize size)
    {
        var spots = size.Corners
            ? ByCorners(section, axis, inward, edge, from, to, size.Width)
            : InTheMiddle(section, axis, inward, edge, from, to);

        foreach (var (across, distance) in spots)
        {
            float stub = MathF.Min(free, distance) - GateLength;
            if (stub > 0.3f) bars.Add(Along(axis, edge, edge + inward * stub, across, size.Runner, size.Height));
            else stub = 0;

            bars.Add(Along(axis, edge + inward * stub, edge + inward * (distance + Overlap), across, size.Width, size.Low));
        }

        return spots.Count;
    }

    /// <summary>Of a few places along the side, the one where the part comes nearest; the middle when there is little in it.</summary>
    private static List<(float Across, float Reach)> InTheMiddle(List<(Vector2 A, Vector2 B)> section, int axis, int inward, float edge,
        float from, float to)
    {
        float? best = null;
        float across = 0;
        foreach (float t in Spots)
        {
            float at = from + (to - from) * t;
            if (Reach(section, axis, inward, edge, at) is not { } reach) continue;

            // Somewhere off the middle only for a real saving: a gate square to the side reads as meant.
            if (best is null || reach < best - 0.5f)
            {
                best = reach;
                across = at;
            }
        }

        return best is { } distance ? [(across, distance)] : [];
    }

    /// <summary>
    /// How near to level with the runner a face has to be to count as one, with a gate by each of
    /// its ends. Tight, since a curve is near level for a little way either side of its nearest
    /// point: at half a millimetre a cylinder read as a face and got two gates side by side.
    /// </summary>
    private const float Level = 0.05f;

    /// <summary>
    /// Gates by the corners: wherever the part comes nearest this side along a face, a gate just
    /// inside each end of it, which is where it lifts; one in the middle of a face too short for
    /// two, or of a point or a curve, so a round part is held as it would be anyway.
    /// </summary>
    private static List<(float Across, float Reach)> ByCorners(List<(Vector2 A, Vector2 B)> section, int axis, int inward, float edge,
        float from, float to, float gateWidth)
    {
        float span = to - from;
        int steps = Math.Clamp((int)(span / 0.25f), 8, 800);
        var reach = new float?[steps + 1];
        float nearest = float.MaxValue;
        for (int k = 0; k <= steps; k++)
        {
            reach[k] = Reach(section, axis, inward, edge, from + span * k / steps);
            if (reach[k] is { } r && r < nearest) nearest = r;
        }

        var spots = new List<(float, float)>();
        if (nearest == float.MaxValue) return spots;

        bool Near(int k) => reach[k] is { } r && r <= nearest + Level;
        void Add(float at) => spots.Add((at, Reach(section, axis, inward, edge, at) ?? nearest));

        float inset = gateWidth / 2 + 0.2f;
        for (int k = 0; k <= steps; k++)
        {
            if (!Near(k)) continue;

            int first = k;
            while (k < steps && Near(k + 1)) k++;

            float a = from + span * first / steps, b = from + span * k / steps;
            if (b - a - 2 * inset < 2 * gateWidth)
            {
                Add((a + b) / 2);
            }
            else
            {
                Add(a + inset);
                Add(b - inset);
            }
        }

        return spots;
    }

    /// <summary>How far from the runner's side to the part, along a line across the plate; null if it misses.</summary>
    private static float? Reach(List<(Vector2 A, Vector2 B)> section, int axis, int inward, float edge, float at)
    {
        float? nearest = null;
        foreach (var (a, b) in section)
        {
            float a0 = axis == 0 ? a.Y : a.X, b0 = axis == 0 ? b.Y : b.X;
            if ((a0 > at) == (b0 > at)) continue;

            float a1 = axis == 0 ? a.X : a.Y, b1 = axis == 0 ? b.X : b.Y;
            float hit = a1 + (at - a0) * (b1 - a1) / (b0 - a0);
            float reach = (hit - edge) * inward;
            if (reach >= 0 && (nearest is null || reach < nearest)) nearest = reach;
        }

        return nearest;
    }

    /// <summary>Where along a side a gate is tried, as a share of the part's extent: the middle first.</summary>
    private static readonly float[] Spots = [0.5f, 0.4f, 0.6f, 0.3f, 0.7f, 0.2f, 0.8f];

    private static Bar Along(int axis, float a, float b, float across, float width, float height) => axis == 0
        ? new(MathF.Min(a, b), MathF.Max(a, b), across - width / 2, across + width / 2, height)
        : new(across - width / 2, across + width / 2, MathF.Min(a, b), MathF.Max(a, b), height);

    /// <summary>Each part cut across halfway up a gate, as segments: where a gate can meet it.</summary>
    private List<(Vector2 A, Vector2 B)>[] Sections(float gateHeight)
    {
        if (sections.TryGetValue(gateHeight, out var known)) return known;

        var made = new List<(Vector2 A, Vector2 B)>[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            var mesh = parts[i];
            float z = bounds[i].Min.Z + gateHeight / 2;
            var cut = new List<(Vector2, Vector2)>();
            Span<Vector3> v = stackalloc Vector3[3];
            Span<Vector2> found = stackalloc Vector2[2];

            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                for (int k = 0; k < 3; k++) v[k] = mesh.Positions[mesh.Indices[t + k]];

                int n = 0;
                for (int e = 0; e < 3 && n < 2; e++)
                {
                    var p = v[e];
                    var q = v[(e + 1) % 3];
                    if ((p.Z > z) == (q.Z > z)) continue;

                    float s = (z - p.Z) / (q.Z - p.Z);
                    found[n++] = new Vector2(p.X + (q.X - p.X) * s, p.Y + (q.Y - p.Y) * s);
                }

                if (n == 2) cut.Add((found[0], found[1]));
            }

            made[i] = cut;
        }

        sections[gateHeight] = made;
        return made;
    }

    private readonly record struct Bar(float X0, float X1, float Y0, float Y1, float Height);

    /// <summary>
    /// The bars as one closed solid: a grid on every edge of every bar, each cell as high as the
    /// tallest bar over it, a floor and roof for each cell with anything on it and a wall wherever
    /// two cells side by side stand to different heights. Walls are split at every height in use,
    /// so where three heights meet at a corner no edge runs past another's end.
    /// </summary>
    private static Mesh Solid(List<Bar> bars)
    {
        // Snapped so that edges meant to coincide, worked out by different sums, do.
        static float Snap(float v) => MathF.Round(v * 1000f) / 1000f;

        var xs = bars.SelectMany(b => new[] { Snap(b.X0), Snap(b.X1) }).Distinct().Order().ToArray();
        var ys = bars.SelectMany(b => new[] { Snap(b.Y0), Snap(b.Y1) }).Distinct().Order().ToArray();
        var levels = bars.Select(b => b.Height).Prepend(0f).Distinct().Order().ToArray();

        int nx = xs.Length - 1, ny = ys.Length - 1;
        var tall = new int[Math.Max(nx, 0), Math.Max(ny, 0)];
        foreach (var bar in bars)
        {
            int level = Array.IndexOf(levels, bar.Height);
            int i0 = Array.BinarySearch(xs, Snap(bar.X0)), i1 = Array.BinarySearch(xs, Snap(bar.X1));
            int j0 = Array.BinarySearch(ys, Snap(bar.Y0)), j1 = Array.BinarySearch(ys, Snap(bar.Y1));
            for (int i = i0; i < i1; i++)
                for (int j = j0; j < j1; j++)
                    tall[i, j] = Math.Max(tall[i, j], level);
        }

        int Height(int i, int j) => i < 0 || j < 0 || i >= nx || j >= ny ? 0 : tall[i, j];

        var positions = new List<Vector3>();
        var indices = new List<int>();
        var made = new Dictionary<(int, int, int), int>();

        int Vertex(int i, int j, int level)
        {
            if (!made.TryGetValue((i, j, level), out int index))
            {
                index = positions.Count;
                positions.Add(new Vector3(xs[i], ys[j], levels[level]));
                made[(i, j, level)] = index;
            }

            return index;
        }

        void Quad(int a, int b, int c, int d) => indices.AddRange([a, b, c, a, c, d]);

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                int h = tall[i, j];
                if (h == 0) continue;

                Quad(Vertex(i, j, h), Vertex(i + 1, j, h), Vertex(i + 1, j + 1, h), Vertex(i, j + 1, h));
                Quad(Vertex(i, j, 0), Vertex(i, j + 1, 0), Vertex(i + 1, j + 1, 0), Vertex(i + 1, j, 0));
            }
        }

        // Walls across X: between cell (i - 1, j) and cell (i, j), facing the lower one.
        for (int i = 0; i <= nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                int west = Height(i - 1, j), east = Height(i, j);
                for (int l = Math.Min(west, east); l < Math.Max(west, east); l++)
                {
                    if (west > east) Quad(Vertex(i, j, l), Vertex(i, j + 1, l), Vertex(i, j + 1, l + 1), Vertex(i, j, l + 1));
                    else Quad(Vertex(i, j, l), Vertex(i, j, l + 1), Vertex(i, j + 1, l + 1), Vertex(i, j + 1, l));
                }
            }
        }

        // Walls across Y: between cell (i, j - 1) and cell (i, j).
        for (int j = 0; j <= ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                int south = Height(i, j - 1), north = Height(i, j);
                for (int l = Math.Min(south, north); l < Math.Max(south, north); l++)
                {
                    if (south > north) Quad(Vertex(i, j, l), Vertex(i, j, l + 1), Vertex(i + 1, j, l + 1), Vertex(i + 1, j, l));
                    else Quad(Vertex(i, j, l), Vertex(i + 1, j, l), Vertex(i + 1, j, l + 1), Vertex(i, j, l + 1));
                }
            }
        }

        return new Mesh(positions, indices);
    }
}
