using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Brickwork laid round walls the way a bricklayer lays it, rather than a pattern laid over them.
///
/// A brick texture wrapped round a corner turns it, but the brick on the corner is cut in two
/// wherever the pattern happens to put it, and the next wall's courses carry on from wherever the
/// last wall left them. A real wall is built the other way round. Each corner is one whole brick,
/// its length on one face and its end on the other, swapping faces from one course to the next;
/// and a wall is set out to a whole number of bricks between its corners. Stretcher bond's
/// half-brick stagger is not laid on separately here: it falls out of the corners swapping, as it
/// does on a building site.
///
/// The bricks are solid blocks standing off the walls, each its own closed shell and none
/// touching another, so the whole is ready to be trimmed to the walls and joined on.
/// </summary>
public static class BrickBond
{
    /// <summary>
    /// How far a brick goes into the wall behind it, so the two overlap rather than meeting face
    /// to face - the same hair, and for the same reason, as <see cref="ReliefField.SinkMm"/>.
    /// </summary>
    public const float SinkMm = 0.3f;

    /// <summary>
    /// A corner brick's back is sunk this much further behind one wall than the other. With the
    /// two equal, the back corner of the brick lies on the mitre through the wall's own corner and
    /// the brick's cap is cut along a line straight through the corner edge of the part - an edge
    /// crossing an edge, which the boolean cannot be handed. See <see cref="WallRun"/>'s fold lines.
    /// </summary>
    private const float SinkSkew = 1.37f;

    /// <summary>
    /// How far a brick at a free end stops short of the end of the wall, and how far a brick
    /// running into an inside corner goes past it into the other wall: a hair either way, so its
    /// end is never in the plane of the wall round the corner.
    /// </summary>
    private const float HairMm = 0.01f;

    /// <summary>How far off square a corner may be and still be bonded: about three degrees.</summary>
    private const float SquareSlack = 0.05f;

    /// <summary>The narrowest piece of brick worth laying, beside an inside corner.</summary>
    private const float LeastPieceMm = 0.2f;

    /// <summary>
    /// Why the walls cannot be bricked, or null when they can. A corner inside the run has to be
    /// a right angle: a corner brick is a right-angled brick.
    /// </summary>
    public static string? Refusal(WallRun run)
    {
        var loop = run.Loop;
        int n = loop.Count;

        for (int k = run.Closed ? 0 : 1; k < run.Count; k++)
        {
            int wall = (run.First + k) % n;
            var (before, _) = loop.WallAt((wall + n - 1) % n);
            var (along, _) = loop.WallAt(wall);

            if (MathF.Abs(Vector2.Dot(before, along)) > SquareSlack)
            {
                float degrees = MathF.Acos(Math.Clamp(Vector2.Dot(before, along), -1f, 1f)) * 180f / MathF.PI;
                return $"Brickwork turns square corners only, and two of these walls meet at {180f - degrees:0} degrees. "
                     + "Pick the walls either side of it separately.";
            }
        }

        return null;
    }

    /// <summary>
    /// The bricks round the walls of <paramref name="run"/>, or an empty mesh when there is no
    /// room for one.
    /// </summary>
    /// <param name="moduleMm">A brick and one joint, along the wall.</param>
    /// <param name="aspect">How many times the module is the height of a course.</param>
    /// <param name="jointMm">The mortar joint between two bricks, and between two courses.</param>
    /// <param name="depthMm">How far a brick stands off the wall.</param>
    /// <param name="bricks">How many bricks were laid, a corner brick counting as one.</param>
    public static Mesh Build(
        WallRun run, float moduleMm, float aspect, float jointMm, float depthMm, out int bricks)
    {
        bricks = 0;
        var mesh = new Mesh();
        if (Refusal(run) is not null) return mesh;

        float joint = MathF.Max(jointMm, 0.05f);
        float depth = MathF.Max(depthMm, 0.05f);
        float module = MathF.Max(moduleMm, 2f * joint + 2f * LeastPieceMm);
        float course = MathF.Max(module / MathF.Max(aspect, 0.2f), joint + LeastPieceMm);

        float height = run.High - run.Low;
        int courses = Math.Max(1, (int)MathF.Round(height / course));
        course = height / courses;
        if (course - joint < LeastPieceMm) return mesh;

        var plans = Plans(run, module, joint);
        var positions = new List<Vector3>();
        var indices = new List<int>();

        for (int k = 0; k < courses; k++)
        {
            float bottom = run.Low + k * course + joint / 2f;
            float top = run.Low + (k + 1) * course - joint / 2f;

            for (int p = 0; p < plans.Length; p++)
            {
                var plan = plans[p];
                bool stretcher = Stretches(k, p);
                float end = stretcher ? plan.Brick : plan.Header;

                foreach (var (from, to) in Pieces(plan, end, stretcher, joint, depth))
                {
                    Box(positions, indices, run.Loop, plan.Wall, from, to, depth, bottom, top);
                    bricks++;
                }

                // The brick round the corner at this wall's far end, unless the run stops there.
                if (plan.EndCorner == Corner.Outside)
                {
                    var next = plans[(p + 1) % plans.Length];
                    float nextEnd = Stretches(k, p + 1) ? next.Brick : next.Header;
                    Turning(positions, indices, run.Loop, plan.Wall, end, nextEnd, depth, bottom, top);
                    bricks++;
                }
            }
        }

        return new Mesh(positions, indices);
    }

    /// <summary>
    /// Whether the wall at <paramref name="place"/> along the run shows a brick's length at both
    /// its ends in course <paramref name="course"/>, rather than its end. Neighbours always
    /// disagree, so every corner has one of each; and a run round a building whose corners are all
    /// square has an even number of walls, so it agrees with itself where it closes.
    /// </summary>
    private static bool Stretches(int course, int place) => ((course + place) & 1) == 1;

    private enum Corner
    {
        /// <summary>The run stops here: the wall's edge, finished square.</summary>
        Free,

        /// <summary>An outside corner, turned by one brick.</summary>
        Outside,

        /// <summary>An inside corner, where one wall's brick runs into the other wall.</summary>
        Inside
    }

    /// <param name="Wall">Which wall of the loop.</param>
    /// <param name="Length">How long it is, corner to corner.</param>
    /// <param name="Brick">The length of a brick on it, fitted so a whole number go between its corners.</param>
    /// <param name="Header">The end of one: half a brick, less half a joint.</param>
    /// <param name="Modules">How many bricks and joints fit corner to corner.</param>
    private readonly record struct WallPlan(
        int Wall, float Length, float Brick, float Header, int Modules, Corner StartCorner, Corner EndCorner);

    /// <summary>
    /// Each wall set out on its own. A wall corner to corner is a whole number of bricks and
    /// joints less one joint; the bricks give a little to make it so, as a bricklayer's perpends
    /// do, and a wall shorter than two bricks takes two short ones.
    /// </summary>
    private static WallPlan[] Plans(WallRun run, float module, float joint)
    {
        var loop = run.Loop;
        int n = loop.Count;
        var walls = run.Walls.ToList();
        var plans = new WallPlan[walls.Count];

        for (int p = 0; p < walls.Count; p++)
        {
            int wall = walls[p];
            float length = loop.LengthOf(wall);
            int modules = Math.Max(2, (int)MathF.Round((length + joint) / module));
            float brick = (length + joint) / modules - joint;

            var start = !run.Closed && p == 0 ? Corner.Free : Turn(loop, (wall + n - 1) % n, wall);
            var end = !run.Closed && p == walls.Count - 1 ? Corner.Free : Turn(loop, wall, (wall + 1) % n);

            plans[p] = new WallPlan(wall, length, brick, (brick - joint) / 2f, modules, start, end);
        }

        return plans;
    }

    private static Corner Turn(WallLoop loop, int before, int after)
    {
        var (a, _) = loop.WallAt(before);
        var (b, _) = loop.WallAt(after);
        return a.X * b.Y - a.Y * b.X > 0 ? Corner.Outside : Corner.Inside;
    }

    /// <summary>
    /// The bricks of one course along one wall, as distances from its start, leaving out the
    /// corner brick at either end that is laid with the corner. <paramref name="end"/> is the
    /// piece at each end: a whole brick's length where this wall shows it, an end where the wall
    /// round the corner does.
    /// </summary>
    private static IEnumerable<(float From, float To)> Pieces(
        WallPlan plan, float end, bool stretcher, float joint, float depth)
    {
        float len = plan.Length;

        switch (plan.StartCorner)
        {
            case Corner.Free:
                yield return (HairMm, end);
                break;

            // A whole brick runs into the corner and on into the wall round it; an end stops at
            // the face of the brick that does, one joint clear of it.
            case Corner.Inside when stretcher:
                yield return (-HairMm, end);
                break;

            case Corner.Inside when end - depth - joint >= LeastPieceMm:
                yield return (depth + joint, end);
                break;
        }

        int between = stretcher ? plan.Modules - 2 : plan.Modules - 1;
        for (int j = 0; j < between; j++)
        {
            float from = end + joint + j * (plan.Brick + joint);
            yield return (from, from + plan.Brick);
        }

        switch (plan.EndCorner)
        {
            case Corner.Free:
                yield return (len - end, len - HairMm);
                break;

            case Corner.Inside when stretcher:
                yield return (len - end, len + HairMm);
                break;

            case Corner.Inside when end - depth - joint >= LeastPieceMm:
                yield return (len - end, len - depth - joint);
                break;
        }
    }

    /// <summary>One straight brick on one wall, from its back in the wall to its face.</summary>
    private static void Box(
        List<Vector3> positions, List<int> indices, WallLoop loop, int wall,
        float from, float to, float depth, float bottom, float top)
    {
        var start = loop.CornerAt(wall);
        var (along, outward) = loop.WallAt(wall);

        Prism(positions, indices,
        [
            start + along * from - outward * SinkMm,
            start + along * to - outward * SinkMm,
            start + along * to + outward * depth,
            start + along * from + outward * depth
        ], bottom, top);
    }

    /// <summary>
    /// The brick on an outside corner: <paramref name="before"/> of it along the wall coming in,
    /// <paramref name="after"/> along the wall going out, as one block with a square corner.
    /// </summary>
    private static void Turning(
        List<Vector3> positions, List<int> indices, WallLoop loop, int wall,
        float before, float after, float depth, float bottom, float top)
    {
        int n = loop.Count;
        int next = (wall + 1) % n;
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
            corner + outAlong * after + outOut * depth,
            corner + inOut * depth + outOut * depth,
            corner - inAlong * before + inOut * depth,
            corner - inAlong * before - inOut * SinkMm
        ], bottom, top);
    }

    /// <summary>
    /// A plan stood up between two heights, closed, its faces outward. The plan must be seen
    /// whole from its first corner, since its caps are a fan from there.
    /// </summary>
    private static void Prism(List<Vector3> positions, List<int> indices, Vector2[] plan, float bottom, float top)
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
}
