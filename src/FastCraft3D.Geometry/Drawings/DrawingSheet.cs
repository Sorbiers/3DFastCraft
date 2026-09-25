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

/// <summary>How a dimension reads when the model stands for something a different size.</summary>
public enum ScaleFormat
{
    /// <summary>Only the model's own size - what the scale is set to makes no difference to what is written.</summary>
    ModelOnly,

    /// <summary>The model's own size, with what it stands for in brackets: "50 mm (4.35 m)".</summary>
    ModelWithReal,

    /// <summary>What it stands for, with the model's own size in brackets: "4.35 m (50 mm)".</summary>
    RealWithModel,

    /// <summary>Only what it stands for - what the model itself measures is left off entirely.</summary>
    RealOnly
}

/// <param name="Title">The part's name, in the title block.</param>
/// <param name="AllDimensions">Every straight, axis-aligned edge dimensioned on each view, not just the overall size.</param>
/// <param name="UnitLabel">What the dimensions are written in.</param>
/// <param name="UnitMillimetres">How many millimetres one of those is.</param>
/// <param name="ModelScale">What the model stands for: 87 means 1:87. Changes nothing about the geometry, only how a dimension is written.</param>
public sealed record DrawingOptions
{
    public Projection Projection { get; init; } = Projection.FirstAngle;
    public bool HiddenLines { get; init; } = true;
    public bool Dimensions { get; init; } = true;
    public bool AllDimensions { get; init; }
    public bool Isometric { get; init; } = true;
    public string Title { get; init; } = "Untitled";
    public string UnitLabel { get; init; } = "mm";
    public float UnitMillimetres { get; init; } = 1f;
    public float ModelScale { get; init; } = 1f;
    public ScaleFormat ScaleFormat { get; init; } = ScaleFormat.ModelWithReal;

    /// <summary>
    /// What the title block's UNITS cell says: <see cref="UnitLabel"/> for a dimension written in
    /// it, plain or with the real size beside it - but once the real size leads or stands alone, a
    /// figure is millimetres or metres depending on its own size, so no one fixed label is honest
    /// and the cell says as much instead.
    /// </summary>
    public string ShownUnitLabel => ModelScale <= 1.001f || ScaleFormat is ScaleFormat.ModelOnly or ScaleFormat.ModelWithReal
        ? UnitLabel
        : "mm, m";
}

/// <summary>
/// A view put on the paper: its lines, where its lower left corner is, in paper millimetres with
/// Y up, and its own scale.
/// </summary>
/// <param name="Scale">
/// Usually <see cref="SheetLayout.Scale"/>, the same as every other view - except the isometric,
/// which is not measured from and is drawn at whatever fits its own corner of the page. A wide,
/// shallow arrangement of several parts can have an isometric far taller than any straight-on
/// view of it, and sizing the whole sheet to fit that one corner shrank the three views actually
/// carrying dimensions far more than they needed, and their figures along with them.
/// </param>
public sealed record PlacedView(ViewLines Lines, Vector2 Corner, float Scale);

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
/// With <see cref="DrawingOptions.AllDimensions"/> off, the measurements given are the overall ones
/// only - width and height on the front, depth on the view from above - each once, as a plain
/// drawing gives them. With it on, every straight, axis-aligned edge on every view is dimensioned
/// too, as a chain of the distances between them, with the overall added once more further out -
/// so any one view carries everything it shows without having to be read alongside another.
/// A curved or slanted edge never comes out exactly level or plumb, so a hole or a fillet is left
/// alone rather than dimensioned wrongly.
///
/// The scale is chosen to fit the front, top and right views and their dimensions; the isometric
/// is not measured from, so it is drawn at whatever scale fits its own corner of the page instead
/// - see <see cref="PlacedView.Scale"/>.
///
/// Everything here is in paper millimetres; the window turns them into ink.
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

    /// <summary>
    /// How close two edges have to sit, along the axis they are being measured on, to count as the
    /// same feature. Loose enough for a mesh's own floating-point noise, tight enough that even a
    /// coarsely faceted hole - whose edges come close to level or plumb only very near its own top,
    /// bottom or sides, and even there are not exactly so - never passes it.
    /// </summary>
    public const float Coincide = 0.02f;

    private static readonly float[] Scales = [20f, 10f, 5f, 2f, 1f, 1f / 2f, 1f / 5f, 1f / 10f, 1f / 20f, 1f / 50f, 1f / 100f, 1f / 200f, 1f / 500f, 1f / 1000f];

    public static SheetLayout Layout(IReadOnlyDictionary<DrawingView, ViewLines> views, Vector2 paper, DrawingOptions options)
    {
        ViewLines? Get(DrawingView view) =>
            views.TryGetValue(view, out var lines) && (view != DrawingView.Isometric || options.Isometric) ? lines : null;

        var front = Get(DrawingView.Front);
        var top = Get(DrawingView.Top);
        var right = Get(DrawingView.Right);
        var iso = Get(DrawingView.Isometric);

        bool chained = options.Dimensions && options.AllDimensions;

        // Every distinct position along each view's own two axes, worked out only when every
        // dimension is asked for - the plain overall figure never needed more than the view's own
        // size already gave it.
        List<float> frontX = [], frontZ = [], topX = [], topY = [], rightY = [], rightZ = [];
        if (chained)
        {
            if (front is not null) { frontX = AxisPositions(front, alongX: true); frontZ = AxisPositions(front, alongX: false); }
            if (top is not null) { topX = AxisPositions(top, alongX: true); topY = AxisPositions(top, alongX: false); }
            if (right is not null) { rightY = AxisPositions(right, alongX: true); rightZ = AxisPositions(right, alongX: false); }
        }

        // The grid of views, row by row from the top of the page. Third angle puts the view from
        // above over the front and the right side to its right; first angle mirrors both.
        var grid = options.Projection == Projection.ThirdAngle
            ? new[,] { { top, iso }, { front, right } }
            : new[,] { { right, front }, { iso, top } };

        // The isometric's own size never enters the columns and rows a scale is chosen to fit:
        // see PlacedView.Scale.
        static Vector2 Size(ViewLines? v) => v is null || v.View == DrawingView.Isometric ? Vector2.Zero : v.Size;

        float[] columns = [MathF.Max(Size(grid[0, 0]).X, Size(grid[1, 0]).X), MathF.Max(Size(grid[0, 1]).X, Size(grid[1, 1]).X)];
        float[] rows = [MathF.Max(Size(grid[0, 0]).Y, Size(grid[0, 1]).Y), MathF.Max(Size(grid[1, 0]).Y, Size(grid[1, 1]).Y)];

        float left = Margin, bottom = Margin + TitleHeight + 4f;
        float width = paper.X - 2f * Margin, height = paper.Y - Margin - bottom;

        // A chain stacks its rows further from the part than a single overall dimension does, so
        // the gap that carries it has to grow with however many rows the busiest view needs.
        float gap = Gap;
        if (chained)
        {
            int stack = new[] { frontX, frontZ, topX, topY, rightY, rightZ }.Max(Rows);
            if (stack > 1) gap += (stack - 1) * (DimensionOffset + 6f);
        }

        // Each column and row carries a gap for the dimensions beside it and the space between.
        float room = MathF.Min(
            (width - 3f * gap) / MathF.Max(columns[0] + columns[1], 1e-3f),
            (height - 3f * gap) / MathF.Max(rows[0] + rows[1], 1e-3f));
        float scale = Scales.FirstOrDefault(s => s <= room + 1e-6f, Scales[^1]);

        float columnWidth0 = columns[0] * scale, columnWidth1 = columns[1] * scale;
        float rowHeight0 = rows[0] * scale, rowHeight1 = rows[1] * scale;

        // Centred on the page, the two columns and rows packed with a gap between.
        float usedWidth = columnWidth0 + columnWidth1 + gap;
        float usedHeight = rowHeight0 + rowHeight1 + gap;
        float x0 = left + (width - usedWidth) / 2f;
        float x1 = x0 + columnWidth0 + gap;
        float y1 = bottom + (height - usedHeight) / 2f;     // the lower row
        float y0 = y1 + rowHeight1 + gap;                   // the upper row

        var placed = new List<PlacedView>();
        var corners = new Dictionary<DrawingView, Vector2>();
        bool thirdAngle = options.Projection == Projection.ThirdAngle;

        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
            {
                if (grid[r, c] is not { } v) continue;

                float cellX = c == 0 ? x0 : x1, cellY = r == 0 ? y0 : y1;
                float cellWidth = c == 0 ? columnWidth0 : columnWidth1, cellHeight = r == 0 ? rowHeight0 : rowHeight1;

                // The isometric earned no room of its own in the cell, so it is drawn as large as
                // it can be within whatever the view sharing that cell needed - never larger than
                // the scale everything else is drawn at, so it never reads as more important.
                float viewScale = v.View == DrawingView.Isometric
                    ? MathF.Min(scale, MathF.Min(cellWidth / MathF.Max(v.Size.X, 1e-3f), cellHeight / MathF.Max(v.Size.Y, 1e-3f)))
                    : scale;
                var size = v.Size * viewScale;

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

                placed.Add(new PlacedView(v, corner, viewScale));
                corners[v.View] = corner;
            }

        var dimensions = new List<Dimension>();
        if (options.Dimensions)
        {
            if (front is not null && corners.TryGetValue(DrawingView.Front, out var f))
            {
                if (!chained)
                {
                    var size = front.Size * scale;
                    dimensions.Add(new(f, f + new Vector2(size.X, 0), new(0, -DimensionOffset), Measure(front.Size.X)));
                    dimensions.Add(new(f + new Vector2(size.X, 0), f + size, new(DimensionOffset, 0), Measure(front.Size.Y)));
                }
                else
                {
                    AddChain(dimensions, frontX, p => (p - front.Min.X) * scale, f, new(1, 0), new(0, -1), Measure);
                    var rightEdge = f + new Vector2(front.Size.X * scale, 0);
                    AddChain(dimensions, frontZ, p => (p - front.Min.Y) * scale, rightEdge, new(0, 1), new(1, 0), Measure);
                }
            }

            if (top is not null && corners.TryGetValue(DrawingView.Top, out var t))
            {
                if (!chained)
                {
                    var size = top.Size * scale;
                    dimensions.Add(new(t + new Vector2(size.X, 0), t + size, new(DimensionOffset, 0), Measure(top.Size.Y)));
                }
                else
                {
                    AddChain(dimensions, topX, p => (p - top.Min.X) * scale, t, new(1, 0), new(0, -1), Measure);
                    var rightEdge = t + new Vector2(top.Size.X * scale, 0);
                    AddChain(dimensions, topY, p => (p - top.Min.Y) * scale, rightEdge, new(0, 1), new(1, 0), Measure);
                }
            }

            if (chained && right is not null && corners.TryGetValue(DrawingView.Right, out var rc))
            {
                // The front already carries a dimension out from whichever side of it is away
                // from the right view, so the right view's own height chain has to go the other
                // way, or the two would draw on top of each other.
                var outward = thirdAngle ? new Vector2(1, 0) : new Vector2(-1, 0);
                AddChain(dimensions, rightY, p => (p - right.Min.X) * scale, rc, new(1, 0), new(0, -1), Measure);
                AddChain(dimensions, rightZ, p => (p - right.Min.Y) * scale, rc, new(0, 1), outward, Measure);
            }
        }

        return new SheetLayout(paper, scale, ScaleText(scale), placed, dimensions,
            new Vector2(paper.X - Margin - TitleWidth, Margin), new Vector2(TitleWidth, TitleHeight));

        string Measure(float millimetres)
        {
            string bare = (millimetres / MathF.Max(options.UnitMillimetres, 1e-6f)).ToString("0.##", CultureInfo.CurrentCulture);
            if (options.ModelScale <= 1.001f || options.ScaleFormat == ScaleFormat.ModelOnly) return bare;

            float realMillimetres = millimetres * options.ModelScale;
            string real = realMillimetres >= 1000f
                ? $"{realMillimetres / 1000f:0.##} m"
                : $"{realMillimetres:0.#} mm";
            if (options.ScaleFormat == ScaleFormat.RealOnly) return real;

            string model = $"{bare} {options.UnitLabel}";
            return options.ScaleFormat == ScaleFormat.RealWithModel ? $"{real} ({model})" : $"{model} ({real})";
        }
    }

    /// <summary>
    /// A stepped chain of dimensions across every distinct position along one axis of one view,
    /// and - once there are more than two - the overall span as one more dimension, stacked
    /// further out again. Two positions are already the overall, so nothing extra is drawn and a
    /// plain box's drawing looks exactly as it did before every dimension was asked for.
    /// </summary>
    /// <param name="positions">Every distinct position along the axis, ascending, in the view's own flattened model millimetres.</param>
    /// <param name="toPaper">How far a position is from <paramref name="corner"/>, in paper millimetres.</param>
    /// <param name="corner">Where the chain starts on the paper: the view's own corner, moved to whichever edge this chain runs along.</param>
    /// <param name="along">Which way the chain runs across the paper: (1,0) below a view, (0,1) beside it.</param>
    /// <param name="outward">Which way each further-out row stacks, away from the part.</param>
    private static void AddChain(List<Dimension> into, IReadOnlyList<float> positions, Func<float, float> toPaper,
                                 Vector2 corner, Vector2 along, Vector2 outward, Func<float, string> measure)
    {
        if (positions.Count < 2) return;

        Vector2 At(float p) => corner + along * toPaper(p);

        for (int i = 0; i + 1 < positions.Count; i++)
            into.Add(new Dimension(At(positions[i]), At(positions[i + 1]), outward * DimensionOffset, measure(positions[i + 1] - positions[i])));

        if (positions.Count > 2)
            into.Add(new Dimension(At(positions[0]), At(positions[^1]), outward * (2f * DimensionOffset), measure(positions[^1] - positions[0])));
    }

    /// <summary>How many rows deep a chain along these positions stacks: none, one, or a chain plus its overall.</summary>
    private static int Rows(IReadOnlyList<float> positions) => positions.Count switch { < 2 => 0, 2 => 1, _ => 2 };

    /// <summary>
    /// Every distinct position along one of a view's own axes where a straight, axis-aligned edge
    /// sits: the vertical edges give widths, the horizontal ones heights. A curved or slanted edge
    /// - a hole, a fillet, a gear tooth - never comes out exactly level or plumb, so it leaves
    /// nothing here rather than being dimensioned wrongly.
    /// </summary>
    private static List<float> AxisPositions(ViewLines view, bool alongX)
    {
        var values = new List<float>();

        foreach (var line in view.Visible)
        {
            if (Vector2.DistanceSquared(line.A, line.B) < Coincide * Coincide) continue;

            bool vertical = MathF.Abs(line.A.X - line.B.X) < Coincide;
            bool horizontal = MathF.Abs(line.A.Y - line.B.Y) < Coincide;

            if (alongX && vertical) values.Add(line.A.X);
            else if (!alongX && horizontal) values.Add(line.A.Y);
        }

        values.Sort();
        var distinct = new List<float>();
        foreach (var v in values)
        {
            if (distinct.Count > 0 && v - distinct[^1] <= Coincide) continue;
            distinct.Add(v);
        }

        return distinct;
    }

    /// <summary>"1:2", "1:1", "5:1".</summary>
    public static string ScaleText(float scale) =>
        scale >= 1f
            ? $"{MathF.Round(scale).ToString(CultureInfo.InvariantCulture)}:1"
            : $"1:{MathF.Round(1f / scale).ToString(CultureInfo.InvariantCulture)}";
}
