namespace FastCraft3D.Geometry;

/// <summary>
/// How far along a long operation is, and what it is doing.
///
/// Reported straight from the worker thread and deliberately not marshalled anywhere: the caller
/// is expected to keep the last one and publish it on its own clock. A boolean node or a grid slice
/// is not worth a dispatcher hop, and there are millions of them.
/// </summary>
/// <param name="Done">
/// Nought to one. Negative where the work genuinely cannot say - the boolean recurses over a tree
/// whose size is not known until it has been built - so that a bar can go back to sweeping rather
/// than sit at a number that means nothing.
/// </param>
/// <param name="Stage">
/// What is happening now, in a few words, or null to leave the last one standing. This is the half
/// that answers "is it stuck?" for the operations that cannot count themselves.
/// </param>
public readonly record struct WorkProgress(float Done, string? Stage = null)
{
    /// <summary>A stage change with no measurable fraction behind it.</summary>
    public static WorkProgress Doing(string stage) => new(-1f, stage);

    public bool Measured => Done >= 0f;
}
