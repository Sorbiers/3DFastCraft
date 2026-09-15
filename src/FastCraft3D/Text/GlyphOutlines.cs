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
    /// <paramref name="spacingMm"/> is added after every letter; negative draws them closer.
    /// </summary>
    public static List<Glyph> Build(string text, string fontFamily, float heightMm, bool bold,
                                    bool italic = false, float spacingMm = 0f)
    {
        if (string.IsNullOrWhiteSpace(text) || heightMm <= 0) return [];

        // A font without an italic gets a slanted copy of its upright letters from WPF, so the
        // box is worth ticking whatever the font.
        var typeface = new Typeface(
            new FontFamily(fontFamily),
            italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var line = Format(text, typeface);
        var loops = spacingMm == 0f
            ? Loops(line.BuildGeometry(new Point(0, 0))).Select(loop => (loop, 0)).ToList()
            : Letters(text, typeface, line);

        return loops.Count == 0 ? [] : Arrange(loops, heightMm, spacingMm);
    }

    /// <summary>
    /// Every font family installed, by its English name, sorted. The @ names are the same fonts
    /// turned on their side for vertical Asian text, and would only be picked by mistake.
    /// </summary>
    public static List<string> InstalledFonts()
    {
        var english = System.Windows.Markup.XmlLanguage.GetLanguage("en-us");
        return Fonts.SystemFontFamilies
            .Select(f => f.FamilyNames.TryGetValue(english, out var name) ? name : f.Source)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FormattedText Format(string text, Typeface typeface) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, WorkingEm, Brushes.Black, 1.0);

    /// <summary>
    /// The loops letter by letter, each tagged with which letter it belongs to so it can be moved
    /// along by the spacing.
    ///
    /// Each letter goes where the whole line put it. Laying the letters out one after another by
    /// their own widths lost the kerning, so AV and To stood visibly further apart than without
    /// spacing and the lettering jumped wider the moment the spacing left zero.
    /// </summary>
    private static List<(List<Vector2> Loop, int Letter)> Letters(string text, Typeface typeface, FormattedText line)
    {
        var loops = new List<(List<Vector2>, int)>();

        // Text elements rather than chars, so an accent stays on its letter.
        var starts = StringInfo.ParseCombiningCharacters(text);
        for (int k = 0; k < starts.Length; k++)
        {
            int start = starts[k];
            int length = (k + 1 < starts.Length ? starts[k + 1] : text.Length) - start;
            string letter = text.Substring(start, length);
            if (string.IsNullOrWhiteSpace(letter)) continue;

            if (line.BuildHighlightGeometry(new Point(0, 0), start, length) is not { } box) continue;

            foreach (var loop in Loops(Format(letter, typeface).BuildGeometry(new Point(box.Bounds.Left, 0))))
                loops.Add((loop, k));
        }

        return loops;
    }

    /// <summary>Every closed loop in the geometry, as plain points.</summary>
    private static List<List<Vector2>> Loops(System.Windows.Media.Geometry outline)
    {
        var geometry = outline.GetFlattenedPathGeometry(WorkingEm * Smoothness, ToleranceType.Absolute);
        var loops = new List<List<Vector2>>();

        foreach (var figure in geometry.Figures)
        {
            var loop = new List<Vector2> { At(figure.StartPoint) };

            foreach (var segment in figure.Segments)
            {
                // Flattened geometry is nothing but polylines, so this is the only case.
                if (segment is PolyLineSegment polyline)
                    foreach (var point in polyline.Points)
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
    /// Scales the loops to size, spaces the letters, centres them, and works out which are holes.
    /// The spacing goes in after the scaling so that it is in millimetres whatever the font.
    /// </summary>
    private static List<Glyph> Arrange(List<(List<Vector2> Loop, int Letter)> loops, float heightMm, float spacingMm)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (var (loop, _) in loops)
            foreach (var point in loop)
            {
                low = MathF.Min(low, point.Y);
                high = MathF.Max(high, point.Y);
            }

        if (high <= low) return [];
        float scale = heightMm / (high - low);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var (loop, letter) in loops)
        {
            var along = new Vector2(letter * spacingMm, 0f);
            for (int i = 0; i < loop.Count; i++)
            {
                loop[i] = loop[i] * scale + along;
                min = Vector2.Min(min, loop[i]);
                max = Vector2.Max(max, loop[i]);
            }
        }

        var centre = (min + max) * 0.5f;
        foreach (var (loop, _) in loops)
            for (int i = 0; i < loop.Count; i++)
                loop[i] -= centre;

        return Polygon2.Nest(loops.Select(l => l.Loop).ToList())
            .Select(shape => new Glyph(shape.Outline, shape.Holes)).ToList();
    }
}
