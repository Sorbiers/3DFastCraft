using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// The three textures that have a shape rather than an outline: lap siding, roof tiles and sawn
/// boarding.
///
/// Each is a course of something repeated up the face, and each says where it changes direction so
/// that <see cref="ReliefField"/> can put its samples there. A ramp is two lines however long it
/// is; a step is two lines a hair apart, which is what makes it sharp; a grain has no lines of its
/// own and has to be sampled evenly, which is why it is the only one of the three that costs
/// anything.
/// </summary>
public static class SurfaceProfiles
{
    /// <summary>
    /// How far apart the two rows of a step are put. Well under a printed layer, so the wall it
    /// makes is upright as far as anything downstream can tell.
    /// </summary>
    private const float StepMm = 2e-3f;

    /// <summary>
    /// Vinyl siding: strips running the whole way across, each sloping out as it goes down and
    /// stepping back at its bottom edge.
    ///
    /// The strip is the full width of the face - there are no ends to stagger, which is exactly
    /// what makes siding siding rather than boarding.
    /// </summary>
    public sealed class Siding(float courseMm, float depthMm) : IRelief
    {
        public float[] Across(float acrossMm) => [-acrossMm / 2f, acrossMm / 2f];

        public float[] Up(float upMm)
        {
            var at = new List<float>();

            // Down the face from the top, so the course at the eaves is a whole one and whatever is
            // left over is cut off at the bottom where a wall meets the ground.
            for (float head = upMm / 2f; head > -upMm / 2f - courseMm; head -= courseMm)
            {
                at.Add(head);
                at.Add(head - StepMm);
                at.Add(head - courseMm + StepMm);
            }

            return [.. at];
        }

        public float Height(Vector2 at)
        {
            float down = (courseMm / 2f - at.Y) % courseMm;
            if (down < 0) down += courseMm;

            // Flush at the head of the course and standing furthest out at its foot.
            return depthMm * (down / courseMm);
        }
    }

    /// <summary>
    /// Roof tiles: flat rectangular tiles, each sloping out from head to tail, laid in courses that
    /// lap the course below and staggered by half a tile.
    ///
    /// No round tail and no texture on the tile - what reads as a roof at this size is the lap and
    /// the slope, and a tile is a rectangle with a joint either side of it.
    /// </summary>
    public sealed class Pantile(float tileMm, float courseMm, float depthMm, float jointMm) : IRelief
    {
        public float[] Across(float acrossMm)
        {
            var at = new List<float>();
            float half = jointMm / 2f;

            // Joints for both parities of course, since alternate courses are staggered by half a
            // tile and one list of samples has to serve them both.
            for (float shift = 0; shift < tileMm; shift += tileMm / 2f)
                for (float u = -acrossMm / 2f + shift; u <= acrossMm / 2f + tileMm; u += tileMm)
                {
                    at.Add(u - half);
                    at.Add(u - half + StepMm);
                    at.Add(u + half - StepMm);
                    at.Add(u + half);
                }

            return [.. at];
        }

        public float[] Up(float upMm)
        {
            var at = new List<float>();

            for (float head = upMm / 2f; head > -upMm / 2f - courseMm; head -= courseMm)
            {
                at.Add(head);
                at.Add(head - StepMm);
                at.Add(head - courseMm + StepMm);
            }

            return [.. at];
        }

        public float Height(Vector2 at)
        {
            float down = (courseMm / 2f - at.Y) % courseMm;
            if (down < 0) down += courseMm;

            float slope = depthMm * (down / courseMm);

            // Alternate courses are set over by half a tile, which is what stops the joints running
            // in a line down the roof.
            int course = (int)MathF.Floor((courseMm / 2f - at.Y) / courseMm);
            float shift = ((course % 2) + 2) % 2 == 1 ? tileMm / 2f : 0f;

            float along = (at.X - shift) % tileMm;
            if (along < 0) along += tileMm;

            // The joint between one tile and the next, cut to the depth of the lap so that it reads
            // as a gap right through the course rather than as a scratch on it.
            bool inJoint = along < jointMm / 2f || along > tileMm - jointMm / 2f;

            return inJoint ? 0f : slope;
        }
    }

    /// <summary>
    /// Sawn boarding: flat rectangular boards with a joint between them and grain worked into the
    /// face of each one.
    ///
    /// The grain is the only part of any of these that costs samples, and it is held to what a
    /// common nozzle will lay: a quarter of a millimetre of relief in ridges no finer than a
    /// nozzle is wide. Finer than that and a printer renders it as a flat board with a rough
    /// finish, which is a great many triangles for nothing.
    /// </summary>
    public sealed class Boarding(
        float boardMm, float depthMm, float jointMm, float nozzleMm, float lengthMm = 0f) : IRelief
    {
        /// <summary>How deep the grain is cut into the board's face, as a share of the relief.</summary>
        private const float GrainShare = 0.35f;

        /// <summary>How many times longer a grain ridge is than it is wide.</summary>
        private const float GrainRun = 14f;

        private float Ripple => MathF.Max(nozzleMm * 1.5f, 0.5f);

        /// <summary>How long a board is, with nought meaning the whole way across.</summary>
        private float Run => lengthMm > boardMm ? lengthMm : 0f;

        public float[] Across(float acrossMm)
        {
            var lines = new List<float>(ReliefField.Evenly(acrossMm, Ripple * GrainRun / 4f));
            if (Run <= 0) return [.. lines];

            // The end joints, for both parities of course, since alternate courses are set over
            // by half a board and one list of samples serves them both.
            for (float shift = 0; shift < Run; shift += Run / 2f)
                for (float u = -acrossMm / 2f + shift; u <= acrossMm / 2f + Run; u += Run)
                {
                    lines.Add(u - jointMm / 2f);
                    lines.Add(u - jointMm / 2f + StepMm);
                    lines.Add(u + jointMm / 2f - StepMm);
                    lines.Add(u + jointMm / 2f);
                }

            return [.. lines];
        }

        public float[] Up(float upMm)
        {
            var lines = new List<float>(ReliefField.Evenly(upMm, Ripple / 2.5f));

            // And the board joints on top of the even sampling, so an edge is an edge and not
            // wherever the nearest sample happened to fall.
            for (float head = upMm / 2f; head > -upMm / 2f - boardMm; head -= boardMm)
            {
                lines.Add(head - jointMm / 2f);
                lines.Add(head - jointMm / 2f + StepMm);
                lines.Add(head + jointMm / 2f - StepMm);
                lines.Add(head + jointMm / 2f);
            }

            return [.. lines];
        }

        public float Height(Vector2 at)
        {
            float down = (boardMm / 2f - at.Y) % boardMm;
            if (down < 0) down += boardMm;

            if (down < jointMm / 2f || down > boardMm - jointMm / 2f) return 0f;

            // And the ends of the board, set over by half a board on alternate courses so the
            // joints do not run in a line down the wall.
            if (Run > 0)
            {
                int course = (int)MathF.Floor((boardMm / 2f - at.Y) / boardMm);
                float shift = ((course % 2) + 2) % 2 == 1 ? Run / 2f : 0f;

                float along = (at.X - shift) % Run;
                if (along < 0) along += Run;

                if (along < jointMm / 2f || along > Run - jointMm / 2f) return 0f;
            }

            // Two octaves, the second finer and fainter, both stretched along the board so the
            // ridges run its length rather than across it.
            float grain =
                ReliefField.Noise(at.X / (Ripple * GrainRun), at.Y / Ripple) * 0.68f +
                ReliefField.Noise(at.X / (Ripple * GrainRun / 3f) + 31f, at.Y / (Ripple / 2.2f) + 17f) * 0.32f;

            return depthMm * (1f - GrainShare * grain);
        }
    }
}
