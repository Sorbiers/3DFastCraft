using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks how the cells should come out.
///
/// The numbers are not independent, which is the whole reason this is a panel and not four boxes.
/// Everything is cut on a voxel grid, so a strut finer than about three voxels comes out in lumps;
/// and a web of hundreds of cells over a large surface runs to a great many triangles. Both are
/// worth saying before pressing the button rather than after, so the panel keeps the detail slider
/// honest against the strut and says what the result is about to cost.
/// </summary>
public partial class VoronoiDialog : ToolPanel
{
    private readonly float longestSideMm;
    private readonly float surfaceAreaMm2;
    private readonly float thinnestMm;
    private readonly Action<VoronoiOptions?> preview;
    private bool updating;

    public VoronoiDialog(IReadOnlyList<SceneObject> objects, Action<VoronoiOptions?> preview)
    {
        this.preview = preview;
        InitializeComponent();

        longestSideMm = objects.Max(Longest);
        thinnestMm = objects.Min(Thinnest);
        surfaceAreaMm2 = objects.Sum(o => o.ToWorldMesh().ComputeSurfaceArea());

        SubjectText.Text = objects.Count == 1
            ? $"Cutting cells into {objects[0].Name}, {longestSideMm:0.#} mm at its longest."
            : $"Cutting cells into {objects.Count} objects, the longest {longestSideMm:0.#} mm.";

        var start = VoronoiOptions.Default;

        CellsSlider.Value = start.Cells;
        StrutSlider.Value = Math.Clamp(longestSideMm / 60f, 1f, 4f);
        SkinSlider.Value = Math.Clamp(longestSideMm / 50f, 1.2f, 4f);
        BaseSlider.Value = 0;
        DetailSlider.Value = Voronoi.WantedResolution(longestSideMm, (float)StrutSlider.Value);

        Describe();
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Describe();
    }

    /// <summary>The chosen settings, or null when the panel was cancelled.</summary>
    public VoronoiOptions? Result { get; private set; }

    private VoronoiKind Kind => LatticeButton.IsChecked == true ? VoronoiKind.Lattice : VoronoiKind.Shell;
    private int Cells => (int)Math.Round(CellsSlider.Value);
    private float Strut => (float)StrutSlider.Value;
    private float Skin => (float)SkinSlider.Value;
    private float Base => (float)BaseSlider.Value;
    private int Resolution => (int)Math.Round(DetailSlider.Value);

    private static float Longest(SceneObject o)
    {
        var s = o.WorldBounds.Size;
        return Math.Max(s.X, Math.Max(s.Y, s.Z));
    }

    private static float Thinnest(SceneObject o)
    {
        var s = o.WorldBounds.Size;
        return Math.Min(s.X, Math.Min(s.Y, s.Z));
    }

    private void Describe()
    {
        if (!IsInitialized) return;

        float voxel = longestSideMm > 0 ? longestSideMm / Resolution : 0f;
        bool lattice = Kind == VoronoiKind.Lattice;

        SkinLabel.Text = lattice ? "Skin" : "Web depth";

        // Roughly how far apart the seeds land, which is what someone is actually choosing when
        // they drag the cell count: a number of cells means nothing without a size beside it.
        float across = surfaceAreaMm2 > 0 && Cells > 0
            ? MathF.Sqrt(surfaceAreaMm2 / Cells)
            : 0f;

        CellsText.Text = across > 0
            ? $"{Cells} - about {across:0.#} mm across each"
            : $"{Cells}";

        DetailText.Text = voxel > 0
            ? $"{Resolution} voxels, {voxel:0.###} mm each"
            : $"{Resolution}";

        int wanted = Voronoi.WantedResolution(longestSideMm, Strut);

        string warning =
            voxel > 0 && Strut < voxel * 2f
                ? $"A {Strut:0.##} mm strut is under two voxels at this detail: it will come out in "
                  + $"lumps, or not at all. Raise the detail to {wanted}, or thicken the strut."
                : voxel > 0 && Strut < voxel * Voronoi.VoxelsPerStrut
                    ? $"A {Strut:0.##} mm strut is about {Strut / voxel:0.#} voxels across - it will "
                      + $"hold, but it will read as rough. {wanted} detail would do it properly."
                    : Strut <= Voronoi.LeastStrutMm + 0.01f
                        ? $"{Strut:0.##} mm is two nozzle widths - the thinnest a printer will lay "
                          + "down, and it will be fragile."
                        : !lattice && Skin < Strut
                            ? $"The web is {Skin:0.##} mm deep and {Strut:0.##} mm wide, so the "
                              + "struts are flatter than they are broad. That prints, but it reads "
                              + "as a cut-out rather than a web."
                            : Estimate(voxel);

        if (Base <= 0f && !lattice)
            warning += Environment.NewLine + Environment.NewLine
                     + "With no solid base the web runs all the way to the bed and the first layer "
                     + "is a handful of struts. A few millimetres of base is usually worth it.";

        WarningText.Text = warning;

        updating = true;
        try
        {
            StrutBox.Text = Strut.ToString("0.##", CultureInfo.CurrentCulture);
            SkinBox.Text = Skin.ToString("0.##", CultureInfo.CurrentCulture);
            BaseBox.Text = Base.ToString("0.##", CultureInfo.CurrentCulture);
        }
        finally { updating = false; }

        // The web itself, on the model. Only the cell count and the strut move it - the skin, the
        // base and the detail are about the solid behind it - but it costs milliseconds and
        // asking every time is simpler than working out which slider moved.
        preview(new VoronoiOptions(Kind, Cells, Strut, Skin, Base, Resolution).Sane());
    }

    /// <summary>
    /// What the result is about to cost. A web has far more surface than the shape it came from -
    /// every strut has four sides and two ends - so the rebuilder's own estimate is doubled.
    /// </summary>
    private string Estimate(float voxel)
    {
        if (voxel <= 0) return "";

        long triangles = VoxelRebuild.EstimateTriangles(surfaceAreaMm2, voxel) * 2;

        return triangles > 400_000
            ? $"About {triangles:N0} triangles - heavy. Simplify afterwards, or drop the detail."
            : $"About {triangles:N0} triangles.";
    }

    private void OnDragged(object sender, RoutedPropertyChangedEventArgs<double> e) => Describe();

    private void OnChoice(object sender, RoutedEventArgs e) => Describe();

    private void OnStrutTyped(object sender, TextChangedEventArgs e) => Typed(StrutBox, StrutSlider);

    private void OnSkinTyped(object sender, TextChangedEventArgs e) => Typed(SkinBox, SkinSlider);

    private void OnBaseTyped(object sender, TextChangedEventArgs e) => Typed(BaseBox, BaseSlider);

    private void Typed(TextBox box, Slider slider)
    {
        if (updating) return;
        if (!float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float value)) return;

        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = new VoronoiOptions(Kind, Cells, Strut, Skin, Base, Resolution).Sane();
        DialogResult = true;
    }
}
