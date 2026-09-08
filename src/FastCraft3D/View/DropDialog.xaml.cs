using System.IO;
using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// What to do with files dropped on the window.
///
/// It asks rather than guessing. A dropped project could reasonably mean either thing - put this
/// up instead of what I have, or add it to what I have - and guessing wrong in the first
/// direction throws away work that was never saved.
/// </summary>
public partial class DropDialog : Window
{
    public DropDialog(IReadOnlyList<string> files)
    {
        InitializeComponent();

        SubjectText.Text = files.Count == 1
            ? Path.GetFileName(files[0])
            : $"{files.Count} files: " + string.Join(", ", files.Select(Path.GetFileName));

        // Opening puts one plate up in place of another, so it only means anything for a single
        // project file. Everything else can still be imported.
        bool project = files.Count == 1
                       && Path.GetExtension(files[0])
                           .Equals(Io.SceneSerializer.Extension, StringComparison.OrdinalIgnoreCase);

        OpenButton.IsEnabled = project;

        if (!project)
        {
            WhyNotText.Text = files.Count > 1
                ? "Only one project at a time can be opened, so these can only be imported."
                : "Only a 3DFastCraft project can be opened. A model file can be imported.";
            WhyNotText.Visibility = Visibility.Visible;
        }
        else
        {
            OpenButton.IsDefault = true;
            ImportButton.IsDefault = false;
        }
    }

    /// <summary>True to open the file as a project, false to import onto the current plate.</summary>
    public bool OpenAsProject { get; private set; }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        OpenAsProject = true;
        DialogResult = true;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        OpenAsProject = false;
        DialogResult = true;
    }
}
