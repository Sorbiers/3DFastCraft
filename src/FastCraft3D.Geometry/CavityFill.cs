using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry;

/// <summary>
/// Where a part would hold liquid poured into it from above, and the solid that fills it to a
/// level: the inside of a cup or a box, a recess, a sealed hollow.
/// </summary>
/// <remarks>
/// Worked out on the voxel grid Rebuild and Hollow use. Liquid in an empty voxel gets away if it
/// can run sideways or down to the edge of the grid, so the voxels it gets away from are found by
/// spreading from the grid's sides and bottom sideways and upwards - the same paths walked the other
/// way. Every empty voxel that spreading never reaches holds liquid. That makes a pocket open to the
/// side fill only up to its opening, as it would, and a sealed hollow fill whole.
///
/// The grid only decides which space is filled. The part keeps its own surface: the fill is built
/// on the grid reaching a voxel into the walls, cut flat at the level, and joined to the part - or
/// has the part taken out of it, when it is kept as a part of its own - by the exact boolean.
/// </remarks>
public sealed class CavityFill
{
    private readonly VoxelRebuild.Grid grid;
    private readonly bool[] holds;

    // The heights of the part's faces that look straight up, lowest first: where a rim is flat,
    // the height liquid spilling over it stands at.
    private readonly float[] flatTops;

    private CavityFill(VoxelRebuild.Grid grid, bool[] holds, Bounds bounds, bool plateFloor, float[] flatTops)
    {
        this.grid = grid;
        this.holds = holds;
        this.flatTops = flatTops;
        Bounds = bounds;
        PlateFloor = plateFloor;
    }

    /// <summary>Whether the plate was taken as a floor the liquid stands on.</summary>
    public bool PlateFloor { get; }

    /// <summary>The part's own extent.</summary>
    public Bounds Bounds { get; }

    /// <summary>How big one voxel is: the finest step the fill's edge against the walls can take.</summary>
    public float VoxelMm => grid.Voxel;

    /// <summary>Whether there is anywhere at all the part would hold liquid.</summary>
    public bool HoldsAnything => holds.Any(h => h);

    /// <summary>Samples the part and finds where it would hold liquid, at any level. Slow; off the UI thread.</summary>
    /// <param name="plateFloor">
    /// The plate at Z = 0 as a floor: nothing drains down through it, so a tube or a frame standing
    /// on the plate holds liquid as a cup does.
    /// </param>
    public static CavityFill Analyse(Mesh world, int resolution, bool plateFloor = false, CancellationToken token = default, IProgress<WorkProgress>? progress = null)
    {
        var grid = VoxelRebuild.Sample(world, resolution, token, progress);
        token.ThrowIfCancellationRequested();

        int nx = grid.Nx, ny = grid.Ny, nz = grid.Nz;
        var inside = grid.Inside;
        var escapes = new bool[inside.Length];
        var queue = new Queue<int>();
        int At(int i, int j, int k) => (k * ny + j) * nx + i;
        bool BelowPlate(int k) => plateFloor && grid.Origin.Z + k * grid.Voxel < 0f;

        void Reach(int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= nx || j >= ny || k >= nz) return;
            int at = At(i, j, k);
            if (inside[at] || escapes[at] || BelowPlate(k)) return;
            escapes[at] = true;
            queue.Enqueue(at);
        }

        // Liquid gets away through the sides and the bottom of the grid; the top is where it is poured.
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++) { Reach(0, j, k); Reach(nx - 1, j, k); }
        for (int k = 0; k < nz; k++)
            for (int i = 0; i < nx; i++) { Reach(i, 0, k); Reach(i, ny - 1, k); }
        for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++) Reach(i, j, 0);

        // From anywhere it gets away, it got away from the voxels beside it and the one above.
        while (queue.Count > 0)
        {
            int at = queue.Dequeue();
            int i = at % nx, j = at / nx % ny, k = at / (nx * ny);
            Reach(i - 1, j, k); Reach(i + 1, j, k);
            Reach(i, j - 1, k); Reach(i, j + 1, k);
            Reach(i, j, k + 1);
        }

        var holds = new bool[inside.Length];
        for (int n = 0; n < holds.Length; n++) holds[n] = !inside[n] && !escapes[n] && !BelowPlate(n / (nx * ny));

        var tops = new SortedSet<float>();
        for (int t = 0; t + 2 < world.Indices.Count; t += 3)
        {
            var a = world.Positions[world.Indices[t]];
            var up = Vector3.Cross(world.Positions[world.Indices[t + 1]] - a, world.Positions[world.Indices[t + 2]] - a);
            if (up.LengthSquared() > 1e-12f && up.Z / up.Length() > 0.999f) tops.Add(MathF.Round(a.Z, 4));
        }

        return new CavityFill(grid, holds, world.ComputeBounds(), plateFloor, tops.ToArray());
    }

    /// <summary>How much the fill to this level comes to, near enough, in cubic millimetres.</summary>
    public double VolumeMm3(float level)
    {
        int top = LayerAt(level);
        long count = 0;
        int layer = grid.Nx * grid.Ny;
        for (int k = 0; k <= top && k < grid.Nz; k++)
            for (int n = k * layer; n < (k + 1) * layer; n++)
                if (holds[n]) count++;

        return count * (double)grid.Voxel * grid.Voxel * grid.Voxel;
    }

    /// <summary>The last layer of grid points at or below the level.</summary>
    private int LayerAt(float level) => (int)MathF.Floor((level - grid.Origin.Z) / grid.Voxel);

    /// <summary>
    /// The fill to a level as it sits on the grid, reaching a voxel into the walls round it and cut
    /// flat at the level. Rough against the walls, which the part itself covers; null when there
    /// is nothing to fill below the level. Quick enough to follow a slider.
    /// </summary>
    /// <param name="below">How far under the level, or under the rim it spills over, the top is cut.</param>
    public Mesh? Body(float level, float below = 0f)
    {
        int nx = grid.Nx, ny = grid.Ny, nz = grid.Nz;
        int wanted = LayerAt(level), layer = nx * ny;
        if (wanted < 0) return null;

        // Where the liquid actually stands. Asked for more than the part holds, it spills over the
        // lowest point of the rim, and the grid alone would stop it at the last layer below that -
        // a fraction of a millimetre short of the rim, a line all round a filled tray. So its top is
        // taken from the part instead: the flat top the rim has between that layer and the next.
        int highest = -1;
        for (int k = Math.Min(wanted, nz - 1); k >= 0 && highest < 0; k--)
            for (int n = k * layer; n < (k + 1) * layer; n++)
                if (holds[n]) { highest = k; break; }
        if (highest < 0) return null;

        bool spills = highest < wanted;
        if (spills) level = SpillHeight(highest, level);
        level -= below;
        int top = Math.Min((spills ? highest : wanted) + 1, nz - 1);

        // A layer past the level, so the flat cut below goes through the fill rather than skimming
        // its grid-shaped top; and a voxel into the walls, so the fill overlaps them and the join
        // is made by the part's own surface.
        var mask = new bool[holds.Length];
        bool any = false;
        for (int k = 0; k <= top; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int at = (k * ny + j) * nx + i;
                    if (!holds[at]) continue;
                    mask[at] = true;
                    any = true;
                }

        if (!any) return null;

        // Spilling, the fill's own columns carried a layer higher, so the cut at the rim goes
        // through fill rather than through the grid's top.
        if (spills && highest + 1 < nz)
            for (int n = highest * layer; n < (highest + 1) * layer; n++)
                if (mask[n] && !grid.Inside[n + layer]) mask[n + layer] = true;

        var grown = (bool[])mask.Clone();
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int at = (k * ny + j) * nx + i;
                    if (!grid.Inside[at] || k > top) continue;
                    if ((i > 0 && mask[at - 1]) || (i + 1 < nx && mask[at + 1])
                        || (j > 0 && mask[at - nx]) || (j + 1 < ny && mask[at + nx])
                        || (k > 0 && mask[at - nx * ny]) || (k + 1 < nz && mask[at + nx * ny]))
                        grown[at] = true;
                }

        var surface = VoxelRebuild.SurfaceNets(grown, grid.Origin, grid.Voxel, nx, ny, nz);
        if (surface.TriangleCount == 0) return null;

        var cut = PlaneClip.Keep(surface, Matrix4x4.Identity, -Vector3.UnitZ, -level);

        // Standing on the plate, flat on it rather than a voxel's steps below it.
        if (PlateFloor && cut.TriangleCount > 0 && cut.ComputeBounds().Min.Z < 0f)
            cut = PlaneClip.Keep(cut, Matrix4x4.Identity, Vector3.UnitZ, 0f);

        return cut.TriangleCount > 0 ? cut : null;
    }

    /// <summary>How far under the level, or a rim it comes up to, a fill stops. See <see cref="Apply"/>.</summary>
    public const float Flush = 0.01f;

    /// <summary>
    /// How high liquid stands that spills over a rim somewhere above grid layer <paramref name="k"/>:
    /// the lowest of the part's flat tops between that layer and the next - the rim - or halfway
    /// between the two where the rim has no flat top. Never above the level asked for.
    /// </summary>
    private float SpillHeight(int k, float level)
    {
        float from = grid.Origin.Z + k * grid.Voxel, to = from + grid.Voxel;
        foreach (float z in flatTops)
            if (z > from + 1e-4f && z <= to + 1e-4f) return MathF.Min(z, level);

        return MathF.Min(from + grid.Voxel / 2f, level);
    }

    /// <summary>
    /// The part filled to a level, as one solid; or, kept apart, the fill alone, fitting the part
    /// exactly. Null when there is nothing to fill or the boolean would not come out closed - the
    /// part is never given back torn.
    /// </summary>
    public (Mesh Part, Mesh? Fill)? Apply(Mesh world, float level, bool apart, CancellationToken token = default)
    {
        // The top is cut a hair under a rim it comes up to: exactly level with the rim's top, the
        // two faces lie in one plane, and the boolean - the union, or taking the part out of a fill
        // kept apart - left edges four faces meet at. A hundredth of a millimetre is no step
        // anybody sees or any printer lays.
        if (Body(level, Flush) is not { } body) return null;
        token.ThrowIfCancellationRequested();

        if (apart)
        {
            if (ManifoldCsg.Subtract(body, world, token) is not { TriangleCount: > 0 } fill) return null;
            return fill.Welded().CheckHealth().IsWatertight ? (world, fill) : null;
        }

        if (ManifoldCsg.Union(world, body, token) is not { TriangleCount: > 0 } filled) return null;
        return filled.Welded().CheckHealth().IsWatertight ? (filled, null) : null;
    }
}
