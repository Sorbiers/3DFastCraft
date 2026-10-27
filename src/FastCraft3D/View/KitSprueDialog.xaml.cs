using System.Windows;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks how a kit sprue is built round the parts, showing it on the plate as the sizes change.
/// Laying it out is quick once the parts have been cut across, which is kept, so the preview
/// follows each change as soon as typing pauses.
/// </summary>
public partial class KitSprueDialog : ToolPanel
{
    private readonly KitSprue sprue;
    private readonly Action<KitSprueLayout?> preview;
    private readonly DispatcherTimer settle;
    private KitSprueLayout? layout;
    private bool ready;

    /// <param name="preview">Shows the sprue and the parts where it puts them, or takes them away when given null.</param>
    public KitSprueDialog(int count, KitSprue sprue, Action<KitSprueLayout?> preview)
    {
        InitializeComponent();

        this.sprue = sprue;
        this.preview = preview;

        SubjectText.Text = (count == 1 ? "The part" : $"The {count} parts")
            + " laid out in rows inside a frame of runners, each held by a thin gate from every side to snip off after printing.";

        var d = KitSprueOptions.Default;
        ClearanceBox.Put(d.Clearance);
        RunnerBox.Put(d.Runner);
        FrameBox.Put(d.Frame);
        HeightBox.Put(d.Height);
        GateWidthBox.Put(d.GateWidth);
        GateHeightBox.Put(d.GateHeight);
        WidthBox.Put(MathF.Min(d.Width, Scene.PlateSize - 20));
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

    /// <summary>The sprue as laid out, or null when the panel was cancelled.</summary>
    public KitSprueLayout? Result { get; private set; }

    private KitSprueOptions? Options()
    {
        if (!ClearanceBox.TryRead(out float clearance) || !RunnerBox.TryRead(out float runner) || !FrameBox.TryRead(out float frame)
            || !HeightBox.TryRead(out float height) || !GateWidthBox.TryRead(out float gateWidth)
            || !GateHeightBox.TryRead(out float gateHeight) || !WidthBox.TryRead(out float width))
        {
            return null;
        }

        return new KitSprueOptions(clearance, runner, frame, height, gateWidth, gateHeight, width, CornersBox.IsChecked == true);
    }

    private void Show()
    {
        if (Options() is not { } options)
        {
            layout = null;
            AcceptButton.IsEnabled = false;
            SummaryText.Text = "Type a size in each box.";
            return;
        }

        layout = sprue.Lay(options);
        preview(layout);
        AcceptButton.IsEnabled = true;

        string rows = layout.Rows == 1 ? "one row" : $"{layout.Rows} rows";
        string text = $"{layout.Size.X:0} x {layout.Size.Y:0} mm, {rows}, {layout.Gates} gates.";

        if (layout.Loose.Count > 0)
            text += layout.Loose.Count == 1
                ? " One part has nothing at its foot for a gate to reach, and is left loose."
                : $" {layout.Loose.Count} parts have nothing at their foot for a gate to reach, and are left loose.";

        if (layout.Size.X > Scene.PlateSize || layout.Size.Y > Scene.PlateSize)
            text += $" Larger than the {Scene.PlateSize:0} mm plate.";

        SummaryText.Text = text;
    }

    private void OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!ready) return;
        settle.Stop();
        settle.Start();
    }

    private void OnCornersClicked(object sender, RoutedEventArgs e) => Show();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        // What was typed last, not the preview from before it.
        settle.Stop();
        Show();
        if (layout is null) return;

        Result = layout;
        DialogResult = true;
    }
}
