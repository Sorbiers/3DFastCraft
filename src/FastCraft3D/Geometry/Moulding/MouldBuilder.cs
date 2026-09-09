using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry.Moulding;

/// <param name="Wall">Millimetres of material between the model and the outside of the block.</param>
/// <param name="SprueRadius">The pour hole. Silicone is thick, so this wants to be generous.</param>
/// <param name="VentRadius">Air escapes have only to let air out, so they are thin.</param>
/// <param name="AddVents">Whether to open the trapped pockets the study found.</param>
/// <param name="KeyRadius">
/// Half the width of a registration key: a square block straddling the parting plane, standing
/// proud of one face and recessed into the other, so the pieces close one way round only. Zero
/// leaves the faces plain.
/// </param>
/// <param name="KeyClearance">How much wider the socket is than the dome, so the halves close.</param>
/// <param name="Resolution">
/// Cells along the block's longest side, for a model too heavy to cut exactly. Ignored otherwise.
/// </param>
public readonly record struct MouldOptions(
    float Wall = 8f,
    float SprueRadius = 5f,
    float VentRadius = 1f,
    bool AddVents = true,
    float KeyRadius = 4f,
    float KeyClearance = 0.2f,
    int Resolution = 512)
{
    /// <summary>
    /// The defaults, written out.
    ///
    /// A record struct's new() does not run the primary constructor, so the values above are only
    /// used when someone names the type and passes arguments - default(MouldOptions) is all zeros,
    /// which here meant no wall, no pour hole and no keys, and a mould that looked plausible until
    /// its volume was measured.
    /// </summary>
    /// <summary>
    /// Half a key, cut down to what the wall can hold.
    ///
    /// A key sits in the corner of the parting face, centred across the wall, so one wider than
    /// the wall hangs off the outside of the block: the dialog's default 8 mm key fits an 8 mm
    /// wall exactly, and a 12 mm key on the same wall overhung the corner by 2 mm. It looked like
    /// a mistake because it was one. The dialog still takes any number; this is where the number
    /// is made to fit.
    ///
    /// Cut to exactly half the wall, not to a hair inside it. A key face a fraction of a
    /// millimetre from the block's own face is the near-coplanar contact this boolean engine is
    /// worst at, and pulling the default key in by 0.2 mm to make room for the socket's clearance
    /// tore a half that had come back watertight for as long as the tool has existed. Flush is
    /// what the default 8 mm key on an 8 mm wall has always been, and it works; the socket is
    /// wider than that by its clearance and breaks the outside face, which is a clean through-cut
    /// and does no harm.
    /// </summary>
    public float KeyHalfIn(float wall) => MathF.Max(0f, MathF.Min(KeyRadius, wall * 0.5f));

    public static MouldOptions Default => new(
        Wall: 8f,
        SprueRadius: 5f,
        VentRadius: 1f,
        AddVents: true,
        KeyRadius: 4f,
        KeyClearance: 0.2f,
        Resolution: 512);
}

/// <param name="Name">What the part is called on the plate.</param>
/// <param name="Mesh">The part itself.</param>
/// <param name="Watertight">Whether it would print as it stands.</param>
public sealed record MouldPart(string Name, Mesh Mesh, bool Watertight);

/// <param name="Parts">The pieces of the mould, in the order they were cut.</param>
/// <param name="Summary">What was made, in a sentence.</param>
public sealed record MouldResult(IReadOnlyList<MouldPart> Parts, string Summary);

/// <summary>
/// Builds a mould you pour silicone into: a block round the model, the model taken out of it, a
/// hole to pour through, vents where air would otherwise sit, and flat cuts so the cast part comes
/// out. Where to cut and where the air collects are <see cref="MouldAnalysis"/>'s answers.
///
/// The cavity is a boolean subtraction and nothing else, because nothing else keeps the model.
/// Building the whole thing on a voxel grid was tried - it is watertight by construction and never
/// fails - and a portrait bust came back out of it looking as though it had been rebuilt from
/// bricks. A cavity is the one surface in the model that has to carry every detail the original
/// had, since it is the only thing the casting will ever see. Exact geometry it is, and the
/// fragility that comes with it is managed rather than avoided:
///
/// <list type="bullet">
/// <item>the bores join the model and the lot leaves the block in one subtraction, rather than
/// being drilled out of a finished cavity, where a bore meets that curved surface from inside
/// along a line the two are nearly tangent on;</item>
/// <item>the cuts run on the plain block before any key goes on, since splitting a solid with a
/// plane is the most reliable thing the engine does;</item>
/// <item>the pour hole and the keys are square rather than round, because a flat face meets a
/// surface in one clean curve where a many-sided prism meets it along every facet edge at
/// whatever angle it happens to make there - that one change took a two-part mould from both
/// halves torn to neither;</item>
/// <item>a key sits a hair off its plane rather than exactly on it, so that no face of it is
/// coplanar with the cut;</item>
/// <item>and each piece is mended between keys, and what comes back is measured rather than
/// assumed - a piece that is not watertight is named as such.</item>
/// </list>
/// </summary>
public static class MouldBuilder
{
    /// <summary>
    /// The most triangles worth cutting exactly.
    ///
    /// It used to be twenty thousand, and it was the boolean that set it: the cavity was the block
    /// with the model subtracted from it, and that is quadratic - 1k took 0.6 s, 16k took 47, and a
    /// scan ran for twenty minutes before it was killed. It tore as well, from about four thousand
    /// triangles up: two halves out of two came back open.
    ///
    /// The cavity is not a boolean any more, and nothing else the exact route does grows with the
    /// model, so there is no size at which it has to give up. What it does need is a model that is
    /// closed to begin with, since it makes the cavity by putting the model inside the block
    /// inside out - an open model has no inside for the void to be, and that is what the grid is
    /// still here for.
    /// </summary>
    public const int ExactLimit = int.MaxValue;


    public static MouldResult Build(
        Mesh model, MouldStudy study, MouldOptions options, CancellationToken token = default,
        IProgress<WorkProgress>? progress = null)
    {
        token.ThrowIfCancellationRequested();

        var bounds = model.ComputeBounds();
        if (bounds.IsEmpty || model.TriangleCount == 0)
            return new MouldResult([], "Nothing to make a mould of.");

        // Cut exactly whenever the model will allow it, and sample when it will not.
        //
        // A closed model can be cut exactly at any size, and the result keeps every triangle it
        // had. An open one cannot: the cavity is the model turned inside out within the block, and
        // a surface with a hole in it does not divide anything into an inside and an outside. The
        // grid does not care - it asks whether points are in the material and rebuilds a surface
        // from the answers - so that is where a torn scan goes.
        if (model.TriangleCount > ExactLimit || !model.CheckHealth().IsWatertight)
            return MouldGrid.Build(model, study, options, token, progress);

        float wall = MathF.Max(options.Wall, 1f);

        progress?.Report(WorkProgress.Doing("Cutting the cavity"));

        var body = Cavity(model, bounds, wall);

        progress?.Report(WorkProgress.Doing("Adding the pour hole"));

        var cavity = body;
        body = Drill(cavity, study, bounds, wall, options, 1f, token);

        // A wider hole, if the first one nicked the surface.
        //
        // The cavity costs nothing and cannot go wrong; the channels are still cut by the boolean,
        // and that is the fragile part of this program. At the crown of a head the surface is
        // nearly level and the bore is vertical, so the two meet almost tangentially across
        // hundreds of triangles a few hundredths of a millimetre wide - the contact this engine is
        // worst at - and it comes back with a handful of open edges the healer cannot close.
        // Widening moves the meeting onto steeper ground, and usually that is the whole of it.
        if (!body.CheckHealth().IsWatertight)
        {
            var wider = Drill(cavity, study, bounds, wall, options, WiderBore, token);
            if (wider.CheckHealth().IsWatertight) body = wider;
        }

        // And if neither came back closed, this is still the mould. The cavity carries every
        // triangle the model had, and what is wrong with it is a nick or two around one hole.
        //
        // It used to go to the grid at this point, and that was the wrong trade by a long way: it
        // threw away a perfect cavity to gain one channel, and a portrait bust came back looking
        // as though it had been rebuilt from bricks. The pieces report whether they are watertight,
        // and a few open edges are what Repair is for.

        var parts = Cut(body, study.Cuts, bounds, wall, options, token, progress);

        return new MouldResult(parts, Describe(parts, study, options));
    }

    /// <summary>
    /// The block with the model taken out of it, and not a boolean in sight.
    ///
    /// A mould is a box with the model as a hole in the middle of it, and that is exactly what a
    /// closed block and a closed model turned inside out are: two shells, one inside the other,
    /// every triangle of the model kept exactly as it came. The boolean this replaces was the only
    /// reason the exact route had a size limit, and it tore above a few thousand triangles anyway.
    ///
    /// It asks one thing of the model: that it is closed, and that it fits inside the block with
    /// the wall to spare. Both are true by construction here - the block is the model's own box
    /// grown by the wall, and the route this belongs to is only taken for a watertight model.
    /// </summary>
    private static Mesh Cavity(Mesh model, Bounds bounds, float wall)
    {
        var hollow = model.Clone();
        hollow.FlipWinding();

        return Mesh.Combine([Block(bounds, wall), hollow]);
    }

    /// <summary>How much wider the second attempt at a bore is.</summary>
    private const float WiderBore = 1.4f;

    /// <summary>Every channel out of the cavity: the pour hole, and the vents if asked for.</summary>
    private static Mesh Drill(
        Mesh cavity, MouldStudy study, Bounds bounds, float wall, MouldOptions options,
        float scale, CancellationToken token)
    {
        var body = Open(cavity, Bore(study.Sprue, bounds, wall, options.SprueRadius * scale), token);

        if (options.AddVents)
            foreach (var vent in study.Vents)
                body = Open(body, Bore(vent, bounds, wall, options.VentRadius * scale), token);

        return body.CheckHealth().IsWatertight
            ? body
            : MeshHealer.Heal(body, token: token).Mesh;
    }

    /// <summary>The block the cavity is taken out of: the model's box, grown by the wall.</summary>
    private static Mesh Block(Bounds bounds, float wall)
    {
        var size = bounds.Size + new Vector3(wall * 2f);
        return MeshTransform.Transformed(
            Primitives.Box(size.X, size.Y, size.Z),
            Matrix4x4.CreateTranslation(bounds.Center));
    }

    /// <summary>
    /// Opens one channel through the mould, from the cavity out through the top. No radius means
    /// no hole.
    ///
    /// Taken out of the finished block rather than added to the model first, and taken out only
    /// where the channel actually runs: a pour hole cannot touch nine tenths of a scan, and the
    /// boolean should not have to look at the nine tenths to find that out.
    /// </summary>
    private static Mesh Open(Mesh body, Mesh? bore, CancellationToken token) =>
        bore is null ? body : LocalCsg.Subtract(body, bore, token);

    /// <summary>
    /// The channel for one hole: from inside the model, up past the top of the block.
    ///
    /// It starts below the point it was given so it certainly meets the model - the study reads
    /// the surface off a grid, and a channel that stops a fraction short leaves a hole that goes
    /// nowhere. It ends above the block rather than flush with it, because a shaft finishing
    /// exactly in the plane of the face it opens is a coplanar pair.
    /// </summary>
    private static Mesh? Bore(Vector3 from, Bounds bounds, float wall, float radius)
    {
        if (radius <= 0f) return null;

        float top = bounds.Max.Z + wall + radius;
        float start = from.Z - radius;
        float length = top - start;
        if (length <= 0f) return null;

        // Square, not round.
        //
        // A round bore is six triangles meeting the model along every one of forty-eight facet
        // edges, at whatever angle the surface happens to make there, and near-tangent contacts
        // along that curve are what tore the pieces. Four flat faces meet a curved surface in four
        // clean curves and nothing else. A pour channel is square in half the mould-making
        // literature anyway; what matters is its section, and this keeps the section asked for.
        float side = radius * 2f;

        return MeshTransform.Transformed(
            Primitives.Box(side, side, length),
            Matrix4x4.CreateTranslation(from.X, from.Y, start + length / 2f));
    }

    /// <summary>
    /// Cuts the block up, then keys the pieces together.
    ///
    /// Every cut happens first, on the plain block. Keying as it went meant the second cut had to
    /// pass through the domes the first one had just added, which tore three pieces out of four.
    /// Each piece remembers which side of each plane it came down on, as one bit per cut: that
    /// says which face of a joint it holds, and which keys are its own, since a four-piece mould
    /// has keys on planes that only two of the pieces touch.
    /// </summary>
    private static List<MouldPart> Cut(
        Mesh body, IReadOnlyList<MouldCut> cuts, Bounds bounds, float wall,
        MouldOptions options, CancellationToken token, IProgress<WorkProgress>? progress = null)
    {
        var pieces = new List<(Mesh Mesh, int Side)> { (body, 0) };

        for (int c = 0; c < cuts.Count; c++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(WorkProgress.Doing($"Splitting on {cuts[c].Axis}"));

            var next = new List<(Mesh, int)>();
            foreach (var (mesh, side) in pieces)
            {
                var (front, back) = PlaneSplit.Split(mesh, cuts[c].Axis, cuts[c].At, SplitKeep.Both, token);

                if (front is not null) next.Add((front, side | (1 << c)));
                if (back is not null) next.Add((back, side));
            }

            if (next.Count > 0) pieces = next;
        }

        var parts = new List<MouldPart>();

        float keyHalf = options.KeyHalfIn(wall);

        for (int i = 0; i < pieces.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(WorkProgress.Doing($"Keying piece {i + 1} of {pieces.Count}"));

            var mesh = pieces[i].Mesh;

            for (int c = 0; c < cuts.Count; c++)
            {
                var mine = Keys(cuts[c], bounds, wall, options)
                    .Where(k => Reaches(k, c, pieces[i].Side, cuts, keyHalf))
                    .ToList();

                if (mine.Count == 0) continue;

                mesh = (pieces[i].Side & (1 << c)) != 0
                    ? Dome(mesh, mine, keyHalf, token)
                    : Socket(mesh, mine, keyHalf + options.KeyClearance, token);
            }

            var healed = MeshHealer.Heal(mesh, token: token).Mesh;
            parts.Add(new MouldPart(
                $"Mould {(char)('A' + i)}", healed, healed.CheckHealth().IsWatertight));
        }

        return parts;
    }

    /// <summary>
    /// Where the keys sit on one parting plane: in the four corners of the face, out in the wall.
    ///
    /// The wall is the only part of a face guaranteed to be solid whatever the model does -
    /// anywhere nearer the middle might be cavity, and a key there would be a lump in the casting.
    /// </summary>
    private static List<Vector3> Keys(MouldCut cut, Bounds bounds, float wall, MouldOptions options)
    {
        if (options.KeyRadius <= 0f) return [];

        var min = bounds.Min - new Vector3(wall);
        var max = bounds.Max + new Vector3(wall);
        float inset = wall * 0.5f;

        // Off the plane by a hair, and both faces use the same centre so they still match. A key
        // centred exactly on the cut has a face coplanar with it, and coplanar faces are the other
        // thing this engine mishandles.
        float nudge = options.KeyHalfIn(wall) * 0.05f;

        var (u, v) = cut.Axis switch
        {
            Axis.X => (Axis.Y, Axis.Z),
            Axis.Y => (Axis.X, Axis.Z),
            _ => (Axis.X, Axis.Y),
        };

        var places = new List<Vector3>();
        foreach (float su in new[] { At(min, u) + inset, At(max, u) - inset })
            foreach (float sv in new[] { At(min, v) + inset, At(max, v) - inset })
                places.Add(With(With(With(Vector3.Zero, cut.Axis, cut.At + nudge), u, su), v, sv));

        return places;
    }

    /// <summary>Whether a key on one plane belongs to this piece and is clear of the other cuts.</summary>
    private static bool Reaches(
        Vector3 key, int cut, int side, IReadOnlyList<MouldCut> cuts, float radius)
    {
        for (int c = 0; c < cuts.Count; c++)
        {
            if (c == cut) continue;

            float away = At(key, cuts[c].Axis) - cuts[c].At;
            if (MathF.Abs(away) < radius * 1.5f) return false;
            if (away > 0 != ((side & (1 << c)) != 0)) return false;
        }

        return true;
    }

    /// <summary>
    /// Mended after every key rather than once at the end: a piece with the pour hole in it is
    /// already thousands of triangles, and letting the small damage from each ball be the ground
    /// the next one is built on is the difference between four sound pieces and one.
    /// </summary>
    private static Mesh Dome(Mesh piece, IReadOnlyList<Vector3> keys, float half, CancellationToken token)
    {
        // Locally: a key sits out in the corner of a wall, where the model is nowhere near it.
        foreach (var key in keys)
            piece = LocalCsg.Union(piece, Brick(key, half), token);

        return piece;
    }

    private static Mesh Socket(Mesh piece, IReadOnlyList<Vector3> keys, float half, CancellationToken token)
    {
        // Already grown by the clearance, which on a box is exactly the gap asked for.
        foreach (var key in keys)
            piece = LocalCsg.Subtract(piece, Brick(key, half), token);

        return piece;
    }

    /// <summary>
    /// One key: a square block, not a ball.
    ///
    /// A ball was the obvious choice - it needs no orientation, so it works on any cut plane - and
    /// the socket for one tore the half it was taken out of every time. A sphere meets a flat face
    /// tangentially all the way round its rim, which is the contact this engine is worst at. The
    /// parting plane and the block are both axis-aligned, so a box key is box against box, which
    /// is the contact it is best at, and it registers just as well.
    /// </summary>
    private static Mesh Brick(Vector3 at, float half) => MeshTransform.Transformed(
        Primitives.Box(half * 2f, half * 2f, half * 2f), Matrix4x4.CreateTranslation(at));

    private static float At(Vector3 v, Axis axis) =>
        axis == Axis.X ? v.X : axis == Axis.Y ? v.Y : v.Z;

    private static Vector3 With(Vector3 v, Axis axis, float value) => axis switch
    {
        Axis.X => new Vector3(value, v.Y, v.Z),
        Axis.Y => new Vector3(v.X, value, v.Z),
        _ => new Vector3(v.X, v.Y, value),
    };

    private static string Describe(
        IReadOnlyList<MouldPart> parts, MouldStudy study, MouldOptions options)
    {
        int torn = parts.Count(p => !p.Watertight);

        string vents = options.AddVents && study.Vents.Count > 0
            ? $", {study.Vents.Count} vent(s)"
            : "";

        string health = torn == 0
            ? "all watertight"
            : $"{torn} of them not watertight - select and use Rebuild on the Tools tab";

        return $"{parts.Count} part(s), {options.Wall:0.##} mm wall, "
             + $"pour hole {options.SprueRadius * 2:0.##} mm{vents}, cut exactly: {health}.";
    }
}
