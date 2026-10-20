using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Io;
using FastCraft3D.ViewModels;

namespace FastCraft3D.View;

/// <summary>
/// What comes up when the app starts with nothing to open: a new project, a file to open, the
/// projects pinned as favorites and the last few opened, each with its picture.
/// </summary>
public partial class StartDialog : Window
{
    public StartDialog(IReadOnlyList<MainViewModel.RecentEntry> pinned, IReadOnlyList<MainViewModel.RecentEntry> recent)
    {
        InitializeComponent();

        PinnedList.ItemsSource = pinned;
        RecentList.ItemsSource = recent;

        // Shown only when there is something under them.
        PinnedHeader.Visibility = PinnedList.Visibility = pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentHeader.Visibility = RecentList.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Escape is the way out of any dialog, and here it is the same as a new project: nothing opened.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            Close();
        };

        ShowBox.IsChecked = StartPage.ShowAtStartup;
        Closed += (_, _) => StartPage.ShowAtStartup = ShowBox.IsChecked == true;

        // The names at once, the pictures as they are read.
        Loaded += async (_, _) =>
        {
            foreach (var entry in pinned.Concat(recent)) await entry.LoadPictureAsync();
        };
    }

    /// <summary>What was chosen: open the file picker, or open this project. Neither means a new one.</summary>
    public bool OpenAFile { get; private set; }

    public string? Project { get; private set; }

    private void OnNew(object sender, RoutedEventArgs e) => Close();

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        OpenAFile = true;
        Close();
    }

    private void OnProject(object sender, RoutedEventArgs e)
    {
        Project = (sender as Button)?.Tag as string;
        Close();
    }
}
