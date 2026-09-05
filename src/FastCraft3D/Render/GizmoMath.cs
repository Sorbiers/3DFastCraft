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
    /// About the centre, both faces move outward and the dimension grows by twice the handle's
    /// travel. Held on one side, the far face stays put and the dimension grows by exactly what
    /// the handle moved - which is what you want when a part has to keep meeting the one next to
    /// it, and is the more useful of the two more often than not.
    ///
    /// The result is clamped well above zero: a scale of zero would collapse the mesh, and a
    /// negative one would silently mirror it.
    /// </summary>
    public static float ScaleRatio(
        float startExtent, double millimetresOutward, float minimum = 0.01f, bool aboutCentre = true)
    {
        if (startExtent < 1e-4f) return 1f;

        double growth = aboutCentre ? millimetresOutward * 2.0 : millimetresOutward;
        return Math.Max((float)((startExtent + growth) / startExtent), minimum);
    }

    /// <summary>
    /// Where a point ends up when everything is scaled about a fixed plane.
    ///
    /// Holding one face still is not just a matter of scaling less: the object has to move as
    /// well, or it would grow away from the face that is meant to be pinned. Positions ride the
    /// same scaling as sizes, measured from the plane that stays put.
    /// </summary>
    public static float ScaledAbout(float anchor, float coordinate, float ratio) =>
        anchor + (coordinate - anchor) * ratio;

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

    /// <summary>
    /// Adjusts a travel distance so the object being dragged lands on a whole multiple of the
    /// snap step.
    ///
    /// The <em>destination</em> is snapped rather than the distance travelled. Snapping the
    /// distance would keep whatever fractional offset the object already had, so two parts that
    /// need to meet exactly never quite would; snapping the destination puts everything on the
    /// same grid. The rest of a multiple selection then moves by the same corrected amount, so
    /// the group keeps its shape.
    /// </summary>
    public static double SnapTravel(double start, double travel, double step)
    {
        if (step <= 0) return travel;

        double destination = start + travel;
        return Math.Round(destination / step) * step - start;
    }

    /// <summary>
    /// A screen drag turned back into a move across a surface, given how far one millimetre of
    /// each of the surface's own directions carries on screen.
    ///
    /// Dragging something that lies on a face is not a move along one axis, so the single-axis
    /// arithmetic above does not fit: the pointer has to be resolved into both directions at
    /// once, which is a two-by-two solve. Null when the surface is edge-on - the two directions
    /// then land on the same screen line and any split between them would be invention.
    /// </summary>
    public static Vector? AcrossSurface(Vector screenDelta, Vector acrossPerMm, Vector upPerMm)
    {
        double determinant = acrossPerMm.X * upPerMm.Y - acrossPerMm.Y * upPerMm.X;
        double scale = acrossPerMm.Length * upPerMm.Length;

        // Two degrees of separation. Below that the answer is enormous and mostly noise.
        if (scale < 1e-9 || Math.Abs(determinant) < scale * 0.035) return null;

        return new Vector(
            (screenDelta.X * upPerMm.Y - screenDelta.Y * upPerMm.X) / determinant,
            (acrossPerMm.X * screenDelta.Y - acrossPerMm.Y * screenDelta.X) / determinant);
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
