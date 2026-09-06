using System.Globalization;
using System.IO;
using System.Numerics;
using System.Xml.Linq;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Io;

/// <summary>
/// Reads the filled shapes out of an SVG drawing as plain outlines, in millimetres.
///
/// It hands back exactly what the font reader hands back - an outer loop and the loops inside it
/// - so a logo goes through the lettering tool unchanged: same placement handles, same bevel,
/// same wrapping round a barrel, same choice of cut or raised. Nothing downstream needs to know
/// where the outlines came from.
///
/// The path data is parsed here rather than handed to the XAML geometry parser. The two mini
/// languages look alike and are not: a drawing exported minified writes flags and numbers with
/// no separator between them - "a1 1 0 011 1" - which the XAML parser rejects, and that is
/// exactly the kind of file people have. Curves are flattened rather than kept, because
/// everything downstream works in straight lines.
///
/// Not read: text (convert it to paths in the drawing program first - there is no way to know
/// the reader has the same fonts), use and symbol references, clipping, masks and strokes. A
/// stroke has no area, so a drawing of nothing but unfilled lines comes back empty.
/// </summary>
public static class SvgOutlines
{
    /// <summary>How finely curves are broken into straight lines, against the drawing's size.</summary>
    private const float Smoothness = 0.0015f;

    /// <summary>The most points a drawing may flatten to before it is refused as too detailed.</summary>
    public const int MaximumPoints = 400_000;

    /// <summary>Where a cubic stops being split however unflat it still looks.</summary>
    private const int MaximumDepth = 16;

    /// <summary>
    /// The shapes in <paramref name="file"/>, scaled so the whole drawing stands
    /// <paramref name="heightMm"/> tall, with its middle at the origin.
    /// </summary>
    public static List<TextShape> Read(string file, float heightMm) =>
        Parse(File.ReadAllText(file), heightMm);

    /// <summary>The same from the document's text, which is what the tests use.</summary>
    public static List<TextShape> Parse(string svg, float heightMm)
    {
        if (heightMm <= 0 || string.IsNullOrWhiteSpace(svg)) return [];

        var root = XDocument.Parse(svg).Root;
        if (root is null) return [];

        var paths = new List<List<Curve>>();
        Walk(root, Matrix3x2.Identity, paths);

        var loops = Flatten(paths);
        return loops.Count == 0 ? [] : Arrange(loops, heightMm);
    }

    // --- The document ---------------------------------------------------------------------

    private static void Walk(XElement element, Matrix3x2 parent, List<List<Curve>> into)
    {
        string name = element.Name.LocalName;

        // Held for later use rather than drawn, and nothing here resolves a reference.
        if (name is "defs" or "symbol" or "clipPath" or "mask" or "marker" or "pattern") return;
        if (Hidden(element)) return;

        var here = Local(element) * parent;

        foreach (var subpath in Shape(element, name))
            into.Add(Moved(subpath, here));

        foreach (var child in element.Elements())
            Walk(child, here, into);
    }

    private static bool Hidden(XElement element)
    {
        if (Text(element, "display") == "none") return true;

        string style = Text(element, "style") ?? "";
        return style.Replace(" ", "").Contains("display:none");
    }

    private static IEnumerable<List<Curve>> Shape(XElement element, string name) => name switch
    {
        "path" => Subpaths(Text(element, "d") ?? ""),
        "rect" => Rectangle(element),
        "circle" => Oval(
            Number(element, "cx"), Number(element, "cy"),
            Number(element, "r"), Number(element, "r")),
        "ellipse" => Oval(
            Number(element, "cx"), Number(element, "cy"),
            Number(element, "rx"), Number(element, "ry")),
        "polygon" => Corners(Text(element, "points") ?? "", closed: true),
        "polyline" => Corners(Text(element, "points") ?? "", closed: false),
        _ => []
    };

    private static List<Curve> Moved(List<Curve> curves, Matrix3x2 by)
    {
        for (int i = 0; i < curves.Count; i++)
            curves[i] = curves[i].Moved(by);

        return curves;
    }

    private static string? Text(XElement element, string name) => element.Attribute(name)?.Value;

    private static float Number(XElement element, string name)
    {
        var text = Text(element, name);
        return text is not null && float.TryParse(
            text.TrimEnd('p', 'x', 'm', 't', '%'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }

    // --- Transforms -----------------------------------------------------------------------

    /// <summary>
    /// The element's own transform. SVG matrices are written the way they are applied to a
    /// column vector; System.Numerics multiplies rows, which is the transpose of the same thing.
    /// </summary>
    private static Matrix3x2 Local(XElement element)
    {
        string? text = Text(element, "transform");
        if (string.IsNullOrWhiteSpace(text)) return Matrix3x2.Identity;

        var result = Matrix3x2.Identity;
        int at = 0;

        while (at < text.Length)
        {
            int open = text.IndexOf('(', at);
            if (open < 0) break;

            int close = text.IndexOf(')', open);
            if (close < 0) break;

            string what = text[at..open].Trim(' ', ',', '\t', '\r', '\n');
            var values = Reader.All(text[(open + 1)..close]);
            at = close + 1;

            // Written left to right and applied in that order, so each one goes on the inside.
            result = Made(what, values) * result;
        }

        return result;
    }

    private static Matrix3x2 Made(string what, float[] v) => what switch
    {
        "matrix" when v.Length >= 6 => new Matrix3x2(v[0], v[1], v[2], v[3], v[4], v[5]),
        "translate" when v.Length >= 1 =>
            Matrix3x2.CreateTranslation(v[0], v.Length > 1 ? v[1] : 0),
        "scale" when v.Length >= 1 => Matrix3x2.CreateScale(v[0], v.Length > 1 ? v[1] : v[0]),
        "rotate" when v.Length >= 3 => Matrix3x2.CreateRotation(
            v[0] * MathF.PI / 180f, new Vector2(v[1], v[2])),
        "rotate" when v.Length >= 1 => Matrix3x2.CreateRotation(v[0] * MathF.PI / 180f),
        "skewX" when v.Length >= 1 => Matrix3x2.CreateSkew(v[0] * MathF.PI / 180f, 0),
        "skewY" when v.Length >= 1 => Matrix3x2.CreateSkew(0, v[0] * MathF.PI / 180f),
        _ => Matrix3x2.Identity
    };

    // --- The simple shapes ------------------------------------------------------------------

    private static IEnumerable<List<Curve>> Rectangle(XElement element)
    {
        float x = Number(element, "x"), y = Number(element, "y");
        float w = Number(element, "width"), h = Number(element, "height");
        if (w <= 0 || h <= 0) yield break;

        float rx = Number(element, "rx"), ry = Number(element, "ry");
        if (rx <= 0) rx = ry;
        if (ry <= 0) ry = rx;

        rx = Math.Min(rx, w * 0.5f);
        ry = Math.Min(ry, h * 0.5f);

        if (rx <= 0 || ry <= 0)
        {
            yield return
            [
                Curve.Start(new Vector2(x, y)),
                Curve.Line(new Vector2(x + w, y)),
                Curve.Line(new Vector2(x + w, y + h)),
                Curve.Line(new Vector2(x, y + h)),
                Curve.Line(new Vector2(x, y))
            ];
            yield break;
        }

        var path = new List<Curve> { Curve.Start(new Vector2(x + rx, y)) };
        path.Add(Curve.Line(new Vector2(x + w - rx, y)));
        path.AddRange(Quarter(new Vector2(x + w - rx, y + ry), rx, ry, -MathF.PI / 2, 0));
        path.Add(Curve.Line(new Vector2(x + w, y + h - ry)));
        path.AddRange(Quarter(new Vector2(x + w - rx, y + h - ry), rx, ry, 0, MathF.PI / 2));
        path.Add(Curve.Line(new Vector2(x + rx, y + h)));
        path.AddRange(Quarter(new Vector2(x + rx, y + h - ry), rx, ry, MathF.PI / 2, MathF.PI));
        path.Add(Curve.Line(new Vector2(x, y + ry)));
        path.AddRange(Quarter(new Vector2(x + rx, y + ry), rx, ry, MathF.PI, MathF.PI * 1.5f));

        yield return path;
    }

    private static IEnumerable<List<Curve>> Oval(float cx, float cy, float rx, float ry)
    {
        if (rx <= 0 || ry <= 0) yield break;

        var path = new List<Curve> { Curve.Start(new Vector2(cx + rx, cy)) };
        for (int q = 0; q < 4; q++)
            path.AddRange(Quarter(
                new Vector2(cx, cy), rx, ry, q * MathF.PI / 2, (q + 1) * MathF.PI / 2));

        yield return path;
    }

    /// <summary>A quarter of an ellipse as one cubic, which is within a thousandth of the arc.</summary>
    private static IEnumerable<Curve> Quarter(
        Vector2 centre, float rx, float ry, float from, float to)
    {
        float alpha = 4f / 3f * MathF.Tan((to - from) / 4f);

        var p0 = new Vector2(centre.X + rx * MathF.Cos(from), centre.Y + ry * MathF.Sin(from));
        var p3 = new Vector2(centre.X + rx * MathF.Cos(to), centre.Y + ry * MathF.Sin(to));
        var d0 = new Vector2(-rx * MathF.Sin(from), ry * MathF.Cos(from));
        var d3 = new Vector2(-rx * MathF.Sin(to), ry * MathF.Cos(to));

        yield return Curve.Cubic(p0 + d0 * alpha, p3 - d3 * alpha, p3);
    }

    private static IEnumerable<List<Curve>> Corners(string points, bool closed)
    {
        var values = Reader.All(points);
        if (values.Length < 6) yield break;

        var path = new List<Curve> { Curve.Start(new Vector2(values[0], values[1])) };
        for (int i = 2; i + 1 < values.Length; i += 2)
            path.Add(Curve.Line(new Vector2(values[i], values[i + 1])));

        if (closed) path.Add(Curve.Line(new Vector2(values[0], values[1])));
        yield return path;
    }

    // --- Path data ---------------------------------------------------------------------------

    /// <summary>
    /// The subpaths of one <c>d</c> attribute. Each starts with its own move, so a letter and
    /// the counter inside it arrive as two loops for the nesting to sort out.
    /// </summary>
    private static IEnumerable<List<Curve>> Subpaths(string data)
    {
        var reader = new Reader(data);
        var paths = new List<List<Curve>>();

        List<Curve>? path = null;
        Vector2 at = Vector2.Zero, start = Vector2.Zero, lastCubic = Vector2.Zero,
                lastQuadratic = Vector2.Zero;
        char command = ' ', previous = ' ';

        while (true)
        {
            if (reader.NextIsCommand) command = reader.Command();
            else if (command is ' ') break;
            else if (!reader.More) break;
            else if (command is 'M') command = 'L';   // a move with extra pairs draws lines
            else if (command is 'm') command = 'l';

            bool relative = char.IsLower(command);
            char kind = char.ToUpperInvariant(command);
            var from = relative ? at : Vector2.Zero;

            switch (kind)
            {
                case 'M':
                    if (path is { Count: > 1 }) paths.Add(path);
                    at = from + reader.Point();
                    start = at;
                    path = [Curve.Start(at)];
                    break;

                case 'Z':
                    if (path is { Count: > 1 })
                    {
                        path.Add(Curve.Line(start));
                        paths.Add(path);
                    }
                    at = start;
                    path = [Curve.Start(at)];
                    break;

                case 'L':
                    at = from + reader.Point();
                    path?.Add(Curve.Line(at));
                    break;

                case 'H':
                    at = new Vector2((relative ? at.X : 0) + reader.Number(), at.Y);
                    path?.Add(Curve.Line(at));
                    break;

                case 'V':
                    at = new Vector2(at.X, (relative ? at.Y : 0) + reader.Number());
                    path?.Add(Curve.Line(at));
                    break;

                case 'C':
                case 'S':
                {
                    var c1 = kind == 'C'
                        ? from + reader.Point()
                        : previous is 'C' or 'S' or 'c' or 's' ? at + (at - lastCubic) : at;

                    var c2 = from + reader.Point();
                    var end = from + reader.Point();

                    path?.Add(Curve.Cubic(c1, c2, end));
                    lastCubic = c2;
                    at = end;
                    break;
                }

                case 'Q':
                case 'T':
                {
                    var q = kind == 'Q'
                        ? from + reader.Point()
                        : previous is 'Q' or 'T' or 'q' or 't' ? at + (at - lastQuadratic) : at;

                    var end = from + reader.Point();

                    // Held as a cubic, so there is only one curve to flatten.
                    path?.Add(Curve.Cubic(
                        at + (q - at) * (2f / 3f), end + (q - end) * (2f / 3f), end));

                    lastQuadratic = q;
                    at = end;
                    break;
                }

                case 'A':
                {
                    float rx = reader.Number(), ry = reader.Number(), turn = reader.Number();
                    bool large = reader.Flag(), sweep = reader.Flag();
                    var end = from + reader.Point();

                    if (path is not null)
                        foreach (var piece in Arc(at, end, rx, ry, turn, large, sweep))
                            path.Add(piece);

                    at = end;
                    break;
                }

                default:
                    return paths; // an unknown command; keep what was read rather than guessing
            }

            previous = command;
            if (!reader.More && !reader.NextIsCommand) break;
        }

        if (path is { Count: > 1 }) paths.Add(path);
        return paths;
    }

    /// <summary>
    /// One elliptical arc as cubics, by the endpoint-to-centre conversion in the SVG
    /// specification. Radii too small to reach are scaled up, which is what the standard asks
    /// for and what drawings produced by rounding a path actually need.
    /// </summary>
    private static IEnumerable<Curve> Arc(
        Vector2 from, Vector2 to, float rx, float ry, float turnDegrees, bool large, bool sweep)
    {
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);

        if (rx < 1e-9f || ry < 1e-9f || (to - from).LengthSquared() < 1e-18f)
        {
            yield return Curve.Line(to);
            yield break;
        }

        float turn = turnDegrees * MathF.PI / 180f;
        float cos = MathF.Cos(turn), sin = MathF.Sin(turn);

        var half = (from - to) * 0.5f;
        var p = new Vector2(cos * half.X + sin * half.Y, -sin * half.X + cos * half.Y);

        float fit = p.X * p.X / (rx * rx) + p.Y * p.Y / (ry * ry);
        if (fit > 1)
        {
            float grow = MathF.Sqrt(fit);
            rx *= grow;
            ry *= grow;
        }

        float numerator = rx * rx * ry * ry - rx * rx * p.Y * p.Y - ry * ry * p.X * p.X;
        float denominator = rx * rx * p.Y * p.Y + ry * ry * p.X * p.X;
        float scale = MathF.Sqrt(Math.Max(numerator / denominator, 0));
        if (large == sweep) scale = -scale;

        var centre = new Vector2(scale * rx * p.Y / ry, -scale * ry * p.X / rx);
        var middle = (from + to) * 0.5f;
        var origin = new Vector2(
            cos * centre.X - sin * centre.Y + middle.X,
            sin * centre.X + cos * centre.Y + middle.Y);

        float start = Angle(new Vector2((p.X - centre.X) / rx, (p.Y - centre.Y) / ry));
        float end = Angle(new Vector2((-p.X - centre.X) / rx, (-p.Y - centre.Y) / ry));
        float sweptTo = end - start;

        if (!sweep && sweptTo > 0) sweptTo -= MathF.Tau;
        else if (sweep && sweptTo < 0) sweptTo += MathF.Tau;

        int pieces = Math.Max((int)MathF.Ceiling(Math.Abs(sweptTo) / (MathF.PI / 2f)), 1);
        float step = sweptTo / pieces;
        float alpha = 4f / 3f * MathF.Tan(step / 4f);

        for (int i = 0; i < pieces; i++)
        {
            float a0 = start + i * step, a1 = a0 + step;

            var q0 = OnArc(origin, rx, ry, cos, sin, a0);
            var q1 = OnArc(origin, rx, ry, cos, sin, a1);
            var d0 = Along(rx, ry, cos, sin, a0);
            var d1 = Along(rx, ry, cos, sin, a1);

            yield return Curve.Cubic(q0 + d0 * alpha, q1 - d1 * alpha, q1);
        }
    }

    private static Vector2 OnArc(
        Vector2 centre, float rx, float ry, float cos, float sin, float angle)
    {
        float x = rx * MathF.Cos(angle), y = ry * MathF.Sin(angle);
        return new Vector2(cos * x - sin * y + centre.X, sin * x + cos * y + centre.Y);
    }

    private static Vector2 Along(float rx, float ry, float cos, float sin, float angle)
    {
        float x = -rx * MathF.Sin(angle), y = ry * MathF.Cos(angle);
        return new Vector2(cos * x - sin * y, sin * x + cos * y);
    }

    private static float Angle(Vector2 v) => MathF.Atan2(v.Y, v.X);

    // --- Flattening ---------------------------------------------------------------------------

    private static List<List<Vector2>> Flatten(List<List<Curve>> paths)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var path in paths)
            foreach (var curve in path)
                foreach (var point in curve.Points)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

        if (min.X > max.X) return [];

        float tolerance = Math.Max((max - min).Length() * Smoothness, 1e-6f);
        var loops = new List<List<Vector2>>();
        int budget = MaximumPoints;

        foreach (var path in paths)
        {
            var loop = new List<Vector2>();

            foreach (var curve in path)
            {
                if (loop.Count == 0) loop.Add(curve.To);
                else if (curve.IsLine) loop.Add(curve.To);
                else Split(loop, loop[^1], curve.First, curve.Second, curve.To, tolerance, 0);

                if (loop.Count > budget) return loops; // too fine to be worth going on with
            }

            budget -= loop.Count;

            // The closing point repeated is noise; a loop is closed by definition.
            while (loop.Count > 1 && (loop[0] - loop[^1]).LengthSquared() < 1e-12f)
                loop.RemoveAt(loop.Count - 1);

            if (loop.Count >= 3 && Math.Abs(Polygon2.SignedArea(loop)) > 1e-9f) loops.Add(loop);
        }

        return loops;
    }

    private static void Split(
        List<Vector2> into, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        float tolerance, int depth)
    {
        if (depth >= MaximumDepth || Straight(p0, p1, p2, p3, tolerance))
        {
            into.Add(p3);
            return;
        }

        Vector2 a = (p0 + p1) * 0.5f, b = (p1 + p2) * 0.5f, c = (p2 + p3) * 0.5f;
        Vector2 d = (a + b) * 0.5f, e = (b + c) * 0.5f, middle = (d + e) * 0.5f;

        Split(into, p0, a, d, middle, tolerance, depth + 1);
        Split(into, middle, e, c, p3, tolerance, depth + 1);
    }

    /// <summary>How far the control points stand off the chord, which bounds the curve's own sag.</summary>
    private static bool Straight(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance)
    {
        var chord = p3 - p0;
        float length = chord.Length();

        if (length < 1e-9f)
            return (p1 - p0).Length() <= tolerance && (p2 - p0).Length() <= tolerance;

        var across = new Vector2(-chord.Y, chord.X) / length;

        return Math.Abs(Vector2.Dot(p1 - p0, across)) <= tolerance
            && Math.Abs(Vector2.Dot(p2 - p0, across)) <= tolerance;
    }

    // --- Placing --------------------------------------------------------------------------------

    /// <summary>
    /// Turns the drawing round the right way, scales it to size and sorts the loops into shapes.
    ///
    /// SVG measures down the page and millimetres measure up it, so every drawing arrives upside
    /// down; flipping y here is the whole of the conversion.
    /// </summary>
    private static List<TextShape> Arrange(List<List<Vector2>> loops, float heightMm)
    {
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);

        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                loop[i] = new Vector2(loop[i].X, -loop[i].Y);
                min = Vector2.Min(min, loop[i]);
                max = Vector2.Max(max, loop[i]);
            }

        var size = max - min;
        if (size.Y <= 0) return [];

        float scale = heightMm / size.Y;
        var centre = (min + max) * 0.5f;

        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
                loop[i] = (loop[i] - centre) * scale;

        return Polygon2.Nest(loops)
            .Select(shape => new TextShape(shape.Outline, shape.Holes))
            .ToList();
    }

    // --- Pieces ------------------------------------------------------------------------------

    /// <summary>One step of a path: a straight line, or a cubic given by its two handles.</summary>
    private readonly record struct Curve(Vector2 First, Vector2 Second, Vector2 To, bool IsLine)
    {
        public static Curve Start(Vector2 at) => new(at, at, at, true);
        public static Curve Line(Vector2 to) => new(to, to, to, true);
        public static Curve Cubic(Vector2 first, Vector2 second, Vector2 to) =>
            new(first, second, to, false);

        public IEnumerable<Vector2> Points
        {
            get
            {
                yield return To;
                if (IsLine) yield break;

                yield return First;
                yield return Second;
            }
        }

        public Curve Moved(Matrix3x2 by) => new(
            Vector2.Transform(First, by), Vector2.Transform(Second, by),
            Vector2.Transform(To, by), IsLine);
    }

    /// <summary>
    /// The number scanner for path data and transform lists.
    ///
    /// SVG lets numbers run together wherever the next one cannot be mistaken for part of the
    /// last: "10-5" is two numbers, ".5.5" is two, and an arc's two flags may be written as a
    /// single character each with nothing between them. This reads that grammar directly rather
    /// than splitting on separators, which is where every simpler attempt comes apart.
    /// </summary>
    private sealed class Reader(string text)
    {
        private int at;

        public bool More
        {
            get
            {
                Skip();
                return at < text.Length && (char.IsAsciiDigit(text[at]) || text[at] is '-' or '+' or '.');
            }
        }

        public bool NextIsCommand
        {
            get
            {
                Skip();
                return at < text.Length && char.IsAsciiLetter(text[at]) && text[at] is not ('e' or 'E');
            }
        }

        public char Command() => text[at++];

        public Vector2 Point() => new(Number(), Number());

        /// <summary>An arc flag is a single character, and may be written with nothing after it.</summary>
        public bool Flag()
        {
            Skip();
            return at < text.Length && text[at++] == '1';
        }

        public float Number()
        {
            Skip();
            int from = at;

            if (at < text.Length && text[at] is '-' or '+') at++;
            while (at < text.Length && char.IsAsciiDigit(text[at])) at++;

            if (at < text.Length && text[at] == '.')
            {
                at++;
                while (at < text.Length && char.IsAsciiDigit(text[at])) at++;
            }

            // An exponent, but only when it is really one: "3e" in isolation is not.
            if (at + 1 < text.Length && text[at] is 'e' or 'E')
            {
                int mark = at++;
                if (at < text.Length && text[at] is '-' or '+') at++;

                if (at < text.Length && char.IsAsciiDigit(text[at]))
                    while (at < text.Length && char.IsAsciiDigit(text[at])) at++;
                else
                    at = mark;
            }

            return from == at ? 0f
                : float.TryParse(text[from..at], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float value) ? value : 0f;
        }

        private void Skip()
        {
            while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == ',')) at++;
        }

        public static float[] All(string text)
        {
            var reader = new Reader(text);
            var values = new List<float>();

            while (reader.More) values.Add(reader.Number());

            return values.ToArray();
        }
    }
}
