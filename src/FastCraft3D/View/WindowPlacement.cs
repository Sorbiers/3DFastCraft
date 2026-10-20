using System.IO;
using System.Text.Json;
using System.Windows;

namespace FastCraft3D.View;

/// <summary>
/// Where the window was and whether it filled the screen, so the next start puts it back.
///
/// The size kept is the window's ordinary one even when it closed maximised, so taking it out of
/// maximised next time gives back the size it had rather than a full-screen window not marked as
/// one. A place that is no longer on any screen - a monitor unplugged since - is not used.
/// </summary>
public static class WindowPlacement
{
    private sealed record Kept(double Left, double Top, double Width, double Height, bool Maximised);

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3DFastCraft", "window.json");

    /// <summary>Puts the window back where it was, before it is shown.</summary>
    public static void Restore(Window window)
    {
        try
        {
            if (!File.Exists(StorePath) || JsonSerializer.Deserialize<Kept>(File.ReadAllText(StorePath)) is not { } kept) return;

            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var bounds = new Rect(kept.Left, kept.Top, kept.Width, kept.Height);

            // Enough of its title bar on a screen to take hold of, or it is left to the default.
            if (kept.Width >= 400 && kept.Height >= 300 && screen.IntersectsWith(new Rect(bounds.Left, bounds.Top, bounds.Width, 40)))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = kept.Left;
                window.Top = kept.Top;
                window.Width = kept.Width;
                window.Height = kept.Height;
            }

            if (kept.Maximised) window.WindowState = WindowState.Maximized;
        }
        catch
        {
            // A damaged file only means the window opens where it would have anyway.
        }
    }

    /// <summary>
    /// Follows the window while the app runs and notes where it was as it closes.
    ///
    /// Read as it goes, not at the end: once the window has closed it reports itself as normal
    /// whatever it was, so a window closed maximised was kept as not being, and opened small.
    /// </summary>
    public static void Track(Window window)
    {
        Rect normal = Rect.Empty;
        bool maximised = window.WindowState == WindowState.Maximized;

        void Note()
        {
            if (window.WindowState == WindowState.Normal && window.IsLoaded)
                normal = new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight);

            // Minimised says nothing about how it should come back.
            if (window.WindowState != WindowState.Minimized) maximised = window.WindowState == WindowState.Maximized;
        }

        window.StateChanged += (_, _) => Note();
        window.LocationChanged += (_, _) => Note();
        window.SizeChanged += (_, _) => Note();
        window.Loaded += (_, _) => Note();

        window.Closing += (_, e) =>
        {
            if (e.Cancel) return;
            Note();
            if (normal.IsEmpty) normal = window.RestoreBounds;
            Save(normal, maximised);
        };
    }

    private static void Save(Rect normal, bool maximised)
    {
        try
        {
            if (normal.IsEmpty || normal.Width <= 0 || normal.Height <= 0) return;

            var kept = new Kept(normal.Left, normal.Top, normal.Width, normal.Height, maximised);
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(kept));
        }
        catch
        {
            // Not worth troubling anyone over on the way out.
        }
    }
}
