using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a screw thread, showing it on the plate as the numbers change.
///
/// A metric size fills in the pitch and the nut's width and height along with the diameter, so the
/// usual case is two choices; Custom opens the diameter and pitch for anything else. As in Custom
/// shape, boxes the chosen part does not use are greyed rather than hidden.
/// </summary>
public partial class ThreadDialog : ToolPanel
{
    private const string CustomSize = "Custom";

    private readonly Action<ThreadOptions?, Mesh?> preview;
    private readonly string? target;
    private bool loading;

    /// <param name="target">The part selected, which a hole cutter can be cut into; or null.</param>
    /// <param name="preview">Shows what the numbers make, or takes the preview away when given nulls.</param>
    public ThreadDialog(ThreadOptions start, string? target, Action<ThreadOptions?, Mesh?> preview)
    {
        this.preview = preview;
        this.target = target;
        InitializeComponent();

        loading = true;
        foreach (var size in Threads.Metric) SizeBox.Items.Add(size.Name);
        SizeBox.Items.Add(CustomSize);

        KindBox.SelectedIndex = (int)start.Kind;
        BodyBox.SelectedIndex = (int)start.Body;
        SizeBox.SelectedItem = Threads.Find(start.Diameter, start.Pitch)?.Name ?? CustomSize;
        DiameterBox.Text = Format(start.Diameter);
        PitchBox.Text = Format(start.Pitch);
        LengthBox.Text = Format(start.Length);
        ClearanceBox.Text = Format(start.Clearance);
        AcrossBox.Text = Format(start.AcrossFlats);
        HeightBox.Text = Format(start.NutHeight);
        HeadHeightBox.Text = Format(start.HeadHeight > 0f ? start.HeadHeight : ThreadOptions.Default.HeadHeight);
        loading = false;

        Refresh();
    }

    /// <summary>Null until the user adds the part.</summary>
    public ThreadOptions? Result { get; private set; }

    /// <summary>Whether Cut was pressed: the hole cutter goes into the part rather than onto the plate.</summary>
    public bool Cuts { get; private set; }

    private static string Format(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>What the boxes say, with anything unreadable left at the default.</summary>
    private ThreadOptions Read()
    {
        var fallback = ThreadOptions.Default;

        return new ThreadOptions(
            (ThreadKind)Math.Max(0, KindBox.SelectedIndex),
            Number(DiameterBox, fallback.Diameter),
            Number(PitchBox, fallback.Pitch),
            Number(LengthBox, fallback.Length),
            Number(ClearanceBox, fallback.Clearance),
            (NutBody)Math.Max(0, BodyBox.SelectedIndex),
            Number(AcrossBox, fallback.AcrossFlats),
            Number(HeightBox, fallback.NutHeight))
        {
            HeadHeight = Number(HeadHeightBox, fallback.HeadHeight)
        };

        static float Number(TextBox box, float otherwise) =>
            float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) && float.IsFinite(v)
                ? v
                : otherwise;
    }

    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        var asked = Read();
        var sane = asked.Sane();
        bool nut = asked.Kind == ThreadKind.Nut;
        bool bolt = asked.Kind == ThreadKind.Bolt;
        bool cutsIn = target is not null && asked.Kind == ThreadKind.HoleCutter;

        DiameterBox.IsEnabled = PitchBox.IsEnabled = SizeBox.SelectedItem as string == CustomSize;
        LengthBox.IsEnabled = !nut;
        BodyBox.IsEnabled = AcrossBox.IsEnabled = nut || bolt;
        HeightBox.IsEnabled = nut;
        HeadHeightBox.IsEnabled = bolt;
        BodyLabel.Text = bolt ? "Head" : "Nut body";
        AcrossLabel.Text = asked.Body == NutBody.Round ? "Outside" : "Across flats";
        CutButton.Visibility = cutsIn ? Visibility.Visible : Visibility.Collapsed;

        var mesh = Threads.Build(sane);
        preview(sane, mesh);

        // Built directly rather than cut, so it should always close; if some number finds a way
        // it does not, say so rather than add it.
        bool sound = mesh.CheckHealth().IsWatertight;
        AddButton.IsEnabled = CutButton.IsEnabled = sound;

        var notes = new List<string>();

        notes.Add(sane.Kind switch
        {
            ThreadKind.Rod => $"{sane.RodOutside:0.##} mm over the crests, {sane.RodCore:0.##} mm at the core.",
            ThreadKind.Bolt => $"{sane.RodOutside:0.##} mm over the crests, {sane.RodCore:0.##} mm at the core, under a "
                               + $"{sane.AcrossFlats:0.##} mm head {sane.HeadHeight:0.##} mm tall. It stands on its head, as it prints best.",
            ThreadKind.Nut => $"{sane.NutBore:0.##} mm through the crests of the thread, "
                              + $"{sane.AcrossFlats / 2f - sane.Diameter / 2f - sane.Clearance / 4f:0.##} mm of wall at the thinnest.",
            _ when cutsIn => $"Cuts a hole {sane.NutBore:0.##} mm through the crests. It starts in the top of {target}, "
                             + "just past the surface: move it where the hole goes and Cut, or Add it to Subtract later.",
            _ => $"Cuts a hole {sane.NutBore:0.##} mm through the crests. Stand it where the hole goes, "
                 + "running a little past the surface, and Subtract it from the part."
        });

        if (MathF.Abs(asked.Diameter - sane.Diameter) > 1e-3f)
            notes.Add($"The diameter is held to {sane.Diameter:0.##} mm.");
        if (MathF.Abs(asked.Pitch - sane.Pitch) > 1e-3f)
            notes.Add(asked.Pitch > sane.Pitch
                ? $"The pitch is held to {sane.Pitch:0.##} mm, the coarsest a {sane.Diameter:0.##} mm thread takes."
                : $"The pitch is held to {sane.Pitch:0.##} mm.");
        if ((nut || bolt) && asked.AcrossFlats < sane.AcrossFlats - 1e-3f)
            notes.Add($"Widened to {sane.AcrossFlats:0.##} mm to leave a {ThreadOptions.MinimumWall:0.#} mm wall round the thread.");
        if (MathF.Abs(asked.Clearance - sane.Clearance) > 1e-3f)
            notes.Add($"The clearance is held to {sane.Clearance:0.##} mm.");

        if (!sane.Engages)
            notes.Add("With this much clearance a rod's crests pass inside a nut's, and it would slide straight through.");
        if (sane.Pitch < 1f)
            notes.Add("A pitch under 1 mm is finer than most filament printers draw cleanly. Print it slowly "
                      + "with fine layers, or on a resin printer; M6 and up print far more reliably.");
        if (!sound)
            notes.Add("These numbers do not make a closed solid, so it cannot be added. Try a slightly different length.");

        SummaryText.Text = string.Join("\n", notes) + $"\n{mesh.TriangleCount:N0} triangles.";
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    /// <summary>A metric size brings its pitch and nut with it; Custom leaves the boxes as they are to edit.</summary>
    private void OnSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || !IsInitialized) return;

        foreach (var size in Threads.Metric)
        {
            if (size.Name != SizeBox.SelectedItem as string) continue;

            loading = true;
            DiameterBox.Text = Format(size.Diameter);
            PitchBox.Text = Format(size.Pitch);
            AcrossBox.Text = Format(size.AcrossFlats);
            HeightBox.Text = Format(size.NutHeight);
            HeadHeightBox.Text = Format(size.HeadHeight);
            loading = false;
        }

        Refresh();
    }

    protected override void OnClosed(EventArgs e)
    {
        preview(null, null);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Read().Sane();
        DialogResult = true;
    }

    private void OnCut(object sender, RoutedEventArgs e)
    {
        Cuts = true;
        Result = Read().Sane();
        DialogResult = true;
    }
}
