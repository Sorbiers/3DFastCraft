using System.Numerics;

namespace FastCraft3D.Geometry.Moulding;

/// <summary>How one candidate pull direction looks.</summary>
/// <param name="Axis">The direction the two halves would come apart along.</param>
/// <param name="SplitAt">Where to cut, in millimetres along that axis.</param>
/// <param name="OpeningArea">
/// The cross-section at the cut, in square millimetres. The widest section is the opening the
/// cast part has to come out through, which is why the cut goes there.
/// </param>
/// <param name="Trapped">
/// How many lines of sight along the axis leave the model and enter it again. Each one is a place
/// where mould material sits over the top of a surface it would have to lift off - an undercut.
/// </param>
/// <param name="Crossed">How many lines met the model at all, so <see cref="Trapped"/> reads as a share.</param>
public readonly record struct PullReport(Axis Axis, float SplitAt, float OpeningArea, int Trapped, int Crossed)
{
    /// <summary>Undercut lines as a fraction of the lines that touch the model at all.</summary>
    public float TrappedShare => Crossed == 0 ? 0f : (float)Trapped / Crossed;

    /// <summary>Nothing is in the way: the part lifts straight out of both halves.</summary>
    public bool LiftsStraightOut => Trapped == 0;
}

/// <summary>One flat cut through the mould block.</summary>
public readonly record struct MouldCut(Axis Axis, float At);

/// <param name="Pulls">One report per axis, the most promising first.</param>
/// <param name="Cuts">The cuts recommended, in order. One gives two parts, two gives four.</param>
/// <param name="Sprue">Where the pour hole should meet the cavity - the highest point of the model.</param>
/// <param name="Vents">
/// High points other than the sprue, where air collects as the silicone rises and has nowhere to go.
/// </param>
/// <param name="Summary">The same thing in a sentence, for the dialog and the status bar.</param>
public sealed record MouldStudy(
    IReadOnlyList<PullReport> Pulls,
    IReadOnlyList<MouldCut> Cuts,
    Vector3 Sprue,
    IReadOnlyList<Vector3> Vents,
    string Summary)
{
    /// <summary>The axis the cuts start from - the one the block comes apart along.</summary>
    public Axis Best => Pulls[0].Axis;

    /// <summary>How many pieces the recommended cuts make.</summary>
    public int Parts => 1 << Cuts.Count;
}

/// <summary>
/// Works out how a mould for a model should come apart.
///
/// All of it comes off one occupancy grid, which is the half of a rebuild that answers whether a
/// point is inside the model. A grid is a plain array of flags with x running fastest, so a line
/// of sight along any axis is a fixed stride through it - and that one sampling then answers every
/// question worth asking:
///
/// <list type="bullet">
/// <item>where the widest cross-section is, which is where practice says to cut;</item>
/// <item>which lines of sight leave the model and come back, which is what an undercut is;</item>
/// <item>where the high points are, which is where the pour hole and the air vents belong.</item>
/// </list>
///
/// Sampling rather than working on the triangles is the point. The question "can this be pulled
/// apart along Z" is about lines of sight through solid material, not about faces, and asking it
/// of a grid is a scan; asking it of a mesh means shooting rays and sorting hits, which is what
/// building the grid already did once for all of them.
/// </summary>
public static class MouldAnalysis
{
    /// <summary>
    /// Above this share of undercut lines a second cut is worth recommending.
    ///
    /// Not zero, because the cast part is silicone: it bends out of shallow undercuts, and a
    /// mould in four pieces to save a handful of grid lines is a worse mould. Not high either -
    /// past a few per cent the part is being asked to deform round something substantial.
    /// </summary>
    public const float TolerableUndercut = 0.02f;

    /// <summary>The most pieces worth making. Three cuts is already eight, which nobody clamps.</summary>
    public const int MaximumCuts = 3;

    public static MouldStudy Study(Mesh mesh, int resolution = 64, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var grid = VoxelRebuild.Sample(mesh, resolution, token);
        if (grid.Inside.Length == 0)
            return new MouldStudy([], [], Vector3.Zero, [], "Nothing to make a mould of.");

        var pulls = new List<PullReport>();
        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
            pulls.Add(Look(grid, axis, token));

        // Fewest undercuts first, and where that ties, the widest opening - a part comes out of a
        // big mouth more easily than a small one, whatever the arithmetic says about undercuts.
        pulls.Sort((a, b) => a.Trapped != b.Trapped
            ? a.Trapped.CompareTo(b.Trapped)
            : b.OpeningArea.CompareTo(a.OpeningArea));

        var cuts = Recommend(pulls);
        var (sprue, vents) = HighPoints(grid, token);

        return new MouldStudy(pulls, cuts, sprue, vents, Describe(pulls, cuts, vents));
    }

    /// <summary>
    /// Scans every line of sight along one axis.
    ///
    /// One pass answers both questions. A line that turns from air to material more than once has
    /// material hanging over a gap somewhere along it, which is exactly the geometry that will not
    /// lift out; and counting the material on each slice as the scan goes by gives the areas the
    /// cut is chosen from, without a second walk over the grid.
    /// </summary>
    private static PullReport Look(VoxelRebuild.Grid grid, Axis axis, CancellationToken token)
    {
        var (stride, steps, lines) = Walk(axis, grid.Nx, grid.Ny, grid.Nz);

        var area = new int[steps];
        int trapped = 0, crossed = 0;

        for (int line = 0; line < lines; line++)
        {
            if ((line & 1023) == 0) token.ThrowIfCancellationRequested();

            int start = LineStart(axis, line, grid.Nx, grid.Ny);
            int runs = 0;
            bool was = false;

            for (int t = 0; t < steps; t++)
            {
                bool now = grid.Inside[start + t * stride];
                if (now)
                {
                    area[t]++;
                    if (!was) runs++;
                }
                was = now;
            }

            if (runs > 0) crossed++;
            if (runs > 1) trapped++;
        }

        int widest = 0;
        for (int t = 1; t < steps; t++)
            if (area[t] > area[widest]) widest = t;

        float at = Component(grid.Origin, axis) + widest * grid.Voxel;

        return new PullReport(axis, at, area[widest] * grid.Voxel * grid.Voxel, trapped, crossed);
    }

    /// <summary>
    /// Which cuts to make.
    ///
    /// The first is always the best axis at its widest section. A second is only worth its clamps
    /// when the first leaves real undercuts behind, and it goes on a different axis - two parallel
    /// cuts make a slice in the middle that is no easier to get anything out of.
    /// </summary>
    private static List<MouldCut> Recommend(List<PullReport> pulls)
    {
        List<MouldCut> cuts = [new MouldCut(pulls[0].Axis, pulls[0].SplitAt)];

        for (int i = 1; i < pulls.Count && cuts.Count < MaximumCuts; i++)
        {
            if (pulls[i - 1].TrappedShare <= TolerableUndercut) break;
            cuts.Add(new MouldCut(pulls[i].Axis, pulls[i].SplitAt));
        }

        return cuts;
    }

    /// <summary>
    /// Where the pour hole and the vents go.
    ///
    /// Both are the same question - where does air end up - and the answer is not simply "the
    /// high points". Air under the cavity ceiling slides along it, so a flat region joined to a
    /// taller one is not a trap at all: the air runs sideways and then up. What traps it is a
    /// stretch of ceiling with nothing higher anywhere along its edge.
    ///
    /// So the ceiling is gathered into level stretches, and a stretch is a trap when nothing
    /// around it stands higher. The tallest trap takes the pour hole and the rest take vents. An
    /// empty column beside one is not an escape - beside the model is mould, not air.
    /// </summary>
    private static (Vector3 Sprue, List<Vector3> Vents) HighPoints(
        VoxelRebuild.Grid grid, CancellationToken token)
    {
        // The top of the material in each column, or -1 where the column misses the model.
        var top = new int[grid.Nx * grid.Ny];
        Array.Fill(top, -1);

        for (int j = 0; j < grid.Ny; j++)
        {
            token.ThrowIfCancellationRequested();

            for (int i = 0; i < grid.Nx; i++)
                for (int k = grid.Nz - 1; k >= 0; k--)
                    if (grid.Inside[(k * grid.Ny + j) * grid.Nx + i])
                    {
                        top[j * grid.Nx + i] = k;
                        break;
                    }
        }

        var claimed = new bool[top.Length];
        var traps = new List<(int Crest, Vector3 At)>();

        // Highest column first, and that ordering is not a detail.
        //
        // Grown from wherever the scan happened to start, a stretch low on the flank chained all
        // the way up and swallowed the crest, and then drained because somewhere along its length
        // the ceiling stepped up. A ring came back with no trapped stretch at all and its pour
        // hole at the origin - over the hole. Started from the top, a crest is always its own.
        foreach (int seed in Enumerable.Range(0, top.Length)
                     .Where(c => top[c] >= 0)
                     .OrderByDescending(c => top[c]))
        {
            if (claimed[seed]) continue;

            token.ThrowIfCancellationRequested();

            // One stretch of ceiling, walked while it stays level. A cell is allowed to differ
            // by one from its neighbour, because a surface sampled onto a grid is never quite
            // flat - and on a dome that lets the whole cap come out as the single stretch it is.
            var stretch = new List<int>();
            var walk = new Queue<int>();
            claimed[seed] = true;
            walk.Enqueue(seed);

            bool drains = false;

            while (walk.Count > 0)
            {
                int cell = walk.Dequeue();
                stretch.Add(cell);

                int ci = cell % grid.Nx, cj = cell / grid.Nx;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int ni = ci + di, nj = cj + dj;
                        if (ni < 0 || nj < 0 || ni >= grid.Nx || nj >= grid.Ny) continue;

                        int next = nj * grid.Nx + ni;
                        if (top[next] < 0) continue;

                        if (Math.Abs(top[next] - top[cell]) <= 1)
                        {
                            if (claimed[next]) continue;
                            claimed[next] = true;
                            walk.Enqueue(next);
                        }
                        else if (top[next] > top[cell])
                        {
                            // Somewhere along this stretch the ceiling steps up. Air gets out.
                            drains = true;
                        }
                    }
            }

            if (drains) continue;

            int crest = stretch.Max(c => top[c]);
            var onTop = stretch.Where(c => top[c] == crest).ToList();

            double ai = onTop.Average(c => c % grid.Nx);
            double aj = onTop.Average(c => c / grid.Nx);

            // Nearest cell of the stretch to its middle, so the vent lands on the model.
            int best = onTop.MinBy(c =>
            {
                double di = c % grid.Nx - ai, dj = c / grid.Nx - aj;
                return di * di + dj * dj;
            });

            traps.Add((crest, new Vector3(
                grid.Origin.X + best % grid.Nx * grid.Voxel,
                grid.Origin.Y + best / grid.Nx * grid.Voxel,
                grid.Origin.Z + crest * grid.Voxel)));
        }

        // The pour hole is simply the model's highest point - not the tallest trap.
        //
        // They were the same thing until a ring was tried. A ring's ceiling has somewhere higher
        // beside it almost everywhere, so nothing came back trapped at all and the pour hole fell
        // to the origin, which on a ring is the hole. Silicone is poured in at the top whether or
        // not the top happens to hold air, so the top is what decides it, and traps only ever
        // decide vents.
        int highest = 0;
        for (int c = 1; c < top.Length; c++)
            if (top[c] > top[highest]) highest = c;

        if (top[highest] < 0) return (Vector3.Zero, []);

        var summit = Level(grid, top, top[highest]);

        // Any trap at the same height as the pour hole is the pour hole, and a hole and a vent in
        // the same place is one hole.
        float apart = MathF.Max(grid.Voxel * 3f, 2f);

        var vents = traps
            .OrderByDescending(t => t.Crest)
            .Select(t => t.At)
            .Where(v => Vector2.Distance(new Vector2(v.X, v.Y), new Vector2(summit.X, summit.Y)) > apart)
            .Take(5)
            .ToList();

        return (summit, vents);
    }

    /// <summary>
    /// The point on the model at a given grid height, taken as near the middle of that level as a
    /// cell actually sits.
    ///
    /// The middle itself will not do: the top of a ring is a circle, and the middle of a circle is
    /// the hole - where there is no model to pour into. Nearest cell to the middle is on the model
    /// by construction.
    /// </summary>
    private static Vector3 Level(VoxelRebuild.Grid grid, int[] top, int height)
    {
        var cells = new List<int>();
        for (int c = 0; c < top.Length; c++)
            if (top[c] == height) cells.Add(c);

        double ai = cells.Average(c => c % grid.Nx);
        double aj = cells.Average(c => c / grid.Nx);

        int best = cells.MinBy(c =>
        {
            double di = c % grid.Nx - ai, dj = c / grid.Nx - aj;
            return di * di + dj * dj;
        });

        return new Vector3(
            grid.Origin.X + best % grid.Nx * grid.Voxel,
            grid.Origin.Y + best / grid.Nx * grid.Voxel,
            grid.Origin.Z + height * grid.Voxel);
    }

    private static Vector3 Point(VoxelRebuild.Grid grid, int i, int j, int k) =>
        grid.Origin + new Vector3(i, j, k) * grid.Voxel;

    /// <summary>A line along an axis is a fixed stride, because the grid runs x fastest then y.</summary>
    private static (int Stride, int Steps, int Lines) Walk(Axis axis, int nx, int ny, int nz) => axis switch
    {
        Axis.X => (1, nx, ny * nz),
        Axis.Y => (nx, ny, nx * nz),
        _ => (nx * ny, nz, nx * ny),
    };

    private static int LineStart(Axis axis, int line, int nx, int ny) => axis switch
    {
        Axis.X => line / ny * nx * ny + line % ny * nx,
        Axis.Y => line / nx * nx * ny + line % nx,
        _ => line,
    };

    private static float Component(Vector3 v, Axis axis) =>
        axis == Axis.X ? v.X : axis == Axis.Y ? v.Y : v.Z;

    private static string Describe(
        IReadOnlyList<PullReport> pulls, IReadOnlyList<MouldCut> cuts, IReadOnlyList<Vector3> vents)
    {
        var best = pulls[0];

        string how = best.LiftsStraightOut
            ? $"Cut on {best.Axis} at {best.SplitAt:0.##} mm - the part lifts straight out"
            : $"Cut on {best.Axis} at {best.SplitAt:0.##} mm - {best.TrappedShare:P0} of it is undercut, "
              + "which silicone will flex out of";

        string parts = cuts.Count == 1
            ? "Two parts"
            : $"{1 << cuts.Count} parts, cut on {string.Join(" then ", cuts.Select(c => c.Axis))}";

        string air = vents.Count == 0 ? "" : $", {vents.Count} vent(s)";

        return $"{how}. {parts}{air}.";
    }
}
