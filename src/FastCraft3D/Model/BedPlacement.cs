using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Model;

/// <summary>
/// Putting parts on the bed as a whole: fitting them to it, spreading them across it, and keeping
/// them from going through it.
///
/// The bed is centred on the origin, as the plate is drawn, so its middle is x = 0, y = 0 and its
/// surface is z = 0.
/// </summary>
public static class BedPlacement
{
    /// <summary>What Fit leaves clear round the edge of the bed, so a part is not printed on the rim.</summary>
    public const float FitMargin = 20f;

    /// <summary>How near the bed a lowest point may be and still count as standing on it.</summary>
    public const float Touching = 0.01f;

    /// <summary>How far everything reaches, taken together.</summary>
    public static Bounds Reach(IEnumerable<SceneObject> objects)
    {
        var bounds = Bounds.Empty;
        foreach (var o in objects) bounds = bounds.Union(o.WorldBounds);
        return bounds;
    }

    /// <summary>
    /// How much a lot this size has to shrink to fit the printable area with a margin round it: one
    /// when it already fits, since Fit never makes anything bigger. The height has no margin - the
    /// printer's height is already the most it takes.
    /// </summary>
    public static float FitRatio(Vector3 size, float width, float depth, float height, float margin = FitMargin)
    {
        var room = new Vector3(
            MathF.Max(width - 2f * margin, 1f),
            MathF.Max(depth - 2f * margin, 1f),
            MathF.Max(height, 1f));

        float ratio = 1f;
        if (size.X > room.X) ratio = MathF.Min(ratio, room.X / size.X);
        if (size.Y > room.Y) ratio = MathF.Min(ratio, room.Y / size.Y);
        if (size.Z > room.Z) ratio = MathF.Min(ratio, room.Z / size.Z);
        return ratio;
    }

    /// <summary>
    /// The middle of the nearest clear place for a footprint of <paramref name="size"/>, going out
    /// from the middle of the plate, <paramref name="gap"/> from anything already there. With no
    /// room left on the plate, beside everything, off its edge: a full plate is not the insert's
    /// to refuse, and on top of another part is the one place it must not go.
    ///
    /// Every insert used to go to the middle of the plate, and the second of two parts made the
    /// same way landed exactly inside the first.
    /// </summary>
    public static Vector2 Clear(Vector2 size, IReadOnlyList<Bounds> taken, float width, float depth, float gap = 5f)
    {
        bool Free(Vector2 c) => taken.All(b =>
            b.IsEmpty
            || c.X + size.X / 2f + gap <= b.Min.X || c.X - size.X / 2f - gap >= b.Max.X
            || c.Y + size.Y / 2f + gap <= b.Min.Y || c.Y - size.Y / 2f - gap >= b.Max.Y);

        if (Free(Vector2.Zero)) return Vector2.Zero;

        // Rings of candidates outward from the middle, nearest first, as far as the plate goes.
        const float step = 5f;
        float reach = MathF.Max(width, depth);
        for (float r = step; r <= reach; r += step)
        {
            int around = Math.Max(8, (int)(2 * MathF.PI * r / step));
            var ring = Enumerable.Range(0, around)
                .Select(i => r * new Vector2(MathF.Cos(2 * MathF.PI * i / around), MathF.Sin(2 * MathF.PI * i / around)))
                .Where(c => MathF.Abs(c.X) + size.X / 2f <= width / 2f && MathF.Abs(c.Y) + size.Y / 2f <= depth / 2f);

            foreach (var c in ring)
                if (Free(c)) return c;
        }

        float right = taken.Where(b => !b.IsEmpty).Select(b => b.Max.X).DefaultIfEmpty(0f).Max();
        return new Vector2(right + gap + size.X / 2f, 0f);
    }

    /// <summary>
    /// Shrinks the lot together about its own middle when <paramref name="ratio"/> is under one -
    /// the gaps with it, so an assembly stays assembled - then stands it centred on the bed.
    /// </summary>
    public static void Fit(IReadOnlyList<SceneObject> objects, float ratio)
    {
        if (objects.Count == 0) return;

        if (ratio < 1f)
        {
            var middle = Reach(objects).Center;
            foreach (var o in objects)
            {
                o.Scale *= ratio;
                o.Position = middle + (o.Position - middle) * ratio;
            }
        }

        var reach = Reach(objects);
        var shift = new Vector3(-reach.Center.X, -reach.Center.Y, -reach.Min.Z);
        foreach (var o in objects) o.Position += shift;
    }

    /// <summary>
    /// Where footprints of these sizes go on a bed <paramref name="width"/> across: in rows from
    /// the back of the bed to the front, <paramref name="gap"/> between neighbours and between
    /// rows, each row centred and the rows together centred on the bed. Deepest first within a
    /// row, so no row is deeper than it has to be. Returns the middle of each footprint in the
    /// order given, and how much of the bed the arrangement covers.
    ///
    /// Packed at whichever row width, up to the bed's own, comes out closest to square - a
    /// handful of small parts on a big bed, packed all the way across it, would leave most of its
    /// depth empty and sit hugging the middle of one edge once centred rather than looking centred
    /// at all; a row only as wide as the parts need spreads the same margin round every side.
    /// </summary>
    public static (List<Vector2> Centres, Vector2 Covers) Arrange(IReadOnlyList<Vector2> sizes, float gap, float width)
    {
        var centres = Enumerable.Repeat(Vector2.Zero, sizes.Count).ToList();
        if (sizes.Count == 0) return (centres, Vector2.Zero);

        gap = MathF.Max(gap, 0f);
        var order = Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].Y).ToList();

        List<List<int>>? chosen = null;
        float bestPenalty = float.MaxValue;

        // Tried across a spread of row widths from a quarter of the bed up to the whole of it -
        // narrow enough to let a handful of small parts pack into something closer to a square,
        // never so narrow that a single part's own width would force it into a row alone.
        float floor = MathF.Max(width / 4f, sizes.Max(s => s.X));
        for (int step = 0; step <= 9; step++)
        {
            float target = floor + (width - floor) * step / 9f;
            var rows = PackRows(order, sizes, gap, target);

            float rowWidth = rows.Max(r => RowWidth(r, sizes, gap));
            float rowsDepth = rows.Sum(r => RowDepth(r, sizes)) + gap * (rows.Count - 1);
            float penalty = MathF.Max(rowWidth, rowsDepth) / MathF.Max(MathF.Min(rowWidth, rowsDepth), 1e-3f);

            if (penalty >= bestPenalty) continue;
            bestPenalty = penalty;
            chosen = rows;
        }

        var covers = new Vector2(
            chosen!.Max(r => RowWidth(r, sizes, gap)),
            chosen.Sum(r => RowDepth(r, sizes)) + gap * (chosen.Count - 1));

        // From the back edge of the arrangement towards the front, which is towards minus Y.
        float back = covers.Y / 2f;
        foreach (var row in chosen)
        {
            float depth = RowDepth(row, sizes);
            float x = -RowWidth(row, sizes, gap) / 2f;

            foreach (int i in row)
            {
                centres[i] = new Vector2(x + sizes[i].X / 2f, back - depth / 2f);
                x += sizes[i].X + gap;
            }

            back -= depth + gap;
        }

        return (centres, covers);
    }

    private static float RowWidth(List<int> row, IReadOnlyList<Vector2> sizes, float gap) =>
        row.Sum(i => sizes[i].X) + gap * (row.Count - 1);

    private static float RowDepth(List<int> row, IReadOnlyList<Vector2> sizes) =>
        row.Max(i => sizes[i].Y);

    /// <summary>
    /// Filled by best fit rather than by always adding to the row just opened: a footprint, taken
    /// in the order given, joins whichever open row would have the least room spare once it is
    /// in, so a gap a taller neighbour left behind is the one filled, rather than opening a
    /// shallow row of its own further along. Only when it fits nowhere open does a new row start;
    /// one too wide for the target on its own still gets a row to itself.
    /// </summary>
    private static List<List<int>> PackRows(IReadOnlyList<int> order, IReadOnlyList<Vector2> sizes, float gap, float target)
    {
        var rows = new List<List<int>>();
        var used = new List<float>();

        foreach (int i in order)
        {
            int best = -1;
            float bestRoom = float.MaxValue;

            for (int r = 0; r < rows.Count; r++)
            {
                float extra = (rows[r].Count > 0 ? gap : 0f) + sizes[i].X;
                float room = target - used[r] - extra;
                if (room < 0f || room >= bestRoom) continue;

                best = r;
                bestRoom = room;
            }

            if (best < 0)
            {
                rows.Add([]);
                used.Add(0f);
                best = rows.Count - 1;
            }

            used[best] += (rows[best].Count > 0 ? gap : 0f) + sizes[i].X;
            rows[best].Add(i);
        }

        return rows;
    }

    /// <summary>
    /// Stands each object where <see cref="Arrange"/> put its footprint, on the bed. Turn and size
    /// are left alone; only where each stands changes.
    /// </summary>
    public static Vector2 Distribute(IReadOnlyList<SceneObject> objects, float gap, float width)
    {
        var footprints = objects.Select(o => o.WorldBounds).ToList();
        var (centres, covers) = Arrange(footprints.Select(b => new Vector2(b.Size.X, b.Size.Y)).ToList(), gap, width);

        for (int i = 0; i < objects.Count; i++)
        {
            var reach = footprints[i];
            objects[i].Position += new Vector3(
                centres[i].X - reach.Center.X,
                centres[i].Y - reach.Center.Y,
                -reach.Min.Z);
        }

        return covers;
    }

    /// <summary>Whether something reaching this far stands on the bed.</summary>
    public static bool Rests(Bounds bounds) => !bounds.IsEmpty && MathF.Abs(bounds.Min.Z) <= Touching;

    /// <summary>
    /// Holds a move, turn or resize to the bed: whatever has gone below it comes back up to stand
    /// on it, and with <paramref name="settle"/>, whatever stood on the bed before the change
    /// still stands on it after - which is what makes a part on the bed grow upward only, rather
    /// than half up and half into the bed.
    ///
    /// <paramref name="before"/> is how far each object reached before the change. Together, the
    /// lot moves up as one so an arrangement keeps its shape; otherwise each on its own. True when
    /// anything had to be moved.
    /// </summary>
    public static bool HoldToBed(IReadOnlyList<SceneObject> objects, IReadOnlyList<Bounds> before, bool together, bool settle)
    {
        if (objects.Count == 0 || before.Count != objects.Count) return false;

        if (together)
        {
            var was = Bounds.Empty;
            foreach (var b in before) was = was.Union(b);

            float shift = Lift(Reach(objects), settle && Rests(was));
            if (shift == 0f) return false;

            foreach (var o in objects) o.PositionZ += shift;
            return true;
        }

        bool moved = false;
        for (int i = 0; i < objects.Count; i++)
        {
            float shift = Lift(objects[i].WorldBounds, settle && Rests(before[i]));
            if (shift == 0f) continue;

            objects[i].PositionZ += shift;
            moved = true;
        }

        return moved;
    }

    /// <summary>How far up something reaching this far has to go: to the bed if it rested there, or out of it.</summary>
    private static float Lift(Bounds now, bool rested)
    {
        if (now.IsEmpty) return 0f;

        float low = now.Min.Z;
        return rested
            ? MathF.Abs(low) > 1e-5f ? -low : 0f
            : low < -1e-5f ? -low : 0f;
    }
}
