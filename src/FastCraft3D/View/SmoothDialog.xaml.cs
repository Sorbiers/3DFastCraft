using System.Windows;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <param name="Passes">How many rounds of smoothing. Zero leaves the shape alone.</param>
/// <param name="Levels">How many times to divide each triangle in four beforehand.</param>
/// <param name="MoveRim">Whether the rim of an unclosed shape may move.</param>
public readonly record struct SmoothSettings(int Passes, int Levels, bool MoveRim)
{
    public Mesh ApplyTo(Mesh mesh) =>
        MeshSmoothing.Smooth(MeshSubdivision.Subdivide(mesh, Levels), Passes, MoveRim);
}

/// <summary>
/// Sets how much to smooth, and shows it happening.
///
/// Two settings, because smoothing has two halves that are easy to confuse. Smoothness is how
/// hard the surface is pulled toward its own average; detail is how finely the shape is divided
/// up first, and without enough of it there is simply nothing to pull - a cube has eight points
/// and no amount of smoothing rounds it.
///
/// The result appears in the viewport as the sliders move, because the only reliable way to
/// judge smoothing is to look at it.
/// </summary>
public partial class SmoothDialog : Window
{
    private readonly IReadOnlyList<SceneObject> targets;
    private readonly List<Mesh> originals;
    private readonly Action<IReadOnlyList<Mesh>> preview;
    private readonly List<Mesh> divided = [];
    private int dividedAt = -1;

    public SmoothDialog(
        IReadOnlyList<SceneObject> objects, Action<IReadOnlyList<Mesh>> preview)
    {
        InitializeComponent();

        targets = objects;
        originals = objects.Select(o => o.Mesh).ToList();
        this.preview = preview;

        SubjectText.Text = objects.Count == 1
            ? $"Smoothing {objects[0].Name}, {originals[0].TriangleCount:N0} triangles."
            : $"Smoothing {objects.Count} objects, {originals.Sum(m => m.TriangleCount):N0} triangles.";

        AmountSlider.Value = 6;

        // Enough detail for the shape at hand: a plain box needs dividing several times before
        // it has anything to smooth, an import usually needs none at all.
        DetailSlider.Value = Math.Min(originals.Max(m => MeshSubdivision.LevelsFor(m)), DetailSlider.Maximum);

        Update();
    }

    /// <summary>The chosen settings, or null when the dialog was cancelled.</summary>
    public SmoothSettings? Result { get; private set; }

    private SmoothSettings Current => new(
        (int)Math.Round(AmountSlider.Value),
        (int)Math.Round(DetailSlider.Value),
        MoveRim.IsChecked == true);

    private void Update()
    {
        if (!IsInitialized) return;

        var settings = Current;

        // Dividing is the expensive half and only depends on one slider, so it is kept between
        // moves of the other.
        if (dividedAt != settings.Levels)
        {
            divided.Clear();
            divided.AddRange(originals.Select(m => MeshSubdivision.Subdivide(m, settings.Levels)));
            dividedAt = settings.Levels;
        }

        var smoothed = divided
            .Select(m => MeshSmoothing.Smooth(m, settings.Passes, settings.MoveRim))
            .ToList();

        AmountText.Text = settings.Passes == 0 ? "none" : $"{settings.Passes}";
        DetailText.Text = settings.Levels == 0
            ? "as it is"
            : $"{settings.Levels} x finer";

        int before = originals.Sum(m => m.TriangleCount);
        int after = smoothed.Sum(m => m.TriangleCount);

        // Smoothing moves the surface, so the part changes size - corners pull in while flat
        // faces dome very slightly out. Worth saying, since these are millimetres someone is
        // about to print.
        var was = Bounds.Empty;
        var now = Bounds.Empty;
        foreach (var m in originals) was = was.Union(m.ComputeBounds());
        foreach (var m in smoothed) now = now.Union(m.ComputeBounds());

        string size = was.IsEmpty || now.IsEmpty
            ? ""
            : $" Size {now.Size.X:0.##} x {now.Size.Y:0.##} x {now.Size.Z:0.##} mm, was "
              + $"{was.Size.X:0.##} x {was.Size.Y:0.##} x {was.Size.Z:0.##}.";

        SummaryText.Text = settings.Passes == 0
            ? $"{after:N0} triangles, unsmoothed."
            : $"{before:N0} triangles becomes {after:N0}."
              + (after > 200_000 ? " That is a lot to work with afterwards." : "")
              + size;

        preview(smoothed);
    }

    private void OnAmountChanged(object sender, RoutedEventArgs e) => Update();

    private void OnAmountChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Update();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Current;
        DialogResult = true;
    }

    /// <summary>Puts back what was on the plate before the preview started.</summary>
    private void OnCancel(object sender, RoutedEventArgs e) => preview(originals);

    protected override void OnClosed(EventArgs e)
    {
        if (Result is null) preview(originals);
        base.OnClosed(e);
    }
}
