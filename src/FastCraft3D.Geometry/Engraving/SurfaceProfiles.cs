using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Sawn boarding: the one texture that really is a height rather than a shape.
///
/// Roof tiles and lap siding used to live here too, sampled as fields. They are flat plates laid
/// at an angle, which is a thing with corners rather than a function to read off a grid, and both
/// faults they shipped were sampling faults rather than tile faults - see <see cref="TileSolid"/>,
/// which builds them as the slabs they are. A grain is the case the grid was right for: it has no
/// lines of its own and has to be sampled evenly, which is why it is the one that costs anything.
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
    /// The pair of sample lines either side of a place the profile jumps - and never the line
    /// itself.
    ///
    /// A sample sitting exactly on a joint edge or a course head is on the knife edge of the
    /// modulo that decides which side of it that point is on, and which way it falls is the last
    /// bit of a float. On a 77 mm cube at a 10 mm pitch the column at x = -9.9 - the far edge of
    /// the joint at x = -10 - came out a hair inside the joint, so the joint was taken to run the
    /// whole 4.8 mm to the next column and the tile ramped across it. Half a tile folded along its
    /// diagonal, which is what the big triangular facets were: 32 mm2 of it, against the 0.03 mm2
    /// the joint walls themselves come to.
    ///
    /// It widens a joint by two microns either side, which is a two-hundredth of a nozzle.
    /// </summary>
    private static void Straddle(List<float> into, float edgeMm)
    {
        into.Add(edgeMm - StepMm);
        into.Add(edgeMm + StepMm);
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
                Straddle(lines, u - jointMm / 2f);
                Straddle(lines, u + jointMm / 2f);
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
                Straddle(lines, edge - jointMm / 2f);
                Straddle(lines, edge + jointMm / 2f);
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
