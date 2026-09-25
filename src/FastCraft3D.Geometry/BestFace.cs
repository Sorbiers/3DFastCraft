using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>What to weigh most when the faces are put in order.</summary>
public enum FaceChoice
{
    /// <summary>Least support to print and clean off. The usual answer.</summary>
    LessSupport,

    /// <summary>Shortest, which is most of the print time.</summary>
    ShorterPrint,

    /// <summary>Hardest to knock over while it prints.</summary>
    FirmerFooting
}

/// <param name="Normal">
/// The face's outward direction in the part's present world space - what to turn towards the bed.
/// </param>
/// <param name="SupportMm2">How much would overhang, laid on this face.</param>
/// <param name="HeightMm">How tall it would stand.</param>
/// <param name="FootprintMm2">The face's own area, which is what sticks to the bed.</param>
/// <param name="TipDegrees">
/// How far it could be tilted before its weight passed outside the footprint. Large is stable; a
/// tall part on a small base is a few degrees and gets knocked over by its own printer.
/// </param>
/// <param name="IsCurrent">Whether the part is already standing on this face.</param>
/// <param name="Alike">
/// How many faces come out to this one's numbers, itself included. A cube has six and they are
/// worth saying once.
/// </param>
public sealed record StandingChoice(
    Vector3 Normal,
    double SupportMm2,
    float HeightMm,
    float FootprintMm2,
    float TipDegrees,
    bool IsCurrent,
    int Alike);

/// <summary>
/// Which way up to print a part.
///
/// <see cref="RestingFaces"/> answers what a part can stand on; this answers which of those to
/// pick. The measures are the ones that decide a print: how much has to be held up while it is
/// laid, how tall it stands, how much of it touches the bed, and how easily it is knocked over.
///
/// Which of those matters most is not a fact about the part, so it is not decided here. Support
/// usually wins - it is the material, the time and the scar left behind when it is broken off -
/// but a tall part that fits the machine only one way up, or one being printed in a hurry, is a
/// different question. Hence the ordering is a choice the panel offers rather than a set of
/// weights buried in this file.
/// </summary>
public static class BestFace
{
    /// <summary>How near straight down a face has to point to count as the one it is standing on.</summary>
    private const float Upright = 0.999f;

    /// <summary>
    /// How many faces are measured.
    ///
    /// They arrive largest first, and a part is all but never best off balanced on its smallest
    /// facet, so the tail is cut rather than measured. Sixty faces on a scan would otherwise be
    /// sixty passes over every triangle it has.
    /// </summary>
    private const int MostWeighed = 24;

    /// <summary>Below this, two supports are the same support: a tenth of a square centimetre.</summary>
    private const double SupportBandMm2 = 10.0;

    /// <summary>Below this, two heights are the same height.</summary>
    private const float HeightBandMm = 0.5f;

    /// <summary>
    /// Every way up the part could be printed, in the order asked for, best first.
    ///
    /// <paramref name="world"/> and <paramref name="faces"/> are both in the part's present world
    /// space, as <see cref="RestingFaces.Find"/> returns them.
    /// </summary>
    public static List<StandingChoice> Rank(
        Mesh world,
        IReadOnlyList<RestingFace> faces,
        float angleDegrees = Overhangs.DefaultAngle,
        FaceChoice favour = FaceChoice.LessSupport,
        CancellationToken token = default)
    {
        if (world.TriangleCount == 0 || faces.Count == 0) return [];

        var weight = RestingFaces.CentreOfMass(world);
        var measured = new List<StandingChoice>();

        foreach (var face in faces.Take(MostWeighed))
        {
            token.ThrowIfCancellationRequested();

            measured.Add(new StandingChoice(
                face.Normal,
                Overhangs.AreaFacing(world, face.Normal, angleDegrees),
                Overhangs.HeightFacing(world, face.Normal),
                face.Area,
                TipDegrees(face, weight),
                Vector3.Dot(Vector3.Normalize(face.Normal), -Vector3.UnitZ) >= Upright,
                Alike: 1));
        }

        // The face it is already standing on is always wanted, even when the cut above left it
        // out, so the panel can say what the part costs where it is and not only what it could
        // cost. A part stood on one of its smaller facets is exactly when that is worth knowing.
        if (!measured.Any(m => m.IsCurrent))
        {
            foreach (var face in faces.Skip(MostWeighed))
            {
                if (Vector3.Dot(Vector3.Normalize(face.Normal), -Vector3.UnitZ) < Upright) continue;

                measured.Add(new StandingChoice(
                    face.Normal,
                    Overhangs.AreaFacing(world, face.Normal, angleDegrees),
                    Overhangs.HeightFacing(world, face.Normal),
                    face.Area,
                    TipDegrees(face, weight),
                    IsCurrent: true,
                    Alike: 1));
                break;
            }
        }

        return Order(Fold(measured), favour);
    }

    /// <summary>
    /// The same choices in a different order. Changing what to favour re-sorts what was measured
    /// rather than measuring it again - nothing about the part has changed.
    /// </summary>
    public static List<StandingChoice> Order(IEnumerable<StandingChoice> choices, FaceChoice favour) =>
        favour switch
        {
            FaceChoice.ShorterPrint => choices
                .OrderBy(c => Band(c.HeightMm, HeightBandMm))
                .ThenBy(c => Band(c.SupportMm2, SupportBandMm2))
                .ThenByDescending(c => c.FootprintMm2)
                .ToList(),

            FaceChoice.FirmerFooting => choices
                .OrderByDescending(c => Band(c.TipDegrees, 1f))
                .ThenBy(c => Band(c.SupportMm2, SupportBandMm2))
                .ThenBy(c => Band(c.HeightMm, HeightBandMm))
                .ToList(),

            // Support first, and among faces that need the same support the shorter one, which is
            // the cheaper print. Footprint breaks the last tie because a wider base sticks down
            // better and there is nothing else to separate them by.
            _ => choices
                .OrderBy(c => Band(c.SupportMm2, SupportBandMm2))
                .ThenBy(c => Band(c.HeightMm, HeightBandMm))
                .ThenByDescending(c => c.FootprintMm2)
                .ToList()
        };

    /// <summary>
    /// Faces that measure the same shown once, with a count.
    ///
    /// A cube has six ways up and they are all the same way up. Offering six rows that differ in
    /// nothing a person can see makes the list useless for the parts that have a real choice in
    /// them, so identical numbers are one row. The one being stood on wins its group, so the mark
    /// saying where the part is now is never the row that got folded away.
    /// </summary>
    private static List<StandingChoice> Fold(List<StandingChoice> measured)
    {
        var kept = new List<StandingChoice>();
        var byNumbers = new Dictionary<(long, long, long, long), int>();

        foreach (var choice in measured)
        {
            var key = (
                Band(choice.SupportMm2, SupportBandMm2),
                Band(choice.HeightMm, HeightBandMm),
                Band(choice.FootprintMm2, 1f),
                Band(choice.TipDegrees, 1f));

            if (byNumbers.TryGetValue(key, out int at))
            {
                kept[at] = kept[at] with
                {
                    Alike = kept[at].Alike + 1,
                    Normal = choice.IsCurrent ? choice.Normal : kept[at].Normal,
                    IsCurrent = kept[at].IsCurrent || choice.IsCurrent
                };
                continue;
            }

            byNumbers[key] = kept.Count;
            kept.Add(choice);
        }

        return kept;
    }

    /// <summary>
    /// How far the part could be tilted, standing on this face, before its weight passed outside
    /// the footprint and it went over.
    ///
    /// The arctangent of how far in from the edge the weight sits against how high it sits: the
    /// textbook measure, and the one that tells a tall thin part from a squat one without needing
    /// either number on its own. A face the weight is barely over gives a degree or two, which is
    /// the honest answer for a part that a fan knocks off the bed.
    /// </summary>
    private static float TipDegrees(RestingFace face, Vector3 weight)
    {
        var normal = Vector3.Normalize(face.Normal);

        // The normal points away from the part, so the weight is behind the plane and this is
        // positive: how high the centre of mass stands over the face it rests on.
        float rise = Vector3.Dot(face.Centre - weight, normal);
        if (rise <= 1e-4f) return 89.9f;

        var onPlane = weight + normal * rise;
        float margin = float.MaxValue;

        foreach (var (from, to) in Rim(face))
            margin = MathF.Min(margin, ToSegment(onPlane, from, to));

        if (margin == float.MaxValue) return 0f;

        return MathF.Min(MathF.Atan2(margin, rise) * 180f / MathF.PI, 89.9f);
    }

    /// <summary>
    /// The edge of the face: the edges of its triangles that no second triangle shares.
    ///
    /// The corners come from one array of positions, so equal corners are the same float values
    /// and match exactly - no tolerance is needed or wanted here.
    /// </summary>
    private static List<(Vector3 From, Vector3 To)> Rim(RestingFace face)
    {
        var tris = face.Triangles;
        var used = new HashSet<(Vector3, Vector3)>();

        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            used.Add((tris[i], tris[i + 1]));
            used.Add((tris[i + 1], tris[i + 2]));
            used.Add((tris[i + 2], tris[i]));
        }

        var rim = new List<(Vector3, Vector3)>();
        foreach (var (from, to) in used)
            if (!used.Contains((to, from))) rim.Add((from, to));

        return rim;
    }

    private static float ToSegment(Vector3 point, Vector3 from, Vector3 to)
    {
        var along = to - from;
        float length = along.LengthSquared();
        if (length < 1e-12f) return (point - from).Length();

        float t = Math.Clamp(Vector3.Dot(point - from, along) / length, 0f, 1f);
        return (point - (from + along * t)).Length();
    }

    private static long Band(double value, double width) => (long)Math.Round(value / width);
}
