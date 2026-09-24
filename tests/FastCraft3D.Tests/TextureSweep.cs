using System.Numerics;
using FastCraft3D.Geometry.Engraving;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// Every texture, over the range of settings anybody would type, judged by one question: is there
/// a bald patch in it.
///
/// Written after three faults in a row that all looked like different bugs and were all the same
/// thing - pieces silently thrown away at an edge - and that all got through because each was
/// tested on the one case in front of me. The judge deliberately knows nothing about any
/// particular pattern: it rasterises whatever came back and looks for the largest square of face
/// that nothing covers. A groove is allowed to be bald. A cell and a half is not.
/// </summary>
public class TextureSweep(ITestOutputHelper log)
{
    private static readonly TextureKind[] Kinds =
    [
        TextureKind.Knurl, TextureKind.Ribs, TextureKind.Hex, TextureKind.Dots,
        TextureKind.Tread, TextureKind.Brick, TextureKind.Tiles
    ];

    [Fact]
    public void NoSettingLeavesABaldPatch()
    {
        float[] pitches = [1f, 2f, 4f, 8f, 20f, 40f];
        float[] grooves = [0.2f, 0.6f, 1.5f];
        float[] aspects = [0f, 3f, 12f];
        (float A, float U)[] faces = [(20f, 20f), (48f, 48f), (120f, 60f)];

        var complaints = new List<string>();
        int cases = 0;

        foreach (var kind in Kinds)
            foreach (float pitch in pitches)
                foreach (float groove in grooves)
                    foreach (float aspect in aspects)
                        foreach (bool across in new[] { false, true })
                            foreach (var (a, u) in faces)
                            {
                                var o = new TextureOptions(kind, pitch, groove, 45f, across, aspect).Sane();
                                var made = SurfaceTexture.Over(a, u, o, seamless: false);
                                cases++;

                                string why = Judge(made, a, u, o);
                                if (why.Length > 0)
                                    complaints.Add(
                                        $"{kind,-9} pitch={o.PitchMm,-5:0.##} groove={o.LineMm,-4:0.##} " +
                                        $"aspect={aspect,-4} across={across,-5} face={a}x{u}: {why}");
                            }

        foreach (var line in complaints.Take(25)) log.WriteLine(line);
        log.WriteLine($"=== {complaints.Count} of {cases} settings left a bald patch");

        Assert.Empty(complaints);
    }

    /// <summary>
    /// What is wrong with this field, or an empty string when nothing is.
    ///
    /// A cell is as big as the pattern's own repeat in either direction, and a hole wider than one
    /// and a half of those in both directions at once is a piece that went missing rather than a
    /// groove. The allowance is generous on purpose: this is here to catch a field falling apart,
    /// not to have an opinion about how a knurl should look.
    /// </summary>
    private static string Judge(List<TextShape> made, float acrossMm, float upMm, TextureOptions o)
    {
        // A cell bigger than the face is not a pattern, and nothing coming back is the right answer.
        float cell = MathF.Max(o.PitchMm, o.PitchMm / MathF.Max(o.Courses, 0.2f));
        if (cell >= MathF.Min(acrossMm, upMm)) return "";

        if (made.Count == 0) return "nothing came back";

        foreach (var shape in made)
            foreach (var p in shape.Outline)
                if (MathF.Abs(p.X) > acrossMm / 2f + 1e-3f || MathF.Abs(p.Y) > upMm / 2f + 1e-3f)
                    return "a piece hangs off the face";

        float step = MathF.Max(MathF.Min(cell, MathF.Min(acrossMm, upMm) / 40f), 0.05f);
        int wide = (int)MathF.Ceiling(acrossMm / step);
        int tall = (int)MathF.Ceiling(upMm / step);
        if (wide > 400 || tall > 400) return "";

        var covered = new bool[wide, tall];
        foreach (var shape in made) Paint(covered, shape, acrossMm, upMm, step);

        int bald = LargestBald(covered, wide, tall);
        float span = bald * step;

        return span > cell * 1.5f
            ? $"a {span:0.#} mm bald square against a {cell:0.#} mm cell ({made.Count} pieces)"
            : "";
    }

    /// <summary>Marks the cells a shape's bounding box covers. Convex pads, so the box will do.</summary>
    private static void Paint(bool[,] covered, TextShape shape, float acrossMm, float upMm, float step)
    {
        float lowX = shape.Outline.Min(p => p.X), highX = shape.Outline.Max(p => p.X);
        float lowY = shape.Outline.Min(p => p.Y), highY = shape.Outline.Max(p => p.Y);

        int fromX = Math.Max((int)MathF.Floor((lowX + acrossMm / 2f) / step), 0);
        int toX = Math.Min((int)MathF.Ceiling((highX + acrossMm / 2f) / step), covered.GetLength(0) - 1);
        int fromY = Math.Max((int)MathF.Floor((lowY + upMm / 2f) / step), 0);
        int toY = Math.Min((int)MathF.Ceiling((highY + upMm / 2f) / step), covered.GetLength(1) - 1);

        for (int x = fromX; x <= toX; x++)
            for (int y = fromY; y <= toY; y++)
                covered[x, y] = true;
    }

    /// <summary>The side of the largest square of face nothing covers, in grid cells.</summary>
    private static int LargestBald(bool[,] covered, int wide, int tall)
    {
        var run = new int[wide, tall];
        int best = 0;

        for (int x = 0; x < wide; x++)
            for (int y = 0; y < tall; y++)
            {
                if (covered[x, y]) { run[x, y] = 0; continue; }

                run[x, y] = x == 0 || y == 0
                    ? 1
                    : 1 + Math.Min(run[x - 1, y], Math.Min(run[x, y - 1], run[x - 1, y - 1]));

                best = Math.Max(best, run[x, y]);
            }

        return best;
    }
}
