using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// The walls a texture is laid round: the ones picked and every wall between them, side by side
/// round a <see cref="WallLoop"/>. A strip has no gaps in it, so taking in a wall two along takes
/// the one between with it.
///
/// Fewer than all of them is a strip with two ends, each square to its end wall and flush with its
/// edge, as a texture on one face is. All of them is the whole way round, and closes on itself as
/// a ring round a barrel does.
/// </summary>
public sealed class WallRun
{
    private readonly FacePatch[] faces;

    private WallRun(WallLoop loop, int first, int count, FacePatch[] faces, float low, float high)
    {
        Loop = loop;
        First = first;
        Count = count;
        this.faces = faces;
        Low = low;
        High = high;

        float length = 0f;
        for (int k = 0; k < count; k++) length += loop.LengthOf((first + k) % loop.Count);
        Length = Closed ? loop.Perimeter : length;
    }

    public WallLoop Loop { get; }

    /// <summary>The wall the strip starts at, going round with the material on the left.</summary>
    public int First { get; }

    public int Count { get; }

    /// <summary>How far it is from one end of the strip to the other, in millimetres.</summary>
    public float Length { get; }

    /// <summary>The foot of the lowest wall in it, and the top of the tallest.</summary>
    public float Low { get; }

    public float High { get; }

    public bool Closed => Count >= Loop.Count;

    public int Last => (First + Count - 1) % Loop.Count;

    /// <summary>The walls in it, in order along the strip.</summary>
    public IEnumerable<int> Walls => Enumerable.Range(0, Count).Select(k => (First + k) % Loop.Count);

    /// <summary>Each wall's own flat face, in the same order.</summary>
    public IReadOnlyList<FacePatch> Faces => faces;

    public bool Holds(int wall) => Mod(wall - First) < Count;

    /// <summary>
    /// The middle of the strip, as a distance round the loop: where its layout is centred. The
    /// whole way round there is no middle, and it is the click, as round a barrel.
    /// </summary>
    public float MiddleArc => Closed ? Loop.StartArc : Loop.ArcOfCorner(First) + Length / 2f;

    /// <summary>The point of the strip <paramref name="across"/> from its middle, stood <paramref name="height"/> off it.</summary>
    public Vector2 At(float across, float height) => Loop.At(MiddleArc + across, height, First, Count);

    /// <summary>
    /// How far either side of a corner its two fold lines are.
    ///
    /// Not on the corner itself. Anything laid there stands off it along the mitre, and from a
    /// little inside the wall to a little out of it that line runs straight through the part's own
    /// corner edge - an edge crossing an edge, which is the one thing the boolean cannot be handed.
    /// A box with a window through it came back with two non-manifold edges at a corner, and the
    /// same box without one got through only because its triangles happened to lie differently. A
    /// hair either side crosses the wall's face instead, and the chord across the corner between
    /// the two strays from it by a few thousandths of a millimetre.
    /// </summary>
    private const float FoldHairMm = 0.01f;

    /// <summary>
    /// Where the strip turns a corner between two distances along it, measured from its middle:
    /// a line a hair either side of each corner inside it. The whole way round a layout slid past
    /// one end comes round again at the other, so each corner is there once for every time round.
    /// </summary>
    public IReadOnlyList<float> FoldsAcross(float from, float to)
    {
        var folds = new List<float>();
        float round = Loop.Perimeter;

        void Add(float x)
        {
            if (x - FoldHairMm > from && x - FoldHairMm < to) folds.Add(x - FoldHairMm);
            if (x + FoldHairMm > from && x + FoldHairMm < to) folds.Add(x + FoldHairMm);
        }

        for (int k = Closed ? 0 : 1; k < Count; k++)
        {
            float at = Loop.ArcOfCorner((First + k) % Loop.Count) - MiddleArc;

            if (Closed)
            {
                for (float x = at - round * MathF.Floor((at - from) / round) - round; x < to + round; x += round)
                    Add(x);
            }
            else
            {
                // An open strip is shorter than the way round, so each corner of it is the one
                // nearest its middle.
                Add(at - round * MathF.Floor(at / round + 0.5f));
            }
        }

        folds.Sort();
        return folds;
    }

    /// <summary>The one wall picked, or null with the reason when it is not a flat wall.</summary>
    public static WallRun? Of(WallLoop loop, Mesh world, int wall, out string? why) =>
        Build(loop, world, wall, 1, out why);

    /// <summary>Every wall, the whole way round.</summary>
    public static WallRun? Round(WallLoop loop, Mesh world, out string? why) =>
        Build(loop, world, 0, loop.Count, out why);

    /// <summary>
    /// The strip carried on to take in <paramref name="wall"/>, and every wall between, from
    /// whichever end of it is nearer - or this strip, when the wall is in it already.
    /// </summary>
    public WallRun? With(Mesh world, int wall, out string? why)
    {
        why = null;
        if (Holds(wall)) return this;

        int on = Mod(wall - Last), back = Mod(First - wall);
        return on <= back
            ? Build(Loop, world, First, Count + on, out why)
            : Build(Loop, world, wall, Count + back, out why);
    }

    /// <summary>
    /// The strip without <paramref name="wall"/>: one at either end let go, or the whole way round
    /// opened there. Null, with the reason, for the only wall, or for one in the middle - that
    /// would leave two strips.
    /// </summary>
    public WallRun? Without(Mesh world, int wall, out string? why)
    {
        why = null;
        if (!Holds(wall)) return this;

        if (Count == 1)
        {
            why = "That is the only wall it is on - click another wall to start again there.";
            return null;
        }

        if (Closed) return Build(Loop, world, (wall + 1) % Loop.Count, Count - 1, out why);
        if (wall == First) return Build(Loop, world, (First + 1) % Loop.Count, Count - 1, out why);
        if (wall == Last) return Build(Loop, world, First, Count - 1, out why);

        why = "Only a wall at either end can be let go - one in the middle would leave two strips.";
        return null;
    }

    private static WallRun? Build(WallLoop loop, Mesh world, int first, int count, out string? why)
    {
        why = null;
        int n = loop.Count;
        count = Math.Min(count, n);

        // A wall already textured is not one face but hundreds of facets, each of them a corner of
        // the outline and a "wall" a fraction of a millimetre high. Laid round those, a strip took
        // over five minutes and came out in tatters; so a wall is only a wall if its face stands a
        // fair share of the height of the one picked first.
        float least = MathF.Max(0.5f, (loop.High - loop.Low) * 0.25f);

        var faces = new FacePatch[count];
        float low = float.MaxValue, high = float.MinValue;

        for (int k = 0; k < count; k++)
        {
            var face = loop.FaceOf(world, (first + k) % n);
            var (bottom, top) = face is null ? (0f, 0f) : Heights(world, face);

            if (face is null || top - bottom < least)
            {
                why = "One of those walls is not flat - is there a texture on it already? "
                    + "A texture goes round flat walls only.";
                return null;
            }

            faces[k] = face;
            low = MathF.Min(low, bottom);
            high = MathF.Max(high, top);
        }

        return new WallRun(loop, first, count, faces, low, high);
    }

    private static (float Low, float High) Heights(Mesh world, FacePatch face)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (int t in face.Triangles)
            for (int k = 0; k < 3; k++)
            {
                float z = world.Positions[world.Indices[t + k]].Z;
                low = MathF.Min(low, z);
                high = MathF.Max(high, z);
            }

        return (low, high);
    }

    private int Mod(int i) => ((i % Loop.Count) + Loop.Count) % Loop.Count;
}
