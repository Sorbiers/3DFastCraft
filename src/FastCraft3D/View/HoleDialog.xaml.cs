using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a screw hole or an insert pocket.
///
/// With one part selected it cuts into that part where the red cutter is put - it starts at the
/// middle of the top, and the handles move and turn it; with none, it makes the cutter as an object
/// of its own, to be taken out with Subtract - which is how a row of them is made.
/// </summary>
public partial class HoleDialog : ToolPanel
{
    private readonly Func<HoleOptions?, bool, bool> preview;
    private readonly string? target;
    private readonly bool loading;

    /// <param name="target">The part the hole goes into, or null to make a cutter.</param>
    /// <param name="preview">
    /// Shows the cutter, given the options and whether it goes all the way through, and says
    /// whether it could be made; given null, takes it away.
    /// </param>
    public HoleDialog(HoleOptions start, string? target, Func<HoleOptions?, bool, bool> preview)
    {
        this.preview = preview;
        this.target = target;
        loading = true;
        InitializeComponent();

        foreach (var size in HoleCutter.Sizes) SizeBox.Items.Add(size.Name);
        SizeBox.SelectedItem = HoleCutter.SizeOf(start.Size).Name;

        ScrewBox.IsChecked = start.Kind == HoleKind.Screw;
        InsertBox.IsChecked = start.Kind == HoleKind.Insert;
        PlainBox.IsChecked = start.Head == HoleHead.Plain;
        CountersunkBox.IsChecked = start.Head == HoleHead.Countersunk;
        CounterboredBox.IsChecked = start.Head == HoleHead.Counterbored;
        NutBox.IsChecked = start.NutPocket;
        DepthBox.Text = Format(start.Depth);
        ThroughBox.IsChecked = target is not null;
        ClearanceBox.Text = Format(start.ExtraClearance);
        CountBox.Text = Math.Max(1, start.Count).ToString(CultureInfo.CurrentCulture);
        RowBox.IsChecked = start.Pattern == HolePattern.Row;
        CircleBox.IsChecked = start.Pattern == HolePattern.Circle;
        SpacingBox.Text = Format(start.Spacing);
        CircleDiameterBox.Text = Format(start.CircleDiameter);

        IntroText.Text = target is null
            ? "Nothing is selected, so this makes the hole as a cutter to Subtract later. Drag its handles to put it where the hole goes."
            : $"Into {target}. The red cutter starts at the middle of the top: drag its handles to move it, or turn it into a side or to an angle.";
        ThroughBox.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        AcceptButton.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;

        loading = false;
        Refresh();
    }

    /// <summary>Null until the hole is accepted.</summary>
    public HoleOptions? Result { get; private set; }

    /// <summary>Whether Add was pressed rather than Cut: the cutter goes on the plate instead of into the part.</summary>
    public bool AddsCutter { get; private set; }

    /// <summary>Whether the hole goes all the way through the part.</summary>
    public bool Through => target is not null && ThroughBox.IsChecked == true && ScrewBox.IsChecked == true;

    private static string Format(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private HoleOptions Read()
    {
        var fallback = new HoleOptions();
        return new HoleOptions
        {
            Kind = InsertBox.IsChecked == true ? HoleKind.Insert : HoleKind.Screw,
            Size = SizeBox.SelectedItem as string ?? fallback.Size,
            Head = PlainBox.IsChecked == true ? HoleHead.Plain : CounterboredBox.IsChecked == true ? HoleHead.Counterbored : HoleHead.Countersunk,
            NutPocket = NutBox.IsChecked == true,
            Depth = MathF.Max(0.5f, Number(DepthBox, fallback.Depth)),
            ExtraClearance = Math.Clamp(Number(ClearanceBox, fallback.ExtraClearance), 0f, 2f),
            Count = int.TryParse(CountBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int count) ? Math.Clamp(count, 1, 100) : 1,
            Pattern = CircleBox.IsChecked == true ? HolePattern.Circle : HolePattern.Row,
            Spacing = MathF.Max(0f, Number(SpacingBox, fallback.Spacing)),
            CircleDiameter = MathF.Max(0f, Number(CircleDiameterBox, fallback.CircleDiameter))
        };
    }

    private static float Number(TextBox box, float otherwise) =>
        float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) && float.IsFinite(v) ? v : otherwise;

    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        var options = Read();
        bool screw = options.Kind == HoleKind.Screw;
        ScrewPanel.IsEnabled = screw;
        DepthBox.IsEnabled = screw && !Through;
        bool several = options.Count > 1;
        RowBox.IsEnabled = CircleBox.IsEnabled = several;
        SpacingBox.IsEnabled = several && options.Pattern == HolePattern.Row;
        CircleDiameterBox.IsEnabled = several && options.Pattern == HolePattern.Circle;

        bool made = preview(options, Through);

        var lines = HoleCutter.Describe(options, Through ? float.NaN : options.Depth);
        if (Through) lines[0] = lines[0].Replace($"{HoleCutter.DepthOf(options, float.NaN):0.##} mm deep", "all the way through");
        lines.InsertRange(0, HoleCutter.DescribePattern(options));
        if (!made) lines.Add("The nut pocket would not join the hole cleanly, so this cannot be made.");

        SummaryText.Text = string.Join("\n", lines);
        AcceptButton.IsEnabled = made;
        AddButton.IsEnabled = made;
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnChoice(object sender, RoutedEventArgs e) => Refresh();

    private void OnClicked(object sender, RoutedEventArgs e) => Refresh();

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    protected override void OnClosed(EventArgs e)
    {
        preview(null, false);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Read();
        DialogResult = true;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        AddsCutter = true;
        Result = Read();
        DialogResult = true;
    }
}
