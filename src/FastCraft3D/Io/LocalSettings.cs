using System.IO;
using System.Text.Json;

namespace FastCraft3D.Io;

/// <param name="PlateSize">The printer's bed, in millimetres.</param>
/// <param name="Unit">What the boxes read in.</param>
public readonly record struct RememberedSettings(float PlateSize, string Unit);

/// <summary>
/// The bed size and unit last used, remembered between sessions.
///
/// The project file keeps them too, but a new scene has no file: without this every start went
/// back to a 200 mm bed in millimetres, and anyone with a bigger printer or working in inches set
/// both again each time. The scale is left to the project - a 1:87 house says nothing about what
/// the next thing drawn will be. Kept beside the recent files list, for the same reason.
/// </summary>
public static class LocalSettings
{
    private static string DefaultStorePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "3DFastCraft", "settings.json");

    public static RememberedSettings? Load(string? storePath = null)
    {
        try
        {
            string path = storePath ?? DefaultStorePath();
            return File.Exists(path) ? JsonSerializer.Deserialize<RememberedSettings>(File.ReadAllText(path)) : null;
        }
        catch
        {
            // A damaged file is not worth a word; the defaults are a fine place to start.
            return null;
        }
    }

    public static void Save(RememberedSettings settings, string? storePath = null)
    {
        try
        {
            string path = storePath ?? DefaultStorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // Read-only or roaming profiles must not get in the way of the work itself.
        }
    }
}
