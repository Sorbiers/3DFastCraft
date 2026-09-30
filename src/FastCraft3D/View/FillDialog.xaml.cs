using System.Windows;
using System.Windows.Threading;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <param name="Level">How high to fill, in world millimetres.</param>
/// <param name="Apart">The fill as an object of its own rather than joined to the part.</param>
/// <param name="Analysis">Where the part holds liquid, worked out already, for the fill to be made from.</param>
public sealed record FillSettings(float Level, bool Apart, CavityFill Analysis);

/// <summary>
/// Asks how high to fill a part, showing the fill on the plate as the level moves.
///
/// Where the part would hold liquid is worked out once, off the UI thread, when the panel opens
/// and again only when the detail changes. The level just picks which of that is filled, so the
/// preview and the volume follow the slider.
/// </summary>
public partial class FillDialog : ToolPanel
{
    private readonly Mesh world;
    private readonly Action<Mesh?> preview;
    private readonly DispatcherTimer settle;
    private CavityFill? analysis;
    private CancellationTokenSource? working;
    private bool updating;

    /// <param name="preview">Shows the fill as it stands, or takes it away when given null.</param>
    public FillDialog(string name, Mesh world, Action<Mesh?> preview)
    {
        InitializeComponent();

        this.world = world;
        this.preview = preview;

        var b = world.ComputeBounds();
        SubjectText.Text = $"Filling {name} as liquid poured in from above would: whatever it holds, up to the level.";

        updating = true;
        LevelSlider.Minimum = b.Min.Z;
        LevelSlider.Maximum = b.Max.Z;
        LevelSlider.Value = b.Max.Z;
        LevelBox.Put(b.Max.Z);
        DetailSlider.Value = 140;

        // On when the part stands on the plate, where "a floor" is what it is standing on.
        PlateBox.IsChecked = MathF.Abs(b.Min.Z) < 0.05f;
        updating = false;

        // The preview is rebuilt once the level has stopped moving, not on every step of a drag.
        settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        settle.Tick += (_, _) => { settle.Stop(); Show(); };

        // Started now rather than once shown: a panel is not always shown in something that loads it.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Analyse));
        Closed += (_, _) =>
        {
            working?.Cancel();
            settle.Stop();
            preview(null);
        };
    }

    /// <summary>The chosen settings, or null when the panel was cancelled.</summary>
    public FillSettings? Result { get; private set; }

    private float Level => (float)LevelSlider.Value;
    private int Resolution => (int)Math.Round(DetailSlider.Value);

    private async void Analyse()
    {
        working?.Cancel();
        var mine = working = new CancellationTokenSource();
        analysis = null;
        AcceptButton.IsEnabled = false;
        SummaryText.Text = "Working out where it would hold liquid...";
        preview(null);

        try
        {
            int resolution = Resolution;
            bool plate = PlateBox.IsChecked == true;
            var found = await Task.Run(() => CavityFill.Analyse(world, resolution, plate, mine.Token), mine.Token);
            if (mine.IsCancellationRequested) return;

            analysis = found;
            DetailText.Text = $"{Resolution} across - the fill finds its way into corners to {found.VoxelMm:0.##} mm.";
            Show();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Show()
    {
        if (analysis is not { } a) return;

        if (!a.HoldsAnything)
        {
            SummaryText.Text = PlateBox.IsChecked == true
                ? "Nothing here would hold liquid: every space inside it opens to the side."
                : "Nothing here would hold liquid: every space inside it opens to the side or underneath. Standing on the plate, try The plate is a floor.";
            AcceptButton.IsEnabled = false;
            preview(null);
            return;
        }

        double cm3 = a.VolumeMm3(Level) / 1000.0;
        var body = a.Body(Level);
        preview(body);

        AcceptButton.IsEnabled = body is not null;
        SummaryText.Text = body is null
            ? "Nothing is below this level to fill. Raise it."
            : $"Fills about {cm3:0.#} cm³" + (ApartBox.IsChecked == true
                ? " as a part of its own, fitting the inside exactly."
                : ", joined to the part as one solid.");
    }

    private void OnLevelDragged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || !IsInitialized) return;
        updating = true;
        LevelBox.Put(Level);
        updating = false;
        settle.Stop();
        settle.Start();
    }

    private void OnLevelTyped(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (updating || !IsInitialized || !LevelBox.TryRead(out float typed)) return;
        updating = true;
        LevelSlider.Value = Math.Clamp(typed, LevelSlider.Minimum, LevelSlider.Maximum);
        updating = false;
        settle.Stop();
        settle.Start();
    }

    private void OnDetailDragged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || analysis is null) return;
        DetailText.Text = $"{Resolution} across";
        Analyse();
    }

    private void OnApartClicked(object sender, RoutedEventArgs e) => Show();

    private void OnPlateClicked(object sender, RoutedEventArgs e) => Analyse();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (analysis is not { } a) return;
        Result = new FillSettings(Level, ApartBox.IsChecked == true, a);
        DialogResult = true;
    }
}
