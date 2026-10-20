using System.IO;

namespace FastCraft3D.Io;

/// <summary>
/// Whether the start page comes up when the app starts with nothing to open. A file of its own
/// beside the recent list, so the choice holds whatever else is reset.
/// </summary>
public static class StartPage
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3DFastCraft", "startpage.txt");

    public static bool ShowAtStartup
    {
        get
        {
            try { return !File.Exists(StorePath) || File.ReadAllText(StorePath).Trim() != "off"; }
            catch { return true; }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, value ? "on" : "off");
            }
            catch
            {
                // A read-only profile only means the page comes up again next time.
            }
        }
    }
}
