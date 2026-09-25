using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>A hole that takes a shaft, or the shaft that goes in one.</summary>
public enum AnchorKind
{
    Bore,
    Shaft
}

/// <summary>
/// A feature a tool knew about when it built a part, written down so that something else can be
/// lined up with it afterwards.
///
/// The gear tool knows exactly where a gear's shaft hole is, how wide it is, which way its flat
/// faces and how far it runs. All of that used to be thrown away the moment the gear became an
/// object, leaving a shaft to be lined up with a hole by eye and by typing coordinates - which is
/// the one part of making a gear train that nothing in the app helped with.
///
/// Held in the mesh's own coordinates, so it travels with the object through a move, a turn or a
/// resize: anything that changes the transform rather than the geometry. Anything that changes
/// the geometry drops it, because a hole cut somewhere else is not this hole.
/// </summary>
/// <param name="Kind">A hole, or the thing that goes in one.</param>
/// <param name="Name">What the tool called it, for the status line.</param>
/// <param name="At">Where it starts.</param>
/// <param name="Along">Which way it runs, a unit vector.</param>
/// <param name="Across">
/// Which way the flat of a D-shaft, or a flat of a hexagon, faces. Square to <paramref
/// name="Along"/>, and zero on a round one, which goes together any way round.
/// </param>
/// <param name="Size">Across the flats of a hexagon, or the diameter of anything else.</param>
/// <param name="Length">How far it runs.</param>
/// <param name="Shape">Round, a D, or a hexagon.</param>
/// <param name="Flat">
/// For a D, how far the flat is from the far side - the dimension a supplier quotes. Nought for
/// anything else.
/// </param>
public readonly record struct Anchor(
    AnchorKind Kind,
    string Name,
    Vector3 At,
    Vector3 Along,
    Vector3 Across,
    float Size,
    float Length,
    BoreShape Shape = BoreShape.Round,
    float Flat = 0f)
{
    /// <summary>Whether it has to go together a particular way round.</summary>
    public bool IsKeyed => Shape != BoreShape.Round && Across.LengthSquared() > 1e-12f;

    public Anchor Moved(Vector3 by) => this with { At = At + by };

    /// <summary>
    /// The same feature seen through a transform - the object's own, which puts it in world
    /// space.
    ///
    /// The directions go through as directions and come back unit length, and the size and length
    /// are stretched by however much the transform stretched the direction they were measured in.
    /// A scale that is not the same across the axis as along it therefore still reads sensibly; a
    /// scale that is not the same in the two directions across the axis has made the hole an
    /// ellipse, which nothing here can describe, and the size is then what it measures across
    /// <see cref="Across"/> or, failing that, across whichever way is square to the axis.
    /// </summary>
    public Anchor Through(Matrix4x4 transform)
    {
        var along = Vector3.TransformNormal(Along, transform);
        float stretch = along.Length();

        var sideways = Across.LengthSquared() > 1e-12f ? Across : Square(Along);
        var across = Vector3.TransformNormal(sideways, transform);
        float widened = across.Length();

        return this with
        {
            At = Vector3.Transform(At, transform),
            Along = stretch > 1e-9f ? along / stretch : Along,
            Across = Across.LengthSquared() > 1e-12f && widened > 1e-9f ? across / widened : Vector3.Zero,
            Length = Length * stretch,
            Size = Size * (widened > 1e-9f ? widened : 1f),
            Flat = Flat * (widened > 1e-9f ? widened : 1f)
        };
    }

    /// <summary>Some direction square to this one.</summary>
    internal static Vector3 Square(Vector3 way) =>
        Vector3.Normalize(Vector3.Cross(way, MathF.Abs(way.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));

    public override string ToString() =>
        $"{Name} - {Shape} {Size:0.##} mm, {Length:0.##} mm long";
}
