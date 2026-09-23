using System.Windows;
using FastCraft3D.Io;

namespace FastCraft3D.View;

/// <summary>Asks which format Export session writes, one file per undo step.</summary>
public partial class ExportSessionDialog : Window
{
    public ExportSessionDialog(int stepCount)
    {
        InitializeComponent();

        SummaryText.Text = stepCount == 1
            ? "1 step taken since the plate was last empty - it will be written as 2 files, the start and that step."
            : $"{stepCount} steps taken since the plate was last empty - they will be written as {stepCount + 1} files, the start and each step.";
    }

    /// <summary>Null until the user confirms.</summary>
    public SessionExportFormat? Result { get; private set; }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        Result = FormatStl.IsChecked == true ? SessionExportFormat.Stl
            : FormatThreeMf.IsChecked == true ? SessionExportFormat.ThreeMf
            : SessionExportFormat.Project;

        DialogResult = true;
    }
}
