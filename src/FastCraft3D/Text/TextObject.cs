using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Text;

/// <summary>
/// Lettering to make as an object of its own. A record class, so a new one comes with these
/// defaults rather than a font with no name and letters no size.
/// </summary>
public sealed record TextOptions
{
    public string Text { get; init; } = "Text";
    public string Font { get; init; } = "Segoe UI";

    /// <summary>How tall the capitals stand, in millimetres.</summary>
    public float Height { get; init; } = 10f;

    /// <summary>How thick the letters are.</summary>
    public float Depth { get; init; } = 3f;

    public bool Bold { get; init; }
    public bool Italic { get; init; }

    /// <summary>Added after every letter; negative draws them closer.</summary>
    public float Spacing { get; init; }

    /// <summary>Standing up and facing the front, rather than lying on the plate reading from above.</summary>
    public bool Upright { get; init; }

    public TextOptions Sane() => this with
    {
        Text = Text ?? "",
        Height = Clamp(Height, 0.5f, 1000f, 10f),
        Depth = Clamp(Depth, 0.1f, 1000f, 3f),
        Spacing = Clamp(Spacing, -50f, 200f, 0f)
    };

    private static float Clamp(float value, float low, float high, float otherwise) =>
        float.IsFinite(value) ? Math.Clamp(value, low, high) : otherwise;
}

/// <summary>
/// Lettering as a solid of its own - a sign, a name tag, a keychain - where Emboss puts it on a face
/// that is already there. The same outlines and the same extrusion, with nothing to cut into, so
/// no boolean runs and it previews as it is typed.
/// </summary>
public static class TextObject
{
    /// <summary>
    /// The lettering centred on the origin, standing on Z = 0: lying flat and reading from above,
    /// or turned up to face the front. Empty when there is nothing to draw.
    /// </summary>
    public static Mesh Build(TextOptions options)
    {
        var o = options.Sane();
        var glyphs = GlyphOutlines.Build(o.Text, o.Font, o.Height, o.Bold, o.Italic, o.Spacing);
        if (glyphs.Count == 0) return new Mesh();

        var solid = TextSolid.Extrude(glyphs.Select(g => new TextShape(g.Outline, g.Holes)).ToList(), 0f, o.Depth);

        // A quarter turn about X takes the letters' up to +Z and their thickness towards the front,
        // so the face read is the one looking at -Y, where the front view looks from.
        return o.Upright
            ? MeshTransform.Transformed(solid, Matrix4x4.CreateRotationX(MathF.PI / 2f))
            : solid;
    }

    /// <summary>What the object is called: the lettering itself, shortened if long.</summary>
    public static string NameFor(TextOptions options)
    {
        string text = options.Text.Trim();
        return text.Length <= 16 ? $"Text {text}" : $"Text {text[..15]}…";
    }
}
