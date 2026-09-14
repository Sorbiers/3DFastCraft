using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a custom shape, showing it on the plate as the numbers change.
///
/// Boxes that mean nothing for the chosen shape are greyed rather than hidden, so the dialog does
/// not jump about as the shape changes and it is plain what each shape takes.
/// </summary>
public partial class CustomShapeDialog : Window
{
    private readonly Action<Mesh?> preview;
    private readonly Action<bool> wireframe;
    private bool loading;

    /// <param name="preview">Shows a mesh on the plate, or takes the preview away when given null.</param>
    /// <param name="wireframe">Turns the viewport's triangle edges on or off.</param>
    public CustomShapeDialog(CustomShape start, bool showingWireframe, Action<Mesh?> preview, Action<bool> wireframe)
    {
        this.preview = preview;
        this.wireframe = wireframe;
        InitializeComponent();

        loading = true;
        foreach (var kind in CustomShape.Kinds) KindBox.Items.Add(kind);
        KindBox.SelectedItem = start.Kind;

        WidthBox.Text = Format(start.Width);
        DepthBox.Text = Format(start.Depth);
        HeightBox.Text = Format(start.Height);
        SegmentsBox.Text = start.Segments.ToString(CultureInfo.CurrentCulture);
        RingsBox.Text = start.Rings.ToString(CultureInfo.CurrentCulture);
        RoundnessBox.Text = Format(start.Roundness);
        WireframeBox.IsChecked = showingWireframe;
        loading = false;

        Refresh();
    }

    /// <summary>Null until the user adds the shape.</summary>
    public CustomShape? Result { get; private set; }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>What the boxes say, with anything unreadable left at what the shape last had.</summary>
    private CustomShape Read()
    {
        var kind = KindBox.SelectedItem is PrimitiveKind k ? k : PrimitiveKind.Cylinder;
        var fallback = CustomShape.Default;

        return new CustomShape(
            kind,
            Number(WidthBox, fallback.Width),
            Number(DepthBox, fallback.Depth),
            Number(HeightBox, fallback.Height),
            Whole(SegmentsBox, fallback.Segments),
            Whole(RingsBox, fallback.Rings),
            Number(RoundnessBox, 0f));

        static float Number(TextBox box, float otherwise) =>
            float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : otherwise;

        static int Whole(TextBox box, int otherwise) =>
            int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int v) ? v : otherwise;
    }

    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        var shape = Read();
        var sane = shape.Sane();

        SegmentsBox.IsEnabled = shape.UsesSegments;
        SegmentsNote.Text = shape.Kind == PrimitiveKind.Cube
            ? "round each rounded edge"
            : $"round the circumference, {CustomShape.MinimumSegments} to {CustomShape.MaximumSegments}";

        RingsBox.IsEnabled = shape.UsesRings;
        RingsNote.Text = shape.Kind == PrimitiveKind.Torus ? "round the tube" : "pole to pole";

        RoundnessBox.IsEnabled = shape.CanRound;
        RoundnessNote.Text = shape.CanRound ? $"mm, up to {sane.MaximumRoundness:0.##}" : "only a cube or cylinder";

        HeightNote.Text = shape.Kind == PrimitiveKind.Torus ? "mm, the thickness of the ring" : "mm, along Z";

        var mesh = sane.Build();
        preview(mesh);

        var notes = new List<string>();
        if (shape.Kind == PrimitiveKind.Torus && shape.Height > sane.Height + 1e-3f)
            notes.Add($"A ring this wide can be at most {sane.Height:0.##} mm thick, or its hole closes up.");
        if (shape.CanRound && shape.Roundness > sane.Roundness + 1e-3f)
            notes.Add($"Roundness is held to {sane.Roundness:0.##} mm, the most this size allows.");
        if (shape.Segments != sane.Segments && shape.UsesSegments)
            notes.Add($"Segments are held to {sane.Segments}.");

        SummaryText.Text = $"{mesh.TriangleCount:N0} triangles."
            + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "")
            + "\nMore segments look smoother but make Subtract, Merge and the other cutting tools slower. "
            + "Round edges on the Edit tab rebuilds a shape at the usual 32 segments, so round it here instead.";
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnKindChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnWireframeChanged(object sender, RoutedEventArgs e)
    {
        if (!loading && IsInitialized) wireframe(WireframeBox.IsChecked == true);
    }

    protected override void OnClosed(EventArgs e)
    {
        preview(null);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Read().Sane();
        DialogResult = true;
    }
}
