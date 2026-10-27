using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FastCraft3D.Io;

/// <summary>
/// Tells Windows about the .3dfp: that this app opens it, the icon it shows, and the picture
/// Explorer draws on it and in its preview pane.
///
/// Per user, under HKCU, so it needs no administrator and no installer: the app is one exe that
/// can live anywhere, and says where it is each time it starts. Written only when something
/// differs, which after the first run is only when the exe has moved.
///
/// The picture is Windows' own. A .3dfp is a 3MF package as well (see <see cref="ProjectFile"/>),
/// so the 3MF thumbnail and preview handler Windows ships draws it as it draws a .3mf. A handler
/// of the app's own would have had to be a separate DLL loaded into Explorer, which a single
/// self-contained exe cannot be. Where Windows' handler is missing, a .3dfp still opens with the
/// app; it shows the app's icon instead of a picture.
///
/// Only the .3dfp: the .3mf belongs to whatever the user opens it with, often a slicer.
/// </summary>
public static class ShellRegistration
{
    private const string ProgId = "3DFastCraft.ProjectFile";
    private const string ThumbnailHandler = "{e357fccd-a995-4576-b01f-234630154e96}";
    private const string PreviewHandler = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";

    /// <summary>"Microsoft 3MF Shell Thumbnail and Preview Handler", in system32.</summary>
    private const string Windows3mfHandler = "{E64164EB-1AE0-4C50-BAEF-A413C2B3A4BC}";

    public static void RegisterProjectFile()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return;

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            string extension = ProjectFile.Extension;

            bool changed = Set(classes, extension, null, ProgId);
            changed |= Set(classes, extension + @"\OpenWithProgids", ProgId, "");
            changed |= Set(classes, ProgId, null, "3DFastCraft project file");
            changed |= Set(classes, ProgId + @"\DefaultIcon", null, $"\"{exe}\",0");
            changed |= Set(classes, ProgId + @"\shell\open\command", null, $"\"{exe}\" \"%1\"");

            if (HasWindows3mfHandler())
            {
                changed |= Set(classes, $@"{extension}\ShellEx\{ThumbnailHandler}", null, Windows3mfHandler);
                changed |= Set(classes, $@"{extension}\ShellEx\{PreviewHandler}", null, Windows3mfHandler);
            }

            // Explorer keeps what it knows about a type; without this the old icon stays until the next sign-in.
            if (changed) SHChangeNotify(AssociationChanged, IdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // A locked-down profile: everything works as before, a .3dfp just opens from inside the app.
        }
    }

    private static bool Set(RegistryKey classes, string path, string? name, string value)
    {
        using var key = classes.CreateSubKey(path);
        if (key.GetValue(name) as string == value) return false;

        key.SetValue(name, value);
        return true;
    }

    private static bool HasWindows3mfHandler()
    {
        using var server = Registry.ClassesRoot.OpenSubKey($@"CLSID\{Windows3mfHandler}\InprocServer32");
        return server?.GetValue(null) is string dll && File.Exists(Environment.ExpandEnvironmentVariables(dll));
    }

    private const int AssociationChanged = 0x08000000;
    private const uint IdList = 0;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
