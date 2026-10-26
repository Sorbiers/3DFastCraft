using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// The project's own page: what is in it, read off as it stands, and notes that are kept in the
/// file with it; and whether a .3dfp is kept beside it, and what new projects do about one.
/// </summary>
public partial class ProjectDialog : Window
{
    /// <param name="projectFileName">The .3dfp this project would keep, by name, to say which file is meant.</param>
    /// <param name="newProjects">What new projects do: null asks, true keeps a .3dfp, false does not.</param>
    public ProjectDialog(string name, string summary, string notes, string projectFileName, bool keepsProjectFile, bool? newProjects)
    {
        InitializeComponent();

        NameText.Text = name;
        SummaryText.Text = summary;
        NotesBox.Text = notes;
        KeepBox.Content = $"Keep a project file beside the .3mf ({projectFileName})";
        KeepBox.IsChecked = keepsProjectFile;
        DefaultBox.SelectedIndex = newProjects switch { null => 0, true => 1, false => 2 };
        Loaded += (_, _) => NotesBox.Focus();
    }

    /// <summary>The notes as left when OK was pressed.</summary>
    public string Notes { get; private set; } = "";

    public bool KeepsProjectFile { get; private set; }

    public bool? NewProjects { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Notes = NotesBox.Text;
        KeepsProjectFile = KeepBox.IsChecked == true;
        NewProjects = DefaultBox.SelectedIndex switch { 1 => true, 2 => false, _ => null };
        DialogResult = true;
    }
}
