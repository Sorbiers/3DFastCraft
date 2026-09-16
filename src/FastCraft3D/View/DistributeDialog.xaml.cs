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
    private readonly Func<float, Vector2> arrange;
    private readonly Vector2 bed;
    private readonly int count;

    /// <param name="count">How many objects are being set out.</param>
    /// <param name="gap">The gap to start from - the last one used.</param>
    /// <param name="bed">The printable area's width and depth.</param>
    /// <param name="arrange">Sets the objects out with a gap and says how much of the bed they cover.</param>
    public DistributeDialog(int count, float gap, Vector2 bed, Func<float, Vector2> arrange)
    {
        InitializeComponent();

        this.count = count;
        this.bed = bed;
        this.arrange = arrange;

        SubjectText.Text = $"Setting {count} objects out in rows across the bed, centred on it.";
        GapBox.Text = gap.ToString("0.##", CultureInfo.CurrentCulture);

        Loaded += (_, _) =>
        {
            GapBox.SelectAll();
            GapBox.Focus();
        };
    }

    /// <summary>The gap chosen, or null when the panel was cancelled.</summary>
    public float? Result { get; private set; }

    private bool TryGap(out float gap) =>
        float.TryParse(GapBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out gap)
        && float.IsFinite(gap) && gap >= 0f;

    private void OnChanged(object sender, RoutedEventArgs e) => Describe();

    private void Describe()
    {
        // TextChanged fires from the constructor, before the rest of the panel is there.
        if (SummaryText is null || arrange is null) return;

        if (!TryGap(out float gap))
        {
            SummaryText.Text = "Type a gap of zero or more.";
            return;
        }

        var covers = arrange(gap);
        bool fits = covers.X <= bed.X + 0.01f && covers.Y <= bed.Y + 0.01f;

        SummaryText.Text = $"{count} objects covering {covers.X:0.#} × {covers.Y:0.#} mm"
                           + (fits
                               ? $" of the {bed.X:0.#} × {bed.Y:0.#} mm bed."
                               : $" - more than the {bed.X:0.#} × {bed.Y:0.#} mm bed. A smaller gap, or Fit, may help.");
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!TryGap(out float gap)) return;

        Result = gap;
        DialogResult = true;
    }
}
