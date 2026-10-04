using System.Globalization;
using System.Numerics;
using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// Asks how far apart to set the selection out across the bed.
///
/// The parts move as the gap is typed, so the arrangement is judged on the bed itself rather than
/// from a number; the view model puts them back if the panel is cancelled.
/// </summary>
public partial class DistributeDialog : ToolPanel
{
    private readonly Func<float, bool, bool, Vector2> arrange;
    private readonly Vector2 bed;
    private readonly int count;

    /// <param name="count">How many objects are being set out.</param>
    /// <param name="gap">The gap to start from - the last one used.</param>
    /// <param name="bed">The printable area's width and depth.</param>
    /// <param name="bestFace">Whether each object starts turned onto its best face.</param>
    /// <param name="drop">Whether each starts dropped to the plate.</param>
    /// <param name="arrange">
    /// Sets the objects out with a gap - turning each onto its best face and standing it on the
    /// plate when asked - and says how much of the bed they cover.
    /// </param>
    public DistributeDialog(
        int count, float gap, bool bestFace, bool drop, Vector2 bed, Func<float, bool, bool, Vector2> arrange)
    {
        InitializeComponent();
        BestFaceBox.IsChecked = bestFace;
        DropBox.IsChecked = drop || bestFace;
        DropBox.IsEnabled = !bestFace;

        this.count = count;
        this.bed = bed;
        this.arrange = arrange;

        SubjectText.Text = $"Setting {count} objects out in rows across the plate, centered on it.";
        GapBox.Put(gap);

        Loaded += (_, _) =>
        {
            GapBox.SelectAll();
            GapBox.Focus();
        };
    }

    /// <summary>The gap chosen, or null when the panel was cancelled.</summary>
    public float? Result { get; private set; }

    /// <summary>Whether each object is turned onto its best face.</summary>
    public bool BestFace => BestFaceBox.IsChecked == true;

    /// <summary>Whether each object is stood on the plate: always, with its best face down.</summary>
    public bool Drop => BestFace || DropBox.IsChecked == true;

    private bool TryGap(out float gap) =>
        GapBox.TryRead(out gap)
        && float.IsFinite(gap) && gap >= 0f;

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        // Standing each on its best face stands it on the plate.
        if (BestFaceBox is not null && DropBox is not null)
        {
            DropBox.IsEnabled = BestFaceBox.IsChecked != true;
            if (BestFaceBox.IsChecked == true) DropBox.IsChecked = true;
        }

        Describe();
    }

    private void Describe()
    {
        // TextChanged fires from the constructor, before the rest of the panel is there.
        if (SummaryText is null || arrange is null) return;

        if (!TryGap(out float gap))
        {
            SummaryText.Text = "Type a gap of zero or more.";
            return;
        }

        var covers = arrange(gap, BestFace, Drop);
        bool fits = covers.X <= bed.X + 0.01f && covers.Y <= bed.Y + 0.01f;

        SummaryText.Text = $"{count} objects covering {covers.X:0.#} × {covers.Y:0.#} mm"
                           + (fits
                               ? $" of the {bed.X:0.#} × {bed.Y:0.#} mm plate."
                               : $" - more than the {bed.X:0.#} × {bed.Y:0.#} mm plate. A smaller gap, or Fit, may help.");
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!TryGap(out float gap)) return;

        Result = gap;
        DialogResult = true;
    }
}
