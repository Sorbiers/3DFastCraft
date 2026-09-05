using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The rebuilt mesh.</param>
/// <param name="Resolution">Voxels along the model's longest side.</param>
/// <param name="VoxelSizeMm">How big one voxel is, which is the detail that survives.</param>
public readonly record struct RebuildResult(Mesh Mesh, int Resolution, float VoxelSizeMm);

/// <summary>
/// Rebuilds a model's surface from scratch, which mends damage that patching cannot.
///
/// <see cref="MeshHealer"/> works on the triangles it is given: it fills holes, turns faces round,
/// welds what should have been welded. None of that helps a mesh whose surface passes through
/// itself, because there is no hole to fill and no face pointing the wrong way - the geometry is
/// simply self-contradictory. A model exported with its lettering sitting inside its body rather
/// than fused to it is closed, valid-looking, and unprintable.
///
/// So this ignores the triangles as a surface and asks a different question: for each point in
/// space, is it inside the model? Answer that everywhere, and the surface is whatever separates
/// the two - which is watertight by construction, whatever mess it was built from.
///
/// The price is resolution. Everything is quantised to the voxel grid, so detail finer than one
/// voxel is gone. That is why this is a separate tool rather than what Repair does quietly.
/// </summary>
public static class VoxelRebuild
{
    public const int DefaultResolution = 80;

    /// <summary>
    /// Roughly how many triangles a rebuild will produce.
    ///
    /// The surface is covered in voxel-sized quads, two triangles each, so the count follows the
    /// model's area rather than its bulk - and it quadruples every time the resolution doubles.
    /// A 20 mm cube at 160 voxels across comes out at three hundred thousand triangles, which is
    /// worth knowing before pressing the button rather than after.
    /// </summary>
    public static long EstimateTriangles(float surfaceAreaMm2, float voxelSizeMm) =>
        voxelSizeMm <= 0 ? 0 : (long)(2.0 * surfaceAreaMm2 / (voxelSizeMm * voxelSizeMm));

    /// <summary>Past this the grid costs more memory than the result is worth.</summary>
    public const int MaximumResolution = 320;

    public const int MinimumResolution = 24;

    /// <param name="Inside">One flag per grid point, true where the model is.</param>
    /// <param name="Origin">Where grid point (0,0,0) sits.</param>
    /// <param name="Voxel">The spacing, in millimetres.</param>
    internal readonly record struct Grid(bool[] Inside, Vector3 Origin, float Voxel, int Nx, int Ny, int Nz);

    /// <summary>
    /// Samples the model onto a grid, which is the half of rebuilding that hollowing also needs.
    /// </summary>
    internal static Grid Sample(Mesh mesh, int resolution)
    {
        resolution = Math.Clamp(resolution, MinimumResolution, MaximumResolution);

        var bounds = mesh.ComputeBounds();
        if (bounds.IsEmpty || mesh.TriangleCount == 0)
            return new Grid([], Vector3.Zero, 0, 0, 0, 0);

        float voxel = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)) / resolution;
        if (voxel <= 0) return new Grid([], Vector3.Zero, 0, 0, 0, 0);

        const float pad = 2.5137f;
        var origin = bounds.Min - new Vector3(voxel * pad);
        var span = bounds.Size + new Vector3(voxel * pad * 2);

        int nx = (int)MathF.Ceiling(span.X / voxel) + 1;
        int ny = (int)MathF.Ceiling(span.Y / voxel) + 1;
        int nz = (int)MathF.Ceiling(span.Z / voxel) + 1;

        return new Grid(Occupancy(mesh, origin, voxel, nx, ny, nz), origin, voxel, nx, ny, nz);
    }

    /// <summary>
    /// Rebuilds the surface at the given resolution, counted along the model's longest side.
    /// Blocks; callers on the UI thread should wrap it in Task.Run.
    /// </summary>
    public static RebuildResult Rebuild(Mesh mesh, int resolution = DefaultResolution)
    {
        resolution = Math.Clamp(resolution, MinimumResolution, MaximumResolution);

        var bounds = mesh.ComputeBounds();
        if (bounds.IsEmpty || mesh.TriangleCount == 0)
            return new RebuildResult(new Mesh(), resolution, 0);

        float voxel = Math.Max(bounds.Size.X, Math.Max(bounds.Size.Y, bounds.Size.Z)) / resolution;
        if (voxel <= 0) return new RebuildResult(new Mesh(), resolution, 0);

        // Air all round, so the surface always closes rather than running into the side of the
        // grid - and offset by an awkward fraction of a voxel rather than a whole one.
        //
        // Models are overwhelmingly axis-aligned, so a grid that starts on a round multiple puts
        // its sample points exactly on the model's own faces. Every one of those samples is then
        // a coin toss decided by floating-point noise, and the boundary comes out ragged: 336
        // open edges on a plain box. Half a voxel plus a nudge lands the samples in open space.
        const float pad = 2.5137f;
        var origin = bounds.Min - new Vector3(voxel * pad);
        var span = bounds.Size + new Vector3(voxel * pad * 2);

        int nx = (int)MathF.Ceiling(span.X / voxel) + 1;
        int ny = (int)MathF.Ceiling(span.Y / voxel) + 1;
        int nz = (int)MathF.Ceiling(span.Z / voxel) + 1;

        bool[] inside = Occupancy(mesh, origin, voxel, nx, ny, nz);
        var surface = SurfaceNets(inside, origin, voxel, nx, ny, nz);

        // The grid leaves a faint terracing on anything that was not aligned to it. A couple of
        // passes take that off without moving the shape anywhere.
        return new RebuildResult(MeshSmoothing.Smooth(surface, passes: 2), resolution, voxel);
    }

    /// <summary>
    /// Decides inside from outside at every grid point.
    ///
    /// One ray per grid line rather than one per point: the ray meets the surface a handful of
    /// times, and sorting those crossings classifies every point along the line at once. The
    /// crossings are counted with their direction, so a point is inside when the total is not
    /// zero. Parity alone - odd is in, even is out - would call the overlap of two solids
    /// outside, which is exactly backwards for the meshes this tool exists to mend.
    /// </summary>
    internal static bool[] Occupancy(Mesh mesh, Vector3 origin, float voxel, int nx, int ny, int nz)
    {
        var inside = new bool[nx * ny * nz];
        var buckets = new Buckets(mesh, origin, voxel, ny, nz);
        var crossings = new List<(float X, int Winding)>(32);

        for (int k = 0; k < nz; k++)
        {
            float z = origin.Z + k * voxel;

            for (int j = 0; j < ny; j++)
            {
                float y = origin.Y + j * voxel;

                crossings.Clear();
                foreach (int t in buckets.For(y, z))
                {
                    if (!Crosses(mesh, t, y, z, out float x, out int winding)) continue;
                    crossings.Add((x, winding));
                }

                if (crossings.Count == 0) continue;
                crossings.Sort((a, b) => a.X.CompareTo(b.X));

                int at = 0, total = 0;
                int row = (k * ny + j) * nx;

                for (int i = 0; i < nx; i++)
                {
                    float x = origin.X + i * voxel;

                    while (at < crossings.Count && crossings[at].X <= x)
                        total += crossings[at++].Winding;

                    inside[row + i] = total != 0;
                }
            }
        }

        return inside;
    }

    /// <summary>
    /// Where a ray along X through (y, z) meets one triangle, and which way it was going through.
    ///
    /// Worked out in the YZ plane, where the ray is a single point: a point-in-triangle test,
    /// then read off the X. The whole difficulty is the boundary. A ray landing exactly on the
    /// edge two triangles share must be counted by exactly one of them - counted twice and the
    /// winding never returns to zero, so the model bleeds out to the edge of the grid; counted
    /// by neither and it does the same in reverse.
    ///
    /// Rounding makes that surprisingly hard, because the two triangles compute the edge from
    /// different corners and can disagree about which side the point is on. So each edge is put
    /// in a fixed order first, by its endpoints rather than by the triangle it belongs to. Both
    /// triangles then evaluate the identical expression and get the identical answer, and the
    /// one that traverses the edge the canonical way takes the tie.
    /// </summary>
    private static bool Crosses(Mesh mesh, int triangle, float y, float z, out float x, out int winding)
    {
        x = 0;
        winding = 0;

        Vector3 a = mesh.Positions[mesh.Indices[triangle]];
        Vector3 b = mesh.Positions[mesh.Indices[triangle + 1]];
        Vector3 c = mesh.Positions[mesh.Indices[triangle + 2]];

        double area = ((double)b.Y - a.Y) * ((double)c.Z - a.Z)
                    - ((double)c.Y - a.Y) * ((double)b.Z - a.Z);
        if (area == 0) return false; // edge-on to the ray, so it covers no area at all

        int facing = area > 0 ? 1 : -1;

        if (!Covers(b, c, y, z, facing, out double ea)) return false;
        if (!Covers(c, a, y, z, facing, out double eb)) return false;
        if (!Covers(a, b, y, z, facing, out double ec)) return false;

        double total = ea + eb + ec;
        if (total == 0) return false;

        x = (float)((ea * a.X + eb * b.X + ec * c.X) / total);
        winding = facing;
        return true;
    }

    /// <summary>
    /// Whether the point is on the inward side of one edge, and how far - which doubles as the
    /// barycentric weight of the corner opposite it.
    /// </summary>
    private static bool Covers(
        Vector3 from, Vector3 to, float y, float z, int facing, out double weight)
    {
        // Fixed order by endpoint, so the triangle on the other side of this edge evaluates the
        // very same expression rather than an algebraically equal one.
        int direction = 1;
        if (from.Y > to.Y || (from.Y == to.Y && from.Z > to.Z))
        {
            (from, to) = (to, from);
            direction = -1;
        }

        double edge = ((double)to.Y - from.Y) * ((double)z - from.Z)
                    - ((double)to.Z - from.Z) * ((double)y - from.Y);

        weight = edge * direction;

        // Exactly on the edge: the triangle that walks it the canonical way takes it, so the
        // pair on either side count the crossing once between them.
        if (edge == 0) return direction > 0;

        return weight * facing > 0;
    }

    /// <summary>
    /// Builds the surface between the inside and the outside.
    ///
    /// Surface nets rather than marching cubes: a vertex inside each cell that straddles the
    /// boundary, and a quad wherever a grid edge changes state. No lookup tables, and it lands
    /// smoother on plain inside/outside data than marching cubes does.
    ///
    /// With one caveat that matters. A cell at a sharp corner can be entered by two separate
    /// pieces of material - the tip of a tetrahedron is the obvious case - and a single vertex
    /// for both welds them into one surface that passes through itself. So each cell gets a
    /// vertex per connected piece of material in it, found by walking the cell's own edges, and
    /// a face takes the vertex belonging to the piece it actually touches.
    /// </summary>
    internal static Mesh SurfaceNets(bool[] inside, Vector3 origin, float voxel, int nx, int ny, int nz)
    {
        int At(int i, int j, int k) => (k * ny + j) * nx + i;
        int CellAt(int i, int j, int k) => (k * (ny - 1) + j) * (nx - 1) + i;

        // Only cells on the boundary hold anything, and they are a surface's worth among a
        // volume's - a dictionary rather than an array over every cell in the grid.
        var corners = new Dictionary<int, int[]>();
        var positions = new List<Vector3>();

        // Allocated once. Inside the loop these would be stack allocations that are not released
        // until the method returns, and there are millions of cells.
        Span<bool> filled = stackalloc bool[8];
        Span<int> piece = stackalloc int[8];

        for (int k = 0; k + 1 < nz; k++)
        for (int j = 0; j + 1 < ny; j++)
        for (int i = 0; i + 1 < nx; i++)
        {
            int count = 0;

            for (int c = 0; c < 8; c++)
            {
                filled[c] = inside[At(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1))];
                if (filled[c]) count++;
            }

            if (count == 0 || count == 8) continue; // wholly in or wholly out: no surface here

            // Which piece of material each filled corner belongs to, joined along cell edges.
            for (int c = 0; c < 8; c++) piece[c] = filled[c] ? c : -1;

            for (bool again = true; again;)
            {
                again = false;
                for (int c = 0; c < 8; c++)
                {
                    if (!filled[c]) continue;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        int other = c ^ (1 << axis);
                        if (!filled[other] || piece[c] == piece[other]) continue;

                        int lowest = Math.Min(piece[c], piece[other]);
                        piece[c] = piece[other] = lowest;
                        again = true;
                    }
                }
            }

            // One vertex per piece, at the middle of the boundary crossings that touch it.
            var slots = new int[8];
            Array.Fill(slots, -1);

            for (int lead = 0; lead < 8; lead++)
            {
                if (!filled[lead] || piece[lead] != lead) continue;

                var centre = Vector3.Zero;
                int crossings = 0;

                for (int c = 0; c < 8; c++)
                {
                    if (!filled[c] || piece[c] != lead) continue;

                    for (int axis = 0; axis < 3; axis++)
                    {
                        int other = c ^ (1 << axis);
                        if (filled[other]) continue; // both filled: not a boundary crossing

                        centre += Middle(origin, voxel, i, j, k, c, other);
                        crossings++;
                    }
                }

                if (crossings == 0) continue;

                int vertex = positions.Count;
                positions.Add(centre / crossings);

                for (int c = 0; c < 8; c++)
                    if (filled[c] && piece[c] == lead)
                        slots[c] = vertex;
            }

            corners[CellAt(i, j, k)] = slots;
        }

        var indices = new List<int>();

        // A quad for every grid edge that changes state, joining the four cells around it.
        for (int k = 1; k < nz - 1; k++)
        for (int j = 1; j < ny - 1; j++)
        for (int i = 1; i < nx - 1; i++)
        {
            bool here = inside[At(i, j, k)];

            AddFace(here, inside[At(i + 1, j, k)], i, j, k, 0,
                [(i, j - 1, k - 1), (i, j, k - 1), (i, j, k), (i, j - 1, k)]);

            AddFace(here, inside[At(i, j + 1, k)], i, j, k, 1,
                [(i - 1, j, k - 1), (i - 1, j, k), (i, j, k), (i, j, k - 1)]);

            AddFace(here, inside[At(i, j, k + 1)], i, j, k, 2,
                [(i - 1, j - 1, k), (i, j - 1, k), (i, j, k), (i - 1, j, k)]);
        }

        return new Mesh(positions, indices);

        void AddFace(bool here, bool beyond, int i, int j, int k, int axis, (int I, int J, int K)[] cells)
        {
            if (here == beyond) return;

            // The material is at one end of the edge; every face takes the vertex for the piece
            // that end belongs to, which is what keeps two sheets in one cell apart.
            int gi = i + (!here && axis == 0 ? 1 : 0);
            int gj = j + (!here && axis == 1 ? 1 : 0);
            int gk = k + (!here && axis == 2 ? 1 : 0);

            var quad = new int[4];
            for (int n = 0; n < 4; n++)
            {
                var (ci, cj, ck) = cells[n];
                if (!corners.TryGetValue(CellAt(ci, cj, ck), out var slots)) return;

                int corner = (gi - ci) | ((gj - cj) << 1) | ((gk - ck) << 2);
                quad[n] = slots[corner];
                if (quad[n] < 0) return;
            }

            // The four cells are listed anticlockwise about the edge, which faces the quad along
            // the positive axis. That is right when the material is on the near side; when it is
            // on the far side the surface faces the other way and the order has to reverse.
            if (!here) (quad[1], quad[3]) = (quad[3], quad[1]);

            indices.Add(quad[0]);
            indices.Add(quad[1]);
            indices.Add(quad[2]);
            indices.Add(quad[0]);
            indices.Add(quad[2]);
            indices.Add(quad[3]);
        }
    }

    /// <summary>The midpoint of one cell edge, given its two corners.</summary>
    private static Vector3 Middle(Vector3 origin, float voxel, int i, int j, int k, int from, int to)
    {
        float x = i + ((from & 1) + (to & 1)) * 0.5f;
        float y = j + (((from >> 1) & 1) + ((to >> 1) & 1)) * 0.5f;
        float z = k + (((from >> 2) & 1) + ((to >> 2) & 1)) * 0.5f;

        return new Vector3(origin.X + x * voxel, origin.Y + y * voxel, origin.Z + z * voxel);
    }

    /// <summary>
    /// Triangles sorted by the patch of YZ they cover, so a ray only tests the few it could
    /// possibly meet rather than the whole model.
    /// </summary>
    private sealed class Buckets
    {
        private const int Side = 48;

        private readonly List<int>[] cells = new List<int>[Side * Side];
        private readonly float originY, originZ, sizeY, sizeZ;

        public Buckets(Mesh mesh, Vector3 origin, float voxel, int ny, int nz)
        {
            originY = origin.Y;
            originZ = origin.Z;
            sizeY = MathF.Max((ny - 1) * voxel, 1e-6f);
            sizeZ = MathF.Max((nz - 1) * voxel, 1e-6f);

            for (int i = 0; i < cells.Length; i++) cells[i] = new List<int>();

            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vector3 a = mesh.Positions[mesh.Indices[t]];
                Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
                Vector3 c = mesh.Positions[mesh.Indices[t + 2]];

                int y0 = Slot(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), originY, sizeY);
                int y1 = Slot(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)), originY, sizeY);
                int z0 = Slot(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)), originZ, sizeZ);
                int z1 = Slot(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)), originZ, sizeZ);

                for (int z = z0; z <= z1; z++)
                    for (int y = y0; y <= y1; y++)
                        cells[z * Side + y].Add(t);
            }
        }

        /// <summary>The triangles that could possibly cross a ray through this point.</summary>
        public List<int> For(float y, float z) =>
            cells[Slot(z, originZ, sizeZ) * Side + Slot(y, originY, sizeY)];

        private static int Slot(float value, float origin, float size) =>
            Math.Clamp((int)((value - origin) / size * Side), 0, Side - 1);
    }
}
