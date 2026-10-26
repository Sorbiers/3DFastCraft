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
/// <param name="ShowShadows">Objects cast a shadow onto the plate and each other.</param>
/// <param name="ShowReflections">The plate reflects what stands on it.</param>
/// <param name="SingleSelection">
/// Stored as the opposite of sticky selection, for the same reason as FoldProperties: a file
/// from before this existed has no flag, and the reader fills a missing one with false, so the
/// default written here has to be the one that reads back as sticky - the 3D Builder behaviour -
/// rather than sticky itself.
/// </param>
/// <param name="SidePanelWidth">How wide the side panel was dragged; nought, as a file from before it reads, for the default.</param>
/// <param name="CustomButtons">The ribbon buttons on the Custom tab, in order, as the tab and the label name them; none, as a file from before it reads.</param>
/// <param name="Light">How the scene is lit; none, as a file from before it reads, for the standard light.</param>
/// <param name="LightPresets">The light presets saved by name; the built-in ones are not stored.</param>
/// <param name="LockFreeMove">Parts are moved by dragging them only while Move is chosen in the bar under the view.</param>
/// <param name="ProjectFileDefault">Whether a new project keeps a .3dfp: null asks each time.</param>
public readonly record struct RememberedSettings(
    float PlateWidth, float PlateDepth, float PlateHeight, string Unit,
    bool ShowAxes = true, bool ShowZAxis = false, bool ShowGridLabels = false, bool FoldProperties = false,
    bool ClassicMode = false, bool ShowShadows = false, bool ShowReflections = false,
    bool SingleSelection = false, float SidePanelWidth = 0f, string[]? CustomButtons = null,
    LightSettings? Light = null, LightPreset[]? LightPresets = null, bool LockFreeMove = false,
    bool? ProjectFileDefault = null);

/// <summary>A light kept under a name, to be picked from the Light section.</summary>
public sealed record LightPreset(string Name, LightSettings Light)
{
    /// <summary>Those that come with the app: always listed first, never saved over or deleted.</summary>
    public static IReadOnlyList<LightPreset> BuiltIn { get; } =
    [
        new("Standard", LightSettings.Standard),
        new("Soft", LightSettings.Soft),
        new("Contrast", LightSettings.Contrast)
    ];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBuiltIn => BuiltIn.Any(p => string.Equals(p.Name, Name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// How the scene is lit, each from nothing to full.
/// </summary>
/// <param name="Ambient">The light that comes from everywhere: how much of its colour a face turned from every lamp still shows.</param>
/// <param name="Key">The lamp over the viewer's shoulder, which follows the view.</param>
/// <param name="Fill">The faint lamp from below and behind, which keeps the undersides from going black.</param>
/// <param name="Shadow">How dark a shadow is, when Shadows is on.</param>
/// <param name="Shine">How much a face glints where the key light catches it.</param>
/// <param name="FixedInScene">
/// The key light stays put while the view turns, instead of following it over the viewer's
/// shoulder. Stored that way round so a file without it reads as following, as it always did.
/// </param>
/// <param name="Turn">
/// Where round the key light stands, in degrees: nought behind the viewer - or, fixed in the scene,
/// in front of the plate - positive to the right.
/// </param>
/// <param name="Height">How high the key light stands, in degrees: nought on the horizon, ninety overhead.</param>
public readonly record struct LightSettings(float Ambient, float Key, float Fill, float Shadow, float Shine,
    bool FixedInScene = false, float Turn = -16.7f, float Height = 23.3f)
{
    /// <summary>The light every version up to 4.7 had: over the viewer's left shoulder, a little above.</summary>
    public static LightSettings Standard => new(0.11f, 0.90f, 0.32f, 0.50f, 0.25f, false, -16.7f, 23.3f);

    /// <summary>Even and matte, close to 3D Builder: white walls stay white on every side.</summary>
    public static LightSettings Soft => new(0.56f, 0.48f, 0.29f, 0.28f, 0.08f, false, -16.7f, 23.3f);

    /// <summary>Hard light low from one side, for reading the relief of a surface - a texture, a seam, a dent.</summary>
    public static LightSettings Contrast => new(0.05f, 1.00f, 0.18f, 0.65f, 0.35f, false, -70f, 20f);
}

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
