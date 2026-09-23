using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FastCraft3D.Render;

/// <summary>
/// Captures a window exactly as it looks on screen.
///
/// A Direct3D surface composites through DWM onto the desktop correctly, where BitBlt on the
/// window's own device context often does not - the viewport comes back black. Copying the
/// screen region the window occupies sidesteps that: it is the same technique the drive-app
/// tooling in tools/ui/shot.ps1 already uses, and the same crop trims the sliver of whatever
/// sits behind the window that GetWindowRect otherwise takes in.
/// </summary>
public static class WindowCapture
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// Brings the window to the front and captures it as a bitmap the caller owns and must
    /// dispose. Separate from <see cref="Save"/> so a "before" shot can be held in memory until
    /// whatever comes after it is known - its file name, in Record's case - rather than having to
    /// be named at the moment it is taken.
    /// </summary>
    public static Bitmap? Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return null;

        SetForegroundWindow(handle);

        if (!GetWindowRect(handle, out var r)) return null;
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        if (width <= 0 || height <= 0) return null;

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);

        // The window rect takes in a sliver of whatever is behind it, so the frame is trimmed
        // off - the same crop shot.ps1 uses.
        var crop = new Rectangle(8, 6, Math.Max(width - 16, 1), Math.Max(height - 15, 1));
        return bitmap.Clone(crop, bitmap.PixelFormat);
    }

    public static void SaveBitmap(Bitmap bitmap, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>Captures the window and saves it in one step. Does nothing if the capture failed.</summary>
    public static void Save(Window window, string path)
    {
        using var bitmap = Capture(window);
        if (bitmap is not null) SaveBitmap(bitmap, path);
    }
}
