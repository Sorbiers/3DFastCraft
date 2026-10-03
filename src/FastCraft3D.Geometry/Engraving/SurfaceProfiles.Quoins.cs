using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

public static partial class SurfaceProfiles
{
    /// <summary>
    /// Cornerstones - quoins - set into a stone field laid round walls: at every outside corner a
    /// stack of big stones, each one turning the corner, long on one face and short on the other,
    /// swapping faces from one to the next the way a corner brick does.
    ///
    /// They are part of the field rather than blocks laid on it. The field is laid on the walls
    /// unrolled, so a stone spanning the place where the strip folds is one stone round the corner,
    /// with the corner as sharp as the wall's; and the stones of the wall beside it come down into
    /// a joint along its edge, as stones laid against a quoin do, rather than being cut off by it.
    /// Only outside corners have them, as on a building: an inside corner is two walls meeting.
    /// </summary>
    public sealed class Quoins : IRelief
    {
        private const int Salt = 0x5155;

        /// <summary>A quoin is this many times as tall as a stone of the wall, near enough.</summary>
        private const float Taller = 1.3f;

        /// <summary>The long face and the short face of a quoin, as multiples of its height.</summary>
        private const float LongFace = 2f, ShortFace = 1.25f;

        /// <summary>
        /// The most of a wall a quoin takes, at either end of it, so the two on a short wall do not
        /// meet in the middle.
        /// </summary>
        private const float MostOfAWall = 0.45f;

        private readonly IRelief wall;
        private readonly List<(float X, float Before, float After, int Seed)> corners = [];
        private readonly float half, joint, depth, round, around;
        private readonly bool pillowed;
        private readonly Repeat.Pattern rough;

        /// <param name="wall">The stone field the quoins are set into.</param>
        /// <param name="kind">Rubble, whose quoins are rounded boulders; or castle walling, squared and rock-faced.</param>
        /// <param name="stoneMm">The field's stone size, which the quoins are sized from.</param>
        /// <param name="aspect">How many times the stone is wider than tall.</param>
        public Quoins(
            IRelief wall, WallRun run, TextureKind kind, float stoneMm, float aspect,
            float jointMm, float depthMm, float nozzleMm)
        {
            this.wall = wall;
            joint = jointMm;
            depth = depthMm;
            pillowed = kind == TextureKind.Rubble;
            around = run.Closed ? run.Length : 0f;

            // Whole quoins from the foot of the wall to its top.
            float height = run.High - run.Low;
            half = height / 2f;
            float stone = MathF.Max(stoneMm / MathF.Max(aspect, 0.2f), 2f * jointMm + TextureOptions.LeastPadMm);
            Courses = Math.Max(1, (int)MathF.Round(height / (Taller * stone)));
            CourseMm = height / Courses;
            LongMm = LongFace * CourseMm;
            ShortMm = ShortFace * CourseMm;

            float step = StoneStep(jointMm, CourseMm, nozzleMm);
            round = MathF.Max(MathF.Min(depthMm * (pillowed ? 2f : 1.2f), CourseMm * (pillowed ? 0.3f : 0.18f)), step * 1.5f);
            rough = new Repeat.Pattern(CourseMm * 0.35f, CourseMm * 0.35f, around, 0f, Salt);

            var loop = run.Loop;
            int n = loop.Count;
            float x = -run.Length / 2f;

            // Each corner the run touches, its own two ends included when it has ends.
            for (int k = 0; k <= run.Count; k++)
            {
                if (run.Closed && k == run.Count) break;

                int corner = (run.First + k) % n;
                if (k > 0) x += loop.LengthOf((corner + n - 1) % n);

                var (before, _) = loop.WallAt((corner + n - 1) % n);
                var (after, _) = loop.WallAt(corner);
                if (before.X * after.Y - before.Y * after.X <= 1e-3f) continue;

                float at = run.Closed ? Wrap(loop.ArcOfCorner(corner) - run.MiddleArc, run.Length) : x;
                float back = run.Closed || k > 0 ? MostOfAWall * loop.LengthOf((corner + n - 1) % n) : 0f;
                float on = run.Closed || k < run.Count ? MostOfAWall * loop.LengthOf(corner) : 0f;
                corners.Add((at, back, on, corner));
            }
        }

        /// <summary>How many quoins are stacked up each corner, and how tall each is with its joint.</summary>
        public int Courses { get; }

        public float CourseMm { get; }

        /// <summary>The length of a quoin's long face and of its short one, from the corner.</summary>
        public float LongMm { get; }

        public float ShortMm { get; }

        /// <summary>How many corners have them.</summary>
        public int Corners => corners.Count;

        /// <summary>Where each of those corners is across the strip.</summary>
        public IEnumerable<float> CornerXs => corners.Select(c => c.X);

        /// <summary>
        /// Which way round quoin <paramref name="course"/> of the corner at <paramref name="x"/> is
        /// laid: true when its long face is on the wall before the corner.
        /// </summary>
        public bool LongBefore(float x, int course) =>
            corners.Where(c => MathF.Abs(c.X - x) < 1e-3f).Select(c => LongFirst(course, c.Seed)).First();

        private static bool LongFirst(int course, int seed) => ((course + seed) & 1) == 0;

        private static float Wrap(float x, float round) => x - round * MathF.Floor(x / round + 0.5f);

        public float[] Across(float acrossMm) => wall.Across(acrossMm);

        public float[] Up(float upMm) => wall.Up(upMm);

        public float Height(Vector2 at)
        {
            int row = (int)MathF.Floor((at.Y + half) / CourseMm);
            float outside = float.MaxValue;

            foreach (var corner in corners)
            {
                // Round the whole way, a corner near one end of the layout is also just past the other.
                for (int turn = around > 0f ? -1 : 0; turn <= (around > 0f ? 1 : 0); turn++)
                {
                    float x = corner.X + turn * around;
                    if (MathF.Abs(at.X - x) > LongMm + round + joint * 2f) continue;

                    for (int j = Math.Max(row - 1, 0); j <= Math.Min(row + 1, Courses - 1); j++)
                    {
                        bool longFirst = LongFirst(j, corner.Seed);
                        float back = MathF.Min(longFirst ? LongMm : ShortMm, corner.Before);
                        float on = MathF.Min(longFirst ? ShortMm : LongMm, corner.After);

                        float x0 = x - back, x1 = x + on;
                        float y0 = -half + j * CourseMm + joint / 2f, y1 = -half + (j + 1) * CourseMm - joint / 2f;

                        if (at.X >= x0 && at.X <= x1 && at.Y >= y0 && at.Y <= y1)
                            return Quoin(at, x, x0, x1, y0, y1, back > 0f, on > 0f, j, corner.Seed);

                        float dx = MathF.Max(MathF.Max(x0 - at.X, at.X - x1), 0f);
                        float dy = MathF.Max(MathF.Max(y0 - at.Y, at.Y - y1), 0f);
                        outside = MathF.Min(outside, MathF.Sqrt(dx * dx + dy * dy));
                    }
                }
            }

            // The wall's own stones come down into a joint along a quoin's edge, as stones laid
            // against one do, rather than being cut off square by it.
            float field = wall.Height(at);
            return outside == float.MaxValue ? field : MathF.Min(field, depth * Shoulder(outside - joint, round));
        }

        /// <summary>
        /// One quoin's face. A side with no wall beyond it - where the walls picked stop at an
        /// outside corner - is the corner itself, so the stone runs full height to it instead of
        /// rounding down, as a quoin seen from one face does.
        /// </summary>
        private float Quoin(
            Vector2 at, float corner, float x0, float x1, float y0, float y1,
            bool walled0, bool walled1, int course, int seed)
        {
            float cornerRound = MathF.Min(x1 - x0, y1 - y0) * 0.15f;
            float acrossIn = SoftMin(walled0 ? at.X - x0 : float.MaxValue, walled1 ? x1 - at.X : float.MaxValue, cornerRound);
            float inside = SoftMin(acrossIn, SoftMin(at.Y - y0, y1 - at.Y, cornerRound), cornerRound);

            float u = 2f * (at.X - (x0 + x1) / 2f) / MathF.Max(x1 - x0, 1e-3f);
            float v = 2f * (at.Y - (y0 + y1) / 2f) / MathF.Max(y1 - y0, 1e-3f);
            float dome = 1f - (pillowed ? 0.16f : 0.05f) * MathF.Min(u * u + v * v, 1.5f);
            float top = depth * ((pillowed ? 0.86f : 0.94f) + 0.06f * Repeat.Random(seed, course, Salt + 1));
            float face = top * dome + rough.Rough(at.X, at.Y) * (pillowed ? 0.18f : 0.2f) * depth;

            float height = Stone(inside, round, face, depth);

            // A boulder's corner is worn round, where a squared stone's stays sharp.
            if (pillowed && walled0 && walled1)
                height *= 1f - 0.3f * (1f - Shoulder(MathF.Abs(at.X - corner), depth * 1.5f));

            return height;
        }
    }
}
