using System.Numerics;

namespace FastCraft3D.Render;

/// <summary>
/// How a selected object is told from the rest by its colour alone, for the ones too dense to be
/// traced with an outline.
///
/// The colour deepens rather than being replaced: the palette already holds oranges and blues, so
/// a stand-in colour could be mistaken for an object painted that way. It is pushed away from its
/// own grey, which is more colour with the hue kept - and works on a pale colour, where adding the
/// same amount to every channel, as selection used to, did nothing: a light green is already near
/// the top of the range. A grey has no colour to deepen, so it leans toward the blue the app uses
/// for what is picked.
///
/// A few multiplications on picking it, nothing per frame.
/// </summary>
public static class SelectionColour
{
    /// <summary>The blue of the progress bar and the selection handles.</summary>
    private static readonly Vector3 Accent = new(0.243f, 0.482f, 0.839f);

    private static readonly Vector3 Luma = new(0.299f, 0.587f, 0.114f);

    public static Vector3 Intensified(Vector3 colour)
    {
        var grey = new Vector3(Vector3.Dot(colour, Luma));
        var deeper = grey + (colour - grey) * 1.8f;

        float chroma = MathF.Max(colour.X, MathF.Max(colour.Y, colour.Z))
                       - MathF.Min(colour.X, MathF.Min(colour.Y, colour.Z));
        float greyness = 1f - MathF.Min(1f, chroma * 4f);

        return Vector3.Clamp(Vector3.Lerp(deeper, Accent, 0.4f * greyness), Vector3.Zero, Vector3.One);
    }
}
