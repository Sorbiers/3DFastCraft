using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

public static partial class SurfaceProfiles
{
    /// <summary>
    /// How fine a stone field is sampled: fine enough that every joint gets two samples across it,
    /// which is what keeps a thin joint from breaking up into dashes, and the smallest stone eight,
    /// and never finer than most of a nozzle - finer than that the printer lays the same line
    /// either way. Twelve to the smallest stone was tried first and came to half a million
    /// triangles round a 60 mm barrel of coursed stone walling, for nothing the eye could find.
    /// </summary>
    private static float StoneStep(float jointMm, float smallestMm, float nozzleMm) =>
        MathF.Max(nozzleMm * 0.75f, MathF.Min(jointMm * 0.5f, smallestMm / 8f));

    /// <summary>
    /// How far up its shoulder a stone is, <paramref name="insideMm"/> in from its foot: steep at
    /// the joint and rounding over onto the top, a quarter of a sine.
    ///
    /// Steep at the foot is what gives a joint a crisp edge and a shadow, which is what a wall is
    /// read by. A quarter circle - upright at the foot - was tried first and stepped: the field is
    /// sampled on a grid, an upright wall falls between two samples, and every joint that ran
    /// across the grid came out as a staircase. The sine leans enough to spread its foot over a
    /// sample or two and is still steep enough to throw the shadow.
    /// </summary>
    private static float Shoulder(float insideMm, float roundMm)
    {
        if (insideMm <= 0f) return 0f;
        if (insideMm >= roundMm) return 1f;

        return MathF.Sin(insideMm / roundMm * MathF.PI / 2f);
    }

    /// <summary>
    /// The smaller of two distances with the corner between them rounded off, so a stone has
    /// rounded corners rather than the sharp ones an inset polygon has. Where three stones meet
    /// it leaves a little pool of mortar, which is what real ones do.
    /// </summary>
    private static float SoftMin(float a, float b, float roundMm)
    {
        if (roundMm <= 0f) return MathF.Min(a, b);

        float h = MathF.Max(roundMm - MathF.Abs(a - b), 0f) / roundMm;
        return MathF.Min(a, b) - h * h * roundMm * 0.25f;
    }

    /// <summary>
    /// A stone's height at a point: its shoulder times its own top, which carries the dome, the
    /// lean and the rough face. Held between a third of the depth and the depth, so roughness
    /// never pits a stone down to its joint and nothing stands taller than was asked.
    /// </summary>
    private static float Stone(float insideMm, float roundMm, float top, float depthMm)
    {
        float rise = Shoulder(insideMm, roundMm);
        return rise <= 0f ? 0f : rise * Math.Clamp(top, depthMm / 3f, depthMm);
    }

    /// <summary>
    /// Rubble walling: irregular stones of every size, rounded and rough, set in mortar.
    ///
    /// Each stone is a cell of a power diagram - the Voronoi diagram with a weight on every site,
    /// which moves each boundary towards the lighter neighbour. The weights are what give a wall
    /// its big stones and the small ones packed between them: lightly weighted, as it was first,
    /// the cells came out a field of nearly equal polygons, like crazy paving. The diagram is bent
    /// by a slow noise before it is read, so the joints wander instead of running ruler straight
    /// from corner to corner.
    /// </summary>
    /// <param name="stoneMm">How wide a stone is on average.</param>
    /// <param name="aspect">How many times wider than tall.</param>
    /// <param name="aroundMm">The way round a barrel it has to repeat on, or nought on a flat face.</param>
    public sealed class Rubble : IRelief
    {
        private const int Salt = 0x5275;

        /// <summary>How far a site may stray from the middle of its cell, as a share of the cell.</summary>
        private const float Jitter = 0.72f;

        /// <summary>
        /// The spread of the weights, as a share of the smaller side of a cell squared. At a
        /// little over half this the stones were still nearly all one size.
        /// </summary>
        private const float Weights = 0.75f;

        private readonly float wide, tall, smallest, depth, joint, step, round, corner, bend;
        private readonly int columns;
        private readonly Repeat.Pattern bendX, bendY, rough;

        public Rubble(float stoneMm, float aspect, float depthMm, float jointMm, float nozzleMm, float aroundMm = 0f)
        {
            // Never smaller than two joints and a pad between them: a stone narrower than that is
            // all shoulder and no top, and a wall of them reads as a field of mortar.
            float least = 2f * jointMm + TextureOptions.LeastPadMm;
            (columns, wide) = Repeat.Fit(aroundMm, MathF.Max(stoneMm, least));
            tall = MathF.Max(stoneMm / MathF.Max(aspect, 0.2f), least);
            smallest = MathF.Min(wide, tall);

            depth = depthMm;
            joint = jointMm;
            step = StoneStep(jointMm, smallest, nozzleMm);
            round = MathF.Max(MathF.Min(depthMm * 1.2f, smallest * 0.2f), step * 1.5f);
            corner = smallest * 0.18f;
            bend = smallest * 0.05f;

            bendX = new Repeat.Pattern(smallest * 1.7f, smallest * 1.7f, aroundMm, 0f, Salt + 1);
            bendY = new Repeat.Pattern(smallest * 1.7f, smallest * 1.7f, aroundMm, 0f, Salt + 2);
            rough = new Repeat.Pattern(smallest * 0.45f, smallest * 0.45f, aroundMm, 0f, Salt + 3);
        }

        public float[] Across(float acrossMm) => ReliefField.Evenly(acrossMm, step);

        public float[] Up(float upMm) => ReliefField.Evenly(upMm, step);

        private Vector2 Site(int column, int row, out int key)
        {
            key = Repeat.Index(column, columns);
            float shift = (row & 1) * 0.5f;

            return new Vector2(
                (column + 0.5f + shift + Jitter * (Repeat.Random(key, row, Salt) - 0.5f)) * wide,
                (row + 0.5f + Jitter * (Repeat.Random(key, row, Salt + 4) - 0.5f)) * tall);
        }

        private float Weight(int key, int row) =>
            (Repeat.Random(key, row, Salt + 5) - 0.5f) * Weights * smallest * smallest;

        public float Height(Vector2 at)
        {
            var p = at + new Vector2(bendX.Signed(at.X, at.Y), bendY.Signed(at.X, at.Y)) * bend;

            int row0 = (int)MathF.Floor(p.Y / tall);
            Span<Vector2> sites = stackalloc Vector2[25];
            Span<float> power = stackalloc float[25];
            int count = 0, best = 0, bestKey = 0, bestRow = 0;
            float nearest = float.MaxValue;

            for (int row = row0 - 2; row <= row0 + 2; row++)
            {
                int column0 = (int)MathF.Floor(p.X / wide - (row & 1) * 0.5f);

                for (int column = column0 - 2; column <= column0 + 2; column++)
                {
                    var site = Site(column, row, out int key);
                    float d = Vector2.DistanceSquared(p, site) - Weight(key, row);
                    if (d < nearest) (nearest, best, bestKey, bestRow) = (d, count, key, row);

                    sites[count] = site;
                    power[count++] = d;
                }
            }

            // How far in from the edge of its own cell: the distance to the nearest of the lines
            // it shares with its neighbours, which in a power diagram are still straight.
            float edge = float.MaxValue;
            for (int k = 0; k < count; k++)
            {
                if (k == best) continue;

                float apart = Vector2.Distance(sites[k], sites[best]);
                if (apart < 1e-5f) continue;

                edge = SoftMin(edge, (power[k] - power[best]) / (2f * apart), corner);
            }

            float inside = edge - joint / 2f;
            if (inside <= 0f) return 0f;

            // Every stone its own height and its own lean, and domed a little towards its site.
            // Domed by the distance in from the edge instead, every stone grew a ridge down its
            // middle where the distances from its two long sides meet, and read as a cut gem.
            float top = depth * (0.72f + 0.28f * Repeat.Random(bestKey, bestRow, Salt + 6));
            var off = p - sites[best];
            var lean = new Vector2(
                Repeat.Signed(bestKey, bestRow, Salt + 7), Repeat.Signed(bestKey, bestRow, Salt + 8));
            float tilt = Vector2.Dot(off, lean) * (0.1f * depth / smallest);
            float dome = 1f - 0.12f * MathF.Min(off.LengthSquared() / (smallest * smallest * 0.36f), 1f);

            return Stone(inside, round, top * dome + tilt + rough.Rough(at.X, at.Y) * 0.16f * depth, depth);
        }
    }

    /// <summary>
    /// CoursedStone walling: squared stones in courses, every course its own height and every stone its
    /// own length, with now and then a stone split into two thin ones laid one on the other.
    ///
    /// Brick with the numbers varied, in other words, and that is all it takes to stop it reading
    /// as brick. The courses are found from a lattice with each line nudged, rather than by adding
    /// up heights from the bottom, so any point can be asked about on its own.
    /// </summary>
    /// <param name="stoneMm">How long a stone is on average.</param>
    /// <param name="aspect">How many times longer than a course is deep.</param>
    public sealed class CoursedStone : IRelief
    {
        private const int Salt = 0x4361;

        /// <summary>How far a course line may be nudged, as a share of a course.</summary>
        private const float CourseJitter = 0.25f;

        /// <summary>How far a joint may be nudged along its course, as a share of a stone.</summary>
        private const float StoneJitter = 0.3f;

        /// <summary>
        /// How far a line may be nudged on a lattice this far apart, so that two nudged towards
        /// each other still leave a stone a joint and a pad wide between them. Close to the joint,
        /// a full nudge left courses and stones narrower than their own joints - a wall that was
        /// nine tenths mortar.
        /// </summary>
        private float Nudge(float most, float apartMm) =>
            Math.Clamp((1f - (joint + TextureOptions.LeastPadMm) / apartMm) / 2f, 0f, most);

        /// <summary>How often a stone is two thin ones instead.</summary>
        private const float Splits = 0.35f;

        private readonly float length, course, courseJitter, depth, joint, step, round, bend, chip, around;
        private readonly Repeat.Pattern bendX, bendY, chipped, rough;

        public CoursedStone(float stoneMm, float aspect, float depthMm, float jointMm, float nozzleMm, float aroundMm = 0f)
        {
            length = MathF.Max(stoneMm, 2f * jointMm + TextureOptions.LeastPadMm);
            course = MathF.Max(stoneMm / MathF.Max(aspect, 0.2f), 2f * jointMm + TextureOptions.LeastPadMm);
            around = aroundMm;

            depth = depthMm;
            joint = jointMm;

            // The thinnest stone is half the shallowest course, when it is split.
            courseJitter = Nudge(CourseJitter, course);
            float thinnest = MathF.Max(course * (1f - 2f * courseJitter) / 2f, TextureOptions.LeastPadMm);
            step = StoneStep(jointMm, thinnest * 2f, nozzleMm);
            round = MathF.Max(MathF.Min(depthMm * 1.2f, thinnest * 0.7f), step * 1.5f);
            bend = course * 0.035f;
            chip = MathF.Min(course * 0.05f, jointMm * 0.25f);

            bendX = new Repeat.Pattern(course * 1.3f, course * 1.3f, aroundMm, 0f, Salt + 1);
            bendY = new Repeat.Pattern(course * 1.3f, course * 1.3f, aroundMm, 0f, Salt + 2);
            chipped = new Repeat.Pattern(course * 0.3f, course * 0.3f, aroundMm, 0f, Salt + 3);
            rough = new Repeat.Pattern(course * 0.35f, course * 0.35f, aroundMm, 0f, Salt + 4);
        }

        public float[] Across(float acrossMm) => ReliefField.Evenly(acrossMm, step);

        public float[] Up(float upMm) => ReliefField.Evenly(upMm, step);

        private float CourseLine(int row) => (row + courseJitter * Repeat.Signed(row, 0, Salt)) * course;

        public float Height(Vector2 at)
        {
            var p = at + new Vector2(bendX.Signed(at.X, at.Y), bendY.Signed(at.X, at.Y)) * bend;

            int row = (int)MathF.Floor(p.Y / course);
            if (p.Y < CourseLine(row)) row--;
            else if (p.Y >= CourseLine(row + 1)) row++;

            float bottom = CourseLine(row), top = CourseLine(row + 1);

            // Each course its own run of stones, fitted so a whole number go round a barrel.
            var (stones, run) = Repeat.Fit(around, length * (0.8f + 0.45f * Repeat.Random(row, 1, Salt)));
            float phase = Repeat.Random(row, 2, Salt) * run;
            float nudge = Nudge(StoneJitter, run);

            float Joint(int k) =>
                phase + (k + nudge * Repeat.Signed(Repeat.Index(k, stones), row, Salt + 5)) * run;

            int stone = (int)MathF.Floor((p.X - phase) / run);
            while (p.X < Joint(stone)) stone--;
            while (p.X >= Joint(stone + 1)) stone++;

            float left = Joint(stone), right = Joint(stone + 1);
            int key = Repeat.Index(stone, stones), part = 0;

            // Only a deep course is split: two halves of a shallow one are a pair of strips.
            float least = 2f * (joint + TextureOptions.LeastPadMm);
            if (Repeat.Random(key, row, Salt + 6) < Splits && top - bottom >= MathF.Max(least, course))
            {
                float split = bottom + (top - bottom) * (0.42f + 0.16f * Repeat.Random(key, row, Salt + 7));
                part = p.Y >= split ? 1 : 2;
                if (part == 1) bottom = split;
                else top = split;

                // And one of the two halves sometimes two stones end to end - only a long one, or
                // the halves come out as chips.
                if (Repeat.Random(key, row, Salt + 8 + part) < 0.5f
                    && right - left >= MathF.Max(1.5f * least, 2.5f * (top - bottom)))
                {
                    float cut = left + (right - left) * (0.35f + 0.3f * Repeat.Random(key, row, Salt + 10 + part));
                    part += p.X >= cut ? 2 : 4;
                    if (p.X >= cut) left = cut;
                    else right = cut;
                }
            }

            float half = joint / 2f;
            float shortest = MathF.Min(right - left, top - bottom) - joint;
            float corner = shortest * (0.18f + 0.22f * Repeat.Random(key, row, Salt + 15 + part));

            float inside = SoftMin(
                SoftMin(p.X - left - half, right - half - p.X, corner),
                SoftMin(p.Y - bottom - half, top - half - p.Y, corner), corner);

            // The edges knocked about a little, as a rock-faced stone's are, but never by more
            // than a quarter of the joint, so a joint stays open.
            inside += chipped.Signed(at.X, at.Y) * chip;
            if (inside <= 0f) return 0f;

            // Domed from the middle of the stone rather than in from its edge, for the same reason
            // as the rubble: in from the edge leaves a ridge down a long stone's middle.
            int id = key * 8 + part;
            float height = depth * (0.74f + 0.26f * Repeat.Random(id, row, Salt + 20));
            float acrossStone = 2f * (p.X - (left + right) / 2f) / MathF.Max(right - left, 1e-3f);
            float upStone = 2f * (p.Y - (bottom + top) / 2f) / MathF.Max(top - bottom, 1e-3f);
            float lean = Repeat.Signed(id, row, Salt + 21) * 0.04f * depth * acrossStone;
            float dome = 1f - 0.12f * MathF.Min(acrossStone * acrossStone + upStone * upStone, 1.5f);

            return Stone(inside, round, height * dome + lean + rough.Rough(at.X, at.Y) * 0.2f * depth, depth);
        }
    }
}
