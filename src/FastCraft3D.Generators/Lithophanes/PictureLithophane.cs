using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Lithophanes;

/// <summary>
/// A Library entry that opens a tool of the app's own rather than a panel of numbers: what the
/// tool is called, for the app to run it by.
/// </summary>
public interface IOpensTool
{
    string Tool { get; }
}

/// <summary>
/// The lithophane tool, listed in the Library beside the moon lamp. A picture has to be chosen
/// from a file and seen lit as the numbers change, which a panel of numbers cannot do, so the
/// entry opens the tool itself; what it makes here is only the picture on its tile.
/// </summary>
public sealed class PictureLithophane : Generator<PictureLithophane.Settings>, IOpensTool
{
    public override string Id => "lithophane.picture";
    public override int Version => 1;
    public override string Category => "Lithophanes";
    public override string Title => "Picture lithophane";
    public override string Summary => "A photograph as a thin plate that shows when lit from behind: flat, curved, or a lamp of three to twelve sides.";

    public string Tool => "lithophane";

    public sealed record Settings();

    /// <summary>A plate of rings, for the tile: something with light and dark in it, made without a file.</summary>
    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        const int w = 60, h = 40;
        var samples = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - w / 2f, dy = y - h / 2f;
                samples[y * w + x] = 0.5f + 0.5f * MathF.Cos(MathF.Sqrt(dx * dx + dy * dy) / 2.5f);
            }

        var plate = Lithophane.Build(new Greyscale(w, h, samples), new LithophaneOptions { Width = 60, Pitch = 1f, Frame = 2f });
        return new Generated([new GeneratedPart("Lithophane", plate, Role: "lithophane")],
            ["Opens the lithophane tool: choose a picture, or several for a lamp."]);
    }
}
