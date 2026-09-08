using System.Numerics;

namespace FastCraft3D.Geometry.Moulding;

/// <summary>
/// Builds a mould by sampling rather than by cutting.
///
/// The exact route - a boolean subtraction - keeps every triangle of the model and is what a
/// cavity deserves. It is also quadratic: measured on this engine, a thousand triangles takes half
/// a second, sixteen thousand takes forty-seven, and a three hundred thousand triangle scan takes
/// twenty minutes and twenty gigabytes before anyone gives up on it. That is not a threshold worth
/// arguing with, and a scan is exactly the kind of model somebody wants a mould of.
///
/// So beyond a certain size the questions get asked of a grid instead. Is this point inside the
/// model, inside a bore, past this plane, within that key - flags, and a surface built round them,
/// which is closed because a surface round a set of flags cannot be anything else. The cost is set
/// by the resolution and barely at all by the triangle count, so a scan costs what a cube does.
///
/// What it gives up is detail finer than one cell. Everything else survives: the block and the cut
/// planes are axis-aligned, a surface on a grid follows those exactly, and the faces that have to
/// seal are put back on their planes afterwards.
/// </summary>
public static class MouldGrid
{
    /// <summary>Air round the block so the surface closes rather than running off the edge.</summary>
    private const int Margin = 2;

    public static MouldResult Build(
        Mesh model, MouldStudy study, MouldOptions options, CancellationToken token,
        IProgress<WorkProgress>? progress = null)
    {
        var bounds = model.ComputeBounds();

        float wall = MathF.Max(options.Wall, 1f);
        var blockMin = bounds.Min - new Vector3(wall);
        var blockMax = bounds.Max + new Vector3(wall);
        var span = blockMax - blockMin;

        int resolution = Math.Clamp(options.Resolution, 48, 768);
        float voxel = MathF.Max(span.X, MathF.Max(span.Y, span.Z)) / resolution;
        if (voxel <= 0f) return new MouldResult([], "Nothing to make a mould of.");

        var origin = blockMin - new Vector3(voxel * Margin);

        int nx = (int)MathF.Ceiling(span.X / voxel) + Margin * 2 + 1;
        int ny = (int)MathF.Ceiling(span.Y / voxel) + Margin * 2 + 1;
        int nz = (int)MathF.Ceiling(span.Z / voxel) + Margin * 2 + 1;

        // The one step whose cost depends on the model, and it is shared by every piece. It is
        // about a third of the work, so it gets the first third of the bar.
        var inModel = VoxelRebuild.Occupancy(model, origin, voxel, nx, ny, nz, token,
            Slice(progress, 0f, 0.35f, "Sampling the model"));

        var bores = Bores(study, options, blockMax.Z + wall).ToList();
        var cuts = study.Cuts;

        var parts = new List<MouldPart>();

        int total = 1 << cuts.Count;

        for (int piece = 0; piece < total; piece++)
        {
            token.ThrowIfCancellationRequested();

            float from = 0.35f + 0.65f * piece / total;
            float to = 0.35f + 0.65f * (piece + 1) / total;

            // The flags are about two thirds of a piece and the surface the other third, so the
            // bar keeps moving through both. Left as one span, it sat still through the surface -
            // which is exactly the stretch anyone would take for a hang.
            float middle = from + (to - from) * 0.65f;
            string doing = $"Piece {piece + 1} of {total}";

            var keys = KeysFor(piece, cuts, blockMin, blockMax, wall, options);
            var flags = new bool[inModel.Length];

            for (int k = 0; k < nz; k++)
            {
                if ((k & 7) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new WorkProgress(from + (middle - from) * k / nz, doing));
                }

                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int at = (k * ny + j) * nx + i;
                        var p = origin + new Vector3(i, j, k) * voxel;

                        flags[at] = Solid(p, at, piece, inModel, blockMin, blockMax, bores, cuts, keys);
                    }
            }

            var mesh = VoxelRebuild.SurfaceNets(flags, origin, voxel, nx, ny, nz,
                Slice(progress, middle, to, $"{doing} - building the surface"));
            if (mesh.TriangleCount == 0) continue;

            // Keeps the number where it was and changes the words. Smoothing walks the whole mesh
            // twice and cannot say how far through it is, but the bar dropping back to a sweep
            // after it had reached two thirds would look more like a fault than a stage.
            progress?.Report(new WorkProgress(to, $"{doing} - smoothing"));
            mesh = Smooth(mesh);

            parts.Add(new MouldPart(
                $"Mould {(char)('A' + parts.Count)}", mesh, mesh.CheckHealth().IsWatertight));
        }

        return new MouldResult(parts, Describe(parts, study, options, voxel));
    }

    /// <summary>
    /// Wraps a reporter so an inner step that counts itself from nought to one lands in the part
    /// of the bar set aside for it. Without this every stage would send the bar back to the start.
    /// </summary>
    private static IProgress<WorkProgress>? Slice(
        IProgress<WorkProgress>? progress, float from, float to, string stage) =>
        progress is null ? null : new Part(progress, from, to, stage);

    private sealed class Part(IProgress<WorkProgress> inner, float from, float to, string stage)
        : IProgress<WorkProgress>
    {
        public void Report(WorkProgress value) => inner.Report(new WorkProgress(
            value.Measured ? from + (to - from) * value.Done : -1f, stage));
    }

    /// <summary>
    /// Takes the staircase off the cavity without rounding the faces that have to seal.
    ///
    /// Off a grid a curved surface is a staircase, a step at every cell, and on a face it reads as
    /// though the model had been rebuilt out of bricks. Taubin smoothing takes that off and does
    /// not shrink the shape.
    ///
    /// Not everything can be smoothed, though. In the middle of a flat face the neighbours are
    /// coplanar, so the average is the vertex itself and nothing moves; but two cells in from every
    /// rim there are off-plane neighbours to pull against, and the perimeter of the block is exactly
    /// where the two halves have to meet. Measured, that left seventy per cent of the parting face
    /// planar and rolled the border off by a fifth of a millimetre - a seam to weep through.
    ///
    /// So the flat faces are found first and put back afterwards. Found rather than assumed: a face
    /// is a coordinate thousands of vertices share, and looking for that catches the planes where
    /// the surface actually landed. Restoring a vertex to where it already was cannot merge it with
    /// another - snapping vertices onto nominal planes was tried instead, and it did merge them,
    /// and every piece came back torn.
    /// </summary>
    private static Mesh Smooth(Mesh mesh)
    {
        var before = mesh.Positions.ToArray();
        var faces = Flats(before);

        var smoothed = MeshSmoothing.Smooth(mesh, passes: 2);

        for (int i = 0; i < before.Length; i++)
            if (OnAFlat(before[i], faces)) smoothed.Positions[i] = before[i];

        return smoothed;
    }

    /// <summary>
    /// The coordinates enough vertices share to be a face rather than a coincidence. A block has
    /// thousands on each side and on the cut; a curved cavity has a handful at any one height.
    /// </summary>
    private static List<(Axis Axis, float At)> Flats(Vector3[] points)
    {
        int enough = Math.Max(64, points.Length / 200);
        var faces = new List<(Axis, float)>();

        foreach (var axis in new[] { Axis.X, Axis.Y, Axis.Z })
            faces.AddRange(points
                .GroupBy(p => MathF.Round(At(p, axis), 4))
                .Where(g => g.Count() >= enough)
                .Select(g => (axis, g.Key)));

        return faces;
    }

    private static bool OnAFlat(Vector3 p, List<(Axis Axis, float At)> faces)
    {
        foreach (var (axis, at) in faces)
            if (MathF.Abs(At(p, axis) - at) < 1e-4f) return true;

        return false;
    }

    /// <summary>
    /// Whether one grid point is material of this piece: inside the block, outside the model, clear
    /// of the bores, this side of every cut - and then the keys, which are the only things allowed
    /// to reach across a cut, because that is what a key is for.
    /// </summary>
    private static bool Solid(
        Vector3 p, int at, int piece, bool[] inModel, Vector3 blockMin, Vector3 blockMax,
        List<Bore> bores, IReadOnlyList<MouldCut> cuts, List<Key> keys)
    {
        foreach (var key in keys)
            if (!key.Dome && key.Holds(p)) return false;

        foreach (var key in keys)
            if (key.Dome && key.Holds(p)) return true;

        if (p.X < blockMin.X || p.Y < blockMin.Y || p.Z < blockMin.Z) return false;
        if (p.X > blockMax.X || p.Y > blockMax.Y || p.Z > blockMax.Z) return false;

        if (inModel[at]) return false;

        foreach (var bore in bores)
            if (bore.Holds(p)) return false;

        for (int c = 0; c < cuts.Count; c++)
        {
            bool above = At(p, cuts[c].Axis) > cuts[c].At;
            if (above != ((piece & (1 << c)) != 0)) return false;
        }

        return true;
    }

    /// <summary>A pour hole or an air vent: a square shaft straight up out of the top of the block.</summary>
    private readonly record struct Bore(float X, float Y, float Half, float Bottom, float Top)
    {
        public bool Holds(Vector3 p) =>
            p.Z >= Bottom && p.Z <= Top &&
            MathF.Abs(p.X - X) <= Half && MathF.Abs(p.Y - Y) <= Half;
    }

    private static IEnumerable<Bore> Bores(MouldStudy study, MouldOptions options, float top)
    {
        if (options.SprueRadius > 0f)
            yield return new Bore(
                study.Sprue.X, study.Sprue.Y, options.SprueRadius,
                study.Sprue.Z - options.SprueRadius, top);

        if (!options.AddVents || options.VentRadius <= 0f) yield break;

        foreach (var vent in study.Vents)
            yield return new Bore(
                vent.X, vent.Y, options.VentRadius, vent.Z - options.VentRadius, top);
    }

    /// <summary>One key: a square block on a parting plane, proud of one face and sunk into the other.</summary>
    private readonly record struct Key(Vector3 At, float Half, bool Dome)
    {
        public bool Holds(Vector3 p) =>
            MathF.Abs(p.X - At.X) <= Half &&
            MathF.Abs(p.Y - At.Y) <= Half &&
            MathF.Abs(p.Z - At.Z) <= Half;
    }

    /// <summary>
    /// The keys this piece carries: four to a face, out in the wall where the block is solid
    /// whatever the model does. A piece takes the domes on the planes it sits above and the sockets
    /// on those it sits below, so the pieces close one way round only.
    /// </summary>
    private static List<Key> KeysFor(
        int piece, IReadOnlyList<MouldCut> cuts, Vector3 blockMin, Vector3 blockMax,
        float wall, MouldOptions options)
    {
        var keys = new List<Key>();
        if (options.KeyRadius <= 0f) return keys;

        float inset = wall * 0.5f;

        for (int c = 0; c < cuts.Count; c++)
        {
            bool dome = (piece & (1 << c)) != 0;
            float half = dome ? options.KeyRadius : options.KeyRadius + options.KeyClearance;

            var (u, v) = cuts[c].Axis switch
            {
                Axis.X => (Axis.Y, Axis.Z),
                Axis.Y => (Axis.X, Axis.Z),
                _ => (Axis.X, Axis.Y),
            };

            foreach (float su in new[] { At(blockMin, u) + inset, At(blockMax, u) - inset })
                foreach (float sv in new[] { At(blockMin, v) + inset, At(blockMax, v) - inset })
                {
                    var p = With(With(With(Vector3.Zero, cuts[c].Axis, cuts[c].At), u, su), v, sv);
                    if (Clear(p, c, piece, cuts, options.KeyRadius)) keys.Add(new Key(p, half, dome));
                }
        }

        return keys;
    }

    /// <summary>Whether a key on one plane belongs to this piece and is clear of the other cuts.</summary>
    private static bool Clear(
        Vector3 key, int cut, int piece, IReadOnlyList<MouldCut> cuts, float half)
    {
        for (int c = 0; c < cuts.Count; c++)
        {
            if (c == cut) continue;

            float away = At(key, cuts[c].Axis) - cuts[c].At;
            if (MathF.Abs(away) < half * 1.5f) return false;
            if (away > 0 != ((piece & (1 << c)) != 0)) return false;
        }

        return true;
    }

    private static float At(Vector3 v, Axis axis) =>
        axis == Axis.X ? v.X : axis == Axis.Y ? v.Y : v.Z;

    private static Vector3 With(Vector3 v, Axis axis, float value) => axis switch
    {
        Axis.X => new Vector3(value, v.Y, v.Z),
        Axis.Y => new Vector3(v.X, value, v.Z),
        _ => new Vector3(v.X, v.Y, value),
    };

    private static string Describe(
        IReadOnlyList<MouldPart> parts, MouldStudy study, MouldOptions options, float voxel)
    {
        int torn = parts.Count(p => !p.Watertight);

        string vents = options.AddVents && study.Vents.Count > 0
            ? $", {study.Vents.Count} vent(s)"
            : "";

        string health = torn == 0
            ? "all watertight"
            : $"{torn} of them not watertight - select and use Rebuild on the Tools tab";

        return $"{parts.Count} part(s), {options.Wall:0.##} mm wall, "
             + $"pour hole {options.SprueRadius * 2:0.##} mm{vents}, "
             + $"sampled to {voxel:0.##} mm: {health}.";
    }
}
