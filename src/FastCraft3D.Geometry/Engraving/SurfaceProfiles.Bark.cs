using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

public static partial class SurfaceProfiles
{
    /// <summary>
    /// Tree bark: long plates split by deep fissures that wander and fork, the plates themselves
    /// streaked along their length and pitted.
    ///
    /// The plates are the cells of a Voronoi diagram drawn on a lattice stretched along the trunk,
    /// so each one is a long lens, and the fissures are the cell edges. Measured in the stretched
    /// lattice the fissures came out as wide again at the ends of a plate as they were along its
    /// sides, a ladder of broad notches across the trunk; each edge's distance is taken back into
    /// millimetres, so a fissure is as wide whichever way it runs.
    /// </summary>
    /// <param name="plateMm">How wide a plate is, across the trunk.</param>
    /// <param name="lengthMm">How long, along it.</param>
    /// <param name="fissureMm">How wide the fissures are at the bottom.</param>
    /// <param name="turned">Whether the trunk lies across the face rather than standing up it.</param>
    public sealed class Bark : IRelief
    {
        private const int Salt = 0x4261;

        private readonly float wide, longMm, depth, fissure, step, slope, meander, ragged, pitWide, pitLong;
        private readonly int columns, rows, pitColumns, pitRows;
        private readonly bool turned;
        private readonly Repeat.Pattern meanderU, meanderV, rag, streaks;

        public Bark(
            float plateMm, float lengthMm, float depthMm, float fissureMm, float nozzleMm,
            bool turned = false, float aroundMm = 0f)
        {
            this.turned = turned;

            // Across the plates and along them, whichever of those is the way round.
            float aroundU = turned ? 0f : aroundMm, aroundV = turned ? aroundMm : 0f;
            // A plate narrower than two fissures and a pad between them is all fissure.
            (columns, wide) = Repeat.Fit(aroundU, MathF.Max(plateMm, 2f * fissureMm + TextureOptions.LeastPadMm), even: true);
            (rows, longMm) = Repeat.Fit(aroundV, MathF.Max(lengthMm, wide));

            depth = depthMm;
            fissure = fissureMm;
            step = MathF.Max(nozzleMm * 0.75f, MathF.Min(fissureMm * 0.5f, wide / 10f));
            slope = MathF.Max(MathF.Min(wide * 0.22f, depthMm * 1.5f), step * 2f);
            meander = wide * 0.55f;
            ragged = MathF.Min(wide * 0.12f, fissureMm * 0.6f);

            meanderU = new Repeat.Pattern(wide * 2f, longMm * 0.8f, aroundU, aroundV, Salt + 1);
            meanderV = new Repeat.Pattern(wide * 3f, longMm * 1.2f, aroundU, aroundV, Salt + 2);
            rag = new Repeat.Pattern(wide * 0.5f, wide * 0.9f, aroundU, aroundV, Salt + 3);
            streaks = new Repeat.Pattern(wide * 0.25f, wide * 1.6f, aroundU, aroundV, Salt + 4);
            (pitColumns, pitWide) = Repeat.Fit(aroundU, wide * 0.45f);
            (pitRows, pitLong) = Repeat.Fit(aroundV, wide * 0.6f);
        }

        /// <summary>
        /// Coarser along the plates than across them, since along is the way nothing much
        /// happens: half again as far apart costs a third fewer triangles and loses nothing a
        /// fissure needs.
        /// </summary>
        public float[] Across(float acrossMm) => ReliefField.Evenly(acrossMm, turned ? step * 1.5f : step);

        public float[] Up(float upMm) => ReliefField.Evenly(upMm, turned ? step : step * 1.5f);

        private Vector2 Site(int column, int row, out int keyU, out int keyV)
        {
            keyU = Repeat.Index(column, columns);
            keyV = Repeat.Index(row, rows);
            float shift = (column & 1) * 0.5f;

            return new Vector2(
                column + 0.5f + 0.8f * (Repeat.Random(keyU, keyV, Salt) - 0.5f),
                row + 0.5f + shift + 0.8f * (Repeat.Random(keyU, keyV, Salt + 5) - 0.5f));
        }

        public float Height(Vector2 at)
        {
            float u = turned ? at.Y : at.X, v = turned ? at.X : at.Y;

            // Bent first, so the fissures wander instead of running dead straight.
            float bentU = u + meanderU.Rough(u, v) * meander;
            float bentV = v + meanderV.Signed(u, v) * longMm * 0.15f;
            var q = new Vector2(bentU / wide, bentV / longMm);

            int column0 = (int)MathF.Floor(q.X);
            Span<Vector2> sites = stackalloc Vector2[25];
            int count = 0, best = -1, bestU = 0, bestV = 0;
            float nearest = float.MaxValue;

            for (int column = column0 - 2; column <= column0 + 2; column++)
            {
                int row0 = (int)MathF.Floor(q.Y - (column & 1) * 0.5f);
                for (int row = row0 - 2; row <= row0 + 2; row++)
                {
                    var site = Site(column, row, out int keyU, out int keyV);
                    float d = Vector2.DistanceSquared(q, site);
                    if (d < nearest) (nearest, best, bestU, bestV) = (d, count, keyU, keyV);
                    sites[count++] = site;
                }
            }

            float edge = float.MaxValue;
            for (int k = 0; k < count; k++)
            {
                if (k == best) continue;

                var apart = sites[k] - sites[best];
                float length = apart.Length();
                if (length < 1e-6f) continue;

                // Distance to the shared edge in the stretched lattice, and then in millimetres:
                // the edge's normal, carried back out of the stretch, says how much longer a step
                // across it is on the face.
                var normal = apart / length;
                float there = (Vector2.DistanceSquared(q, sites[k]) - nearest) / (2f * length);
                float scale = new Vector2(normal.X / wide, normal.Y / longMm).Length();
                edge = MathF.Min(edge, there / scale);
            }

            // Ragged, so a plate's edge is torn rather than cut.
            float inside = edge - fissure / 2f + rag.Signed(u, v) * ragged;
            if (inside <= 0f) return 0f;

            // Rounded over at the edge and level on top. Rounded right across, each plate grew a
            // crease down its middle where the distances from its two sides meet, and the bark
            // read as a heap of crystals.
            float rise = Shoulder(inside, slope);

            float top = depth * (0.8f + 0.2f * Repeat.Random(bestU, bestV, Salt + 6));
            float streak = streaks.Rough(u, v) * 0.16f * depth;
            return rise * Math.Clamp(top + streak - Pits(u, v), depth / 4f, depth);
        }

        /// <summary>
        /// How far the plate is pitted here: round dimples on a lattice of their own, about one in
        /// every other cell. Thresholded noise was the first thing tried and pitted the bark with
        /// little squares, the lattice the noise is drawn on showing through every one of them.
        /// </summary>
        private float Pits(float u, float v)
        {
            int column0 = (int)MathF.Floor(u / pitWide), row0 = (int)MathF.Floor(v / pitLong);
            // Two and a half samples across at the least, or a pit is one sunk corner of the mesh
            // and reads as a rivet.
            float least = MathF.Max(wide * 0.12f, step * 2.5f);
            float sunk = 0f;

            for (int column = column0 - 1; column <= column0 + 1; column++)
                for (int row = row0 - 1; row <= row0 + 1; row++)
                {
                    int ku = Repeat.Index(column, pitColumns), kv = Repeat.Index(row, pitRows);
                    if (Repeat.Random(ku, kv, Salt + 7) > 0.4f) continue;

                    float cu = (column + 0.5f + 0.6f * Repeat.Signed(ku, kv, Salt + 8)) * pitWide;
                    float cv = (row + 0.5f + 0.6f * Repeat.Signed(ku, kv, Salt + 9)) * pitLong;
                    float radius = least * (1f + 0.8f * Repeat.Random(ku, kv, Salt + 10));

                    float d = ((u - cu) * (u - cu) + (v - cv) * (v - cv)) / (radius * radius);
                    if (d < 1f) sunk = MathF.Max(sunk, (1f - d) * (1f - d));
                }

            return sunk * 0.35f * depth;
        }
    }
}
