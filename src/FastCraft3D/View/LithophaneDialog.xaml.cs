using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using Microsoft.Win32;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a lithophane, showing what it will look like lit as the numbers change.
///
/// Two previews, because one of them cannot do the job. The plate on the build plate says how big
/// it is and which way it curves; the picture in this panel says whether it is worth printing,
/// and is the only one that can - a lithophane unlit is a grey slab, and no amount of turning it
/// round in the viewport will show what the light will make of it.
///
/// The panel's preview is worked out from the thickness the printer will really lay down, stepped
/// to whole layers, so a picture about to lose its detail to a coarse layer height says so here.
/// The plate's preview is built coarser than the real thing: at the detail actually asked for it
/// is a few hundred thousand triangles, rebuilt on every keystroke.
/// </summary>
public partial class LithophaneDialog : ToolPanel
{
    /// <summary>Samples across the preview on the plate, however fine the real one is to be.</summary>
    private const int PreviewSamples = 140;

    private readonly Action<Mesh?> preview;

    /// <summary>Each number and the slider that drives it, so the two can be kept together.</summary>
    private readonly List<(TextBox Box, Slider Slider)> pairs = new();

    /// <summary>
    /// Both previews are worked out once the numbers stop moving rather than on every change.
    /// A slider asks many times a second while it is dragged, and each answer is a mesh of some
    /// tens of thousands of triangles and a lit bitmap - the thumb stuck to the mouse without it.
    /// </summary>
    private readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(120) };

    private bool loading;
    private bool syncing;

    /// <param name="preview">Shows a plate on the build plate, or takes it away when given null.</param>
    public LithophaneDialog(string file, LithophaneOptions start, Action<Mesh?> preview)
    {
        this.preview = preview;
        InitializeComponent();

        pairs.Add((WidthBox, WidthSlider));
        pairs.Add((MinBox, MinSlider));
        pairs.Add((MaxBox, MaxSlider));
        pairs.Add((PitchBox, PitchSlider));
        pairs.Add((LayerBox, LayerSlider));
        pairs.Add((FrameBox, FrameSlider));
        pairs.Add((BrightnessBox, BrightnessSlider));
        pairs.Add((ContrastBox, ContrastSlider));
        pairs.Add((GammaBox, GammaSlider));
        pairs.Add((AngleBox, AngleSlider));

        settle.Tick += (_, _) =>
        {
            settle.Stop();
            Rebuild();
        };

        loading = true;
        WidthBox.Text = Format(start.Width);
        MinBox.Text = Format(start.MinThickness);
        MaxBox.Text = Format(start.MaxThickness);
        PitchBox.Text = Format(start.Pitch);
        LayerBox.Text = Format(start.LayerHeight);
        FrameBox.Text = Format(start.Frame);
        BrightnessBox.Text = Format(start.Brightness);
        ContrastBox.Text = Format(start.Contrast);
        GammaBox.Text = Format(start.Gamma);
        AngleBox.Text = Format(start.Angle);
        NegativeBox.IsChecked = start.Negative;
        FlatBox.IsChecked = start.Shape == LithophaneShape.Flat;
        CurvedBox.IsChecked = start.Shape == LithophaneShape.Curved;
        loading = false;

        SyncSliders();
        Load(file);
    }

    /// <summary>Null until the plate is added.</summary>
    public LithophaneOptions? Result { get; private set; }

    /// <summary>The picture itself, for the caller to build the real plate from.</summary>
    public Greyscale? Picture { get; private set; }

    /// <summary>What it is called, for the object's name.</summary>
    public string PictureName { get; private set; } = "Lithophane";

    private static string Format(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    private void Load(string file)
    {
        try
        {
            Picture = PictureReader.Read(file);
            PictureName = Path.GetFileNameWithoutExtension(file);
            PictureText.Text = Path.GetFileName(file);
            PictureText.ToolTip = file;
        }
        catch (Exception ex)
        {
            Picture = null;
            PictureText.Text = $"could not be read: {ex.Message}";
        }

        // At once rather than on the timer: a picture has just been chosen, and the panel
        // should open with it in rather than with an empty frame that fills in a moment later.
        Rebuild();
    }

    /// <summary>What the boxes say, with anything unreadable left at the default.</summary>
    private LithophaneOptions Read()
    {
        var fallback = new LithophaneOptions();

        return new LithophaneOptions
        {
            Width = Number(WidthBox, fallback.Width),
            MinThickness = Number(MinBox, fallback.MinThickness),
            MaxThickness = Number(MaxBox, fallback.MaxThickness),
            Pitch = Number(PitchBox, fallback.Pitch),
            LayerHeight = Number(LayerBox, fallback.LayerHeight),
            Frame = Number(FrameBox, fallback.Frame),
            Brightness = Number(BrightnessBox, fallback.Brightness),
            Contrast = Number(ContrastBox, fallback.Contrast),
            Gamma = Number(GammaBox, fallback.Gamma),
            Angle = Number(AngleBox, fallback.Angle),
            Negative = NegativeBox.IsChecked == true,
            Shape = CurvedBox.IsChecked == true ? LithophaneShape.Curved : LithophaneShape.Flat
        }.Sane();

        static float Number(TextBox box, float otherwise) =>
            float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) && float.IsFinite(v)
                ? v
                : otherwise;
    }

    /// <summary>Asks for the previews, once whatever is being changed has stopped changing.</summary>
    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        settle.Stop();
        settle.Start();
    }

    private void Rebuild()
    {
        if (loading || !IsInitialized) return;

        var o = Read();
        bool curved = o.Shape == LithophaneShape.Curved;
        AngleBox.IsEnabled = curved;
        AngleSlider.IsEnabled = curved;
        AddButton.IsEnabled = Picture is not null;

        if (Picture is not { } picture)
        {
            preview(null);
            Backlit.Source = null;
            SummaryText.Text = "Choose a picture to carry.";
            return;
        }

        var grid = Lithophane.Grid(picture.Width, picture.Height, o);

        ShowBacklit(picture, o);

        // Coarser than what will be built, so typing into a box does not stop to lay out a
        // quarter of a million triangles between one keystroke and the next.
        float coarse = MathF.Max(o.Pitch, o.Width / PreviewSamples);
        preview(Lithophane.Build(picture, o with { Pitch = coarse }));

        int greys = Lithophane.GreyLevels(o);
        var notes = new List<string>
        {
            $"{grid.Width:0.#} x {grid.Height:0.#} mm, {o.MinThickness:0.##} to {o.MaxThickness:0.##} mm thick.",
            $"{grid.Columns} x {grid.Rows} samples, {grid.Triangles:N0} triangles.",
            $"{greys} greys at a {o.LayerHeight:0.##} mm layer - the picture cannot show more than that."
        };

        if (greys < 12)
            notes.Add("Too few to hold a photograph: print it in finer layers, or put the thin and thick ends further apart.");
        if (grid.Triangles > 400_000)
            notes.Add("A heavy mesh. Coarser detail costs the print nothing the nozzle could have drawn anyway.");
        if (o.MinThickness < 0.6f)
            notes.Add("Under 0.6 mm is thinner than two walls on most printers, and may come out with holes in it.");

        notes.Add("It stands upright, which is how it has to print: laid flat, every grey becomes a layer step.");
        SummaryText.Text = string.Join("\n", notes);
    }

    /// <summary>The lit picture, drawn as a grey bitmap at whatever size the panel gives it.</summary>
    private void ShowBacklit(Greyscale picture, LithophaneOptions o)
    {
        const int across = 260;
        int up = Math.Clamp((int)MathF.Round(across * (float)picture.Height / picture.Width), 1, 4 * across);

        var lit = Lithophane.Backlit(picture, o, across, up);
        var pixels = new byte[lit.Length];
        for (int i = 0; i < lit.Length; i++) pixels[i] = (byte)Math.Clamp(lit[i] * 255f, 0f, 255f);

        Backlit.Source = BitmapSource.Create(across, up, 96, 96, PixelFormats.Gray8, null, pixels, across);
    }

    /// <summary>
    /// Puts what the boxes say onto the sliders. One way only: a number typed outside a slider's
    /// range is left as it was typed, with the slider standing at its end, rather than being
    /// pulled back to a range that is only there to make dragging useful.
    /// </summary>
    private void SyncSliders()
    {
        syncing = true;

        foreach (var (box, slider) in pairs)
        {
            if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) || !float.IsFinite(v))
                continue;

            // A typed number keeps its own precision; only dragging moves in steps.
            slider.IsSnapToTickEnabled = false;
            slider.Value = Math.Clamp(v, slider.Minimum, slider.Maximum);
            slider.IsSnapToTickEnabled = true;
        }

        syncing = false;
    }

    /// <summary>A slider moved: its box follows, and the box is what everything else reads.</summary>
    private void OnSlide(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Not while the markup is still being read: setting a slider's range there moves it off
        // zero, and the box it belongs to has not been filled in yet.
        if (loading || syncing || !IsInitialized || sender is not Slider slider) return;

        var box = pairs.FirstOrDefault(p => ReferenceEquals(p.Slider, slider)).Box;
        if (box is null) return;

        syncing = true;
        box.Text = Format((float)e.NewValue);
        syncing = false;

        Refresh();
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnChanged(object sender, TextChangedEventArgs e)
    {
        if (!syncing) SyncSliders();
        Refresh();
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = PictureReader.Filter, Title = "Choose a picture" };
        if (dialog.ShowDialog() == true) Load(dialog.FileName);
    }

    protected override void OnClosed(EventArgs e)
    {
        // A tick still to come would put the preview back on the plate after the caller has
        // taken it away again, leaving it standing there with nothing holding it.
        settle.Stop();

        preview(null);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Picture is null) return;

        Result = Read();
        DialogResult = true;
    }
}
