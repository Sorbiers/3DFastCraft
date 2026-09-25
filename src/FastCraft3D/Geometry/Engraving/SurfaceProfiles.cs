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
    /// The heads of the courses over a face this tall: where <c>Height</c>'s own repeat begins,
    /// which is the only lattice the sample lines may be put on. See
    /// <see cref="ReliefField.Lattice"/> for what happens when they are walked from the edge of the
    /// face instead.
    ///
    /// It costs the tidiness the old walk was after - a whole course at the top of the face, the
    /// part one at the bottom where a wall meets the ground - because the part course now falls
    /// wherever the middle of the face lands in the repeat. That was never true of what was built
    /// anyway: <c>Height</c> has always measured from the middle, so the walk from the top was
    /// describing a course the field did not have.
    /// </summary>
    private static float[] Heads(float courseMm, float upMm) =>
        ReliefField.Lattice(courseMm / 2f, courseMm, -upMm / 2f - courseMm, upMm / 2f + courseMm);

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

            foreach (float head in Heads(courseMm, upMm))
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

            // Joints for both parities of course at once. Alternate courses are staggered by half a
            // tile, so between them the joints fall every half tile - on the lattice Height reckons
            // them from, which is the middle of the face and not its edge.
            foreach (float u in ReliefField.Lattice(
                0f, tileMm / 2f, -acrossMm / 2f - tileMm, acrossMm / 2f + tileMm))
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

            foreach (float head in Heads(courseMm, upMm))
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

            // The end joints, for both parities of course at once: alternate courses are set over
            // by half a board, so between them the joints fall every half board's length - on the
            // lattice Height reckons them from.
            foreach (float u in ReliefField.Lattice(
                0f, Run / 2f, -acrossMm / 2f - Run, acrossMm / 2f + Run))
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
            // wherever the nearest sample happened to fall - which is what it was, since the walk
            // started at the top of the face rather than on the lattice Height repeats on.
            foreach (float edge in Heads(boardMm, upMm))
            {
                lines.Add(edge - jointMm / 2f);
                lines.Add(edge - jointMm / 2f + StepMm);
                lines.Add(edge + jointMm / 2f - StepMm);
                lines.Add(edge + jointMm / 2f);
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
