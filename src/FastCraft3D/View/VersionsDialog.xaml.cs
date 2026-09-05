using System.IO;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Io;

namespace FastCraft3D.View;

/// <summary>
/// Lists the versions kept inside a project file, and restores or forgets one.
/// </summary>
public partial class VersionsDialog : Window
{
    private readonly string projectPath;

    public VersionsDialog(string projectPath)
    {
        this.projectPath = projectPath;
        InitializeComponent();

        HeaderText.Text = $"Versions kept in {Path.GetFileName(projectPath)}.";
        Refresh();
    }

    /// <summary>The version the user chose to restore, or null.</summary>
    public int? RestoreIndex { get; private set; }

    private void Refresh()
    {
        var versions = SceneSerializer.ReadVersions(projectPath);

        VersionList.ItemsSource = versions
            .Select((v, i) => new
            {
                Index = i,
                v.Label,
                // Shown in local time: a version is a note to yourself about when you were working.
                Detail = $"{v.SavedUtc.ToLocalTime():d MMM yyyy HH:mm} - " +
                         (v.ObjectCount == 1 ? "1 object" : $"{v.ObjectCount} objects")
            })
            .Reverse() // newest first, which is what anyone looks for
            .ToList();

        if (versions.Count == 0)
        {
            HeaderText.Text = $"No versions kept in {Path.GetFileName(projectPath)} yet. " +
                              "Use \"Save a version\" on the File tab to keep one.";
        }

        UpdateButtons();
    }

    private int? SelectedIndex()
    {
        dynamic? item = VersionList.SelectedItem;
        return item is null ? null : (int)item.Index;
    }

    private void UpdateButtons()
    {
        bool any = VersionList.SelectedItem is not null;
        RestoreButton.IsEnabled = any;
        DeleteButton.IsEnabled = any;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (SelectedIndex() is not { } index) return;

        RestoreIndex = index;
        DialogResult = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (SelectedIndex() is not { } index) return;

        var versions = SceneSerializer.ReadVersions(projectPath);
        if (index >= versions.Count) return;

        // Forgetting a version cannot be undone, so it is confirmed rather than just done.
        var answer = MessageBox.Show(this,
            $"Forget the version \"{versions[index].Label}\"?\n\nThis cannot be undone.",
            "Forget version", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        SceneSerializer.DeleteVersion(projectPath, index);
        Refresh();
    }
}
