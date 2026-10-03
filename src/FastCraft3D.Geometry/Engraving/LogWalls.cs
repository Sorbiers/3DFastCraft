using System.Numerics;
using FastCraft3D.Geometry.Csg;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Log walls: round logs laid one on another along each wall, crossing at the corners with their
/// ends standing out past them, as a log cabin's do.
///
/// At a corner the logs of the two walls take turns - each wall's courses half a log higher than
/// the last's, so a log of one sits between two of the other - and every log runs on past the
/// corner by most of its own thickness, round, its end cut square. That is the whole look of a
/// cabin corner, and it falls out of nothing more than laying the logs long and lifting every
/// other wall by half a course. The walls lifted have half a log at the foot and the top.
/// </summary>
public static class LogWalls
{
    /// <summary>Sides to a log's round. A log is read by its outline, so it has to look round.</summary>
    private const int Sides = 20;

    /// <summary>How far a log runs on past an outside corner, as a share of a course.</summary>
    private const float Overhang = 0.75f;

    /// <summary>How tall a course of logs is on these walls: a whole number of them from foot to top.</summary>
    public static float CourseOf(WallRun run, float diameterMm)
    {
        float height = run.High - run.Low;
        return height / Math.Max(1, (int)MathF.Round(height / MathF.Max(diameterMm, 0.5f)));
    }

    /// <summary>How many logs <see cref="Build"/> lays, found without laying them.</summary>
    public static int Count(WallRun run, float diameterMm)
    {
        float course = CourseOf(run, diameterMm);
        int courses = (int)MathF.Round((run.High - run.Low) / course);
        if (BrickBond.Refusal(run) is not null || course / 2f < 0.2f) return 0;

        // A lifted wall has a half log at its foot and its top, one more than the others.
        int count = 0;
        for (int p = 0; p < run.Count; p++) count += (p & 1) == 1 ? courses + 1 : courses;
        return count;
    }

    /// <summary>The furthest the logs stand off the walls, the ends past the corners included.</summary>
    public static float Reach(WallRun run, float diameterMm, float proudMm) =>
        MathF.Max(proudMm, Overhang * CourseOf(run, diameterMm));

    /// <summary>
    /// The logs round the walls of <paramref name="run"/>, joined into one solid, or an empty mesh
    /// when the corners are not square - a cabin's corner is - or there is no room for a log.
    /// </summary>
    /// <param name="diameterMm">How thick a log is, more or less: the walls are fitted to a whole number.</param>
    /// <param name="gapMm">The gap between one log and the next above it.</param>
    /// <param name="proudMm">How far a log stands off the wall, at most half its thickness.</param>
    public static Mesh Build(WallRun run, float diameterMm, float gapMm, float proudMm, out int logs)
    {
        logs = 0;
        if (BrickBond.Refusal(run) is not null) return new Mesh();

        var loop = run.Loop;
        int n = loop.Count;
        float course = CourseOf(run, diameterMm);
        int courses = (int)MathF.Round((run.High - run.Low) / course);
        float gap = Math.Clamp(gapMm, 0.05f, course * 0.4f);
        float radius = (course - gap) / 2f;
        if (radius < 0.2f) return new Mesh();

        float proud = Math.Clamp(proudMm, 0.1f, radius);
        float axis = proud - radius;
        float over = Overhang * course;
        float hair = WallBlocks.HairMm;

        var pieces = new List<Mesh>();
        var walls = run.Walls.ToList();

        for (int p = 0; p < walls.Count; p++)
        {
            int wall = walls[p];
            float length = loop.LengthOf(wall);

            // A corner between two walls picked: outside, the log runs on past it; inside, into
            // the other wall. Where the walls picked stop it stops short of the edge.
            bool startTurns = run.Closed || p > 0, endTurns = run.Closed || p < walls.Count - 1;
            bool startOut = startTurns && WallBlocks.Outside(loop, (wall + n - 1) % n, wall);
            bool endOut = endTurns && WallBlocks.Outside(loop, wall, (wall + 1) % n);

            var sections = new List<(float S, bool Round)>();
            if (startOut) sections.AddRange([(-over, true), (-hair, true), (hair, false)]);
            else sections.Add((startTurns ? -hair : hair, false));

            if (endOut) sections.AddRange([(length - hair, false), (length + hair, true), (length + over, true)]);
            else sections.Add((endTurns ? length + hair : length - hair, false));

            // Every other wall half a course up, so the corners interleave.
            bool lifted = (p & 1) == 1;
            int last = lifted ? courses : courses - 1;

            for (int k = 0; k <= last; k++)
            {
                float middle = run.Low + (lifted ? k : k + 0.5f) * course;
                int half = !lifted ? 0 : k == 0 ? 1 : k == courses ? -1 : 0;

                var mesh = Log(loop, wall, sections, axis, middle, radius, half, run.Low + hair, run.High - hair);
                if (mesh.TriangleCount > 0)
                {
                    pieces.Add(mesh);
                    logs++;
                }
            }
        }

        if (pieces.Count == 0) return new Mesh();

        // The ends crossing at the corners overlap, and a solid of overlapping shells is not one the
        // boolean can be handed, so they are joined first.
        var joined = ManifoldCsg.UnionAll(pieces);
        return joined is { TriangleCount: > 0 } && joined.CheckHealth().IsWatertight ? joined : Mesh.Combine(pieces);
    }

    /// <summary>
    /// One log along one wall, its round taken at each of <paramref name="sections"/>: in the open,
    /// whole; along the wall, its back cut flat a little inside the wall so a thin wall is not
    /// pierced. <paramref name="half"/> is the half log at the foot of a lifted wall (1, its top
    /// half) or at its top (-1, its bottom half).
    /// </summary>
    private static Mesh Log(
        WallLoop loop, int wall, List<(float S, bool Round)> sections,
        float axis, float middle, float radius, int half, float low, float high)
    {
        var round = Round(axis, middle, radius, half, low, high);
        if (round.Count < 3) return new Mesh();

        var start = loop.CornerAt(wall);
        var (along, outward) = loop.WallAt(wall);
        var positions = new List<Vector3>();
        var indices = new List<int>();
        int m = round.Count;

        foreach (var (s, whole) in sections)
            foreach (var (t, z) in round)
            {
                var at = start + along * s + outward * (whole ? t : MathF.Max(t, -WallBlocks.SinkMm));
                positions.Add(new Vector3(at.X, at.Y, z));
            }

        for (int r = 0; r + 1 < sections.Count; r++)
            for (int k = 0; k < m; k++)
            {
                int a = r * m + k, b = r * m + (k + 1) % m, c = (r + 1) * m + (k + 1) % m, d = (r + 1) * m + k;
                indices.AddRange([a, b, c, a, c, d]);
            }

        // Each end a fan from its front-most point, which is never cut and never on a straight run.
        int end = (sections.Count - 1) * m;
        for (int k = 1; k + 1 < m; k++)
        {
            indices.AddRange([0, k + 1, k]);
            indices.AddRange([end, end + k, end + k + 1]);
        }

        WallBlocks.Outward(positions, indices, 0);
        return new Mesh(positions, indices);
    }

    /// <summary>
    /// A log's round as (out from the wall, up) points, starting at its front. A half log stops a
    /// hair short of the foot or the top of the wall, so its flat face is not in the plane of the
    /// part's own.
    /// </summary>
    private static List<(float T, float Z)> Round(float axis, float middle, float radius, int half, float low, float high)
    {
        var points = new List<(float, float)>();

        if (half == 0)
        {
            for (int k = 0; k < Sides; k++)
            {
                float a = MathF.Tau * k / Sides;
                points.Add((axis + radius * MathF.Cos(a), middle + radius * MathF.Sin(a)));
            }

            return points;
        }

        // From the front round over the top (or under the bottom) to the back, the flat face the
        // chord back to the front.
        float cut = half > 0 ? low : high;
        float off = Math.Clamp(MathF.Abs(cut - middle) / radius, 0f, 0.95f);
        float from = MathF.Asin(off), to = MathF.PI - from;
        int count = Sides / 2;

        for (int k = 0; k <= count; k++)
        {
            float a = from + (to - from) * k / count;
            points.Add((axis + radius * MathF.Cos(a), middle + half * radius * MathF.Sin(a)));
        }

        return points;
    }
}
