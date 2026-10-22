using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// A message box that belongs to the app's window.
///
/// MessageBox.Show without an owner takes whatever window Windows thinks is active, and when there
/// is none - the app in the background, or asked from the window's own Closing - the box stood
/// alone: its own thumbnail on the taskbar, free to fall behind the window it was asking about.
/// </summary>
internal static class Prompt
{
    public static MessageBoxResult Show(string text, string caption = "", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult preset = MessageBoxResult.None)
    {
        var app = Application.Current;
        var owner = app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app?.MainWindow;
        return owner is { IsLoaded: true }
            ? MessageBox.Show(owner, text, caption, buttons, image, preset)
            : MessageBox.Show(text, caption, buttons, image, preset);
    }
}
