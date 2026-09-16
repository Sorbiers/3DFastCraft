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
    /// Where footprints of these sizes go on a bed <paramref name="width"/> across: in rows from the
    /// back of the bed to the front, <paramref name="gap"/> between neighbours and between rows,
    /// each row centred and the rows together centred on the bed. Deepest first, so no row is
    /// deeper than it has to be. Returns the middle of each footprint in the order given, and how
    /// much of the bed the arrangement covers.
    /// </summary>
    public static (List<Vector2> Centres, Vector2 Covers) Arrange(IReadOnlyList<Vector2> sizes, float gap, float width)
    {
        var centres = Enumerable.Repeat(Vector2.Zero, sizes.Count).ToList();
        if (sizes.Count == 0) return (centres, Vector2.Zero);

        gap = MathF.Max(gap, 0f);

        // Rows filled in turn: a footprint goes on the end of the row until it would run off the
        // bed, and then starts the next. One too wide for the bed has a row to itself.
        var rows = new List<List<int>>();
        float used = 0f;
        foreach (int i in Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i].Y))
        {
            if (rows.Count == 0 || used + gap + sizes[i].X > width)
            {
                rows.Add([]);
                used = -gap;
            }

            rows[^1].Add(i);
            used += gap + sizes[i].X;
        }

        float RowWidth(List<int> row) => row.Sum(i => sizes[i].X) + gap * (row.Count - 1);
        float RowDepth(List<int> row) => row.Max(i => sizes[i].Y);

        var covers = new Vector2(
            rows.Max(RowWidth),
            rows.Sum(RowDepth) + gap * (rows.Count - 1));

        // From the back edge of the arrangement towards the front, which is towards minus Y.
        float back = covers.Y / 2f;
        foreach (var row in rows)
        {
            float depth = RowDepth(row);
            float x = -RowWidth(row) / 2f;

            foreach (int i in row)
            {
                centres[i] = new Vector2(x + sizes[i].X / 2f, back - depth / 2f);
                x += sizes[i].X + gap;
            }

            back -= depth + gap;
        }

        return (centres, covers);
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
