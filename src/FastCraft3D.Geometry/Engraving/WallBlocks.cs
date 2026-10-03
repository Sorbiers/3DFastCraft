using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Solid pieces laid on walls - a brick, a corner brick, a corner post - each its own closed
/// shell, from a little inside the wall to its face. What <see cref="BrickBond"/>,
/// <see cref="CornerPosts"/> and <see cref="LogWalls"/> are built of.
/// </summary>
internal static class WallBlocks
{
    /// <summary>
    /// How far a piece goes into the wall behind it, so the two overlap rather than meeting face
    /// to face - the same hair, and for the same reason, as <see cref="ReliefField.SinkMm"/>.
    /// </summary>
    public const float SinkMm = 0.3f;

    /// <summary>
    /// A piece turning a corner is sunk this much further behind one wall than the other. With the
    /// two equal, its back corner lies on the mitre through the wall's own corner and its cap is
    /// cut along a line straight through the part's corner edge - an edge crossing an edge, which
    /// the boolean cannot be handed. See <see cref="WallRun"/>'s fold lines.
    /// </summary>
    public const float SinkSkew = 1.37f;

    /// <summary>
    /// How far a piece at a free end stops short of the end of the wall, and how far one running
    /// into an inside corner goes past it into the other wall: a hair either way, so its end is
    /// never in the plane of the wall round the corner.
    /// </summary>
    public const float HairMm = 0.01f;

    /// <summary>Whether the corner between two walls of a loop is an outside one.</summary>
    public static bool Outside(WallLoop loop, int before, int after)
    {
        var (a, _) = loop.WallAt(before);
        var (b, _) = loop.WallAt(after);
        return a.X * b.Y - a.Y * b.X > 0;
    }

    /// <summary>A straight piece on one wall, from <paramref name="from"/> to <paramref name="to"/> along it.</summary>
    public static void Straight(
        List<Vector3> positions, List<int> indices, WallLoop loop, int wall,
        float from, float to, float proud, float bottom, float top)
    {
        var start = loop.CornerAt(wall);
        var (along, outward) = loop.WallAt(wall);

        Prism(positions, indices,
        [
            start + along * from - outward * SinkMm,
            start + along * to - outward * SinkMm,
            start + along * to + outward * proud,
            start + along * from + outward * proud
        ], bottom, top);
    }

    /// <summary>
    /// A piece turning the outside corner at the end of <paramref name="wall"/>:
    /// <paramref name="before"/> of it along that wall, <paramref name="after"/> along the next,
    /// as one block with a square corner.
    /// </summary>
    public static void Turning(
        List<Vector3> positions, List<int> indices, WallLoop loop, int wall,
        float before, float after, float proud, float bottom, float top)
    {
        int next = (wall + 1) % loop.Count;
        var corner = loop.CornerAt(next);
        var (inAlong, inOut) = loop.WallAt(wall);
        var (outAlong, outOut) = loop.WallAt(next);
        float skewed = SinkMm * SinkSkew;

        // Starting from the back corner, which sees the whole of the L, so the caps can be laid
        // as a fan from it.
        Prism(positions, indices,
        [
            corner - inOut * SinkMm - outOut * skewed,
            corner + outAlong * after - outOut * skewed,
            corner + outAlong * after + outOut * proud,
            corner + inOut * proud + outOut * proud,
            corner - inAlong * before + inOut * proud,
            corner - inAlong * before - inOut * SinkMm
        ], bottom, top);
    }

    /// <summary>
    /// A square post standing in the inside corner at the end of <paramref name="wall"/>,
    /// <paramref name="size"/> out from each wall.
    /// </summary>
    public static void Nook(
        List<Vector3> positions, List<int> indices, WallLoop loop, int wall,
        float size, float bottom, float top)
    {
        int next = (wall + 1) % loop.Count;
        var corner = loop.CornerAt(next);
        var (_, inOut) = loop.WallAt(wall);
        var (_, outOut) = loop.WallAt(next);
        float skewed = SinkMm * SinkSkew;

        Prism(positions, indices,
        [
            corner - inOut * SinkMm - outOut * skewed,
            corner + inOut * size - outOut * skewed,
            corner + inOut * size + outOut * size,
            corner - inOut * SinkMm + outOut * size
        ], bottom, top);
    }

    /// <summary>
    /// A plan stood up between two heights, closed, its faces outward. The plan must be seen
    /// whole from its first corner, since its caps are a fan from there.
    /// </summary>
    public static void Prism(List<Vector3> positions, List<int> indices, Vector2[] plan, float bottom, float top)
    {
        float area = 0f;
        for (int i = 0; i < plan.Length; i++)
        {
            var a = plan[i];
            var b = plan[(i + 1) % plan.Length];
            area += a.X * b.Y - b.X * a.Y;
        }

        // Anticlockwise from above, the first corner kept first.
        if (area < 0) Array.Reverse(plan, 1, plan.Length - 1);

        int n = plan.Length, low = positions.Count, high = low + n;
        foreach (var p in plan) positions.Add(new Vector3(p.X, p.Y, bottom));
        foreach (var p in plan) positions.Add(new Vector3(p.X, p.Y, top));

        for (int i = 1; i + 1 < n; i++)
        {
            indices.AddRange([low, low + i + 1, low + i]);
            indices.AddRange([high, high + i, high + i + 1]);
        }

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            indices.AddRange([low + i, low + j, high + j, low + i, high + j, high + i]);
        }
    }

    /// <summary>
    /// Turns the triangles added since <paramref name="from"/> round if they enclose their solid
    /// inside out - for a piece whose winding is easier to check than to work out.
    /// </summary>
    public static void Outward(List<Vector3> positions, List<int> indices, int from)
    {
        double volume = 0;
        for (int t = from; t + 2 < indices.Count; t += 3)
            volume += Vector3.Dot(positions[indices[t]], Vector3.Cross(positions[indices[t + 1]], positions[indices[t + 2]]));

        if (volume >= 0) return;

        for (int t = from; t + 2 < indices.Count; t += 3)
            (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
    }
}
