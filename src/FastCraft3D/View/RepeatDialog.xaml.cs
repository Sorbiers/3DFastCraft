using System.Globalization;
using System.Numerics;
using System.Windows;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks how to repeat the selection: along a line, or round a circle.
///
/// It exists because a staircase used to be a dozen boxes typed in one at a time - name, three
/// sizes and three positions apiece - and so did a row of dowels, and a wall of openings. A step
/// and a rise per copy covers all three. The circle reaches what a line cannot reach at all: a
/// bolt circle, a ring of teeth, crenellations, and with a rise, a spiral stair.
///
/// One dialog rather than two commands, because it is the same idea either way - make N copies,
/// arranged - and only the fields differ.
/// </summary>
public partial class RepeatDialog : ToolPanel
{
    private readonly IReadOnlyList<SceneObject> subjects;

    /// <summary>Set when Repeat was pressed along a line.</summary>
    public RepeatSettings? Result { get; private set; }

    /// <summary>Set when Repeat was pressed round a circle. Only ever one of the three.</summary>
    public RingSettings? RingResult { get; private set; }

    /// <summary>Set when Repeat was pressed in a grid.</summary>
    public GridSettings? GridResult { get; private set; }

    /// <summary>
    /// Guards writing the computed angle into its own box: that raises TextChanged, which would
    /// re-enter the method doing the writing.
    /// </summary>
    private bool writingAngle;

    public RepeatDialog(IReadOnlyList<SceneObject> objects)
    {
        InitializeComponent();

        subjects = objects;
        SubjectText.Text = objects.Count == 1
            ? $"Repeating {objects[0].Name}."
            : $"Repeating {objects.Count} objects.";

        Loaded += (_, _) =>
        {
            // Seeded from where the selection already stands, so choosing the circle and pressing
            // Repeat leaves it where it is rather than flinging it out to some default radius.
            RadiusBox.Text = RepeatArray
                .DistanceFromCentre(subjects, Vector2.Zero)
                .ToString("0.##", CultureInfo.CurrentCulture);

            CountBox.SelectAll();
            CountBox.Focus();
            Describe();
        };
    }

    private bool RoundACircle => RingMode?.IsChecked == true;

    private bool InAGrid => GridMode?.IsChecked == true;

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (writingAngle) return;
        Describe();
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        // Checked fires while the window is still being built, before the panels exist.
        if (LinePanel is null || RingPanel is null || GridPanel is null || CountRow is null || GrowPanel is null) return;

        // A grid counts its copies in its own three boxes, and does not grow them.
        LinePanel.Visibility = !RoundACircle && !InAGrid ? Visibility.Visible : Visibility.Collapsed;
        RingPanel.Visibility = RoundACircle ? Visibility.Visible : Visibility.Collapsed;
        GridPanel.Visibility = InAGrid ? Visibility.Visible : Visibility.Collapsed;
        CountRow.Visibility = InAGrid ? Visibility.Collapsed : Visibility.Visible;
        GrowPanel.Visibility = InAGrid ? Visibility.Collapsed : Visibility.Visible;
        Describe();
    }

    private void Describe()
    {
        if (SummaryText is null) return;

        if (InAGrid) DescribeGrid();
        else if (RoundACircle) DescribeRing();
        else DescribeLine();
    }

    private void DescribeLine()
    {
        if (Read() is not { } s || s.Copies < 1)
        {
            SummaryText.Text = "Nothing to make.";
            return;
        }

        var span = s.Step * s.Copies;
        int made = s.Copies * subjects.Count;

        string reach = span.Length() < 1e-4f
            ? "stacked in the same place"
            : $"reaching {span.X:0.##}, {span.Y:0.##}, {span.Z:0.##} mm from where it starts";

        SummaryText.Text = $"{made} new object(s), {reach}.{Grown(s.Grow, s.Copies)}";
    }

    private void DescribeGrid()
    {
        if (ReadGrid() is not { } g)
        {
            SummaryText.Text = "Type how many along X, Y and Z - whole numbers, 1 or more.";
            return;
        }

        long stations = RepeatArray.GridStations(g);
        if (stations - 1 > RepeatArray.MaximumCopies)
        {
            SummaryText.Text = $"{stations:N0} is too many - {RepeatArray.MaximumCopies + 1:N0} at most, the original included.";
            return;
        }

        if (stations == 1)
        {
            SummaryText.Text = "One of each is just the original: nothing to make.";
            return;
        }

        var size = BedPlacement.Reach(subjects).Size;
        var pitch = g.Gaps ? size + g.Spacing : g.Spacing;
        var covers = new Vector3(
            size.X + pitch.X * (g.Columns - 1),
            size.Y + pitch.Y * (g.Rows - 1),
            size.Z + pitch.Z * (g.Layers - 1));

        SummaryText.Text = $"{g.Columns} x {g.Rows}" + (g.Layers > 1 ? $" x {g.Layers}" : "")
                           + $" - {(stations - 1) * subjects.Count} new object(s), covering about "
                           + $"{covers.X:0.#} x {covers.Y:0.#}" + (g.Layers > 1 ? $" x {covers.Z:0.#}" : "") + " mm.";
    }

    private GridSettings? ReadGrid()
    {
        if (!Whole(ColumnsBox?.Text, out int columns) || !Whole(RowsBox?.Text, out int rows) || !Whole(LayersBox?.Text, out int layers))
            return null;

        return new GridSettings(columns, rows, layers,
            new Vector3(Number(SpacingXBox?.Text), Number(SpacingYBox?.Text), Number(SpacingZBox?.Text)),
            GapsBox?.IsChecked == true);

        static bool Whole(string? text, out int value) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) && value >= 1;
    }

    private void DescribeRing()
    {
        SpreadTheAngle();

        if (ReadRing() is not { } s || s.Copies < 1)
        {
            SummaryText.Text = "Nothing to make.";
            return;
        }

        int made = s.Copies * subjects.Count;

        string arrangement = subjects.Count == 1
            ? $"{s.Copies + 1} round the circle"
            : $"{s.Copies + 1} groups of {subjects.Count} round the circle";

        string at = s.Radius < 1e-3f ? ", turning in place" : $" at {s.Radius:0.##} mm";

        // A full turn is Copies+1 steps rather than Copies, because the original is one of the
        // objects on the circle. Getting that wrong leaves either a gap or two things in one place.
        bool closes = MathF.Abs(s.StepDegrees) > 1e-4f
                   && MathF.Abs(MathF.IEEERemainder(s.StepDegrees * (s.Copies + 1), 360f)) < 0.05f;

        string turn = MathF.Abs(s.StepDegrees) < 1e-4f
            ? "all at the same angle"
            : closes
                ? $"one every {s.StepDegrees:0.##} degrees - a full turn"
                : $"one every {s.StepDegrees:0.##} degrees, spanning {s.StepDegrees * s.Copies:0.##}";

        float now = RepeatArray.DistanceFromCentre(subjects, s.Centre);
        string moved = MathF.Abs(now - s.Radius) < 5e-3f
            ? ""
            : $" Moves the selection from {now:0.##} to {s.Radius:0.##} mm from the centre.";

        string climb = MathF.Abs(s.Rise) < 1e-4f
            ? ""
            : $" Each copy is {s.Rise:0.##} mm higher, {s.Rise * s.Copies:0.##} mm over the ring.";

        SummaryText.Text =
            $"{made} new object(s) - {arrangement}{at}, {turn}.{moved}{climb}{Grown(s.Grow, s.Copies)}";
    }

    /// <summary>
    /// Fills in the angle when a full turn is asked for: 360 divided by the copies plus one,
    /// because the original is one of the objects on the circle. Five copies of one thing is six
    /// at 60 degrees, not five at 72 with a gap where the sixth should be.
    /// </summary>
    private void SpreadTheAngle()
    {
        if (AngleBox is null || FullTurnBox is null) return;

        bool full = FullTurnBox.IsChecked == true;

        // Left visible rather than hidden, so the number it worked out can be read off.
        AngleBox.IsEnabled = !full;

        if (!full || Count() is not { } copies) return;

        string spread = (360f / (copies + 1)).ToString("0.####", CultureInfo.CurrentCulture);
        if (AngleBox.Text == spread) return;

        writingAngle = true;
        AngleBox.Text = spread;
        writingAngle = false;
    }

    private int? Count() =>
        int.TryParse(CountBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int count)
            ? Math.Clamp(count, 1, RepeatArray.MaximumCopies)
            : null;

    private RepeatSettings? Read()
    {
        if (Count() is not { } copies) return null;

        return new RepeatSettings(
            copies,
            new Vector3(Number(StepXBox?.Text), Number(StepYBox?.Text), Number(StepZBox?.Text)),
            Grow(),
            AnchorBox?.IsChecked == true);
    }

    private RingSettings? ReadRing()
    {
        if (Count() is not { } copies) return null;

        return new RingSettings(
            copies,
            new Vector2(Number(CentreXBox?.Text), Number(CentreYBox?.Text)),
            MathF.Max(Number(RadiusBox?.Text), 0f),
            Number(AngleBox?.Text),
            Number(RiseBox?.Text),
            FaceBox?.IsChecked == true,
            Grow());
    }

    private Vector3 Grow() =>
        new(Number(GrowXBox?.Text), Number(GrowYBox?.Text), Number(GrowZBox?.Text));

    private static string Grown(Vector3 grow, int copies) =>
        grow.Length() < 1e-4f
            ? ""
            : $" The last one is {grow.X * copies:0.##} x {grow.Y * copies:0.##} x " +
              $"{grow.Z * copies:0.##} mm larger than the first.";

    private static float Number(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : 0f;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (InAGrid)
        {
            if (ReadGrid() is not { } grid || RepeatArray.GridStations(grid) is 1 or > RepeatArray.MaximumCopies + 1) return;
            GridResult = grid;
        }
        else if (RoundACircle)
        {
            if (ReadRing() is not { } ring) return;
            RingResult = ring;
        }
        else
        {
            if (Read() is not { } settings) return;
            Result = settings;
        }

        DialogResult = true;
    }
}
