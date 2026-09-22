using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Text;

/// <summary>How the lettering is laid out.</summary>
public enum TextLayout
{
    /// <summary>In a line, lying on the plate and read from above. How lettering prints best.</summary>
    Flat,

    /// <summary>In a line, standing up and facing the front.</summary>
    Upright,

    /// <summary>Round a circle, lying on the plate: a clock face, a coaster, a ring of words.</summary>
    Circle,

    /// <summary>Round a cylinder, standing up: a cup, a napkin ring, a barrel label.</summary>
    Cylinder
}

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

    /// <summary>In a line, or bent round something.</summary>
    public TextLayout Layout { get; init; } = TextLayout.Flat;

    /// <summary>
    /// The radius of the circle or cylinder the lettering is bent round. Measured to the baseline,
    /// so the letters stand outside it: a 30 mm radius puts the feet of the letters on a 60 mm
    /// circle and their tops beyond it.
    /// </summary>
    public float Radius { get; init; } = 30f;

    /// <summary>
    /// Round a circle, which side of it the lettering stands on: outward from the middle, running
    /// round the top, or inward, running round the bottom. The bottom of a badge wants the second,
    /// and both read the right way up from above.
    /// </summary>
    public bool Inward { get; init; }

    /// <summary>Whether the layout is bent round something, and so needs a radius.</summary>
    public bool IsRound => Layout is TextLayout.Circle or TextLayout.Cylinder;

    public TextOptions Sane() => this with
    {
        Text = Text ?? "",
        Height = Clamp(Height, 0.5f, 1000f, 10f),
        Depth = Clamp(Depth, 0.1f, 1000f, 3f),
        Spacing = Clamp(Spacing, -50f, 200f, 0f),
        Radius = Clamp(Radius, 1f, 2000f, 30f)
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

        var shapes = glyphs.Select(g => new TextShape(g.Outline, g.Holes)).ToList();

        switch (o.Layout)
        {
            case TextLayout.Upright:
            {
                // A quarter turn about X takes the letters' up to +Z and their thickness towards
                // the front, so the face read is the one looking at -Y, where the front view
                // looks from.
                var solid = TextSolid.Extrude(shapes, 0f, o.Depth);
                return MeshTransform.Transformed(solid, Matrix4x4.CreateRotationX(MathF.PI / 2f));
            }

            case TextLayout.Circle:
                // Bent in the layout rather than after it is a solid: the letters are flat shapes
                // until they are extruded, and bending a shape is a dozen lines where bending a
                // solid is a deformation that has to keep it closed.
                return TextSolid.Extrude(
                    shapes.Select(s => RoundACircle(s, o.Radius, o.Inward)).ToList(), 0f, o.Depth);

            case TextLayout.Cylinder:
            {
                // The same wrap Emboss uses to put lettering on a barrel, with nothing underneath
                // it: across is arc length, so a letter keeps its width whatever the radius.
                var wrapped = TextSolid.Build(shapes, new CylinderSurface(Vector3.Zero, o.Radius), 0f, o.Depth);
                return Standing(wrapped);
            }

            default:
                return TextSolid.Extrude(shapes, 0f, o.Depth);
        }
    }

    /// <summary>Set down on the plate, whatever height it was built at.</summary>
    private static Mesh Standing(Mesh mesh)
    {
        var box = mesh.ComputeBounds();
        return box.IsEmpty || MathF.Abs(box.Min.Z) < 1e-6f
            ? mesh
            : MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(0f, 0f, -box.Min.Z));
    }

    /// <summary>
    /// One glyph bent round a circle: what was along the line becomes the angle, and what was up
    /// becomes the distance out. The middle of the lettering sits at the top of the circle and it
    /// runs anticlockwise from there, which is how a ring of words reads.
    ///
    /// Every segment is cut up first. A glyph's stem is one straight line from foot to head, and
    /// bending its two ends round the circle without touching what lies between would leave it a
    /// chord across the arc - straight where the letter beside it is curved.
    /// </summary>
    private static TextShape RoundACircle(TextShape shape, float radius, bool inward) => new(
        Bent(shape.Outline, radius, inward),
        shape.Holes.Select(h => (IReadOnlyList<Vector2>)Bent(h, radius, inward)).ToList());

    private static List<Vector2> Bent(IReadOnlyList<Vector2> loop, float radius, bool inward)
    {
        var bent = new List<Vector2>(loop.Count * 2);

        Vector2 Round(Vector2 p)
        {
            // Clockwise as the lettering runs, which is what makes it read left to right seen
            // from above. Anticlockwise turns the map into a mirror - the determinant comes out
            // negative - and every letter came back reversed, which is what it looked like.
            //
            // Inward it runs the other way along the bottom of the circle and its tops point at
            // the middle, and that map is not a mirror either: two sign changes, not one.
            float angle = inward
                ? -MathF.PI / 2f + p.X / radius
                : MathF.PI / 2f - p.X / radius;

            // Held off the middle, so lettering taller than the radius folds through itself
            // rather than turning inside out. The panel says when that is about to happen.
            float out_ = MathF.Max(inward ? radius - p.Y : radius + p.Y, 0.05f);

            return new Vector2(out_ * MathF.Cos(angle), out_ * MathF.Sin(angle));
        }

        for (int i = 0; i < loop.Count; i++)
        {
            var from = loop[i];
            var to = loop[(i + 1) % loop.Count];

            // Fine enough that the arc reads as a curve, and counted on the arc rather than on the
            // segment so a big radius does not pay for detail nobody can see.
            float along = MathF.Abs(to.X - from.X);
            int pieces = Math.Clamp((int)MathF.Ceiling(along / 0.35f), 1, 64);

            for (int k = 0; k < pieces; k++)
                bent.Add(Round(Vector2.Lerp(from, to, (float)k / pieces)));
        }

        return bent;
    }

    /// <summary>What the object is called: the lettering itself, shortened if long.</summary>
    public static string NameFor(TextOptions options)
    {
        string text = options.Text.Trim();
        return text.Length <= 16 ? $"Text {text}" : $"Text {text[..15]}…";
    }
}
