using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The hollowed mesh: an outer surface and an inner one facing into the cavity.</param>
/// <param name="WallMm">The wall thickness actually achieved, which the grid may round.</param>
/// <param name="VolumeSavedCm3">How much material the cavity removes.</param>
public readonly record struct HollowResult(Mesh Mesh, float WallMm, double VolumeSavedCm3);

/// <summary>
/// Turns a solid into a shell of a given wall thickness.
///
/// The obvious approach - offset the surface inward and subtract it - is the one that does not
/// work. Offsetting a mesh means moving every vertex along its normal, which folds itself inside
/// out at any concave corner and leaves a self-intersecting mess at every crease. It is the
/// classic hard problem in mesh processing.
///
/// Sampling sidesteps it entirely. The model is already voxelised for <see cref="VoxelRebuild"/>,
/// so hollowing is: measure how deep inside the material each point sits, and keep only the
/// points within one wall thickness of the surface. What is left is the shell, and the surface
/// around it - outer and inner both - comes out of the same extraction, correctly facing.
///
/// The cavity is sealed. A resin print needs a drain hole cut in it afterwards, which a cylinder
/// and Subtract will do.
/// </summary>
public static class MeshHollow
{
    /// <summary>
    /// A wall this thin will not survive slicing, and below about two nozzle widths it is not
    /// worth attempting.
    /// </summary>
    public const float MinimumWallMm = 0.4f;

    /// <summary>
    /// Hollows to the given wall thickness. Blocks; a caller on the UI thread should wrap it in
    /// Task.Run.
    /// </summary>
    public static HollowResult Hollow(
        Mesh mesh, float wallMm, int resolution = VoxelRebuild.DefaultResolution)
    {
        wallMm = Math.Max(wallMm, MinimumWallMm);

        var grid = VoxelRebuild.Sample(mesh, resolution);
        if (grid.Inside.Length == 0) return new HollowResult(mesh, wallMm, 0);

        var depth = DepthInside(grid);

        // The shell is what lies within one wall of the surface. Everything deeper becomes the
        // cavity, and the extraction finds both faces of the wall in one pass.
        float wallInVoxels = wallMm / grid.Voxel;
        var shell = new bool[grid.Inside.Length];
        int hollowed = 0;

        for (int i = 0; i < shell.Length; i++)
        {
            if (!grid.Inside[i]) continue;

            if (depth[i] <= wallInVoxels) shell[i] = true;
            else hollowed++;
        }

        // Nothing deep enough to remove: the part is thinner than the wall it was asked for.
        if (hollowed == 0) return new HollowResult(mesh, wallMm, 0);

        var surface = VoxelRebuild.SurfaceNets(shell, grid.Origin, grid.Voxel, grid.Nx, grid.Ny, grid.Nz);
        double removed = hollowed * Math.Pow(grid.Voxel, 3) / 1000.0;

        return new HollowResult(MeshSmoothing.Smooth(surface, passes: 2), wallMm, removed);
    }

    /// <summary>
    /// How far inside the material each point sits, in voxels.
    ///
    /// A chamfer transform: two sweeps over the grid, one forward and one back, each taking the
    /// best distance any already-visited neighbour can offer plus the step to it. Weights of
    /// 3, 4 and 5 for face, edge and corner neighbours approximate true distance to within a few
    /// per cent, which is far closer than counting steps along the axes - that would leave the
    /// wall half again as thick across a diagonal as along one.
    /// </summary>
    private static float[] DepthInside(VoxelRebuild.Grid grid)
    {
        int nx = grid.Nx, ny = grid.Ny, nz = grid.Nz;
        var distance = new float[grid.Inside.Length];

        const float far = float.MaxValue / 4;
        for (int i = 0; i < distance.Length; i++) distance[i] = grid.Inside[i] ? far : 0;

        int At(int x, int y, int z) => (z * ny + y) * nx + x;

        // Forward: every neighbour already passed over.
        for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            int here = At(x, y, z);
            if (distance[here] == 0) continue;

            float best = distance[here];
            for (int dz = -1; dz <= 0; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dz == 0 && (dy > 0 || (dy == 0 && dx >= 0))) continue;
                best = Better(best, distance, At, nx, ny, nz, x + dx, y + dy, z + dz, dx, dy, dz);
            }

            distance[here] = best;
        }

        // Backward: the neighbours the forward sweep had not reached yet.
        for (int z = nz - 1; z >= 0; z--)
        for (int y = ny - 1; y >= 0; y--)
        for (int x = nx - 1; x >= 0; x--)
        {
            int here = At(x, y, z);
            if (distance[here] == 0) continue;

            float best = distance[here];
            for (int dz = 0; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dz == 0 && (dy < 0 || (dy == 0 && dx <= 0))) continue;
                best = Better(best, distance, At, nx, ny, nz, x + dx, y + dy, z + dz, dx, dy, dz);
            }

            distance[here] = best;
        }

        // The sweeps work in thirds of a voxel, so that a diagonal step can cost four and a
        // corner five without any of them being fractions.
        for (int i = 0; i < distance.Length; i++) distance[i] /= 3f;

        return distance;
    }

    private static float Better(
        float best, float[] distance, Func<int, int, int, int> at, int nx, int ny, int nz,
        int x, int y, int z, int dx, int dy, int dz)
    {
        if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) return best;

        int steps = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
        float cost = steps switch { 1 => 3f, 2 => 4f, _ => 5f };

        return Math.Min(best, distance[at(x, y, z)] + cost);
    }
}
