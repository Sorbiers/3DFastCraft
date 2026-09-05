using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks for the radius to round a primitive's edges to.
///
/// The dialog only ever sees objects that are still primitives; the caller filters them out.
/// It reports the largest radius each shape allows, because past that a box is simply a sphere
/// and the number stops meaning anything.
/// </summary>
public partial class RoundDialog : Window
{
    private readonly IReadOnlyList<SceneObject> subjects;
    private readonly Action<IReadOnlyList<Mesh>?> preview;
    private float maximum;
    private bool updating;

    /// <param name="preview">
    /// Shows a set of meshes on the plate, or puts the originals back when given null. Rounding
    /// rebuilds the shape from its parameters rather than filleting it, so what a given radius
    /// looks like is genuinely hard to picture - much easier to see.
    /// </param>
    public RoundDialog(IReadOnlyList<SceneObject> subjects, Action<IReadOnlyList<Mesh>?> preview)
    {
        this.subjects = subjects;
        this.preview = preview;
        InitializeComponent();

        SubjectText.Text = subjects.Count == 1
            ? $"Rounding {subjects[0].Name}."
            : $"Rounding {subjects.Count} objects.";

        // A cylinder has no upright edges, so that option would do nothing.
        bool anySides = subjects.Any(o => RoundedPrimitives.AvailableEdges(o.Origin!.Value)
            .HasFlag(RoundEdges.Sides));
        EdgeSides.IsEnabled = anySides;
        if (!anySides)
        {
            EdgeSides.IsChecked = false;
            EdgeSides.ToolTip = "A cylinder has no upright edges to round.";
        }

        updating = true;
        RefreshLimit(preferred: null);
        updating = false;

        UpdateSummary();
        ShowPreview();
    }

    /// <summary>Null until the user confirms.</summary>
    public float? Result { get; private set; }

    /// <summary>
    /// Rebuilds each shape at the current radius and puts it on the plate. Cheap enough to do on
    /// every slider move: these are primitives being regenerated from a handful of parameters,
    /// not meshes being reworked.
    /// </summary>
    private void ShowPreview()
    {
        if (!IsInitialized) return;

        var edges = SelectedEdges();
        if (edges == RoundEdges.None || Radius <= 0)
        {
            preview(null);
            return;
        }

        var rounded = new List<Mesh>(subjects.Count);
        foreach (var o in subjects)
        {
            var size = new Vector3(o.SizeX, o.SizeY, o.SizeZ);
            rounded.Add(RoundedPrimitives.Create(o.Origin!.Value, size, Radius, edges));
        }

        preview(rounded);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Result is null) preview(null);
        base.OnClosed(e);
    }

    /// <summary>Which edge groups the user asked for.</summary>
    public RoundEdges Edges { get; private set; } = RoundEdges.All;

    private RoundEdges SelectedEdges()
    {
        var edges = RoundEdges.None;
        if (EdgeTop.IsChecked == true) edges |= RoundEdges.Top;
        if (EdgeBottom.IsChecked == true) edges |= RoundEdges.Bottom;
        if (EdgeSides.IsChecked == true) edges |= RoundEdges.Sides;
        return edges;
    }

    /// <summary>
    /// The ceiling depends on which edges are being rounded - rounding only the upright edges of
    /// a tall thin box is limited by its footprint, not its height - so it is recomputed whenever
    /// the choice changes, keeping the current radius if it still fits.
    /// </summary>
    private void RefreshLimit(float? preferred)
    {
        var edges = SelectedEdges();

        maximum = subjects.Count == 0 || edges == RoundEdges.None
            ? 0f
            : subjects.Min(o => RoundedPrimitives.MaximumRadius(
                o.Origin!.Value, new Vector3(o.SizeX, o.SizeY, o.SizeZ), edges));

        MaximumText.Text = maximum > 0 ? $"of {maximum:0.##} mm max" : "no edges selected";
        RadiusSlider.Maximum = Math.Max(maximum, 0.01);
        RadiusSlider.IsEnabled = maximum > 0;
        RadiusBox.IsEnabled = maximum > 0;

        Radius = preferred is { } want ? Math.Min(want, maximum) : maximum / 4f;
    }

    private void OnEdgesChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;

        updating = true;
        RefreshLimit(preferred: Radius);
        updating = false;

        UpdateSummary();
        ShowPreview();
    }

    private float Radius
    {
        get => (float)RadiusSlider.Value;
        set
        {
            RadiusSlider.Value = Math.Clamp(value, 0, maximum);
            RadiusBox.Text = RadiusSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        }
    }

    private void OnRadiusDragged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || !IsLoaded) return;
        updating = true;
        RadiusBox.Text = RadiusSlider.Value.ToString("0.##", CultureInfo.CurrentCulture);
        updating = false;
        UpdateSummary();
        ShowPreview();
    }

    private void OnRadiusTyped(object sender, TextChangedEventArgs e)
    {
        if (updating || !IsLoaded) return;
        if (!float.TryParse(RadiusBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float typed)) return;

        updating = true;
        RadiusSlider.Value = Math.Clamp(typed, 0, maximum);
        updating = false;
        UpdateSummary();
        ShowPreview();
    }

    /// <summary>
    /// Shows the result before it is applied, including the warning that matters most: rounding
    /// regenerates the shape, so any earlier non-uniform scaling is baked in at that moment.
    /// </summary>
    private void UpdateSummary()
    {
        float radius = Radius;

        var edges = SelectedEdges();

        if (edges == RoundEdges.None)
        {
            SummaryText.Text = "Choose at least one group of edges to round.";
            return;
        }

        if (radius <= 0.001f)
        {
            SummaryText.Text = "A radius of zero leaves the shape with sharp edges.";
            return;
        }

        var first = subjects[0];
        var size = new Vector3(first.SizeX, first.SizeY, first.SizeZ);
        var preview = RoundedPrimitives.Create(first.Origin!.Value, size, radius, edges);

        SummaryText.Text =
            $"{first.Name}: {preview.TriangleCount:N0} triangles after rounding.\n" +
            "The shape is rebuilt at its current size, so its scale is reset - resizing it "
            + "unevenly afterwards will stretch the rounded edges.";
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var edges = SelectedEdges();
        if (edges == RoundEdges.None)
        {
            MessageBox.Show(this, "Choose at least one group of edges to round.",
                "Round edges", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Edges = edges;
        Result = Radius;
        DialogResult = true;
    }
}
