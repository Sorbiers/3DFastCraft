using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Io;
using FastCraft3D.ViewModels;

namespace FastCraft3D.View;

/// <summary>
/// What comes up when the app starts with nothing to open: a new project, a file to open, the
/// projects pinned to it and the last few opened, each with its picture.
/// </summary>
public partial class StartDialog : Window
{
    private readonly System.Collections.ObjectModel.ObservableCollection<MainViewModel.RecentEntry> pinnedShown;
    private readonly Action<string> unpin;

    public StartDialog(IReadOnlyList<MainViewModel.RecentEntry> pinned, IReadOnlyList<MainViewModel.RecentEntry> recent, Action<string> unpin)
    {
        InitializeComponent();

        this.unpin = unpin;
        pinnedShown = new(pinned);
        PinnedList.ItemsSource = pinnedShown;
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

    private void OnUnpin(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } button) return;
        e.Handled = true;
        button.IsEnabled = false;
        unpin(path);

        // Faded out before it goes, so the tiles below closing up reads as the one pulled out.
        var entry = pinnedShown.First(p => p.Path == path);
        void Gone()
        {
            pinnedShown.Remove(entry);
            if (pinnedShown.Count == 0) PinnedHeader.Visibility = PinnedList.Visibility = Visibility.Collapsed;
        }

        if (PinnedList.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement tile)
        {
            Gone();
            return;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Gone();
        tile.BeginAnimation(OpacityProperty, fade);
    }

    private void OnProject(object sender, RoutedEventArgs e)
    {
        Project = (sender as Button)?.Tag as string;
        Close();
    }
}
