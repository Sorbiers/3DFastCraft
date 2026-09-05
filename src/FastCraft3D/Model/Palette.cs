using System.Globalization;
using System.Numerics;

namespace FastCraft3D.Model;

/// <param name="Name">Shown as the swatch tooltip.</param>
/// <param name="Colour">Diffuse colour, components in 0..1.</param>
public readonly record struct Swatch(string Name, Vector3 Colour);

/// <summary>
/// The colours objects can be painted, and the conversions between the 0..1 vector the scene
/// stores and the forms people actually type.
///
/// Deliberately free of WPF and SharpDX types: colour belongs to the model, and both the
/// renderer and the OBJ exporter read it from there.
/// </summary>
public static class Palette
{
    /// <summary>
    /// Handed out in turn to newly inserted objects, so a fresh scene is legible without anyone
    /// having to paint anything. These six are the first row and a half of <see cref="Swatches"/>,
    /// so the automatic colours are all reachable by hand afterwards.
    /// </summary>
    public static readonly Vector3[] Cycle =
    [
        new(0.30f, 0.55f, 0.85f), new(0.85f, 0.45f, 0.30f), new(0.40f, 0.72f, 0.45f),
        new(0.75f, 0.40f, 0.70f), new(0.90f, 0.72f, 0.25f), new(0.35f, 0.70f, 0.75f)
    ];

    /// <summary>
    /// The picker grid: three rows of eight, warm hues then cool then neutrals, so a colour is
    /// found by looking rather than by reading.
    /// </summary>
    public static readonly IReadOnlyList<Swatch> Swatches =
    [
        new("Crimson", FromHex("#D6455C")), new("Terracotta", Cycle[1]),
        new("Orange", FromHex("#E8813A")), new("Amber", Cycle[4]),
        new("Gold", FromHex("#EFD34D")), new("Lime", FromHex("#7ACB4F")),
        new("Green", Cycle[2]), new("Mint", FromHex("#4FC79A")),

        new("Teal", Cycle[5]), new("Cyan", FromHex("#45BFD6")),
        new("Sky", FromHex("#57A9F0")), new("Blue", Cycle[0]),
        new("Indigo", FromHex("#5C6BD6")), new("Violet", FromHex("#8E63D6")),
        new("Orchid", Cycle[3]), new("Pink", FromHex("#E87BA8")),

        new("White", FromHex("#F2F3F5")), new("Silver", FromHex("#C9CDD2")),
        new("Grey", FromHex("#9AA0A8")), new("Slate", FromHex("#5F6A75")),
        new("Charcoal", FromHex("#3A3F45")), new("Black", FromHex("#23262A")),
        new("Sand", FromHex("#D9C39A")), new("Brown", FromHex("#8C6242"))
    ];

    public static Vector3 Default => Cycle[0];

    public static (byte R, byte G, byte B) ToBytes(Vector3 colour) => (
        ToByte(colour.X), ToByte(colour.Y), ToByte(colour.Z));

    public static Vector3 FromBytes(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f);

    public static string ToHex(Vector3 colour)
    {
        var (r, g, b) = ToBytes(colour);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>For the fixed swatches above, where a bad literal is a bug rather than input.</summary>
    public static Vector3 FromHex(string text) => TryFromHex(text, out var colour)
        ? colour
        : throw new ArgumentException($"'{text}' is not a colour", nameof(text));

    /// <summary>Accepts <c>#RRGGBB</c>, <c>RRGGBB</c> and the three-digit shorthand.</summary>
    public static bool TryFromHex(string? text, out Vector3 colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string digits = text.Trim().TrimStart('#');
        if (digits.Length == 3)
        {
            // #ABC means #AABBCC - each digit doubled, not zero-padded.
            digits = string.Concat(digits.Select(c => new string(c, 2)));
        }

        if (digits.Length != 6) return false;
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
            return false;

        colour = FromBytes((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    /// <summary>
    /// Hue in degrees 0..360, saturation and value in 0..1.
    ///
    /// Double throughout, as in the CSG core and for the same reason: the picker converts to HSV
    /// and back on every mouse move, and in single precision that drift is enough to walk a
    /// component across a rounding boundary - 0.7 became either B2 or B3 depending on the route.
    /// </summary>
    public static (double H, double S, double V) ToHsv(Vector3 colour)
    {
        double r = Clamp01(colour.X), g = Clamp01(colour.Y), b = Clamp01(colour.Z);
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double span = max - min;

        double hue;
        if (span <= 0) hue = 0; // grey has no hue; the picker keeps its strip where it was
        else if (max == r) hue = 60 * (((g - b) / span + 6) % 6);
        else if (max == g) hue = 60 * ((b - r) / span + 2);
        else hue = 60 * ((r - g) / span + 4);

        return (hue, max <= 0 ? 0 : span / max, max);
    }

    public static Vector3 FromHsv(double hue, double saturation, double value)
    {
        double h = ((hue % 360) + 360) % 360 / 60;
        double s = Clamp01(saturation), v = Clamp01(value);

        double chroma = v * s;
        double second = chroma * (1 - Math.Abs(h % 2 - 1));
        double floor = v - chroma;

        (double r, double g, double b) = (int)h switch
        {
            0 => (chroma, second, 0d),
            1 => (second, chroma, 0d),
            2 => (0d, chroma, second),
            3 => (0d, second, chroma),
            4 => (second, 0d, chroma),
            _ => (chroma, 0d, second)
        };

        return new Vector3((float)(r + floor), (float)(g + floor), (float)(b + floor));
    }

    /// <summary>Black or white, whichever stays readable on the given colour.</summary>
    public static bool PrefersDarkText(Vector3 colour) =>
        0.299f * colour.X + 0.587f * colour.Y + 0.114f * colour.Z > 0.55f;

    private static byte ToByte(float component) =>
        (byte)Math.Round(Clamp01(component) * 255, MidpointRounding.AwayFromZero);

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
}
