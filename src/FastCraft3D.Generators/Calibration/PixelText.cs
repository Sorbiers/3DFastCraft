using System.Globalization;
using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Calibration;

/// <summary>
/// Numbers for test prints, drawn in a blocky font three pixels wide and five high, built from
/// boxes to cut into a face.
///
/// A font's own outlines were the obvious choice and are wrong at this size: a digit two
/// millimetres tall in a typeface has strokes a fifth of a millimetre wide, which a 0.4 mm nozzle
/// cannot lay, and curves it can only guess at. Pixels as wide as a nozzle line print as what they
/// are. Notches alone were tried first on the brick fit test; on clear filament nobody could count
/// them.
/// </summary>
internal static class PixelText
{
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['1'] = [".#.", "##.", ".#.", ".#.", "###"],
        ['2'] = ["###", "..#", "###", "#..", "###"],
        ['3'] = ["###", "..#", "###", "..#", "###"],
        ['4'] = ["#.#", "#.#", "###", "..#", "..#"],
        ['5'] = ["###", "#..", "###", "..#", "###"],
        ['6'] = ["###", "#..", "###", "#.#", "###"],
        ['7'] = ["###", "..#", "..#", "..#", "..#"],
        ['8'] = ["###", "#.#", "###", "#.#", "###"],
        ['9'] = ["###", "#.#", "###", "..#", "###"],
        ['+'] = ["...", ".#.", "###", ".#.", "..."],
        ['-'] = ["...", "...", "###", "...", "..."],
        ['.'] = [".", ".", ".", ".", "#"],
        ['X'] = ["#.#", "#.#", ".#.", "#.#", "#.#"],
        ['Y'] = ["#.#", "#.#", ".#.", ".#.", ".#."],
        ['Z'] = ["###", "..#", ".#.", "#..", "###"],
        [' '] = ["..", "..", "..", "..", ".."]
    };

    /// <summary>How many pixels wide a line of it is, the gaps between letters included.</summary>
    public static int Columns(string text) => text.Sum(c => Glyphs[c][0].Length) + Math.Max(0, text.Length - 1);

    public static bool Draws(string text) => text.All(Glyphs.ContainsKey);

    /// <summary>
    /// A number as it is written on a test print: a sign where it matters, no trailing noughts.
    /// </summary>
    public static string Number(float value, bool signed = false) =>
        value.ToString(signed ? "+0.##;-0.##;0" : "0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// A box per pixel, reading along X and upright along Y, centred on the origin, from
    /// <paramref name="low"/> to <paramref name="high"/> in Z. Each is a hair bigger than its
    /// pixel, so neighbours overlap rather than touch and the lot cuts as one.
    /// </summary>
    public static List<Mesh> Boxes(string text, float pixel, float low, float high)
    {
        // The whole line as rows of pixels, filled or not, so a row can be laid in runs.
        var rows = new bool[5][];
        for (int row = 0; row < 5; row++)
        {
            var line = new List<bool>();
            foreach (char c in text)
            {
                if (line.Count > 0) line.Add(false);
                line.AddRange(Glyphs[c][row].Select(p => p == '#'));
            }

            rows[row] = [.. line];
        }

        // Each run of pixels along a row one box, the rows a hair taller than a pixel so each
        // overlaps the next, and alternate rows a whisker deeper. Pixel by pixel, neighbours in a
        // row overlapped with their floors in one plane, and two meeting at a corner - the middle
        // of an X - overlapped in a square; either way the boolean split the overlap along the
        // same line from both sides, and the cut came back with edges of four faces.
        float hair = MathF.Max(0.01f, pixel / 20f), step = MathF.Max(0.02f, pixel / 50f);
        float left = -Columns(text) * pixel / 2f, top = 2.5f * pixel;
        var boxes = new List<Mesh>();

        for (int row = 0; row < 5; row++)
        {
            float y1 = top - row * pixel, floor = low - (row % 2) * step;
            for (int col = 0; col < rows[row].Length; col++)
            {
                if (!rows[row][col]) continue;
                int end = col;
                while (end + 1 < rows[row].Length && rows[row][end + 1]) end++;

                boxes.Add(Shapes.Box(left + col * pixel - hair, y1 - pixel - hair, floor, left + (end + 1) * pixel + hair, y1 + hair, high));
                col = end;
            }
        }

        return boxes;
    }

    /// <summary>
    /// The same, stood on an upright face that looks along -Y, reading along +X with its middle at
    /// <paramref name="middle"/>, cut <paramref name="depth"/> into the face.
    /// </summary>
    public static List<Mesh> OnFront(string text, float pixel, Vector3 middle, float depth) =>
        OnFace(text, pixel, middle, Vector3.UnitX, Vector3.UnitZ, depth);

    /// <summary>
    /// On any face: reading along <paramref name="across"/>, upright along <paramref name="up"/>,
    /// cut <paramref name="depth"/> in from the face whose middle is <paramref name="middle"/>.
    /// The face looks out along across × up.
    /// </summary>
    public static List<Mesh> OnFace(string text, float pixel, Vector3 middle, Vector3 across, Vector3 up, float depth)
    {
        var outward = Vector3.Cross(across, up);
        var stand = new Matrix4x4(
            across.X, across.Y, across.Z, 0,
            up.X, up.Y, up.Z, 0,
            outward.X, outward.Y, outward.Z, 0,
            middle.X, middle.Y, middle.Z, 1);
        return Boxes(text, pixel, -depth, 1f).Select(b => MeshTransform.Transformed(b, stand)).ToList();
    }

    /// <summary>The largest pixel, up to <paramref name="most"/>, that fits the text in a space so wide and so tall.</summary>
    public static float Fit(string text, float wide, float tall, float most) =>
        MathF.Min(most, MathF.Min(wide / Columns(text), tall / 5f));
}
