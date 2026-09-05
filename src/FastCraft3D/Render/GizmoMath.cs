using System.Windows;

namespace FastCraft3D.Render;

/// <summary>
/// The arithmetic behind dragging a handle, separated from the viewport that supplies the
/// projections. Rendering needs a live Direct3D device, but these rules - how far a drag
/// moves an object, how much it resizes it, which way a ring turns - are the part most likely
/// to be subtly wrong, so they are kept pure and unit-tested.
/// </summary>
public static class GizmoMath
{
    /// <summary>
    /// Distance travelled along a world axis, in millimetres.
    /// Only the component of the drag that runs along the axis on screen counts, so dragging
    /// across the axis does nothing rather than sliding the object unpredictably.
    /// </summary>
    public static double MillimetresAlongAxis(Vector screenDelta, Vector axisScreenUnit, double pixelsPerMm)
    {
        if (pixelsPerMm <= 0) return 0;
        return (screenDelta.X * axisScreenUnit.X + screenDelta.Y * axisScreenUnit.Y) / pixelsPerMm;
    }

    /// <summary>
    /// How much to multiply the current size by.
    ///
    /// Resizing is about the object's centre, so both faces move outward and the dimension
    /// grows by twice the handle's travel. The result is clamped well above zero: allowing a
    /// scale of zero would collapse the mesh, and a negative one would silently mirror it.
    /// </summary>
    public static float ScaleRatio(float startExtent, double millimetresOutward, float minimum = 0.01f)
    {
        if (startExtent < 1e-4f) return 1f;
        float ratio = (float)((startExtent + millimetresOutward * 2.0) / startExtent);
        return Math.Max(ratio, minimum);
    }

    /// <summary>
    /// Degrees swept around a ring between two pointer positions.
    ///
    /// Two sign flips are folded in. Screen Y grows downward, so a clockwise sweep on screen is
    /// a negative rotation in a right-handed world; and a ring whose axis points away from the
    /// viewer is seen from behind, so it has to turn the other way to follow the pointer.
    /// </summary>
    public static double RotationDegrees(
        Point centre, Point start, Point current, float facingSign, bool snap, double snapStep = 15.0)
    {
        double startAngle = Math.Atan2(start.Y - centre.Y, start.X - centre.X);
        double nowAngle = Math.Atan2(current.Y - centre.Y, current.X - centre.X);
        double degrees = -(nowAngle - startAngle) * 180.0 / Math.PI * facingSign;

        if (snap && snapStep > 0)
            degrees = Math.Round(degrees / snapStep) * snapStep;

        return degrees;
    }

    /// <summary>Folds an angle into (-180, 180] so the readout never drifts to 720 degrees.</summary>
    public static float NormaliseDegrees(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f) degrees -= 360f;
        if (degrees <= -180f) degrees += 360f;
        return degrees;
    }
}
