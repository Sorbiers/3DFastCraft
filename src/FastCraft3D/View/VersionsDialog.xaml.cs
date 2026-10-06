using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                Picture = Decode(v.Thumbnail),
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

    /// <summary>The picture kept with a version, or null if it has none or the bytes are not a picture.</summary>
    private static System.Windows.Media.Imaging.BitmapSource? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;

        try
        {
            using var stream = new MemoryStream(png);
            var picture = new System.Windows.Media.Imaging.BitmapImage();
            picture.BeginInit();
            picture.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            picture.StreamSource = stream;
            picture.EndInit();
            picture.Freeze();
            return picture;
        }
        catch
        {
            // A picture that will not decode is not worth losing the list over.
            return null;
        }
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

    /// <summary>
    /// A double click on a version restores it, as Restore does. Only on a version: the list takes
    /// a double click anywhere in it, on its scroll bar or the empty space below the last one,
    /// and none of those is a choice.
    /// </summary>
    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(VersionList, source) is ListBoxItem)
        {
            OnRestore(sender, e);
        }
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
