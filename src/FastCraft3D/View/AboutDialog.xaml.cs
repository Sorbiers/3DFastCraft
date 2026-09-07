using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace FastCraft3D.View;

/// <summary>What this is, who owns it, and where to find it.</summary>
public partial class AboutDialog : Window
{
    private const string Home = "https://github.com/Sorbiers/3DFastCraft";

    public AboutDialog()
    {
        InitializeComponent();

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionLine.Text = version is null
            ? "Version unknown"
            : $"Version {version.Major}.{version.Minor}.{version.Build}"
              + $"   -   .NET {Environment.Version}"
              + $"   -   {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}";

        CopyrightLine.Text = $"Copyright (c) {BuiltIn()} Andrey Ryabinin. All rights reserved.";
    }

    /// <summary>
    /// The year the build was made rather than the year it is run in, so a copy left on a
    /// machine over new year does not quietly start claiming a date it was not written in.
    /// </summary>
    private static int BuiltIn()
    {
        var built = new FileInfo(Environment.ProcessPath ?? string.Empty);
        return built.Exists ? built.LastWriteTime.Year : DateTime.Now.Year;
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        // UseShellExecute, or .NET tries to run the URL as a program and throws.
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    /// <summary>The details a bug report wants, so nobody has to transcribe them by hand.</summary>
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        string details = $"3DFastCraft - {VersionLine.Text}{Environment.NewLine}"
                       + $"{CopyrightLine.Text}{Environment.NewLine}"
                       + $"{Home}{Environment.NewLine}"
                       + $"Windows {Environment.OSVersion.Version}";

        // Another process can hold the clipboard open, and Windows answers that by throwing.
        // Failing to copy an about box is not worth an unhandled exception.
        try
        {
            Clipboard.SetText(details);
            CopyButton.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopyButton.Content = "Clipboard busy";
        }
    }
}
