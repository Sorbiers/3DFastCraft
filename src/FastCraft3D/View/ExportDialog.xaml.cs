using System.Windows;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks what to export before the file dialog appears.
///
/// It exists mainly so that scope is a deliberate choice. Exporting whatever happened to be
/// selected is how half a model reaches a slicer unnoticed, so the whole plate is the default
/// and narrowing to the selection has to be asked for.
/// </summary>
public partial class ExportDialog : Window
{
    private readonly Scene scene;
    private bool loaded;

    public ExportDialog(Scene scene)
    {
        this.scene = scene;
        InitializeComponent();

        int all = scene.Shown.Count;
        int selected = scene.Selection.Count;

        ScopeAll.Content = all == 1 ? "Everything on the plate - 1 object" : $"Everything on the plate - {all} objects";
        ScopeSelected.Content = selected == 1 ? "Selected only - 1 object" : $"Selected only - {selected} objects";
        ScopeSelected.IsEnabled = selected > 0;

        loaded = true;
        UpdateSummary();
    }

    /// <summary>Null until the user confirms.</summary>
    public ExportOptions? Result { get; private set; }

    private ExportOptions CurrentOptions() => new(
        Format3mf.IsChecked == true ? ExportFormat.ThreeMf
            : FormatObj.IsChecked == true ? ExportFormat.Obj
            : FormatAscii.IsChecked == true ? ExportFormat.AsciiStl
            : ExportFormat.BinaryStl,
        SelectedOnly: ScopeSelected.IsChecked == true,
        DropToPlate: DropToPlate.IsChecked == true,
        ByPlate: ByPlate.IsChecked == true);

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
        UpdateSummary();
    }

    /// <summary>
    /// 3MF only with a file per plate, and chosen with it: it is the one that keeps the parts and
    /// their filaments, which is what a slicer taking the plates one at a time wants.
    /// </summary>
    private void OnByPlateChanged(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;

        bool byPlate = ByPlate.IsChecked == true;
        Format3mf.IsEnabled = byPlate;
        if (byPlate) Format3mf.IsChecked = true;
        else if (Format3mf.IsChecked == true) FormatBinary.IsChecked = true;

        UpdateSummary();
    }

    /// <summary>
    /// Reports what the current options would actually write, including whether the result is
    /// printable. Better to see that here than after the file has been written.
    /// </summary>
    private void UpdateSummary()
    {
        var options = CurrentOptions();
        var subjects = ExportComposer.Subjects(scene, options);

        var plates = subjects.Select(o => o.Plate).Distinct().Order().ToList();
        ByPlate.IsEnabled = plates.Count > 1;
        ByPlate.Content = plates.Count > 1 ? $"One file per plate - {plates.Count} plates" : "One file per plate - it is all on one plate";
        if (options.ByPlate && plates.Count < 2)
        {
            ByPlate.IsChecked = false; // which comes back here with the options as they now are
            return;
        }

        if (subjects.Count == 0)
        {
            SummaryText.Text = "There is nothing on the plate to export.";
            WarningText.Visibility = Visibility.Collapsed;
            return;
        }

        Mesh merged = ExportComposer.MergeForStl(subjects, options.DropToPlate);
        var bounds = merged.ComputeBounds();

        // Each object on its own, as Repair judges them. The merged mesh is not a fair test: its
        // health welds points that coincide, so two sound parts touching - a stamp standing on a
        // block - read as thousands of edges shared by four faces, and the warning said the plate
        // was broken while Repair said, rightly, that nothing was.
        var healths = subjects.Select(o => (o.Name, Health: o.Mesh.CheckHealth())).ToList();
        var broken = healths.Where(h => !h.Health.IsWatertight || h.Health.IsInsideOut).ToList();

        string parts = subjects.Count == 1 ? "1 object" : $"{subjects.Count} objects";
        SummaryText.Text =
            $"{parts}, {healths.Sum(h => h.Health.TriangleCount):N0} triangles, {healths.Sum(h => h.Health.VolumeCm3):0.##} cm3\n" +
            $"Bounding size {bounds}";
        if (options.ByPlate)
            SummaryText.Text += $"\nA file for each of plates {string.Join(", ", plates.SkipLast(1))} and {plates[^1]}";

        WarningText.Visibility = broken.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = broken.Count switch
        {
            0 => string.Empty,
            1 => $"May not print cleanly: {broken[0].Name} - {broken[0].Health.Describe()}",
            _ => $"May not print cleanly: {broken.Count} objects are not sound - {string.Join(", ", broken.Take(3).Select(b => b.Name))}{(broken.Count > 3 ? "..." : "")}. Repair can mend them."
        };
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (ExportComposer.Subjects(scene, CurrentOptions()).Count == 0)
        {
            MessageBox.Show(this, "There is nothing on the plate to export.",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = CurrentOptions();
        DialogResult = true;
    }
}
