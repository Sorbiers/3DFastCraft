using System.Numerics;

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
