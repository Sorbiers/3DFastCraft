using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// A primitive with its detail and size chosen before it is made - Insert, Custom, as 3D Builder
/// had it.
///
/// The ordinary Insert buttons make every round shape at 32 segments, which shows as flats on
/// anything bigger than a thimble. Changing that afterwards was tried and dropped: once a shape
/// has been cut, rebuilding it from its kind fills the cut back in, so the app would have had to
/// remember what every object was made from and forget it the moment anything reworked it.
/// Choosing up front needs none of that - the shape is simply made right the first time.
/// </summary>
/// <param name="Width">Along X.</param>
/// <param name="Depth">Along Y. Different from the width makes an oval of anything round.</param>
/// <param name="Height">Along Z. For a torus, the thickness of the ring.</param>
/// <param name="Segments">Round the circumference.</param>
/// <param name="Rings">Pole to pole on a sphere; round the tube of a torus.</param>
/// <param name="Roundness">The radius the edges of a cube, or the rims of a cylinder, are rounded to.</param>
public readonly record struct CustomShape(
    PrimitiveKind Kind, float Width, float Depth, float Height, int Segments, int Rings, float Roundness)
{
    public const int MinimumSegments = 3;
    public const int MaximumSegments = 256;
    public const int MinimumRings = 2;
    public const int MaximumRings = 128;

    /// <summary>
    /// A size smaller than this is a typing slip rather than a part, and a shape built at zero
    /// has no volume to print.
    /// </summary>
    public const float MinimumSize = 0.1f;

    /// <summary>The shapes that have anything to choose. A pyramid or a wedge has no curve and no detail.</summary>
    public static readonly PrimitiveKind[] Kinds =
        [PrimitiveKind.Cube, PrimitiveKind.Cylinder, PrimitiveKind.Cone, PrimitiveKind.Sphere, PrimitiveKind.Torus];

    /// <summary>
    /// Written out, because a record struct's new() skips these and hands back a shape with no
    /// size and no segments. Twice the segments Insert uses, since smoother is the reason to be here.
    /// </summary>
    public static CustomShape Default => new(PrimitiveKind.Cylinder, 20f, 20f, 20f, 64, 32, 0f);

    public bool UsesSegments => Kind is not PrimitiveKind.Cube || Roundness > 0f;

    public bool UsesRings => Kind is PrimitiveKind.Sphere or PrimitiveKind.Torus;

    public bool CanRound => Kind is PrimitiveKind.Cube or PrimitiveKind.Cylinder;

    private RoundEdges Rims => Kind == PrimitiveKind.Cube ? RoundEdges.All : RoundEdges.Top | RoundEdges.Bottom;

    public float MaximumRoundness =>
        CanRound ? RoundedPrimitives.MaximumRadius(Kind, new Vector3(Width, Depth, Height), Rims) : 0f;

    /// <summary>
    /// The thickest a torus of this width can be. A ring thicker than half its width has no hole
    /// left - the tube runs into itself - which is not a solid anything can print.
    /// </summary>
    public float MaximumTorusHeight => MathF.Min(Width, Depth) / 2f * 0.95f;

    /// <summary>The same shape with every number brought inside what it can be built from.</summary>
    public CustomShape Sane()
    {
        var sane = this with
        {
            Width = MathF.Max(Width, MinimumSize),
            Depth = MathF.Max(Depth, MinimumSize),
            Height = MathF.Max(Height, MinimumSize),
            Segments = Math.Clamp(Segments, MinimumSegments, MaximumSegments),
            Rings = Math.Clamp(Rings, MinimumRings, MaximumRings)
        };

        if (sane.Kind == PrimitiveKind.Torus)
            sane = sane with { Height = MathF.Min(sane.Height, sane.MaximumTorusHeight) };

        return sane with { Roundness = sane.CanRound ? Math.Clamp(sane.Roundness, 0f, sane.MaximumRoundness) : 0f };
    }

    /// <summary>The shape, centred on its own origin, at exactly its size.</summary>
    public Mesh Build()
    {
        var s = Sane();

        // Round shapes are built on a circle as wide as the width, and pressed to the depth
        // afterwards: every generator here makes circles, and an oval is a circle scaled one way.
        float squash = s.Depth / s.Width;

        Mesh mesh = s.Kind switch
        {
            PrimitiveKind.Cube when s.Roundness > 0f =>
                RoundedPrimitives.RoundedBox(s.Width, s.Depth, s.Height, s.Roundness, RoundEdges.All,
                    RoundedPrimitives.ArcStepsFor(s.Segments)),

            PrimitiveKind.Cube => Primitives.Box(s.Width, s.Depth, s.Height),

            // Rounded on the circle, then pressed to the oval with everything else.
            PrimitiveKind.Cylinder => Squashed(
                RoundedPrimitives.RoundedCylinder(s.Width / 2f, s.Height, s.Roundness,
                    RoundEdges.Top | RoundEdges.Bottom, s.Segments, RoundedPrimitives.ArcStepsFor(s.Segments)),
                squash),

            PrimitiveKind.Cone => Squashed(Primitives.Cone(s.Width / 2f, s.Height, s.Segments), squash),

            PrimitiveKind.Sphere => Squashed(
                MeshTransform.Transformed(Primitives.Sphere(0.5f, s.Segments, s.Rings),
                    Matrix4x4.CreateScale(s.Width, s.Width, s.Height)),
                squash),

            PrimitiveKind.Torus => Squashed(
                Primitives.Torus(s.Width / 2f - s.Height / 2f, s.Height / 2f, s.Segments, s.Rings),
                squash),

            _ => Primitives.Create(s.Kind, s.Width)
        };

        // Centred on the box it fills, whatever the generator took as its origin.
        var centre = mesh.ComputeBounds().Center;
        return centre.LengthSquared() < 1e-10f
            ? mesh
            : MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(-centre));

        static Mesh Squashed(Mesh m, float squash) =>
            MathF.Abs(squash - 1f) < 1e-6f ? m : MeshTransform.Transformed(m, Matrix4x4.CreateScale(1f, squash, 1f));
    }
}
