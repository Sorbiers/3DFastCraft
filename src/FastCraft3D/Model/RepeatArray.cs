using System.Numerics;
using FastCraft3D.Model.Commands;

namespace FastCraft3D.Model;

/// <param name="Copies">How many to make, over and above the original.</param>
/// <param name="Step">How far each copy moves from the one before it.</param>
/// <param name="Grow">How much larger each copy is than the one before it, in millimetres.</param>
/// <param name="FromTheFace">
/// Whether the step is measured from where the last copy ended rather than from its middle.
/// Resizing works about the centre, so a run of growing copies stepped by their centres leaves
/// half the growth as a gap; stepped by their faces they stack, which is what a flight of steps
/// and a stack of courses both want.
/// </param>
public readonly record struct RepeatSettings(int Copies, Vector3 Step, Vector3 Grow, bool FromTheFace);

/// <param name="Copies">How many to make, over and above the original.</param>
/// <param name="Centre">The point on the plate the ring turns about, in millimetres.</param>
/// <param name="Radius">
/// How far from that centre the ring sits. Seeded from where the selection already is, so leaving
/// it alone repeats it where it stands; typing a different number moves the whole ring in or out,
/// the original along with it - see <see cref="Ring.Seats"/>.
/// </param>
/// <param name="StepDegrees">
/// The angle from one copy to the next, anticlockwise seen from above. A full turn is
/// 360/(Copies+1), not 360/Copies, because the original is one of the objects on the circle.
/// </param>
/// <param name="Rise">
/// How much higher each copy is than the one before it. A ring with a rise is a spiral stair,
/// which the straight repeat cannot make at all.
/// </param>
/// <param name="FaceTheCentre">
/// Whether each copy is turned by the angle it travelled, so it keeps the same face towards the
/// centre. Off for a bolt circle - a round hole does not care which way it points - and on for
/// anything with a front: gear teeth, crenellations, the treads of a spiral stair.
/// </param>
/// <param name="Grow">How much larger each copy is than the one before it, in millimetres.</param>
public readonly record struct RingSettings(
    int Copies,
    Vector2 Centre,
    float Radius,
    float StepDegrees,
    float Rise,
    bool FaceTheCentre,
    Vector3 Grow);

/// <param name="Copies">The new objects, in order round the circle.</param>
/// <param name="Seats">
/// Where each source has to sit for the ring to close, in the order they were given.
///
/// Asking for a radius the selection is not at has to move the selection: a ring whose first
/// object is still at the old radius is not a ring, it is a mistake nobody would have asked for.
/// The move is handed back rather than applied so the caller can put it and the copies into one
/// undo step - undoing to a half-made ring would be worse than not having made one.
/// </param>
public sealed record Ring(IReadOnlyList<SceneObject> Copies, IReadOnlyList<TransformState> Seats);

/// <summary>
/// Repeats the selection along a line, optionally growing as it goes.
///
/// Written because building a staircase meant twelve boxes typed in one at a time - a name,
/// three sizes and three positions apiece, about a hundred entries for one flight - and the same
/// again for a row of dowels and a wall of openings. A step and a rise per copy covers all of it,
/// and being one operation it is also one undo.
///
/// Kept apart from the view model so it can be reasoned about on its own: it is arithmetic on
/// transforms, and arithmetic is worth testing.
/// </summary>
public static class RepeatArray
{
    /// <summary>More than this and it is a mistake rather than a model.</summary>
    public const int MaximumCopies = 500;

    /// <summary>
    /// The copies, in order. <paramref name="name"/> is asked for each one so they come back with
    /// names the scene will accept.
    /// </summary>
    public static List<SceneObject> Make(
        IReadOnlyList<SceneObject> sources, RepeatSettings settings, Func<string, string> name)
    {
        var made = new List<SceneObject>();
        int copies = Math.Clamp(settings.Copies, 0, MaximumCopies);

        if (copies == 0 || sources.Count == 0) return made;

        foreach (var source in sources)
        {
            var size = new Vector3(source.SizeX, source.SizeY, source.SizeZ);
            var offset = Vector3.Zero;

            for (int n = 1; n <= copies; n++)
            {
                var copy = source.Clone();
                copy.Name = name(source.Name);

                offset += settings.Step;

                if (settings.Grow != Vector3.Zero)
                {
                    var grown = size + settings.Grow * n;

                    // A copy that has grown to nothing is not a copy; hold it at a hair rather
                    // than letting the scale go to zero or negative and mirror the geometry.
                    copy.SizeX = MathF.Max(grown.X, 1e-3f);
                    copy.SizeY = MathF.Max(grown.Y, 1e-3f);
                    copy.SizeZ = MathF.Max(grown.Z, 1e-3f);

                    // Resizing moves both faces outward by half the growth. Measured from the
                    // face, half of that has to come back out of the step.
                    if (settings.FromTheFace) offset += settings.Grow * 0.5f * Anchor(settings.Step);
                }

                copy.Position = source.Position + offset;
                made.Add(copy);
            }
        }

        return made;
    }

    /// <summary>
    /// Repeats the selection round a circle on the build plate.
    ///
    /// Only about the vertical axis, and that is not a shortcut being taken. Transforms compose as
    /// scale, then X, then Y, then Z, then translate, so turning something about the world Z axis
    /// is exactly an addition to its Z angle - no decomposing a matrix back into three Euler
    /// angles, which is the step that misbehaves near the poles. A ring about X or Y would need
    /// that decomposition, and on a Z-up plate it is not the ring anyone is asking for.
    /// </summary>
    public static Ring MakeRing(
        IReadOnlyList<SceneObject> sources, RingSettings settings, Func<string, string> name)
    {
        if (sources.Count == 0) return new Ring([], []);

        int copies = Math.Clamp(settings.Copies, 0, MaximumCopies);
        float radius = MathF.Max(settings.Radius, 0f);
        var centre = new Vector3(settings.Centre.X, settings.Centre.Y, 0f);

        // The selection goes round as one rigid group. Three parts that make up a bracket have to
        // keep their arrangement while the bracket travels; snapping each of them onto the circle
        // separately would take the bracket apart.
        var middle = Middle(sources);
        var outward = new Vector2(middle.X - centre.X, middle.Y - centre.Y);
        float distance = outward.Length();

        // Sitting on the centre there is no direction to go out along, so +X is as good as any.
        var direction = distance > 1e-4f ? outward / distance : new Vector2(1f, 0f);

        // Moving in or out along the radius does not turn anything, so the seats keep their angles.
        var shift = new Vector3(
            centre.X + direction.X * radius - middle.X,
            centre.Y + direction.Y * radius - middle.Y,
            0f);

        var seats = sources
            .Select(o => new TransformState(o.Position + shift, o.Rotation, o.Scale))
            .ToList();

        var sizes = sources.Select(o => new Vector3(o.SizeX, o.SizeY, o.SizeZ)).ToList();
        var made = new List<SceneObject>();

        // Station by station rather than source by source, so a group parts stay adjacent in the
        // object list - "the three of the second bracket", which is how you go looking for them.
        for (int n = 1; n <= copies; n++)
        {
            float degrees = settings.StepDegrees * n;
            var turn = Matrix4x4.CreateRotationZ(degrees * MathF.PI / 180f);

            for (int i = 0; i < sources.Count; i++)
            {
                var copy = sources[i].Clone();
                copy.Name = name(sources[i].Name);

                // Turning about a vertical axis leaves Z alone, so the rise can simply be added.
                var spun = centre + Vector3.Transform(seats[i].Position - centre, turn);
                copy.Position = spun + new Vector3(0f, 0f, settings.Rise * n);

                if (settings.FaceTheCentre)
                    copy.Rotation = seats[i].Rotation + new Vector3(0f, 0f, degrees);

                if (settings.Grow != Vector3.Zero)
                {
                    var grown = sizes[i] + settings.Grow * n;

                    // As on a line: a copy grown to nothing is not a copy, and a scale through
                    // zero mirrors the geometry.
                    copy.SizeX = MathF.Max(grown.X, 1e-3f);
                    copy.SizeY = MathF.Max(grown.Y, 1e-3f);
                    copy.SizeZ = MathF.Max(grown.Z, 1e-3f);
                }

                made.Add(copy);
            }
        }

        return new Ring(made, seats);
    }

    /// <summary>
    /// How far the selection sits from a centre now. This is what the radius box is seeded with,
    /// so that opening the dialog and pressing Repeat leaves the selection where it stands.
    /// </summary>
    public static float DistanceFromCentre(IReadOnlyList<SceneObject> sources, Vector2 centre)
    {
        if (sources.Count == 0) return 0f;

        var middle = Middle(sources);
        return new Vector2(middle.X - centre.X, middle.Y - centre.Y).Length();
    }

    /// <summary>Where the selection sits, taken as one thing.</summary>
    private static Vector3 Middle(IReadOnlyList<SceneObject> sources) =>
        sources.Aggregate(Vector3.Zero, (sum, o) => sum + o.Position) / sources.Count;

    /// <summary>
    /// Which face each axis is anchored to.
    ///
    /// Where the run is travelling, the anchored face is the one it is travelling away from, so
    /// the sign of the step decides it. Where it is not - a flight of steps grows upward while it
    /// travels along - the lower face is the one that stays put, because that is the one standing
    /// on something.
    /// </summary>
    private static Vector3 Anchor(Vector3 step) => new(
        step.X != 0 ? MathF.Sign(step.X) : 1f,
        step.Y != 0 ? MathF.Sign(step.Y) : 1f,
        step.Z != 0 ? MathF.Sign(step.Z) : 1f);
}
