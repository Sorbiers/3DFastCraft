using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

public static partial class SurfaceProfiles
{
    /// <summary>
    /// Wood grain: rounded bands between fine grooves, flowing along the board and swirling round
    /// knots, with the rings of the knot inside the swirl.
    ///
    /// The grooves are the contour lines of one smooth field - the distance across the board, bent
    /// by a slow sway and lifted into a hump at every knot. Contours of a smooth field never cross,
    /// so the grain can bunch and swirl as hard as it likes and never tangle; and where the hump
    /// stands taller than the lines are apart, the contours close on themselves, which is what a
    /// knot's rings are. Nothing has to be drawn round a knot: it falls out of the same field.
    ///
    /// The groove is measured in millimetres from its contour, by way of how steep the field is
    /// there, rather than as a share of the gap between two contours. Taken as a share, the
    /// grooves fattened where the lines spread round a knot and closed up where they bunched,
    /// which is the wrong way round from timber and lost the rings altogether.
    /// </summary>
    /// <param name="spacingMm">How far apart the grain lines are.</param>
    /// <param name="grooveMm">How wide a groove is.</param>
    /// <param name="turned">Whether the grain runs up the face rather than along it.</param>
    public sealed class Grain : IRelief
    {
        private const int Salt = 0x4772;

        /// <summary>How likely a stretch of board is to have a knot in it.</summary>
        private const float Knots = 0.7f;

        private readonly float spacing, depth, groove, step, knotAlong, knotAcross;
        private readonly int lines, knotColumns, knotRows;
        private readonly bool turned;
        private readonly Repeat.Pattern sway, ripple, spread, streaks;

        public Grain(
            float spacingMm, float depthMm, float grooveMm, float nozzleMm,
            bool turned = false, float aroundMm = 0f)
        {
            this.turned = turned;

            // Along the grain and across it, whichever of those is the way round.
            float aroundA = turned ? 0f : aroundMm, aroundC = turned ? aroundMm : 0f;

            // Across the way round, the lines themselves are what has to come out whole.
            (lines, spacing) = Repeat.Fit(aroundC, spacingMm);

            depth = depthMm;
            groove = grooveMm;
            step = MathF.Max(nozzleMm * 0.75f, MathF.Min(grooveMm * 0.75f, spacing / 6f));

            (knotColumns, knotAlong) = Repeat.Fit(aroundA, spacing * 26f);
            (knotRows, knotAcross) = Repeat.Fit(aroundC, spacing * 14f);

            sway = new Repeat.Pattern(spacing * 22f, spacing * 9f, aroundA, aroundC, Salt + 1);
            ripple = new Repeat.Pattern(spacing * 7f, spacing * 3f, aroundA, aroundC, Salt + 2);
            spread = new Repeat.Pattern(spacing * 14f, spacing * 2.2f, aroundA, aroundC, Salt + 11);
            streaks = new Repeat.Pattern(spacing * 5f, spacing * 0.7f, aroundA, aroundC, Salt + 3);
        }

        /// <summary>
        /// Half as far apart again along the grain as across it: along is the way the field barely
        /// changes. Twice as far was tried and left a cliff down each side of every knot, where
        /// the lines turn to run across the grain for a moment and fell between the samples.
        /// </summary>
        public float[] Across(float acrossMm) => ReliefField.Evenly(acrossMm, turned ? step : step * 1.5f);

        public float[] Up(float upMm) => ReliefField.Evenly(upMm, turned ? step * 1.5f : step);

        /// <summary>
        /// The field the grain lines are contours of: how far across the board, bent. Its slope
        /// across the grain is held well under one for the sway, so the contours lean and bunch
        /// but only a knot ever folds them back.
        /// </summary>
        private float Field(float a, float c)
        {
            // The third term is what makes the bands uneven - some wide, some narrow - as a tree's
            // years are. Without it the grain was as regular as corduroy.
            float bent = c + spacing * (1f * sway.Signed(a, c) + 0.2f * ripple.Signed(a, c)
                                        + 0.45f * spread.Signed(a, c));

            int column0 = (int)MathF.Floor(a / knotAlong);
            int row0 = (int)MathF.Floor(c / knotAcross);

            for (int column = column0 - 1; column <= column0 + 1; column++)
                for (int row = row0 - 1; row <= row0 + 1; row++)
                {
                    int ka = Repeat.Index(column, knotColumns), kc = Repeat.Index(row, knotRows);
                    if (Repeat.Random(ka, kc, Salt) > Knots) continue;

                    float along = (column + 0.5f + 0.5f * Repeat.Signed(ka, kc, Salt + 4)) * knotAlong;
                    float across = (row + 0.5f + 0.5f * Repeat.Signed(ka, kc, Salt + 5)) * knotAcross;

                    float reachA = spacing * (3f + 2.5f * Repeat.Random(ka, kc, Salt + 6));
                    float reachC = spacing * (1f + 0.6f * Repeat.Random(ka, kc, Salt + 7));
                    float da = (a - along) / reachA, dc = (c - across) / reachC;

                    float spread = da * da + dc * dc;
                    if (spread > 12f) continue;

                    // Tall enough to fold the lines back into rings, and either way up.
                    float hump = spacing * (2.4f + 1.8f * Repeat.Random(ka, kc, Salt + 8));
                    if (Repeat.Random(ka, kc, Salt + 9) < 0.5f) hump = -hump;

                    bent += hump * MathF.Exp(-spread);
                }

            return bent;
        }

        public float Height(Vector2 at)
        {
            float a = turned ? at.Y : at.X, c = turned ? at.X : at.Y;

            float here = Field(a, c);
            float h = spacing * 0.05f;
            float slopeA = (Field(a + h, c) - Field(a - h, c)) / (2f * h);
            float slopeC = (Field(a, c + h) - Field(a, c - h)) / (2f * h);
            float steep = MathF.Max(MathF.Sqrt(slopeA * slopeA + slopeC * slopeC), 1e-3f);

            float band = MathF.Floor(here / spacing);
            float off = MathF.Min(here - band * spacing, (band + 1f) * spacing - here);

            // In millimetres from the nearest contour, and how far apart the contours are here.
            float away = off / steep;
            float apart = spacing / steep;

            // Rounded right across, a groove at each side: a band is a rope, as the grain on a
            // pressed board is. Narrowed where the lines bunch, so a groove never eats its band.
            float reach = MathF.Min(groove * 1.7f, apart * 0.45f);
            float x = MathF.Min(away / MathF.Max(reach, 1e-4f), 1f);
            float rise = 1f - (1f - x) * (1f - x);

            int key = Repeat.Index((int)band, lines);
            float top = depth * (0.86f + 0.14f * Repeat.Random(key, 0, Salt + 10));
            // Coarse enough to land on the samples. A third of a line apart, as fine as real
            // streaks are, it fell between them and came back as a moire of hairlines.
            float streak = streaks.Signed(a, c) * 0.05f * depth;

            return rise * Math.Clamp(top + streak, depth / 2f, depth);
        }
    }
}
