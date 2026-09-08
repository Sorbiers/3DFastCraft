using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// A cutting plane's facing as two angles off an axis, so it can be typed as well as dragged.
///
/// Two angles, not three. A plane's normal has two degrees of freedom; a third would be a turn
/// about the normal itself, which leaves the plane exactly where it was. That is the same reason
/// the tilt rings leave out the ring lying along the normal.
///
/// The pair is a turn about the first axis across from the chosen one, then a turn about the
/// second. Fixing that order is what makes the pair unique: every facing has exactly one, so a
/// ring dragged by hand reads straight back into the boxes instead of setting them wandering.
/// </summary>
public static class PlaneTilt
{
    /// <summary>
    /// The two axes a plane on this one tilts about, in the order the angles apply.
    ///
    /// Cyclic, so the pair and the axis itself are always right-handed - which is what makes a
    /// positive angle turn the same way as the ring of that colour.
    /// </summary>
    public static (Vector3 First, Vector3 Second) AxesFor(Axis axis) => axis switch
    {
        Axis.X => (Vector3.UnitY, Vector3.UnitZ),
        Axis.Y => (Vector3.UnitZ, Vector3.UnitX),
        _ => (Vector3.UnitX, Vector3.UnitY)
    };

    /// <summary>What to call those two axes on screen.</summary>
    public static (string First, string Second) NamesFor(Axis axis) => axis switch
    {
        Axis.X => ("Y", "Z"),
        Axis.Y => ("Z", "X"),
        _ => ("X", "Y")
    };

    /// <summary>The facing a pair of angles describes.</summary>
    public static Vector3 Normal(Axis axis, double firstDegrees, double secondDegrees)
    {
        var (first, second) = AxesFor(axis);
        Vector3 along = PlaneSplit.NormalFor(axis);

        double a = firstDegrees * Math.PI / 180.0;
        double b = secondDegrees * Math.PI / 180.0;

        return Vector3.Normalize(
            first * (float)(Math.Sin(b) * Math.Cos(a))
            + second * (float)(-Math.Sin(a))
            + along * (float)(Math.Cos(a) * Math.Cos(b)));
    }

    /// <summary>
    /// The angles that facing was reached by, and the inverse of <see cref="Normal"/>.
    ///
    /// The first angle comes back between -90 and 90. A plane tilted 120 degrees is the same
    /// plane as one tilted 60 the other way about, so that is what it reads as - the boxes show
    /// the shortest way to the facing they have, not the way it was got to.
    /// </summary>
    public static (double First, double Second) Angles(Axis axis, Vector3 normal)
    {
        var (first, second) = AxesFor(axis);
        Vector3 along = PlaneSplit.NormalFor(axis);

        normal = Vector3.Normalize(normal);

        double a = Math.Asin(Math.Clamp(-Vector3.Dot(normal, second), -1.0, 1.0));
        double b = Math.Atan2(Vector3.Dot(normal, first), Vector3.Dot(normal, along));

        return (Degrees(a), Degrees(b));

        static double Degrees(double radians)
        {
            double d = radians * 180.0 / Math.PI;

            // Negative zero would show as "-0.0" in a box that has nothing in it.
            return d == 0 ? 0 : d;
        }
    }
}
