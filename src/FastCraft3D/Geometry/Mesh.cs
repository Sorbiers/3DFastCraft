using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// A renderer-agnostic indexed triangle mesh in millimetres, Z-up.
/// Deliberately free of any WPF / SharpDX types so the viewport stays swappable.
/// Vertices are shared (welded) so that CSG and watertightness checks work.
/// </summary>
public sealed class Mesh
{
    public List<Vector3> Positions { get; }
    public List<int> Indices { get; }

    public Mesh()
    {
        Positions = new List<Vector3>();
        Indices = new List<int>();
    }

    public Mesh(IEnumerable<Vector3> positions, IEnumerable<int> indices)
    {
        Positions = positions.ToList();
        Indices = indices.ToList();
    }

    public int VertexCount => Positions.Count;
    public int TriangleCount => Indices.Count / 3;

    public Mesh Clone() => new(Positions, Indices);

    public Bounds ComputeBounds() => Bounds.FromPoints(Positions);

    /// <summary>Total area of every triangle, in mm2.</summary>
    public float ComputeSurfaceArea()
    {
        double area = 0;
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            Vector3 a = Positions[Indices[i]];
            area += Vector3.Cross(Positions[Indices[i + 1]] - a, Positions[Indices[i + 2]] - a).Length();
        }

        return (float)(area / 2);
    }

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        int i = Positions.Count;
        Positions.Add(a);
        Positions.Add(b);
        Positions.Add(c);
        Indices.Add(i);
        Indices.Add(i + 1);
        Indices.Add(i + 2);
    }

    /// <summary>Reverses triangle winding, flipping which side is "outside".</summary>
    public void FlipWinding()
    {
        for (int i = 0; i + 2 < Indices.Count; i += 3)
            (Indices[i + 1], Indices[i + 2]) = (Indices[i + 2], Indices[i + 1]);
    }

    /// <summary>
    /// Signed volume via the divergence theorem. Positive means outward-facing
    /// winding, which is what STL requires. Negative means the mesh is inside-out.
    /// </summary>
    public double ComputeSignedVolume()
    {
        double v = 0;
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            Vector3 a = Positions[Indices[i]];
            Vector3 b = Positions[Indices[i + 1]];
            Vector3 c = Positions[Indices[i + 2]];
            v += (double)a.X * ((double)b.Y * c.Z - (double)b.Z * c.Y)
               - (double)a.Y * ((double)b.X * c.Z - (double)b.Z * c.X)
               + (double)a.Z * ((double)b.X * c.Y - (double)b.Y * c.X);
        }
        return v / 6.0;
    }

    /// <summary>
    /// Merges vertices that coincide within <paramref name="tolerance"/> and drops degenerate
    /// triangles. Essential after importing binary STL, which stores every triangle as three
    /// unshared vertices, and after CSG, which emits an unshared vertex per fan corner.
    ///
    /// Note the neighbourhood search. Simply quantising each position into a grid cell and
    /// merging exact key matches is much faster but silently wrong: two vertices a nanometre
    /// apart can straddle a cell boundary and round to different keys, so they never merge.
    /// That leaves zero-length edges and holes that no amount of downstream repair can close.
    /// With a cell size of exactly <paramref name="tolerance"/>, any two points within
    /// tolerance are at most one cell apart on each axis, so scanning the 27-cell
    /// neighbourhood is exhaustive.
    /// </summary>
    public Mesh Welded(float tolerance = 1e-4f)
    {
        float cell = MathF.Max(tolerance, 1e-9f);
        float toleranceSq = tolerance * tolerance;
        var buckets = new Dictionary<(int, int, int), List<int>>(Positions.Count);
        var remap = new int[Positions.Count];
        var newPositions = new List<Vector3>(Positions.Count);

        for (int i = 0; i < Positions.Count; i++)
        {
            Vector3 p = Positions[i];
            int cx = (int)MathF.Floor(p.X / cell);
            int cy = (int)MathF.Floor(p.Y / cell);
            int cz = (int)MathF.Floor(p.Z / cell);

            int found = -1;
            for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                    for (int dz = -1; dz <= 1 && found < 0; dz++)
                    {
                        if (!buckets.TryGetValue((cx + dx, cy + dy, cz + dz), out var candidates)) continue;
                        foreach (int c in candidates)
                            if (Vector3.DistanceSquared(newPositions[c], p) <= toleranceSq)
                            {
                                found = c;
                                break;
                            }
                    }

            if (found >= 0)
            {
                remap[i] = found;
                continue;
            }

            remap[i] = newPositions.Count;
            var key = (cx, cy, cz);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new List<int>();
            bucket.Add(newPositions.Count);
            newPositions.Add(p);
        }

        var newIndices = new List<int>(Indices.Count);
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            int a = remap[Indices[i]], b = remap[Indices[i + 1]], c = remap[Indices[i + 2]];
            if (a == b || b == c || a == c) continue; // collapsed to a line or point
            newIndices.Add(a);
            newIndices.Add(b);
            newIndices.Add(c);
        }

        return new Mesh(newPositions, newIndices);
    }

    /// <summary>
    /// Inspects manifoldness so exports can warn before writing an unprintable file.
    ///
    /// Welded first when it has to be. Two shells that meet on a face - a group of parts that
    /// touch - have two copies of that face, one from each shell, and while the vertices are
    /// separate the two copies never meet: every edge still belongs to exactly two triangles and
    /// the mesh reports itself watertight when it is not a solid at all. Everything built on it
    /// afterwards inherits the damage.
    ///
    /// A welded mesh has no two vertices in the same place, so the test below costs one pass and
    /// nothing else. Only the meshes that are actually suspect pay for the weld.
    /// </summary>
    public MeshHealth CheckHealth() => HasCoincidentVertices() ? Welded().Measure() : Measure();

    /// <summary>Whether two vertices sit on top of each other, which makes the count untrustworthy.</summary>
    private bool HasCoincidentVertices()
    {
        var seen = new HashSet<(int, int, int)>(Positions.Count);

        foreach (var p in Positions)
            if (!seen.Add(((int)MathF.Round(p.X * 1e4f), (int)MathF.Round(p.Y * 1e4f), (int)MathF.Round(p.Z * 1e4f))))
                return true;

        return false;
    }

    private MeshHealth Measure()
    {
        var directed = new Dictionary<(int, int), int>(Indices.Count);
        for (int i = 0; i + 2 < Indices.Count; i += 3)
        {
            int a = Indices[i], b = Indices[i + 1], c = Indices[i + 2];
            Bump(directed, (a, b));
            Bump(directed, (b, c));
            Bump(directed, (c, a));
        }

        int boundary = 0, nonManifold = 0, inconsistent = 0;
        var seen = new HashSet<(int, int)>();
        foreach (var ((a, b), count) in directed)
        {
            var undirected = a < b ? (a, b) : (b, a);
            if (!seen.Add(undirected)) continue;

            directed.TryGetValue((b, a), out int reverse);
            int total = count + reverse;
            if (total == 1) boundary++;
            else if (total > 2) nonManifold++;
            else if (count != 1 || reverse != 1) inconsistent++;
        }

        return new MeshHealth(TriangleCount, boundary, nonManifold, inconsistent, ComputeSignedVolume());

        static void Bump(Dictionary<(int, int), int> d, (int, int) k) =>
            d[k] = d.TryGetValue(k, out int v) ? v + 1 : 1;
    }

    public static Mesh Combine(IEnumerable<Mesh> meshes)
    {
        var result = new Mesh();
        foreach (var m in meshes)
        {
            int offset = result.Positions.Count;
            result.Positions.AddRange(m.Positions);
            foreach (int i in m.Indices) result.Indices.Add(i + offset);
        }
        return result;
    }
}

/// <param name="BoundaryEdges">Edges used by only one triangle — holes in the surface.</param>
/// <param name="NonManifoldEdges">Edges shared by three or more triangles.</param>
/// <param name="InconsistentEdges">Edges whose two triangles disagree on facing direction.</param>
public readonly record struct MeshHealth(
    int TriangleCount,
    int BoundaryEdges,
    int NonManifoldEdges,
    int InconsistentEdges,
    double SignedVolume)
{
    public bool IsWatertight => BoundaryEdges == 0 && NonManifoldEdges == 0 && InconsistentEdges == 0;
    public bool IsInsideOut => SignedVolume < 0;

    /// <summary>Volume in cm3, the unit slicers report.</summary>
    public double VolumeCm3 => Math.Abs(SignedVolume) / 1000.0;

    public string Describe()
    {
        if (IsWatertight && !IsInsideOut) return "Watertight - ready to print";
        var problems = new List<string>();
        if (BoundaryEdges > 0) problems.Add($"{BoundaryEdges} open edge(s)");
        if (NonManifoldEdges > 0) problems.Add($"{NonManifoldEdges} non-manifold edge(s)");
        if (InconsistentEdges > 0) problems.Add($"{InconsistentEdges} flipped face(s)");
        if (IsInsideOut) problems.Add("mesh is inside-out");
        return string.Join(", ", problems);
    }
}
