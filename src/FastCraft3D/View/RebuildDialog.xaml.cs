using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Chooses how finely to rebuild a model's surface.
///
/// The one number that matters is how big a voxel is, because that is the size of the smallest
/// feature that survives. The dialog works in voxels across the model - which is what governs
/// the cost - but says the millimetres, because that is what tells someone whether their
/// lettering is about to disappear.
/// </summary>
public partial class RebuildDialog : Window
{
    private readonly float longestSideMm;
    private readonly float surfaceAreaMm2;
    private bool updating;

    public RebuildDialog(IReadOnlyList<SceneObject> objects)
    {
        InitializeComponent();

        longestSideMm = Longest(objects);

        // The triangle count follows the area being covered, not the model's bulk.
        surfaceAreaMm2 = objects.Sum(o => o.ToWorldMesh().ComputeSurfaceArea());

        SubjectText.Text = objects.Count == 1
            ? $"Rebuilding {objects[0].Name}, {longestSideMm:0.#} mm at its longest."
            : $"Rebuilding {objects.Count} objects, up to {longestSideMm:0.#} mm at their longest.";

        ResolutionSlider.Minimum = VoxelRebuild.MinimumResolution;
        ResolutionSlider.Maximum = VoxelRebuild.MaximumResolution;
        ResolutionSlider.Value = VoxelRebuild.DefaultResolution;

        Describe();
    }

    /// <summary>The chosen resolution, or null when the dialog was cancelled.</summary>
    public int? Result { get; private set; }

    private int Resolution => (int)Math.Round(ResolutionSlider.Value);

    private static float Longest(IReadOnlyList<SceneObject> objects)
    {
        float longest = 0;
        foreach (var o in objects)
        {
            var size = o.WorldBounds.Size;
            longest = Math.Max(longest, Math.Max(size.X, Math.Max(size.Y, size.Z)));
        }

        return longest;
    }

    private void Describe()
    {
        float voxel = longestSideMm > 0 ? longestSideMm / Resolution : 0;

        long triangles = VoxelRebuild.EstimateTriangles(surfaceAreaMm2, voxel);

        string detail = voxel > 1f
            ? $"Detail finer than {voxel:0.##} mm goes - that is coarse, and lettering or engraving "
              + "will go with it."
            : voxel > 0.3f
                ? $"Detail finer than {voxel:0.##} mm goes. Fine enough for shapes, not for engraving."
                : $"Detail finer than {voxel:0.##} mm goes, so most of it survives.";

        // The count is the part worth reading. It quadruples every time the detail doubles, and a
        // plain cube at the finest setting is a third of a million triangles.
        string cost = triangles > 400_000
            ? $"About {triangles:N0} triangles - very heavy, and slow to work with afterwards."
            : triangles > 120_000
                ? $"About {triangles:N0} triangles - heavy."
                : $"About {triangles:N0} triangles.";

        WarningText.Text = detail + Environment.NewLine + cost
                           + Environment.NewLine + Environment.NewLine
                           + "The surface is rebuilt, so it will not be the mesh you started with "
                           + "even where nothing was wrong.";

        updating = true;
        try { ResolutionBox.Text = Resolution.ToString(CultureInfo.CurrentCulture); }
        finally { updating = false; }
    }

    private void OnResolutionDragged(object sender, RoutedPropertyChangedEventArgs<double> e) => Describe();

    private void OnResolutionTyped(object sender, TextChangedEventArgs e)
    {
        if (updating) return;
        if (!int.TryParse(ResolutionBox.Text, out int wanted)) return;

        ResolutionSlider.Value = Math.Clamp(
            wanted, VoxelRebuild.MinimumResolution, VoxelRebuild.MaximumResolution);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Resolution;
        DialogResult = true;
    }
}
