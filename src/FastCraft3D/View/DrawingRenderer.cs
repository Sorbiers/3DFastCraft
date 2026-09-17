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

        foreach (var placed in sheet.Views)
        {
            var lines = placed.Lines;
            Vector2 OnPaper(Vector2 model) => placed.Corner + (model - lines.Min) * sheet.Scale;

            // Not on the isometric, which is there to show the shape at a glance: dashed lines
            // through it only make it harder to read.
            if (options.HiddenLines && lines.View != DrawingView.Isometric && lines.Hidden.Count > 0)
                dc.DrawGeometry(null, hidden, Segments(lines.Hidden.Select(l => (P(OnPaper(l.A)), P(OnPaper(l.B))))));
            dc.DrawGeometry(null, outline, Segments(lines.Visible.Select(l => (P(OnPaper(l.A)), P(OnPaper(l.B))))));

            var size = lines.Size * sheet.Scale;
            Text(dc, Caption(lines.View), P(placed.Corner + new Vector2(size.X / 2f, size.Y + 4f)), 2.5, Faint, centre: true);
        }

        foreach (var dimension in sheet.Dimensions)
            DrawDimension(dc, dimension, P, thin);

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
    /// </summary>
    private static void DrawDimension(DrawingContext dc, Dimension d, Func<Vector2, Point> P, Pen thin)
    {
        var outward = Vector2.Normalize(d.Offset);
        var along = Vector2.Normalize(d.To - d.From);
        const float clear = 1.5f, beyond = 2f, arrow = 2.8f, arrowHalf = 0.9f;

        dc.DrawLine(thin, P(d.From + outward * clear), P(d.From + d.Offset + outward * beyond));
        dc.DrawLine(thin, P(d.To + outward * clear), P(d.To + d.Offset + outward * beyond));

        var start = d.From + d.Offset;
        var end = d.To + d.Offset;
        dc.DrawLine(thin, P(start), P(end));

        Arrow(start, along);
        Arrow(end, -along);

        var middle = (start + end) / 2f - outward * 3.2f;
        bool upright = MathF.Abs(along.Y) > MathF.Abs(along.X);
        Text(dc, d.Text, P(middle), 3.5, Ink, centre: true, turned: upright);

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
                                       Func<Vector2, Point> P, Pen frame, Pen thin)
    {
        var corner = sheet.TitleCorner;
        var size = sheet.TitleSize;
        float half = size.Y / 2f;

        dc.DrawRectangle(Brushes.White, frame, new Rect(P(corner + new Vector2(0, size.Y)), P(corner + new Vector2(size.X, 0))));
        dc.DrawLine(thin, P(corner + new Vector2(0, half)), P(corner + new Vector2(size.X, half)));

        Text(dc, options.Title, P(corner + new Vector2(3f, half + half / 2f)), 5.0, Ink, centre: false, middle: true);

        (string Label, string Value)[] cells =
        [
            ("SCALE", sheet.ScaleText),
            ("UNITS", options.UnitLabel),
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

    /// <summary>Text this many millimetres tall, placed by its centre, its left end, or turned upright.</summary>
    private static void Text(DrawingContext dc, string text, Point at, double millimetres, Brush brush,
                             bool centre, bool turned = false, bool middle = true)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), millimetres * PerMillimetre, brush, 1.0);

        double dx = centre ? -formatted.Width / 2 : 0;
        double dy = middle ? -formatted.Height / 2 : 0;

        if (turned) dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
        dc.DrawText(formatted, new Point(at.X + dx, at.Y + dy));
        if (turned) dc.Pop();
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
