using System.IO;
using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// What to do with a project dropped on the window.
///
/// It asks rather than guessing. A dropped project could reasonably mean either thing - put this
/// up instead of what I have, or add it to what I have - and guessing wrong in the first
/// direction throws away work that was never saved. Only a project comes here: see
/// <see cref="Io.IncomingFiles.NeedsAsking"/>.
/// </summary>
public partial class DropDialog : Window
{
    public DropDialog(string project)
    {
        InitializeComponent();

        SubjectText.Text = Path.GetFileName(project);
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
