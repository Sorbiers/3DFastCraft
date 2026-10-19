using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// The project's own page: what is in it, read off as it stands, and notes that are kept in the
/// file with it. The notes are the only thing here that changes anything.
/// </summary>
public partial class ProjectDialog : Window
{
    public ProjectDialog(string name, string summary, string notes)
    {
        InitializeComponent();

        NameText.Text = name;
        SummaryText.Text = summary;
        NotesBox.Text = notes;
        Loaded += (_, _) => NotesBox.Focus();
    }

    /// <summary>The notes as left when OK was pressed.</summary>
    public string Notes { get; private set; } = "";

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Notes = NotesBox.Text;
        DialogResult = true;
    }
}
