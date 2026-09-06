using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>How lettering is laid onto a shape.</summary>
public enum TextProjection
{
    /// <summary>Flat on a face, which is what most lettering wants.</summary>
    Planar,

    /// <summary>Wrapped around the object, for a ring, a barrel or a cup.</summary>
    Cylindrical,

    /// <summary>Wrapped over the object as if onto a ball.</summary>
    Spherical
}

/// <summary>
/// Where a point of flat lettering ends up in space.
///
/// Lettering is laid out flat - that is what a font gives you - and then put onto a shape. Making
/// that a separate step is what lets the same outlines go flat onto a face, round a cylinder, or
/// over a ball, without the letters themselves knowing anything about it.
/// </summary>
public interface IPlacementSurface
{
    /// <param name="uv">Position in the flat layout, in millimetres.</param>
    /// <param name="height">Distance out of the surface: positive stands proud, negative sinks in.</param>
    Vector3 At(Vector2 uv, float height);

    /// <summary>
    /// How far a straight line between two layout points strays from the surface - the gap
    /// between the middle of the chord and the surface under it.
    ///
    /// This is what says whether a step needs breaking up, rather than its length. Length is the
    /// obvious measure and is wrong on a barrel: nothing bends along the axis, so a step straight
    /// up one is already perfect however long it is, while the same step round the barrel is not.
    /// Asking about the gap instead splits only what actually strays, which on ordinary lettering
    /// is the difference between a few thousand triangles and a hundred and fifty thousand.
    /// </summary>
    float Sag(Vector2 from, Vector2 to);

    /// <summary>
    /// How far outside the surface a cutter has to start to be sure of clearing it.
    ///
    /// A boolean handles two faces that meet exactly worst of all, so lettering always starts a
    /// little past the surface. On a face a hair is enough. On anything round it is not: the
    /// object is a many-sided prism pretending to be a barrel, so its real surface wanders either
    /// side of the smooth one the lettering was laid on, and a hair leaves the cutter grazing the
    /// material instead of crossing it. That produced a couple of torn edges every time and the
    /// lettering was refused rather than applied.
    /// </summary>
    float ClearanceMm { get; }
}

/// <summary>The gap measured for any surface, from nothing but where it puts three points.</summary>
public static class SurfaceSag
{
    /// <summary>
    /// A fiftieth of the radius, which comfortably clears both the flats of a thirty-two sided
    /// barrel and the small amount the lettering itself chords across the curve.
    /// </summary>
    public static float ClearanceFor(float radius) => MathF.Max(radius * 0.02f, 0.05f);

    public static float Of(IPlacementSurface surface, Vector2 from, Vector2 to)
    {
        var onTheSurface = surface.At((from + to) * 0.5f, 0);
        var acrossTheChord = (surface.At(from, 0) + surface.At(to, 0)) * 0.5f;

        return (onTheSurface - acrossTheChord).Length();
    }
}

/// <summary>
/// Flat on a picked face.
///
/// The origin of the layout is the middle of the face, not the face's own origin - that is
/// whichever triangle corner the search happened to start from, which would drop lettering in a
/// corner of the wall rather than the middle of it.
/// </summary>
public sealed class PlanarSurface(FacePatch face) : IPlacementSurface
{
    /// <summary>The face itself, for the one caller that can do better than a boolean on it.</summary>
    public FacePatch Face { get; } = face;

    /// <summary>Where the layout's origin sits in the face's own frame.</summary>
    public Vector2 Middle { get; } = (face.Min + face.Max) * 0.5f;

    public Vector3 At(Vector2 uv, float height) => face.ToLocal(Middle + uv, height);

    // A straight line in the layout is a straight line on the face, so nothing ever strays.
    public float Sag(Vector2 from, Vector2 to) => 0;

    // The face is exactly where it says it is, so a hair past it is enough.
    public float ClearanceMm => 0.02f;
}

/// <summary>
/// Wrapped around an upright cylinder.
///
/// Across is arc length, so a letter keeps its proportions however big the barrel is, and text
/// long enough to go right round meets itself rather than piling up.
/// </summary>
public sealed class CylinderSurface(Vector3 centre, float radius, float startAngle = 0) : IPlacementSurface
{
    public Vector3 At(Vector2 uv, float height)
    {
        float angle = startAngle + (radius > 0 ? uv.X / radius : 0);
        float out_ = radius + height;

        return new Vector3(
            centre.X + out_ * MathF.Cos(angle),
            centre.Y + out_ * MathF.Sin(angle),
            centre.Z + uv.Y);
    }

    public float Sag(Vector2 from, Vector2 to) => SurfaceSag.Of(this, from, to);

    public float ClearanceMm => SurfaceSag.ClearanceFor(radius);
}

/// <summary>
/// Wrapped over a ball. Across is arc length around the equator, up is arc length towards the
/// pole, so lettering keeps its shape near the middle and gathers as it climbs - which is what
/// happens to anything wrapped over a sphere.
///
/// The starting latitude is where the middle of the lettering sits. Without it every word would
/// be pinned to the equator, and the point of picking a spot on the ball is to letter that spot.
/// </summary>
public sealed class SphereSurface(
    Vector3 centre, float radius, float startAngle = 0, float startLatitude = 0) : IPlacementSurface
{
    public Vector3 At(Vector2 uv, float height)
    {
        if (radius <= 0) return centre;

        float latitude = Math.Clamp(
            startLatitude + uv.Y / radius, -MathF.PI / 2 + 0.001f, MathF.PI / 2 - 0.001f);
        float ring = MathF.Cos(latitude);

        // Across is measured on the ring the letter actually sits on, so a word does not stretch
        // as it climbs away from the equator.
        float angle = startAngle + (ring > 1e-4f ? uv.X / (radius * ring) : 0);
        float out_ = radius + height;

        return new Vector3(
            centre.X + out_ * ring * MathF.Cos(angle),
            centre.Y + out_ * ring * MathF.Sin(angle),
            centre.Z + out_ * MathF.Sin(latitude));
    }

    public float Sag(Vector2 from, Vector2 to) => SurfaceSag.Of(this, from, to);

    public float ClearanceMm => SurfaceSag.ClearanceFor(radius);
}
