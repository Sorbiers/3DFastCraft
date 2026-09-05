using System.Numerics;

namespace FastCraft3D.Geometry;

/// <param name="Mesh">The healed mesh.</param>
/// <param name="Before">How it looked going in.</param>
/// <param name="After">How it looks now.</param>
/// <param name="TrianglesDropped">Degenerate and sliver triangles thrown away.</param>
/// <param name="HolesFilled">Boundary loops that were capped.</param>
/// <param name="FacesFlipped">Triangles turned round to agree with their neighbours.</param>
public readonly record struct HealResult(
    Mesh Mesh, MeshHealth Before, MeshHealth After,
    int TrianglesDropped, int HolesFilled, int FacesFlipped)
{
    public bool Improved => After.BoundaryEdges + After.NonManifoldEdges + After.InconsistentEdges
        < Before.BoundaryEdges + Before.NonManifoldEdges + Before.InconsistentEdges;

    public string Describe()
    {
        if (Before.IsWatertight) return "Nothing needed repairing";

        var parts = new List<string>();
        if (HolesFilled > 0) parts.Add($"{HolesFilled} hole(s) filled");
        if (TrianglesDropped > 0) parts.Add($"{TrianglesDropped} sliver(s) removed");
        if (FacesFlipped > 0) parts.Add($"{FacesFlipped} face(s) turned round");

        string did = parts.Count > 0 ? string.Join(", ", parts) : "nothing to do";
        return After.IsWatertight ? $"Repaired - {did}" : $"{did}; {After.Describe()}";
    }
}

/// <summary>
/// Makes a damaged mesh printable again.
///
/// <see cref="MeshRepair"/> tidies the two defects a boolean routinely leaves - coincident fins
/// and T-junctions - and it runs after every operation. This goes further, and is for a mesh
/// that arrived broken: an imported STL with a few triangles missing, a shell someone exported
/// inside out, faces that disagree with their neighbours. It welds, drops what is genuinely
/// nothing, makes the winding agree and caps the holes.
///
/// What it cannot do is rescue a mesh that overlaps itself. Local patching has no answer to
/// that - the tools that manage it voxelise the model and rebuild the surface, which would
/// flatten every detail this app exists to cut. So the pass declines rather than guesses, and
/// leaves the mesh as it found it.
///
/// It is deliberately separate from the automatic repair. Dropping triangles and inventing new
/// ones is not something to do quietly after every edit; it is a repair, the user asks for it,
/// and it says what it did.
/// </summary>
public static class MeshHealer
{
    /// <summary>
    /// A triangle thinner than this, measured as twice its area over its longest edge, carries
    /// no surface at all.
    ///
    /// Deliberately tiny. An earlier version swept out anything under a tenth of a micron, on
    /// the theory that hairlines cause trouble downstream - and it tore far more holes than the
    /// filling pass could close, turning two bad edges into eleven. Only triangles that are
    /// genuinely nothing are dropped; a thin one is still a surface, and keeping it is what lets
    /// its neighbours stay attached to something.
    /// </summary>
    public const float SliverHeightMm = 1e-7f;

    public static HealResult Heal(Mesh mesh, float tolerance = 1e-4f, int maxPasses = 4)
    {
        var before = mesh.CheckHealth();
        var current = mesh.Welded(tolerance);

        int dropped = 0, filled = 0, flipped = 0;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            int was = current.TriangleCount;

            current = MeshRepair.Repair(current, tolerance);

            var (trimmed, slivers) = DropSlivers(current);
            current = trimmed;
            dropped += slivers;

            var (turned, flips) = UnifyWinding(current);
            current = turned;
            flipped += flips;

            var (capped, holes) = FillHoles(current);
            current = capped;
            filled += holes;

            if (current.CheckHealth().IsWatertight) break;
            if (slivers == 0 && holes == 0 && flips == 0 && current.TriangleCount == was) break;
        }

        var after = current.CheckHealth();

        // Never hand back something worse than was handed in.
        //
        // Capping holes and turning faces round assumes the damage is local - a few triangles
        // missing, a shell inside out. Against a mesh that overlaps itself, those assumptions do
        // not hold and the passes can tear more than they close; measured against deliberately
        // broken boolean output, this pass made two thirds of them worse. Repair is offered as a
        // help, so when it does not help it declines.
        if (Defects(after) >= Defects(before) && !after.IsWatertight)
            return new HealResult(mesh, before, before, 0, 0, 0);

        return new HealResult(current, before, after, dropped, filled, flipped);
    }

    /// <summary>
    /// Throws away triangles too thin to be a surface.
    ///
    /// Area alone is the wrong measure: a long thin triangle can have a respectable area and
    /// still be a hairline. What matters is its height - twice the area over its longest side -
    /// which is how far the surface actually extends away from that side.
    /// </summary>
    public static (Mesh Mesh, int Dropped) DropSlivers(Mesh mesh, float minimumHeight = SliverHeightMm)
    {
        var indices = new List<int>(mesh.Indices.Count);
        int dropped = 0;

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[t]];
            Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[t + 2]];

            float twiceArea = Vector3.Cross(b - a, c - a).Length();
            float longest = MathF.Sqrt(Math.Max(
                (b - a).LengthSquared(),
                Math.Max((c - b).LengthSquared(), (a - c).LengthSquared())));

            if (longest <= 0 || twiceArea / longest < minimumHeight)
            {
                dropped++;
                continue;
            }

            indices.Add(mesh.Indices[t]);
            indices.Add(mesh.Indices[t + 1]);
            indices.Add(mesh.Indices[t + 2]);
        }

        return dropped == 0 ? (mesh, 0) : (new Mesh(mesh.Positions, indices), dropped);
    }

    /// <summary>
    /// Turns triangles round until neighbours agree which way the surface faces, then turns each
    /// shell as a whole so it faces outward.
    ///
    /// Two triangles sharing an edge are consistent when they walk it in opposite directions.
    /// Spreading that rule outward from one triangle settles a whole shell; a mesh in several
    /// pieces gets one starting point each. Edges shared by more than two triangles are left out
    /// of the spreading, because there is no telling which pair is meant to be the surface.
    ///
    /// Agreeing is not the same as being right, though: a shell exported inside out agrees with
    /// itself perfectly and is still wrong, and its only tell is a negative volume. So each shell
    /// is checked on its own - one bad shell among several would be invisible in the total.
    ///
    /// A shell enclosed by another is left alone whichever way it faces. That is a cavity, and a
    /// cavity is supposed to face inward; turning it outward would fill in the hollow.
    /// </summary>
    public static (Mesh Mesh, int Flipped) UnifyWinding(Mesh mesh)
    {
        int triangles = mesh.TriangleCount;
        if (triangles == 0) return (mesh, 0);

        var neighbours = new Dictionary<(int, int), List<int>>(mesh.Indices.Count);
        for (int t = 0; t < triangles; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                var key = Key(mesh.Indices[t * 3 + k], mesh.Indices[t * 3 + (k + 1) % 3]);
                if (!neighbours.TryGetValue(key, out var list)) neighbours[key] = list = new List<int>();
                list.Add(t);
            }
        }

        var winding = new int[triangles * 3];
        mesh.Indices.CopyTo(winding);

        var settled = new bool[triangles];
        int flipped = 0;
        var shells = new List<List<int>>();

        for (int seed = 0; seed < triangles; seed++)
        {
            if (settled[seed]) continue;

            settled[seed] = true;
            var queue = new Queue<int>();
            queue.Enqueue(seed);

            var shell = new List<int>();
            shells.Add(shell);

            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                shell.Add(t);

                for (int k = 0; k < 3; k++)
                {
                    int a = winding[t * 3 + k], b = winding[t * 3 + (k + 1) % 3];
                    if (!neighbours.TryGetValue(Key(a, b), out var sharing) || sharing.Count != 2)
                        continue;

                    int other = sharing[0] == t ? sharing[1] : sharing[0];
                    if (settled[other]) continue;

                    // Consistent means the neighbour walks this edge the other way round.
                    if (Walks(winding, other, a, b))
                    {
                        (winding[other * 3 + 1], winding[other * 3 + 2]) =
                            (winding[other * 3 + 2], winding[other * 3 + 1]);
                        flipped++;
                    }

                    settled[other] = true;
                    queue.Enqueue(other);
                }
            }
        }

        flipped += FaceShellsOutward(mesh.Positions, winding, shells);

        return flipped == 0 ? (mesh, 0) : (new Mesh(mesh.Positions, winding), flipped);
    }

    /// <summary>
    /// Turns any shell that came out inside out, unless something else encloses it.
    ///
    /// Enclosure is judged on bounding boxes. A proper containment test would need ray casting
    /// against every other shell; the box test costs nothing, and the case it exists for - the
    /// inner surface of a hollowed part sitting well inside the outer one - it gets right.
    /// </summary>
    private static int FaceShellsOutward(List<Vector3> positions, int[] winding, List<List<int>> shells)
    {
        if (shells.Count == 0) return 0;

        var extents = shells.Select(shell => ShellBounds(positions, winding, shell)).ToList();
        int flipped = 0;

        for (int i = 0; i < shells.Count; i++)
        {
            if (ShellVolume(positions, winding, shells[i]) >= 0) continue;

            bool enclosed = false;
            for (int j = 0; j < shells.Count && !enclosed; j++)
                if (j != i && Encloses(extents[j], extents[i]))
                    enclosed = true;

            if (enclosed) continue;

            foreach (int t in shells[i])
            {
                (winding[t * 3 + 1], winding[t * 3 + 2]) = (winding[t * 3 + 2], winding[t * 3 + 1]);
                flipped++;
            }
        }

        return flipped;
    }

    private static Bounds ShellBounds(List<Vector3> positions, int[] winding, List<int> shell)
    {
        var points = new List<Vector3>(shell.Count * 3);
        foreach (int t in shell)
            for (int k = 0; k < 3; k++)
                points.Add(positions[winding[t * 3 + k]]);

        return Bounds.FromPoints(points);
    }

    private static bool Encloses(Bounds outer, Bounds inner) =>
        !outer.IsEmpty && !inner.IsEmpty &&
        outer.Min.X <= inner.Min.X && outer.Min.Y <= inner.Min.Y && outer.Min.Z <= inner.Min.Z &&
        outer.Max.X >= inner.Max.X && outer.Max.Y >= inner.Max.Y && outer.Max.Z >= inner.Max.Z;

    private static double ShellVolume(List<Vector3> positions, int[] winding, List<int> shell)
    {
        double volume = 0;

        foreach (int t in shell)
        {
            Vector3 a = positions[winding[t * 3]];
            Vector3 b = positions[winding[t * 3 + 1]];
            Vector3 c = positions[winding[t * 3 + 2]];

            volume += Vector3.Dot(a, Vector3.Cross(b, c));
        }

        return volume / 6.0;
    }

    /// <summary>Whether a triangle walks the edge from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static bool Walks(int[] winding, int triangle, int a, int b)
    {
        for (int k = 0; k < 3; k++)
            if (winding[triangle * 3 + k] == a && winding[triangle * 3 + (k + 1) % 3] == b)
                return true;

        return false;
    }

    /// <summary>
    /// Caps every hole with a fan from its own middle.
    ///
    /// A hole shows up as a run of edges used by one triangle each. Those edges are walked in
    /// the direction the surface around them uses, so the patch walks them the other way and
    /// comes out facing the same direction as its neighbours. Fanning from a new point at the
    /// middle rather than from a corner keeps every triangle real even when the loop is a
    /// long thin crack, which is what a boolean usually leaves.
    /// </summary>
    public static (Mesh Mesh, int Filled) FillHoles(Mesh mesh, int largestHole = 2000)
    {
        var used = new Dictionary<(int, int), int>(mesh.Indices.Count);
        var directed = new List<(int A, int B)>();

        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Indices[t + k], b = mesh.Indices[t + (k + 1) % 3];
                var key = Key(a, b);
                used[key] = used.GetValueOrDefault(key) + 1;
                directed.Add((a, b));
            }
        }

        var openings = new Dictionary<int, (int A, int B)>();
        foreach (var edge in directed)
            if (used[Key(edge.A, edge.B)] == 1)
                openings.TryAdd(edge.A, edge);

        if (openings.Count == 0) return (mesh, 0);

        var positions = new List<Vector3>(mesh.Positions);
        var indices = new List<int>(mesh.Indices);
        var walked = new HashSet<int>();
        int filled = 0;

        foreach (int start in openings.Keys.ToList())
        {
            if (walked.Contains(start)) continue;

            var loop = new List<int>();
            int at = start;

            while (openings.TryGetValue(at, out var edge) && walked.Add(at))
            {
                loop.Add(at);
                at = edge.B;

                if (at == start || loop.Count > largestHole) break;
            }

            // Only a loop that closes describes a hole; a run that peters out is damage this
            // pass cannot honestly interpret, so it is left for the next one.
            if (at != start || loop.Count < 3) continue;

            var centre = Vector3.Zero;
            foreach (int v in loop) centre += positions[v];
            centre /= loop.Count;

            int middle = positions.Count;
            positions.Add(centre);

            for (int i = 0; i < loop.Count; i++)
            {
                int a = loop[i], b = loop[(i + 1) % loop.Count];

                // Reversed against the surrounding surface, which is what makes the patch agree
                // with it rather than fight it.
                indices.Add(middle);
                indices.Add(b);
                indices.Add(a);
            }

            filled++;
        }

        return filled == 0 ? (mesh, 0) : (new Mesh(positions, indices), filled);
    }

    private static int Defects(MeshHealth health) =>
        health.BoundaryEdges + health.NonManifoldEdges + health.InconsistentEdges;

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}
