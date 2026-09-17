using System.IO;
using System.Text.Json;

namespace FastCraft3D.Io;

/// <param name="PlateWidth">The printable area across X, in millimetres.</param>
/// <param name="PlateDepth">Across Y.</param>
/// <param name="PlateHeight">How tall a part the printer takes.</param>
/// <param name="Unit">What the boxes read in.</param>
/// <param name="ShowAxes">The X and Y lines across the plate.</param>
/// <param name="ShowZAxis">The upright line, as tall as the printable height.</param>
/// <param name="ShowGridLabels">Distances written along the positive X and Y axes.</param>
/// <param name="FoldProperties">
/// Whether the side panel's Properties section is folded away. Stored that way round because a
/// file from before it has none, and the reader fills a missing flag with false rather than with
/// the default written here - the section has to come back open.
/// </param>
/// <param name="ClassicMode">Only the tools 3D Builder had on the ribbon. Advanced, the default, when missing.</param>
public readonly record struct RememberedSettings(
    float PlateWidth, float PlateDepth, float PlateHeight, string Unit,
    bool ShowAxes = true, bool ShowZAxis = false, bool ShowGridLabels = false, bool FoldProperties = false,
    bool ClassicMode = false);

/// <summary>
/// The printable area, the unit and how the grid is drawn, as last used, remembered between sessions.
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
