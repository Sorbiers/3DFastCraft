using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using FastCraft3D.Geometry.Drawings;

namespace FastCraft3D.View;

/// <summary>
/// Puts a laid-out drawing sheet into ink: lines, dimensions, captions and a title block, all as
/// vectors, so a printer or a PDF gets sharp lines at any size.
///
/// The sheet is laid out in paper millimetres with Y up, as a drawing is measured; WPF works in
/// ninety-sixths of an inch with Y down, and everything is turned over here and nowhere else.
/// </summary>
public static class DrawingRenderer
{
    private const double PerMillimetre = 96.0 / 25.4;

    private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x16)));
    private static readonly Brush Faint = Frozen(new SolidColorBrush(Color.FromRgb(0x6A, 0x70, 0x78)));

    /// <summary>The page's size in device-independent pixels.</summary>
    public static Size PageSize(Vector2 paperMillimetres) => new(paperMillimetres.X * PerMillimetre, paperMillimetres.Y * PerMillimetre);

    public static DrawingVisual Render(SheetLayout sheet, DrawingOptions options, DateTime date)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        var page = PageSize(sheet.Paper);
        dc.DrawRectangle(Brushes.White, null, new Rect(page));

        Point P(Vector2 mm) => new(mm.X * PerMillimetre, (sheet.Paper.Y - mm.Y) * PerMillimetre);
        Pen PenOf(Brush brush, double millimetres, DashStyle? dashes = null) =>
            Frozen(new Pen(brush, millimetres * PerMillimetre) { DashStyle = dashes ?? DashStyles.Solid, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        var outline = PenOf(Ink, 0.5);
        var hidden = PenOf(Faint, 0.25, new DashStyle([6.0, 3.0], 0));
        var thin = PenOf(Ink, 0.18);
        var frame = PenOf(Ink, 0.35);

        // The border.
        dc.DrawRectangle(null, frame, new Rect(P(new Vector2(DrawingSheet.Margin, sheet.Paper.Y - DrawingSheet.Margin)),
                                               P(new Vector2(sheet.Paper.X - DrawingSheet.Margin, DrawingSheet.Margin))));

        // Where text already stands, so figures that would land on it can be moved or left off.
        var taken = new List<Rect>();

        foreach (var placed in sheet.Views)
        {
            var lines = placed.Lines;
            Vector2 OnPaper(Vector2 model) => placed.Corner + (model - lines.Min) * placed.Scale;

            // Not on the isometric, which is there to show the shape at a glance: dashed lines
            // through it only make it harder to read.
            if (options.HiddenLines && lines.View != DrawingView.Isometric && lines.Hidden.Count > 0)
                dc.DrawGeometry(null, hidden, Segments(lines.Hidden.Select(l => (P(OnPaper(l.A)), P(OnPaper(l.B))))));
            dc.DrawGeometry(null, outline, Segments(lines.Visible.Select(l => (P(OnPaper(l.A)), P(OnPaper(l.B))))));

            var size = lines.Size * placed.Scale;
            taken.Add(Text(dc, Caption(lines.View), P(placed.Corner + new Vector2(size.X / 2f, size.Y + 4f)), 2.5, Faint, centre: true));
        }

        foreach (var dimension in sheet.Dimensions)
            DrawDimension(dc, dimension, P, thin, options, taken);

        DrawTitleBlock(dc, sheet, options, date, P, frame, thin);

        Text(dc, "Drawn with 3DFastCraft", P(new Vector2(DrawingSheet.Margin + 2f, DrawingSheet.Margin + 2f)), 2.2, Faint, centre: false);

        return visual;
    }

    private static string Caption(DrawingView view) => view switch
    {
        DrawingView.Front => "FRONT",
        DrawingView.Top => "TOP",
        DrawingView.Right => "RIGHT",
        _ => "ISOMETRIC"
    };

    /// <summary>
    /// A dimension: extension lines out from the part, a line between them with an arrow at each
    /// end, and the figure between the line and the part - turned to read from the right on an
    /// upright one, as drawings are read. On the far side it ran into the caption of the view
    /// beyond.
    ///
    /// A figure longer than the space between its arrows is written outside, past one end, the
    /// dimension line run out to it - or left off, as asked. With overlaps to be left off, a
    /// dimension whose figure would land on text already drawn is left off whole.
    /// </summary>
    private static void DrawDimension(DrawingContext dc, Dimension d, Func<Vector2, Point> P, Pen thin, DrawingOptions options, List<Rect> taken)
    {
        var outward = Vector2.Normalize(d.Offset);
        var along = Vector2.Normalize(d.To - d.From);
        const float clear = 1.5f, beyond = 2f, arrow = 2.8f, arrowHalf = 0.9f, height = 3.5f;

        var start = d.From + d.Offset;
        var end = d.To + d.Offset;
        bool upright = MathF.Abs(along.Y) > MathF.Abs(along.X);

        float wide = (float)(Formatted(d.Text, height, Ink).Width / PerMillimetre);
        bool fits = Vector2.Distance(start, end) >= wide + 2 * arrow + 1f;
        if (!fits && options.SmallFigures == SmallFigures.Hide) return;

        // Where the figure goes: between the arrows, or out past the end - or the start, if the
        // end's side is already written on.
        Vector2 Place(Vector2 at) => at - outward * 3.2f;
        Rect Box(Vector2 centre)
        {
            var c = P(centre);
            double w = wide * PerMillimetre, h = height * 1.3 * PerMillimetre;
            return upright ? new Rect(c.X - h / 2, c.Y - w / 2, h, w) : new Rect(c.X - w / 2, c.Y - h / 2, w, h);
        }

        var figure = Place((start + end) / 2f);
        Vector2? leader = null;
        if (!fits)
        {
            var past = end + along * (arrow + 1f + wide / 2f);
            var before = start - along * (arrow + 1f + wide / 2f);
            figure = Place(past);
            leader = end + along * (arrow + 1f);
            if (taken.Any(t => t.IntersectsWith(Box(figure))) && !taken.Any(t => t.IntersectsWith(Box(Place(before)))))
            {
                figure = Place(before);
                leader = start - along * (arrow + 1f);
            }
        }

        var box = Box(figure);
        if (options.HideOverlaps && taken.Any(t => t.IntersectsWith(box))) return;
        taken.Add(box);

        dc.DrawLine(thin, P(d.From + outward * clear), P(d.From + d.Offset + outward * beyond));
        dc.DrawLine(thin, P(d.To + outward * clear), P(d.To + d.Offset + outward * beyond));
        dc.DrawLine(thin, P(start), P(end));
        if (leader is { } l) dc.DrawLine(thin, P(Vector2.Distance(l, end) < Vector2.Distance(l, start) ? end : start), P(l));

        // Too short a line for the arrows to fit inside it: they point in from outside instead.
        Arrow(start, fits ? along : -along);
        Arrow(end, fits ? -along : along);

        Text(dc, d.Text, P(figure), height, Ink, centre: true, turned: upright);

        void Arrow(Vector2 tip, Vector2 into)
        {
            var side = new Vector2(-into.Y, into.X) * arrowHalf;
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                c.BeginFigure(P(tip), true, true);
                c.LineTo(P(tip + into * arrow + side), true, false);
                c.LineTo(P(tip + into * arrow - side), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(Ink, null, geometry);
        }
    }

    private static void DrawTitleBlock(DrawingContext dc, SheetLayout sheet, DrawingOptions options, DateTime date,
                                       Func<Vector2, Point> P, Pen frame, Pen thin, string first = "SCALE")
    {
        var corner = sheet.TitleCorner;
        var size = sheet.TitleSize;
        float half = size.Y / 2f;

        dc.DrawRectangle(Brushes.White, frame, new Rect(P(corner + new Vector2(0, size.Y)), P(corner + new Vector2(size.X, 0))));
        dc.DrawLine(thin, P(corner + new Vector2(0, half)), P(corner + new Vector2(size.X, half)));

        Text(dc, options.Title, P(corner + new Vector2(3f, half + half / 2f)), 5.0, Ink, centre: false, middle: true);

        (string Label, string Value)[] cells =
        [
            (first, sheet.ScaleText),
            ("UNITS", options.ShownUnitLabel),
            ("PROJECTION", options.Projection == Projection.FirstAngle ? "First angle" : "Third angle"),
            ("DATE", date.ToString("d", CultureInfo.CurrentCulture))
        ];
        float[] widths = [18f, 18f, 38f, 36f];

        float x = corner.X;
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) dc.DrawLine(thin, P(new Vector2(x, corner.Y)), P(new Vector2(x, corner.Y + half)));
            Text(dc, cells[i].Label, P(new Vector2(x + 2f, corner.Y + half - 2.2f)), 1.8, Faint, centre: false, middle: true);
            Text(dc, cells[i].Value, P(new Vector2(x + 2f, corner.Y + 3.8f)), 3.2, Ink, centre: false, middle: true);
            x += widths[i];
        }
    }

    private static FormattedText Formatted(string text, double millimetres, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), millimetres * PerMillimetre, brush, 1.0);

    /// <summary>Text this many millimetres tall, placed by its centre, its left end, or turned upright. Returns where it went, unturned.</summary>
    private static Rect Text(DrawingContext dc, string text, Point at, double millimetres, Brush brush,
                             bool centre, bool turned = false, bool middle = true)
    {
        var formatted = Formatted(text, millimetres, brush);

        double dx = centre ? -formatted.Width / 2 : 0;
        double dy = middle ? -formatted.Height / 2 : 0;

        if (turned) dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
        dc.DrawText(formatted, new Point(at.X + dx, at.Y + dy));
        if (turned) dc.Pop();
        return new Rect(at.X + dx, at.Y + dy, formatted.Width, formatted.Height);
    }

    /// <summary>One object on a parts list: what it is called, the lines of its isometric, and what it measures.</summary>
    public sealed record PartEntry(string Name, ViewLines Isometric, IReadOnlyList<string> Details);

    private const float PartCellWidth = 135f, PartCellHeight = 44f;

    private static int PartColumns(Vector2 paper) => Math.Max(1, (int)((paper.X - 2 * DrawingSheet.Margin - 4f) / PartCellWidth));

    private static int PartRows(Vector2 paper) => Math.Max(1, (int)((paper.Y - 2 * DrawingSheet.Margin - DrawingSheet.TitleHeight - 12f) / PartCellHeight));

    /// <summary>How many parts go on a page this size.</summary>
    public static int PartsPerPage(Vector2 paper) => PartColumns(paper) * PartRows(paper);

    /// <summary>
    /// A page of the parts list: each object's isometric in a box, its name beside it and what it
    /// measures under the name, and the title block with the page where the scale was.
    /// </summary>
    public static DrawingVisual RenderParts(IReadOnlyList<PartEntry> parts, int page, Vector2 paper, DrawingOptions options, DateTime date)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(PageSize(paper)));

        Point P(Vector2 mm) => new(mm.X * PerMillimetre, (paper.Y - mm.Y) * PerMillimetre);
        Pen PenOf(Brush brush, double millimetres) =>
            Frozen(new Pen(brush, millimetres * PerMillimetre) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        var outline = PenOf(Ink, 0.3);
        var thin = PenOf(Ink, 0.18);
        var frame = PenOf(Ink, 0.35);

        dc.DrawRectangle(null, frame, new Rect(P(new Vector2(DrawingSheet.Margin, paper.Y - DrawingSheet.Margin)),
                                               P(new Vector2(paper.X - DrawingSheet.Margin, DrawingSheet.Margin))));

        int columns = PartColumns(paper), rows = PartRows(paper), each = columns * rows;
        int pages = Math.Max(1, (parts.Count + each - 1) / each);
        float left = DrawingSheet.Margin + 4f, top = paper.Y - DrawingSheet.Margin - 6f;

        Text(dc, "PARTS", P(new Vector2(left, top)), 3.2, Faint, centre: false);
        top -= 4f;

        for (int k = 0; k < each && page * each + k < parts.Count; k++)
        {
            var part = parts[page * each + k];
            int column = k / rows, row = k % rows;
            var cell = new Vector2(left + column * PartCellWidth, top - (row + 1) * PartCellHeight);

            // The picture, as large as fits its box and centred in it.
            const float box = 38f;
            dc.DrawRectangle(null, thin, new Rect(P(cell + new Vector2(0, box + 2f)), P(cell + new Vector2(box, 2f))));
            var lines = part.Isometric;
            float fit = MathF.Min((box - 4f) / MathF.Max(lines.Size.X, 1e-3f), (box - 4f) / MathF.Max(lines.Size.Y, 1e-3f));
            var origin = cell + new Vector2((box - lines.Size.X * fit) / 2f, 2f + (box - lines.Size.Y * fit) / 2f);
            dc.DrawGeometry(null, outline, Segments(lines.Visible.Select(l => (P(origin + (l.A - lines.Min) * fit), P(origin + (l.B - lines.Min) * fit)))));

            var at = cell + new Vector2(box + 4f, box - 2f);
            Text(dc, $"{page * each + k + 1}. {part.Name}", P(at), 4.0, Ink, centre: false);
            for (int i = 0; i < part.Details.Count; i++)
                Text(dc, part.Details[i], P(at - new Vector2(0, 6.5f + i * 5f)), 3.0, Ink, centre: false);
        }

        var sheet = new SheetLayout(paper, 1f, $"{page + 1} / {pages}", [], [],
            new Vector2(paper.X - DrawingSheet.Margin - DrawingSheet.TitleWidth, DrawingSheet.Margin), new Vector2(DrawingSheet.TitleWidth, DrawingSheet.TitleHeight));
        DrawTitleBlock(dc, sheet, options, date, P, frame, thin, "PAGE");

        Text(dc, "Drawn with 3DFastCraft", P(new Vector2(DrawingSheet.Margin + 2f, DrawingSheet.Margin + 2f)), 2.2, Faint, centre: false);
        return visual;
    }

    private static System.Windows.Media.Geometry Segments(IEnumerable<(Point A, Point B)> segments)
    {
        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            foreach (var (a, b) in segments)
            {
                c.BeginFigure(a, false, false);
                c.LineTo(b, true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>Shows one drawn sheet as an element, so it can be scaled to fit a window.</summary>
public sealed class SheetHost : FrameworkElement
{
    private Visual? sheet;

    public void Show(Visual visual, Size size)
    {
        if (sheet is not null) RemoveVisualChild(sheet);
        sheet = visual;
        AddVisualChild(visual);
        Width = size.Width;
        Height = size.Height;
    }

    protected override int VisualChildrenCount => sheet is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) => sheet ?? throw new ArgumentOutOfRangeException(nameof(index));
}
