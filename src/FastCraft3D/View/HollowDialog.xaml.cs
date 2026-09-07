using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <param name="WallMm">How thick to leave the wall.</param>
/// <param name="Resolution">Voxels along the model's longest side.</param>
/// <param name="Open">A side to leave open, so the cavity reaches daylight.</param>
public readonly record struct HollowSettings(float WallMm, int Resolution, OpenSide Open);

/// <summary>
/// Asks how thick a wall to leave.
///
/// Two numbers, and they are not independent: the wall is measured on a grid, so a wall much
/// finer than a couple of voxels cannot be held accurately. The dialog says so rather than
/// letting someone ask for a 0.5 mm wall at a resolution that can only place it to the nearest
/// millimetre.
/// </summary>
public partial class HollowDialog : Window
{
    private readonly float longestSideMm;
    private readonly float thinnestMm;
    private readonly float surfaceAreaMm2;
    private bool updating;

    public HollowDialog(IReadOnlyList<SceneObject> objects)
    {
        InitializeComponent();

        longestSideMm = objects.Max(o => Longest(o));
        thinnestMm = objects.Min(o => Thinnest(o));
        surfaceAreaMm2 = objects.Sum(o => o.ToWorldMesh().ComputeSurfaceArea());

        SubjectText.Text = objects.Count == 1
            ? $"Hollowing {objects[0].Name}, {thinnestMm:0.#} mm across at its thinnest."
            : $"Hollowing {objects.Count} objects, the thinnest {thinnestMm:0.#} mm across.";

        WallSlider.Value = Math.Clamp(thinnestMm / 8f, 1.2f, 4f);
        DetailSlider.Value = VoxelRebuild.DefaultResolution;

        Describe();
    }

    /// <summary>The chosen settings, or null when the dialog was cancelled.</summary>
    public HollowSettings? Result { get; private set; }

    private float Wall => (float)WallSlider.Value;
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

        float voxel = longestSideMm > 0 ? longestSideMm / Resolution : 0;

        DetailText.Text = voxel > 0 ? $"{Resolution} voxels, {voxel:0.###} mm each" : $"{Resolution}";

        // A wall the part has no room for, and a wall the grid cannot measure, are the two ways
        // this goes wrong; both are worth saying before rather than after.
        string warning =
            Wall * 2 >= thinnestMm
                ? $"A {Wall:0.#} mm wall on both sides leaves nothing in a part {thinnestMm:0.#} mm "
                  + "thick - it will come back solid."
                : voxel > 0 && Wall < voxel * 2
                    ? $"A {Wall:0.#} mm wall is thinner than two voxels at this detail, so it will "
                      + "come out uneven. Raise the detail or thicken the wall."
                    : Wall < 1f
                        ? $"{Wall:0.#} mm is a thin wall - fine on resin, fragile on FDM."
                        : $"About {VoxelRebuild.EstimateTriangles(surfaceAreaMm2, voxel) * 2:N0} "
                          + "triangles - the outside and the cavity both.";

        WarningText.Text = warning + Environment.NewLine + Environment.NewLine
                           + "The cavity is sealed. A resin print needs a drain hole, which a "
                           + "cylinder and Subtract will cut.";

        updating = true;
        try { WallBox.Text = Wall.ToString("0.##", CultureInfo.CurrentCulture); }
        finally { updating = false; }
    }

    private void OnWallDragged(object sender, RoutedPropertyChangedEventArgs<double> e) => Describe();

    private void OnWallTyped(object sender, TextChangedEventArgs e)
    {
        if (updating) return;
        if (!float.TryParse(WallBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float typed)) return;

        WallSlider.Value = Math.Clamp(typed, WallSlider.Minimum, WallSlider.Maximum);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = new HollowSettings(Wall, Resolution, (OpenSide)Math.Max(OpenBox.SelectedIndex, 0));
        DialogResult = true;
    }
}
