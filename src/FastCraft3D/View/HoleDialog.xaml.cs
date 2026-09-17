using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a screw hole or an insert pocket.
///
/// With one part selected it cuts straight into the top of that part, at an offset from its middle;
/// with none, it makes the cutter as an object of its own, to be put where it is wanted and taken
/// out with Subtract - which is also how a hole goes into a side, or a row of them is made.
/// </summary>
public partial class HoleDialog : ToolPanel
{
    private readonly Func<HoleOptions?, float, float, bool, bool> preview;
    private readonly string? target;
    private readonly bool loading;

    /// <param name="target">The part the hole goes into, or null to make a cutter.</param>
    /// <param name="preview">
    /// Shows the cutter where it will go, given the options, the X and Y offsets and whether it
    /// goes all the way through, and says whether it could be made; given null, takes it away.
    /// </param>
    public HoleDialog(HoleOptions start, float offsetX, float offsetY, string? target,
                      Func<HoleOptions?, float, float, bool, bool> preview)
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
        OffsetXBox.Text = Format(offsetX);
        OffsetYBox.Text = Format(offsetY);

        IntroText.Text = target is null
            ? "Nothing is selected, so this makes the hole as a cutter: put it where the hole goes, select the part and then the cutter, and Subtract."
            : $"Into the top of {target}. For a hole in a side, or several at once, make cutters instead: select nothing and open Hole again.";
        PlacePanel.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        ThroughBox.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
        AcceptButton.Content = target is null ? "Add cutter" : "Cut";

        loading = false;
        Refresh();
    }

    /// <summary>Null until the hole is accepted.</summary>
    public HoleOptions? Result { get; private set; }

    public float OffsetX { get; private set; }
    public float OffsetY { get; private set; }

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
            ExtraClearance = Math.Clamp(Number(ClearanceBox, fallback.ExtraClearance), 0f, 2f)
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

        OffsetX = Number(OffsetXBox, 0f);
        OffsetY = Number(OffsetYBox, 0f);
        bool made = preview(options, OffsetX, OffsetY, Through);

        var lines = HoleCutter.Describe(options, Through ? float.NaN : options.Depth);
        if (Through) lines[0] = lines[0].Replace($"{HoleCutter.DepthOf(options, float.NaN):0.##} mm deep", "all the way through");
        if (!made) lines.Add("The nut pocket would not join the hole cleanly, so this cannot be made.");

        SummaryText.Text = string.Join("\n", lines);
        AcceptButton.IsEnabled = made;
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnChoice(object sender, RoutedEventArgs e) => Refresh();

    private void OnClicked(object sender, RoutedEventArgs e) => Refresh();

    private void OnSizeChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    protected override void OnClosed(EventArgs e)
    {
        preview(null, 0f, 0f, false);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Read();
        DialogResult = true;
    }
}
