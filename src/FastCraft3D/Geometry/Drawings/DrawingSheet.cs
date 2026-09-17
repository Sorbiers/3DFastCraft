using System.Globalization;
using System.Numerics;

namespace FastCraft3D.Geometry.Drawings;

/// <summary>Which way round the views are laid out.</summary>
public enum Projection
{
    /// <summary>ISO, used across Europe and most of the world: the top view goes below the front.</summary>
    FirstAngle,

    /// <summary>ASME, used in North America: the top view goes above the front.</summary>
    ThirdAngle
}

/// <param name="Title">The part's name, in the title block.</param>
/// <param name="UnitLabel">What the dimensions are written in.</param>
/// <param name="UnitMillimetres">How many millimetres one of those is.</param>
public sealed record DrawingOptions
{
    public Projection Projection { get; init; } = Projection.FirstAngle;
    public bool HiddenLines { get; init; } = true;
    public bool Dimensions { get; init; } = true;
    public bool Isometric { get; init; } = true;
    public string Title { get; init; } = "Untitled";
    public string UnitLabel { get; init; } = "mm";
    public float UnitMillimetres { get; init; } = 1f;
}

/// <summary>A view put on the paper: its lines, where its lower left corner is, in paper millimetres with Y up.</summary>
public sealed record PlacedView(ViewLines Lines, Vector2 Corner);

/// <summary>A dimension on the paper: from one point to another, the line drawn a distance out from them, and what it says.</summary>
/// <param name="From">Where it starts, on the part.</param>
/// <param name="To">Where it ends, on the part.</param>
/// <param name="Offset">How far out the dimension line stands, square to the measurement: below or to the right.</param>
public sealed record Dimension(Vector2 From, Vector2 To, Vector2 Offset, string Text);

/// <param name="Paper">The page, in millimetres.</param>
/// <param name="Scale">Paper millimetres to a model millimetre.</param>
public sealed record SheetLayout(
    Vector2 Paper, float Scale, string ScaleText, List<PlacedView> Views, List<Dimension> Dimensions,
    Vector2 TitleCorner, Vector2 TitleSize);

/// <summary>
/// Lays three views of a part out on a page, as an engineering drawing is laid out: the front, the
/// view from above lined up with it, the view from the right beside it, and an isometric in the
/// corner left over. A standard scale is chosen - 1:1, 1:2, 2:1 and so on - the largest the views
/// fit at, with room for their dimensions and a title block.
///
/// The measurements given are the overall ones - width and height on the front, depth on the view
/// from above - each once, as a drawing gives them. Everything here is in paper millimetres; the
/// window turns them into ink.
/// </summary>
public static class DrawingSheet
{
    public const float Margin = 10f;
    public const float TitleHeight = 24f;
    public const float TitleWidth = 110f;

    /// <summary>Paper left between views, and round them for their dimensions.</summary>
    public const float Gap = 18f;

    /// <summary>How far a dimension line stands off the part.</summary>
    public const float DimensionOffset = 8f;

    private static readonly float[] Scales = [20f, 10f, 5f, 2f, 1f, 1f / 2f, 1f / 5f, 1f / 10f, 1f / 20f, 1f / 50f, 1f / 100f, 1f / 200f, 1f / 500f, 1f / 1000f];

    public static SheetLayout Layout(IReadOnlyDictionary<DrawingView, ViewLines> views, Vector2 paper, DrawingOptions options)
    {
        ViewLines? Get(DrawingView view) =>
            views.TryGetValue(view, out var lines) && (view != DrawingView.Isometric || options.Isometric) ? lines : null;

        var front = Get(DrawingView.Front);
        var top = Get(DrawingView.Top);
        var right = Get(DrawingView.Right);
        var iso = Get(DrawingView.Isometric);

        // The grid of views, row by row from the top of the page. Third angle puts the view from
        // above over the front and the right side to its right; first angle mirrors both.
        var grid = options.Projection == Projection.ThirdAngle
            ? new[,] { { top, iso }, { front, right } }
            : new[,] { { right, front }, { iso, top } };

        static Vector2 Size(ViewLines? v) => v?.Size ?? Vector2.Zero;

        float[] columns = [MathF.Max(Size(grid[0, 0]).X, Size(grid[1, 0]).X), MathF.Max(Size(grid[0, 1]).X, Size(grid[1, 1]).X)];
        float[] rows = [MathF.Max(Size(grid[0, 0]).Y, Size(grid[0, 1]).Y), MathF.Max(Size(grid[1, 0]).Y, Size(grid[1, 1]).Y)];

        float left = Margin, bottom = Margin + TitleHeight + 4f;
        float width = paper.X - 2f * Margin, height = paper.Y - Margin - bottom;

        // Each column and row carries a gap for the dimensions beside it and the space between.
        float room = MathF.Min(
            (width - 3f * Gap) / MathF.Max(columns[0] + columns[1], 1e-3f),
            (height - 3f * Gap) / MathF.Max(rows[0] + rows[1], 1e-3f));
        float scale = Scales.FirstOrDefault(s => s <= room + 1e-6f, Scales[^1]);

        float columnWidth0 = columns[0] * scale, columnWidth1 = columns[1] * scale;
        float rowHeight0 = rows[0] * scale, rowHeight1 = rows[1] * scale;

        // Centred on the page, the two columns and rows packed with a gap between.
        float usedWidth = columnWidth0 + columnWidth1 + Gap;
        float usedHeight = rowHeight0 + rowHeight1 + Gap;
        float x0 = left + (width - usedWidth) / 2f;
        float x1 = x0 + columnWidth0 + Gap;
        float y1 = bottom + (height - usedHeight) / 2f;     // the lower row
        float y0 = y1 + rowHeight1 + Gap;                   // the upper row

        var placed = new List<PlacedView>();
        var corners = new Dictionary<DrawingView, Vector2>();
        bool thirdAngle = options.Projection == Projection.ThirdAngle;

        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
            {
                if (grid[r, c] is not { } v) continue;

                float cellX = c == 0 ? x0 : x1, cellY = r == 0 ? y0 : y1;
                float cellWidth = c == 0 ? columnWidth0 : columnWidth1, cellHeight = r == 0 ? rowHeight0 : rowHeight1;
                var size = v.Size * scale;

                // Each view is set against the front, as its projection lines would carry it: the
                // view from above shares the front's width and sits right above or below it, the
                // view from the right shares its height and sits right beside it. The isometric is
                // only centred in what is left.
                var corner = v.View switch
                {
                    DrawingView.Top => new Vector2(cellX, thirdAngle ? cellY : cellY + cellHeight - size.Y),
                    DrawingView.Right => new Vector2(thirdAngle ? cellX : cellX + cellWidth - size.X, cellY),
                    DrawingView.Front => new Vector2(cellX, cellY),
                    _ => new Vector2(cellX + (cellWidth - size.X) / 2f, cellY + (cellHeight - size.Y) / 2f)
                };

                placed.Add(new PlacedView(v, corner));
                corners[v.View] = corner;
            }

        var dimensions = new List<Dimension>();
        if (options.Dimensions)
        {
            if (front is not null && corners.TryGetValue(DrawingView.Front, out var f))
            {
                var size = front.Size * scale;
                dimensions.Add(new(f, f + new Vector2(size.X, 0), new(0, -DimensionOffset), Measure(front.Size.X)));
                dimensions.Add(new(f + new Vector2(size.X, 0), f + size, new(DimensionOffset, 0), Measure(front.Size.Y)));
            }

            if (top is not null && corners.TryGetValue(DrawingView.Top, out var t))
            {
                var size = top.Size * scale;
                dimensions.Add(new(t + new Vector2(size.X, 0), t + size, new(DimensionOffset, 0), Measure(top.Size.Y)));
            }
        }

        return new SheetLayout(paper, scale, ScaleText(scale), placed, dimensions,
            new Vector2(paper.X - Margin - TitleWidth, Margin), new Vector2(TitleWidth, TitleHeight));

        string Measure(float millimetres) =>
            (millimetres / MathF.Max(options.UnitMillimetres, 1e-6f)).ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>"1:2", "1:1", "5:1".</summary>
    public static string ScaleText(float scale) =>
        scale >= 1f
            ? $"{MathF.Round(scale).ToString(CultureInfo.InvariantCulture)}:1"
            : $"1:{MathF.Round(1f / scale).ToString(CultureInfo.InvariantCulture)}";
}
