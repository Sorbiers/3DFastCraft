using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>Where the cells go: over the skin, or right through the solid.</summary>
public enum VoronoiKind
{
    /// <summary>A web of struts following the surface, with nothing behind it.</summary>
    Shell,

    /// <summary>
    /// Struts running every way through the whole of it: a foam, under a skin. Built on the
    /// cells' edges rather than their walls - see <see cref="Voronoi"/>.
    /// </summary>
    Lattice
}

/// <param name="Kind">A web over the surface, or a foam through the solid.</param>
/// <param name="Cells">How many seeds. More seeds, smaller cells.</param>
/// <param name="StrutMm">How wide a strut comes out.</param>
/// <param name="SkinMm">
/// How deep the skin is. For a shell it is the whole of the material; for a lattice it is the
/// solid face over the foam, and nought leaves the foam bare.
/// </param>
/// <param name="BaseMm">How much of the bottom to leave solid, so the thing stands and prints.</param>
/// <param name="Resolution">Voxels along the model's longest side.</param>
/// <param name="Smooth">
/// Whether to take the voxel steps off the result. See <see cref="Voronoi.Build"/>.
/// </param>
public readonly record struct VoronoiOptions(
    VoronoiKind Kind, int Cells, float StrutMm, float SkinMm, float BaseMm, int Resolution,
    bool Smooth = true)
{
    public static VoronoiOptions Default =>
        new(VoronoiKind.Shell, 120, 1.6f, 2f, 0f, VoxelRebuild.DefaultResolution);

    public VoronoiOptions Sane() => this with
    {
        Cells = Math.Clamp(Cells, 2, 2000),
        StrutMm = Math.Clamp(StrutMm, Voronoi.LeastStrutMm, 100f),
        SkinMm = Math.Clamp(SkinMm, 0f, 100f),
        BaseMm = Math.Clamp(BaseMm, 0f, 1000f),
        Resolution = Math.Clamp(Resolution, VoxelRebuild.MinimumResolution, VoxelRebuild.MaximumResolution)
    };
}

/// <param name="Mesh">What came out. Empty when it was refused.</param>
/// <param name="Cells">How many seeds it actually placed.</param>
/// <param name="VoxelMm">The grid it was cut on, which is the finest detail that survived.</param>
/// <param name="Kept">
/// How much of the material is left, nought to one. Counted on the grid, which is exact for what
/// was actually built - and the one number that says at a glance whether the settings made a web
/// or a block with holes in it.
/// </param>
/// <param name="Loose">
/// How many pieces came away from the main one. A lattice whose struts do not all reach each
/// other prints as a bag of parts, and nothing else on screen would say so.
/// </param>
/// <param name="Refusal">Why there is nothing, or null.</param>
public readonly record struct VoronoiResult(
    Mesh Mesh, int Cells, float VoxelMm, float Kept = 0f, string? Refusal = null, int Loose = 0);

/// <summary>
/// Turns a solid into a web of struts along the walls of a Voronoi tessellation - the lamp, the
/// vase, the lightened bracket.
///
/// Scatter seeds; every point in space belongs to whichever seed is nearest. A wall between two
/// cells is where those two are equidistant, and an edge is where three are - so a point is on a
/// wall when the first and second nearest seeds are within a strut of each other, and on an edge
/// when the first and third are. That is all this has to ask: no tessellation is ever built, no
/// cell is ever a polygon, and nothing is subtracted from anything.
///
/// Which of the two a kind wants is the whole difference between them, and getting it wrong is
/// not subtle. A shell wants walls: a wall is a plate, and a plate crossing a thin skin leaves a
/// strut. A lattice wants edges: walls through a volume are plates that fill it, and a cell has
/// fourteen of them, so 5 mm walls in 16 mm cells came back three quarters solid - a block with
/// bubbles in it rather than a lattice. The edges are the struts everyone means by the word.
///
/// That last part is the reason it is done this way. The usual recipe is to triangulate the seeds,
/// dualise them into cells, build a solid of struts and subtract it - and a web of hundreds of
/// thin walls against a curved surface is precisely the case the BSP engine tears on, as the
/// engraver found out. Here the whole shape is a question asked of each point of a grid:
///
///     inside the model,
///     and within the skin,
///     and near a cell wall.
///
/// Answer that everywhere and the surface is whatever separates yes from no, which
/// <see cref="VoxelRebuild.SurfaceNets"/> extracts watertight by construction. The same grid the
/// rebuilder and the hollower already use, and the same price they pay: detail finer than a voxel
/// is gone, so a strut wants three voxels across it or it comes out lumpy.
/// </summary>
public static class Voronoi
{
    /// <summary>Two nozzle widths. Below this a strut is not worth printing.</summary>
    public const float LeastStrutMm = 0.8f;

    /// <summary>How many voxels a strut needs across it before it stops being lumpy.</summary>
    public const float VoxelsPerStrut = 3f;

    /// <summary>
    /// The resolution a strut of this width wants on a model of this size, held to what the grid
    /// will take. Handed to the panel so it can say what it is about to cost.
    /// </summary>
    public static int WantedResolution(float longestSideMm, float strutMm) =>
        strutMm <= 0 || longestSideMm <= 0
            ? VoxelRebuild.DefaultResolution
            : Math.Clamp((int)MathF.Ceiling(longestSideMm * VoxelsPerStrut / strutMm),
                         VoxelRebuild.MinimumResolution, VoxelRebuild.MaximumResolution);

    /// <summary>
    /// Builds it. Blocks; a caller on the UI thread should wrap it in Task.Run.
    /// </summary>
    public static VoronoiResult Build(Mesh mesh, VoronoiOptions options,
                                      CancellationToken token = default,
                                      IProgress<WorkProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();

        var o = options.Sane();
        var bounds = mesh.ComputeBounds();

        if (bounds.IsEmpty || mesh.TriangleCount == 0)
            return new VoronoiResult(new Mesh(), 0, 0f, 0f, "There is nothing there to cut cells into.");

        progress?.Report(WorkProgress.Doing("Measuring the model"));
        var grid = VoxelRebuild.Sample(mesh, o.Resolution, token, progress);

        if (grid.Inside.Length == 0)
            return new VoronoiResult(new Mesh(), 0, 0f, 0f, "The model would not go onto a grid.");

        if (o.StrutMm < grid.Voxel * 2f)
            return new VoronoiResult(new Mesh(), 0, grid.Voxel, 0f,
                $"A {o.StrutMm:0.##} mm strut is under two voxels at this detail, so it would come out"
                + $" in lumps or not at all. Raise the detail to {WantedResolution(Longest(bounds), o.StrutMm)}"
                + " or thicken the strut.");

        // A web with no depth to it is not thin, it is absent: the grid has nothing to keep.
        if (o.Kind == VoronoiKind.Shell && o.SkinMm < grid.Voxel * 1.5f)
            return new VoronoiResult(new Mesh(), 0, grid.Voxel, 0f,
                $"A {o.SkinMm:0.##} mm web is under a voxel and a half at this detail, so there would"
                + " be nothing left of it. Deepen the web, or raise the detail.");

        token.ThrowIfCancellationRequested();
        progress?.Report(WorkProgress.Doing("Scattering the seeds"));

        var pool = o.Kind == VoronoiKind.Shell
            ? OnTheSurface(mesh, Math.Clamp(o.Cells * 20, 2000, 40000))
            : Throughout(grid, Math.Clamp(o.Cells * 20, 2000, 40000));

        if (pool.Count < 2)
            return new VoronoiResult(new Mesh(), 0, grid.Voxel, 0f, "There is not enough of it to put two cells in.");

        var seeds = SpreadOut(pool, Math.Min(o.Cells, pool.Count), token);
        if (seeds.Count < 2)
            return new VoronoiResult(new Mesh(), seeds.Count, grid.Voxel, 0f, "Two seeds at least, or there is no wall between them.");

        token.ThrowIfCancellationRequested();
        progress?.Report(WorkProgress.Doing("Measuring the skin"));

        var depth = DepthInside(grid, token);
        float skinInVoxels = o.SkinMm / grid.Voxel;
        float baseTo = bounds.Min.Z + o.BaseMm;

        token.ThrowIfCancellationRequested();
        progress?.Report(WorkProgress.Doing("Cutting the cells"));

        var cells = new SeedGrid(seeds, bounds);
        var kept = new bool[grid.Inside.Length];
        int slices = Math.Max(grid.Nz, 1);
        int done = 0;

        Parallel.For(0, slices, new ParallelOptions { CancellationToken = token }, k =>
        {
            int from = k * grid.Nx * grid.Ny;

            for (int i = from; i < from + grid.Nx * grid.Ny && i < kept.Length; i++)
            {
                if (!grid.Inside[i]) continue;

                var at = At(grid, i);

                // The bottom stays solid where one was asked for: a web standing on three struts
                // falls over on the bed, and a lamp wants something to sit on anyway.
                if (o.BaseMm > 0f && at.Z <= baseTo)
                {
                    kept[i] = true;
                    continue;
                }

                bool inSkin = depth[i] <= skinInVoxels;

                // A shell is the skin, and only where the web runs through it. A lattice is the
                // skin plus the web behind it - the same two tests, joined the other way round.
                if (o.Kind == VoronoiKind.Shell && !inSkin) continue;
                if (o.Kind == VoronoiKind.Lattice && inSkin && o.SkinMm > 0f)
                {
                    kept[i] = true;
                    continue;
                }

                cells.Nearest(at, out float first, out float second, out float third);

                // A shell is cut on the walls, a lattice on the edges where three walls meet.
                float toEdge = o.Kind == VoronoiKind.Lattice ? third - first : second - first;
                if (toEdge < o.StrutMm) kept[i] = true;
            }

            int seen = Interlocked.Increment(ref done);
            if ((seen & 7) == 0) progress?.Report(new WorkProgress(seen / (float)slices, "Cutting the cells"));
        });

        token.ThrowIfCancellationRequested();
        progress?.Report(WorkProgress.Doing("Mending the pinches"));

        Unpinch(kept, grid.Nx, grid.Ny, grid.Nz, token);

        token.ThrowIfCancellationRequested();
        progress?.Report(WorkProgress.Doing("Building the surface"));

        var built = VoxelRebuild.SurfaceNets(kept, grid.Origin, grid.Voxel, grid.Nx, grid.Ny, grid.Nz, progress);

        // The grid leaves its own ripple on anything that does not run along it: a round wall
        // crosses a column of voxels every so often and comes back ribbed, which on a barrel is
        // a stripe every half millimetre all the way round. Two Taubin passes take that off and
        // leave the shape alone - it is the high frequency that is the grid's and the low one
        // that is the model's, and Taubin's inward-then-outward pair separates exactly those.
        //
        // Two rather than more because a strut is only a few voxels across: smoothing hard would
        // start eating it rather than its steps.
        if (o.Smooth && built.TriangleCount > 0)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(WorkProgress.Doing("Taking the steps off"));

            built = MeshSmoothing.Smooth(built, passes: 2);
        }

        token.ThrowIfCancellationRequested();

        // A grid this fine leaves the odd crumb: a voxel or two on their own, extracted into a
        // dozen triangles enclosing nothing. Harmless in a slicer and untidy in a model, and
        // cheap to be rid of now that the pieces have to be counted anyway - the count is worth
        // having for a lattice, where struts that did not reach the rest of the web are a real
        // fault rather than a crumb.
        int loose = 0;

        if (built.TriangleCount > 0)
        {
            var pieces = MeshComponents.Split(built);

            if (pieces.Count > 1)
            {
                float crumb = grid.Voxel * grid.Voxel * grid.Voxel * 2f;
                var solidPieces = pieces.Where(p => Math.Abs(p.ComputeSignedVolume()) > crumb).ToList();

                if (solidPieces.Count > 0 && solidPieces.Count < pieces.Count)
                    built = Mesh.Combine(solidPieces);

                loose = Math.Max(solidPieces.Count - 1, 0);
            }
        }

        // Counted on the grid rather than measured off the mesh: the same voxels the shape was
        // decided on, so it says what was actually cut and not what the extraction made of it.
        long was = 0, left = 0;
        for (int i = 0; i < kept.Length; i++)
        {
            if (!grid.Inside[i]) continue;

            was++;
            if (kept[i]) left++;
        }

        float share = was == 0 ? 0f : (float)left / was;

        return built.TriangleCount == 0
            ? new VoronoiResult(new Mesh(), seeds.Count, grid.Voxel, 0f,
                "Nothing survived: every strut fell between the voxels. Fewer cells, a wider strut, or more detail.")
            : new VoronoiResult(built, seeds.Count, grid.Voxel, share, Loose: loose);
    }

    /// <summary>
    /// The outline the web will have where it meets the surface, as line segments on the model
    /// itself. For choosing the numbers with, which a solid preview cannot be used for.
    ///
    /// The outside of a finished strut is the surface where the nearest seed and the next nearest
    /// are exactly a strut's width apart in distance, so contouring that same quantity over the
    /// model's own triangles draws precisely the edge the solid would have - and draws it without
    /// a grid, in milliseconds. A coarse preview grid could not: a strut of the width somebody is
    /// choosing is finer than a coarse voxel, so the preview would refuse to show the very thing
    /// being chosen.
    ///
    /// A triangle much larger than a strut is split before it is contoured, or a plain box - six
    /// faces, twelve triangles - would come back with a couple of straight lines across it.
    /// </summary>
    public static List<(Vector3 From, Vector3 To)> Outline(
        Mesh mesh, VoronoiOptions options, CancellationToken token = default)
    {
        var lines = new List<(Vector3, Vector3)>();
        var o = options.Sane();
        var bounds = mesh.ComputeBounds();

        if (bounds.IsEmpty || mesh.TriangleCount == 0) return lines;

        var pool = OnTheSurface(mesh, Math.Clamp(o.Cells * 20, 2000, 40000));
        if (pool.Count < 2) return lines;

        var seeds = SpreadOut(pool, Math.Min(o.Cells, pool.Count), token);
        if (seeds.Count < 2) return lines;

        var cells = new SeedGrid(seeds, bounds);
        var positions = mesh.Positions;
        var indices = mesh.Indices;

        // Fine enough that a strut's edge is a curve rather than a chord, and capped so a dense
        // import does not multiply an already large triangle count. A dense mesh needs little
        // splitting anyway - its triangles are already smaller than a strut - so the cap costs
        // nothing where it bites and keeps the preview inside a frame where it would not.
        float step = MathF.Max(o.StrutMm * 0.7f, 0.05f);
        int most = mesh.TriangleCount > 40_000 ? 2 : mesh.TriangleCount > 8_000 ? 4 : 16;

        bool edges = o.Kind == VoronoiKind.Lattice;

        float Wall(Vector3 at)
        {
            cells.Nearest(at, out float first, out float second, out float third);
            return edges ? third - first : second - first;
        }

        for (int t = 0; t + 2 < indices.Count; t += 3)
        {
            if ((t & 0x3FF) == 0) token.ThrowIfCancellationRequested();

            var a = positions[indices[t]];
            var b = positions[indices[t + 1]];
            var c = positions[indices[t + 2]];

            float longest = MathF.Max((b - a).Length(), MathF.Max((c - b).Length(), (a - c).Length()));
            int n = Math.Clamp((int)MathF.Ceiling(longest / step), 1, most);

            // The triangle's own lattice of points, row by row: row i has i + 1 of them.
            var at = new Vector3[(n + 1) * (n + 2) / 2];
            var value = new float[at.Length];
            int put = 0;

            for (int i = 0; i <= n; i++)
            for (int j = 0; j <= i; j++)
            {
                float u = (float)(i - j) / n, v = (float)j / n;
                at[put] = a + (b - a) * u + (c - a) * v;
                value[put] = Wall(at[put]);
                put++;
            }

            int Row(int i) => i * (i + 1) / 2;

            for (int i = 0; i < n; i++)
            for (int j = 0; j <= i; j++)
            {
                // The upright sub-triangle, and the inverted one that fills the gap beside it.
                Cross(at, value, Row(i) + j, Row(i + 1) + j, Row(i + 1) + j + 1, o.StrutMm, lines);
                if (j < i) Cross(at, value, Row(i) + j, Row(i) + j + 1, Row(i + 1) + j + 1, o.StrutMm, lines);
            }
        }

        return lines;
    }

    /// <summary>
    /// Where one sub-triangle crosses the strut's edge. Two of its corners on one side of the
    /// level and one on the other gives exactly one segment; all three on a side gives none.
    /// </summary>
    private static void Cross(Vector3[] at, float[] value, int p, int q, int r, float level,
                              List<(Vector3, Vector3)> into)
    {
        bool insideP = value[p] < level, insideQ = value[q] < level, insideR = value[r] < level;
        if (insideP == insideQ && insideQ == insideR) return;

        // The corner on its own is whichever one the other two agree against, and the segment
        // crosses the two edges running away from it.
        int alone = insideP == insideQ ? r : insideP == insideR ? q : p;
        int first = alone == p ? q : p;
        int second = alone == r ? q : r;

        into.Add((Between(at, value, alone, first, level), Between(at, value, alone, second, level)));
    }

    private static Vector3 Between(Vector3[] at, float[] value, int from, int to, float level)
    {
        float span = value[to] - value[from];
        float part = MathF.Abs(span) < 1e-9f ? 0.5f : Math.Clamp((level - value[from]) / span, 0f, 1f);

        return Vector3.Lerp(at[from], at[to], part);
    }

    /// <summary>
    /// Fills the voxels that would leave the surface pinched to a line.
    ///
    /// Two voxels that meet only along an edge, or only at a corner, are a shape that no surface
    /// can wrap manifold: the material is joined, but the skin round it has to pass through
    /// itself at the join. The extraction is careful about a cell holding two separate pieces of
    /// material - it gives each its own vertex - but it cannot do anything about two pieces that
    /// genuinely touch at nothing wider than a line.
    ///
    /// A shell never runs into it, because its material is a slab a few voxels thick. A lattice
    /// does, over and over: its struts cross at angles and a pair of them passing close leaves
    /// exactly this. Nine such edges in three quarters of a million triangles, which the healer
    /// cannot mend either - there is no hole to fill and no face pointing the wrong way.
    ///
    /// So the pinch is opened rather than repaired: fill the voxel beside it and the two pieces
    /// meet across a face like anything else. Filling rather than emptying, because these happen
    /// where a strut is at its thinnest and taking material away there would break it. It costs a
    /// handful of voxels out of millions.
    ///
    /// Repeated until nothing changes: a fill can put two other voxels corner to corner. It only
    /// ever adds, so it finishes.
    /// </summary>
    private static void Unpinch(bool[] kept, int nx, int ny, int nz, CancellationToken token)
    {
        int At(int x, int y, int z) => (z * ny + y) * nx + x;

        for (int pass = 0; pass < 4; pass++)
        {
            token.ThrowIfCancellationRequested();
            bool changed = false;

            for (int z = 0; z + 1 < nz; z++)
            for (int y = 0; y + 1 < ny; y++)
            for (int x = 0; x + 1 < nx; x++)
            {
                // Each of the three planes of the little cube: two voxels across a diagonal with
                // the other two empty is a join one line wide.
                changed |= Open(kept, At(x, y, z), At(x + 1, y + 1, z), At(x + 1, y, z), At(x, y + 1, z));
                changed |= Open(kept, At(x, y, z), At(x + 1, y, z + 1), At(x + 1, y, z), At(x, y, z + 1));
                changed |= Open(kept, At(x, y, z), At(x, y + 1, z + 1), At(x, y + 1, z), At(x, y, z + 1));

                changed |= Open(kept, At(x, y + 1, z), At(x + 1, y, z), At(x, y, z), At(x + 1, y + 1, z));
                changed |= Open(kept, At(x, y, z + 1), At(x + 1, y, z), At(x, y, z), At(x + 1, y, z + 1));
                changed |= Open(kept, At(x, y, z + 1), At(x, y + 1, z), At(x, y, z), At(x, y + 1, z + 1));

            }

            if (!changed) return;
        }
    }

    /// <summary>Two across a diagonal with both the others empty: fill one of them.</summary>
    private static bool Open(bool[] kept, int a, int b, int first, int second)
    {
        if (!kept[a] || !kept[b] || kept[first] || kept[second]) return false;

        kept[first] = true;
        return true;
    }

    private static float Longest(Bounds bounds) =>
        Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z));

    /// <summary>The world position of one voxel. The grid runs x fastest, then y, then z.</summary>
    private static Vector3 At(VoxelRebuild.Grid grid, int index)
    {
        int i = index % grid.Nx;
        int j = index / grid.Nx % grid.Ny;
        int k = index / (grid.Nx * grid.Ny);

        return grid.Origin + new Vector3(i, j, k) * grid.Voxel;
    }

    // --- Where the seeds go ---------------------------------------------------------------

    /// <summary>
    /// Points spread over the surface by area, so a densely triangulated patch does not attract
    /// more of them than a plain one.
    ///
    /// Laid out by a low-discrepancy sequence rather than drawn at random: the same model gives
    /// the same web twice, which matters when someone prints one, changes the strut and prints
    /// the other half.
    /// </summary>
    private static List<Vector3> OnTheSurface(Mesh mesh, int want)
    {
        var positions = mesh.Positions;
        var indices = mesh.Indices;
        int triangles = mesh.TriangleCount;

        var upTo = new float[triangles];
        float total = 0f;

        for (int t = 0; t < triangles; t++)
        {
            var a = positions[indices[t * 3]];
            var b = positions[indices[t * 3 + 1]];
            var c = positions[indices[t * 3 + 2]];

            total += Vector3.Cross(b - a, c - a).Length() / 2f;
            upTo[t] = total;
        }

        var found = new List<Vector3>(want);
        if (total <= 0f) return found;

        for (int k = 0; k < want; k++)
        {
            float along = Fraction(k * 0.6180339887498949f) * total;
            int t = Array.BinarySearch(upTo, along);
            if (t < 0) t = ~t;
            t = Math.Clamp(t, 0, triangles - 1);

            var a = positions[indices[t * 3]];
            var b = positions[indices[t * 3 + 1]];
            var c = positions[indices[t * 3 + 2]];

            // Uniform over the triangle: the square root pulls the corner-heavy pair straight.
            float u = MathF.Sqrt(Radical(k, 2));
            float v = Radical(k, 3);

            found.Add(a + (b - a) * u * (1f - v) + (c - a) * u * v);
        }

        return found;
    }

    /// <summary>Points spread through the material, for a lattice.</summary>
    private static List<Vector3> Throughout(VoxelRebuild.Grid grid, int want)
    {
        var inside = new List<int>();
        for (int i = 0; i < grid.Inside.Length; i++)
            if (grid.Inside[i]) inside.Add(i);

        var found = new List<Vector3>(want);
        if (inside.Count == 0) return found;

        for (int k = 0; k < want; k++)
            found.Add(At(grid, inside[(int)(Fraction(k * 0.6180339887498949f) * inside.Count) % inside.Count]));

        return found;
    }

    /// <summary>
    /// Thins a pool down to the wanted number, taking each time whichever is furthest from
    /// everything already taken.
    ///
    /// Even spacing without a rejection loop that can fail to terminate, and without a radius
    /// anyone has to choose: ask for a hundred and get a hundred, spread as far apart as a
    /// hundred can be. Lloyd relaxation would even them further; this is close enough that the
    /// difference is not visible in a printed web.
    /// </summary>
    private static List<Vector3> SpreadOut(List<Vector3> pool, int want, CancellationToken token)
    {
        var taken = new List<Vector3>(want) { pool[0] };
        var nearest = new float[pool.Count];

        for (int i = 0; i < pool.Count; i++) nearest[i] = Vector3.DistanceSquared(pool[i], pool[0]);

        while (taken.Count < want)
        {
            token.ThrowIfCancellationRequested();

            int furthest = 0;
            float best = -1f;

            for (int i = 0; i < pool.Count; i++)
                if (nearest[i] > best)
                {
                    best = nearest[i];
                    furthest = i;
                }

            if (best <= 0f) break; // the pool has nothing left that is not already a seed

            var picked = pool[furthest];
            taken.Add(picked);

            for (int i = 0; i < pool.Count; i++)
                nearest[i] = MathF.Min(nearest[i], Vector3.DistanceSquared(pool[i], picked));
        }

        return taken;
    }

    /// <summary>The fractional part, for a sequence that walks evenly round the unit interval.</summary>
    private static float Fraction(float value) => value - MathF.Floor(value);

    /// <summary>The radical inverse of n in the given base - the van der Corput sequence.</summary>
    private static float Radical(int n, int b)
    {
        float result = 0f, weight = 1f / b;

        for (int at = n + 1; at > 0; at /= b)
        {
            result += at % b * weight;
            weight /= b;
        }

        return Math.Clamp(result, 0f, 1f);
    }

    // --- Finding the two nearest seeds ------------------------------------------------------

    /// <summary>
    /// The seeds, bucketed by where they are, so a point meets the few that could be nearest
    /// rather than all of them.
    ///
    /// Brute force is the obvious thing and is genuinely too slow here: a lattice at any useful
    /// detail asks this of several million points, and a few hundred seeds each time is a
    /// thousand million distance tests. The search walks out ring by ring from the point's own
    /// bucket and stops when the next ring cannot beat what it already has, so the answer is the
    /// true nearest pair rather than an approximation.
    /// </summary>
    private sealed class SeedGrid
    {
        private readonly List<int>[] buckets;
        private readonly Vector3[] seeds;
        private readonly Vector3 origin;
        private readonly float cell;
        private readonly int nx, ny, nz;

        public SeedGrid(List<Vector3> placed, Bounds bounds)
        {
            seeds = [.. placed];

            // About one seed a bucket, which keeps both the bucketing and the walk cheap.
            var span = Vector3.Max(bounds.Size, new Vector3(1e-3f));
            float volume = span.X * span.Y * span.Z;
            cell = MathF.Max(MathF.Cbrt(volume / Math.Max(seeds.Length, 1)), 1e-3f);

            origin = bounds.Min - new Vector3(cell);
            nx = Math.Clamp((int)(span.X / cell) + 3, 1, 256);
            ny = Math.Clamp((int)(span.Y / cell) + 3, 1, 256);
            nz = Math.Clamp((int)(span.Z / cell) + 3, 1, 256);

            buckets = new List<int>[nx * ny * nz];

            for (int i = 0; i < seeds.Length; i++)
            {
                int at = Index(seeds[i]);
                (buckets[at] ??= []).Add(i);
            }
        }

        /// <summary>
        /// How far the nearest three seeds are. Two of them say where a wall is, three where an
        /// edge is, and finding the third costs nothing beyond one more comparison per seed.
        /// </summary>
        public void Nearest(Vector3 p, out float first, out float second, out float third)
        {
            first = float.MaxValue;
            second = float.MaxValue;
            third = float.MaxValue;

            int cx = Column(p.X, origin.X, nx);
            int cy = Column(p.Y, origin.Y, ny);
            int cz = Column(p.Z, origin.Z, nz);

            int reach = Math.Max(nx, Math.Max(ny, nz));

            for (int ring = 0; ring <= reach; ring++)
            {
                // Everything in this ring is at least this far off, so once the third best is
                // inside it there is nothing further out that can improve on any of them.
                if (ring > 0 && (ring - 1) * cell > third) return;

                for (int z = cz - ring; z <= cz + ring; z++)
                {
                    if (z < 0 || z >= nz) continue;

                    for (int y = cy - ring; y <= cy + ring; y++)
                    {
                        if (y < 0 || y >= ny) continue;

                        for (int x = cx - ring; x <= cx + ring; x++)
                        {
                            if (x < 0 || x >= nx) continue;

                            // Only the shell of the ring is new; its inside was walked already.
                            int off = Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz)));
                            if (off != ring) continue;

                            var bucket = buckets[(z * ny + y) * nx + x];
                            if (bucket is null) continue;

                            foreach (int i in bucket)
                            {
                                float away = Vector3.Distance(p, seeds[i]);

                                if (away < first)
                                {
                                    third = second;
                                    second = first;
                                    first = away;
                                }
                                else if (away < second)
                                {
                                    third = second;
                                    second = away;
                                }
                                else if (away < third)
                                {
                                    third = away;
                                }
                            }
                        }
                    }
                }
            }
        }

        private int Index(Vector3 p) =>
            (Column(p.Z, origin.Z, nz) * ny + Column(p.Y, origin.Y, ny)) * nx + Column(p.X, origin.X, nx);

        private int Column(float value, float from, int count) =>
            Math.Clamp((int)((value - from) / cell), 0, count - 1);
    }

    // --- How deep each point sits ------------------------------------------------------------

    /// <summary>
    /// How far inside the material each grid point is, in voxels.
    ///
    /// The same two-pass chamfer sweep the hollower uses, kept here rather than shared because
    /// the two want it at different moments and neither should have to know about the other.
    /// </summary>
    private static float[] DepthInside(VoxelRebuild.Grid grid, CancellationToken token)
    {
        int nx = grid.Nx, ny = grid.Ny, nz = grid.Nz;
        var distance = new float[grid.Inside.Length];

        const float far = float.MaxValue / 4f;
        for (int i = 0; i < distance.Length; i++) distance[i] = grid.Inside[i] ? far : 0f;

        int At(int x, int y, int z) => (z * ny + y) * nx + x;

        // A step along an axis is one voxel, across a face diagonal is root two, across a corner
        // root three. Without that a wall comes out half again as thick across a diagonal.
        const float side = 1f, face = 1.41421356f, corner = 1.7320508f;

        for (int pass = 0; pass < 2; pass++)
        {
            token.ThrowIfCancellationRequested();

            bool forward = pass == 0;

            for (int zi = 0; zi < nz; zi++)
            for (int yi = 0; yi < ny; yi++)
            for (int xi = 0; xi < nx; xi++)
            {
                int z = forward ? zi : nz - 1 - zi;
                int y = forward ? yi : ny - 1 - yi;
                int x = forward ? xi : nx - 1 - xi;

                int here = At(x, y, z);
                if (distance[here] == 0f) continue;

                float best = distance[here];

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;

                    // Only the half of the neighbourhood this pass has already been over.
                    int order = dz != 0 ? dz : dy != 0 ? dy : dx;
                    if (forward ? order > 0 : order < 0) continue;

                    int ax = x + dx, ay = y + dy, az = z + dz;
                    if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz) continue;

                    int steps = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
                    float step = steps == 1 ? side : steps == 2 ? face : corner;

                    best = MathF.Min(best, distance[At(ax, ay, az)] + step);
                }

                distance[here] = best;
            }
        }

        return distance;
    }
}
