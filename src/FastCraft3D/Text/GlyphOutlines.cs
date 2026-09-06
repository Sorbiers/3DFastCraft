using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using FastCraft3D.Geometry;

namespace FastCraft3D.Text;

/// <param name="Outline">The outer loop of one shape, anticlockwise.</param>
/// <param name="Holes">Loops enclosed by it - the middle of an O, the two of a B.</param>
public readonly record struct Glyph(List<Vector2> Outline, List<List<Vector2>> Holes);

/// <summary>
/// Turns typed text into outlines, in millimetres.
///
/// The one place in the app that asks the operating system about fonts. It hands back plain
/// points, so everything downstream stays free of WPF - the same arrangement the renderer has.
///
/// Which loops are holes is left to <see cref="Polygon2.Nest"/>, which decides it by
/// containment; fonts disagree about which way round they draw a counter.
/// </summary>
public static class GlyphOutlines
{
    /// <summary>How finely curves are broken into straight lines, as a fraction of the text height.</summary>
    private const double Smoothness = 0.004;

    /// <summary>Built at this size and scaled after, so the flattening tolerance means the same thing.</summary>
    private const double WorkingEm = 100.0;

    /// <summary>
    /// The outlines of <paramref name="text"/>, sized so its capitals stand
    /// <paramref name="heightMm"/> tall, and laid out with its middle at the origin.
    /// </summary>
    public static List<Glyph> Build(string text, string fontFamily, float heightMm, bool bold)
    {
        if (string.IsNullOrWhiteSpace(text) || heightMm <= 0) return [];

        var typeface = new Typeface(
            new FontFamily(fontFamily),
            FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            WorkingEm,
            Brushes.Black,
            1.0);

        var geometry = formatted.BuildGeometry(new Point(0, 0));
        var flattened = geometry.GetFlattenedPathGeometry(
            WorkingEm * Smoothness, ToleranceType.Absolute);

        var loops = Loops(flattened);
        if (loops.Count == 0) return [];

        return Arrange(loops, heightMm);
    }

    /// <summary>Every closed loop in the geometry, as plain points.</summary>
    private static List<List<Vector2>> Loops(PathGeometry geometry)
    {
        var loops = new List<List<Vector2>>();

        foreach (var figure in geometry.Figures)
        {
            var loop = new List<Vector2> { At(figure.StartPoint) };

            foreach (var segment in figure.Segments)
            {
                // Flattened geometry is nothing but polylines, so this is the only case.
                if (segment is PolyLineSegment line)
                    foreach (var point in line.Points)
                        loop.Add(At(point));
                else if (segment is LineSegment single)
                    loop.Add(At(single.Point));
            }

            // The closing point repeated is noise; the loop is closed by definition.
            if (loop.Count > 1 && (loop[0] - loop[^1]).LengthSquared() < 1e-8f)
                loop.RemoveAt(loop.Count - 1);

            if (loop.Count >= 3) loops.Add(loop);
        }

        return loops;

        // Screen coordinates run down the page; millimetres run up it.
        static Vector2 At(Point p) => new((float)p.X, (float)-p.Y);
    }

    /// <summary>
    /// Scales the loops to size, centres them, and works out which are holes.
    /// </summary>
    private static List<Glyph> Arrange(List<List<Vector2>> loops, float heightMm)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var loop in loops)
            foreach (var point in loop)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

        var size = max - min;
        if (size.Y <= 0) return [];

        float scale = heightMm / size.Y;
        var centre = (min + max) * 0.5f;

        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
                loop[i] = (loop[i] - centre) * scale;

        return Nest(loops);
    }

    private static List<Glyph> Nest(List<List<Vector2>> loops) =>
        Polygon2.Nest(loops).Select(shape => new Glyph(shape.Outline, shape.Holes)).ToList();
}
