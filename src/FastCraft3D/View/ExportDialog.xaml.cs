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

        int all = scene.Objects.Count;
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
        FormatObj.IsChecked == true ? ExportFormat.Obj
            : FormatAscii.IsChecked == true ? ExportFormat.AsciiStl
            : ExportFormat.BinaryStl,
        SelectedOnly: ScopeSelected.IsChecked == true,
        DropToPlate: DropToPlate.IsChecked == true);

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!loaded) return;
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

        if (subjects.Count == 0)
        {
            SummaryText.Text = "There is nothing on the plate to export.";
            WarningText.Visibility = Visibility.Collapsed;
            return;
        }

        Mesh merged = ExportComposer.MergeForStl(subjects, options.DropToPlate);
        var health = merged.CheckHealth();
        var bounds = merged.ComputeBounds();

        string parts = subjects.Count == 1 ? "1 object" : $"{subjects.Count} objects";
        SummaryText.Text =
            $"{parts}, {health.TriangleCount:N0} triangles, {health.VolumeCm3:0.##} cm3\n" +
            $"Bounding size {bounds}";

        bool printable = health.IsWatertight && !health.IsInsideOut;
        WarningText.Visibility = printable ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = printable ? string.Empty : $"May not print cleanly: {health.Describe()}";
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
