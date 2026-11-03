using System.Numerics;
using System.Windows;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks how the tabs are put on the corners of each part, showing them on the plate as the sizes
/// change. The corners are found once, when the panel opens; laying the tabs is quick after that.
/// A click on a corner in the view puts tabs on it or takes them off.
/// </summary>
public partial class CornerTabsDialog : ToolPanel
{
    private readonly IReadOnlyList<CornerTabs> parts;
    private readonly Action<IReadOnlyList<CornerTabLayout>?> preview;
    private readonly DispatcherTimer settle;
    private readonly List<HashSet<int>> picked;
    private List<CornerTabLayout>? layouts;
    private string? said;
    private bool ready;

    /// <param name="names">The parts' names, in the order of <paramref name="parts"/>.</param>
    /// <param name="preview">Shows the tabs on each part, or takes them away when given null.</param>
    public CornerTabsDialog(IReadOnlyList<string> names, IReadOnlyList<CornerTabs> parts, Action<IReadOnlyList<CornerTabLayout>?> preview)
    {
        InitializeComponent();

        this.parts = parts;
        this.preview = preview;
        picked = parts.Select(p => p.Lifting.ToHashSet()).ToList();

        SubjectText.Text = (names.Count == 1 ? $"Tabs on the corners of {names[0]}" : $"Tabs on the corners of the {names.Count} parts")
            + " that lift off the plate, each held by a thin gate at the foot to snip off after printing.";

        var d = CornerTabOptions.Default;
        ReachBox.Put(d.Reach);
        FrameWidthBox.Put(d.FrameWidth);
        PadBox.Put(d.Pad);
        WidthBox.Put(d.Width);
        HeightBox.Put(d.Height);
        GateWidthBox.Put(d.GateWidth);
        GateHeightBox.Put(d.GateHeight);
        ready = true;

        settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        settle.Tick += (_, _) => { settle.Stop(); Show(); };

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Show));
        Closed += (_, _) =>
        {
            settle.Stop();
            preview(null);
        };
    }

    /// <summary>The tabs for each part, in the order given, or null when the panel was cancelled.</summary>
    public IReadOnlyList<CornerTabLayout>? Result { get; private set; }

    /// <summary>
    /// A click in the view, at the point on the part or the plate under it: the corner nearest it
    /// gets its tabs, or loses them. Picking starts from the corners found, so a click on a
    /// box takes one corner's tabs off and leaves the other three.
    /// </summary>
    public void Pick(Vector3 point)
    {
        var at = new Vector2(point.X, point.Y);
        (int Part, int Corner, float Distance)? best = null;
        for (int p = 0; p < parts.Count; p++)
        {
            if (parts[p].CornerNear(at) is not { } k) continue;
            float d = Vector2.Distance(parts[p].Corners[k].At, at);
            if (best is null || d < best.Value.Distance) best = (p, k, d);
        }

        if (best is not { } near)
        {
            said = "No corner there - click near a corner of the part's foot.";
            Show();
            return;
        }

        if (ByHandRadio.IsChecked != true)
        {
            ready = false;
            ByHandRadio.IsChecked = true;
            ready = true;
        }

        if (!picked[near.Part].Remove(near.Corner)) picked[near.Part].Add(near.Corner);
        said = null;
        Show();
    }

    private CornerTabOptions? Options()
    {
        if (!ReachBox.TryRead(out float reach) || !FrameWidthBox.TryRead(out float frameWidth) || !PadBox.TryRead(out float pad)
            || !WidthBox.TryRead(out float width) || !HeightBox.TryRead(out float height)
            || !GateWidthBox.TryRead(out float gateWidth) || !GateHeightBox.TryRead(out float gateHeight))
        {
            return null;
        }

        return new CornerTabOptions(OneRadio.IsChecked == true ? TabsPerCorner.One : TabsPerCorner.Two, FrameBox.IsChecked == true,
            reach, frameWidth, pad, width, height, gateWidth, gateHeight);
    }

    private void Show()
    {
        bool frame = FrameBox.IsChecked == true;
        FrameWidthRow.IsEnabled = frame;
        PadRow.IsEnabled = !frame;

        if (Options() is not { } options)
        {
            layouts = null;
            AcceptButton.IsEnabled = false;
            SummaryText.Text = "Type a size in each box.";
            return;
        }

        bool byHand = ByHandRadio.IsChecked == true;
        layouts = parts.Select((p, i) => p.Lay(options, byHand ? picked[i] : null)).ToList();
        preview(layouts);

        int tabs = layouts.Sum(l => l.Tabs), held = layouts.Sum(l => l.Held), blocked = layouts.Sum(l => l.Blocked);
        bool failed = layouts.Any(l => l.Why is not null);
        AcceptButton.IsEnabled = tabs > 0 && !failed;

        string text = tabs == 0
            ? parts.All(p => p.Lifting.Count == 0) && !byHand
                ? "No corner sharp enough to lift. Click one in the view to hold it anyway."
                : "No tabs - click a corner in the view to put them on."
            : $"{tabs} tabs on {held} {(held == 1 ? "corner" : "corners")}.";

        if (blocked > 0)
            text += blocked == 1 ? " One tab is left off: the part is in its way." : $" {blocked} tabs are left off: the part is in their way.";

        if (layouts.FirstOrDefault(l => l.Why is not null)?.Why is { } why) text += " " + why;

        var widest = layouts.Where(l => l.Mesh is not null).Select(l => l.Size).DefaultIfEmpty().MaxBy(s => MathF.Max(s.X, s.Y));
        if (widest.X > Scene.PlateSize || widest.Y > Scene.PlateSize) text += $" Larger than the {Scene.PlateSize:0} mm plate.";
        if (tabs > 0 && !byHand) text += " Click a corner in the view to take its tabs off or put them on.";
        if (said is not null) text = said + " " + text;

        SummaryText.Text = text;
    }

    private void OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!ready) return;
        settle.Stop();
        settle.Start();
    }

    private void OnChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        said = null;
        Show();
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        // What was typed last, not the preview from before it.
        settle.Stop();
        Show();
        if (layouts is null || !AcceptButton.IsEnabled) return;

        Result = layouts;
        DialogResult = true;
    }
}
