using System.Globalization;
using System.Windows.Media;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Text;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using Numerics = System.Numerics;

namespace FastCraft3D.Render;

/// <param name="Width">The printable area across X, in millimetres.</param>
/// <param name="Depth">Across Y.</param>
/// <param name="Height">How tall a part the printer takes: what the Z axis is drawn up to.</param>
/// <param name="Axes">The X and Y lines across the plate.</param>
/// <param name="ZAxis">The upright line.</param>
/// <param name="Labels">Distances along the positive X and Y axes.</param>
/// <param name="UnitLabel">What the labels are written in - "mm", "in".</param>
/// <param name="UnitMillimetres">How many millimetres one of those is.</param>
public readonly record struct PlateLook(
    float Width, float Depth, float Height, bool Axes, bool ZAxis, bool Labels,
    string UnitLabel, float UnitMillimetres);

/// <summary>
/// The checkerboard build plate.
///
/// Built as two meshes of alternating squares rather than a textured quad: no texture asset to
/// ship or sample, and it stays crisp at any zoom. The squares are 10 mm, so the board doubles
/// as a ruler while modelling.
/// </summary>
public static class BuildPlateVisual
{
    public const float SquareSize = 10f;

    private static readonly Color XColour = Color.FromRgb(0xE8, 0x54, 0x62);
    private static readonly Color YColour = Color.FromRgb(0x35, 0xC7, 0x5A);
    private static readonly Color ZColour = Color.FromRgb(0x3B, 0x9C, 0xF0);

    public static IEnumerable<Element3D> Create(PlateLook look)
    {
        var light = new Mesh();
        var dark = new Mesh();

        float halfX = look.Width / 2f;
        float halfY = look.Depth / 2f;
        int across = (int)MathF.Ceiling(look.Width / SquareSize);
        int along = (int)MathF.Ceiling(look.Depth / SquareSize);

        for (int ix = 0; ix < across; ix++)
        {
            for (int iy = 0; iy < along; iy++)
            {
                float x0 = -halfX + ix * SquareSize;
                float y0 = -halfY + iy * SquareSize;
                float x1 = MathF.Min(x0 + SquareSize, halfX);
                float y1 = MathF.Min(y0 + SquareSize, halfY);

                var target = (ix + iy) % 2 == 0 ? light : dark;
                // Wound counter-clockwise seen from above so the plate is lit from the top.
                target.AddTriangle(
                    new Numerics.Vector3(x0, y0, 0),
                    new Numerics.Vector3(x1, y0, 0),
                    new Numerics.Vector3(x1, y1, 0));
                target.AddTriangle(
                    new Numerics.Vector3(x0, y0, 0),
                    new Numerics.Vector3(x1, y1, 0),
                    new Numerics.Vector3(x0, y1, 0));
            }
        }

        yield return Tile(light, Color.FromRgb(0xF2, 0xF3, 0xF5));
        yield return Tile(dark, Color.FromRgb(0xDD, 0xDF, 0xE3));
        yield return Outline(halfX, halfY);

        if (look.Axes)
        {
            // Each axis in two halves, the negative one paler: which way is plus is then plain
            // from any side, where one colour end to end said only where the line was.
            yield return Line(new(-halfX, 0, 0), new(0, 0, 0), Paler(XColour), 1.4);
            yield return Line(new(0, 0, 0), new(halfX, 0, 0), XColour, 1.8);
            yield return Line(new(0, -halfY, 0), new(0, 0, 0), Paler(YColour), 1.4);
            yield return Line(new(0, 0, 0), new(0, halfY, 0), YColour, 1.8);
        }

        if (look.ZAxis)
            yield return Line(new(0, 0, 0), new(0, 0, look.Height), ZColour, 1.8);

        if (look.Labels && Labels(look) is { TriangleCount: > 0 } writing)
            yield return Writing(writing);
    }

    /// <summary>
    /// How solid the board is. Enough to read as a surface from above, little enough to see a
    /// part through it from below - which is where you look to check what a cut left behind, and
    /// an opaque board simply hid it.
    /// </summary>
    private const float Solidity = 0.55f;

    private static MeshGeometryModel3D Tile(Mesh mesh, Color colour)
    {
        var shade = colour.ToColor4();
        shade.Alpha = Solidity;

        return new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(mesh),
            Material = new PhongMaterial
            {
                DiffuseColor = shade,
                SpecularColor = new SharpDX.Color4(0, 0, 0, 1),
                AmbientColor = new SharpDX.Color4(0.55f, 0.55f, 0.58f, 1f),

                // So a part standing on the board shows in shadow when Shadows is on. Off costs
                // nothing extra - the viewport's own IsShadowMappingEnabled is what runs the pass.
                RenderShadowMap = true
            },
            // The flag as well as the alpha: it is what puts the board through the ordered
            // transparency pass instead of straight into the depth buffer.
            IsTransparent = true,

            // Held a hair further from the camera than everything else, so a part standing on
            // the board does not fight it for pixels. Their surfaces are in the same place by
            // definition - that is what standing on the plate means - and seen from underneath
            // the two tore into stripes. Moving the board down instead would have to be by more
            // than the depth buffer can tell apart, which is a hundredth of a millimetre up
            // close and most of a millimetre across a metre-high model; a bias is measured in
            // what the buffer can tell apart, so one number covers every distance.
            DepthBias = 8,

            // The plate is one-sided geometry viewed from both above and below.
            CullMode = SharpDX.Direct3D11.CullMode.None,
            IsHitTestVisible = false
        };
    }

    /// <summary>
    /// One stretch of an axis.
    ///
    /// The checkerboard says how far, but not from where: every square looks like every other, so
    /// the one point every typed coordinate is measured from was the one point on the plate you
    /// could not see. Coloured as the axis indicator in the corner and the manipulator arrows are,
    /// since the whole point is that red is X and green is Y wherever you meet them.
    /// </summary>
    private static LineGeometryModel3D Line(SharpDX.Vector3 from, SharpDX.Vector3 to, Color colour, double thickness)
    {
        var builder = new LineBuilder();
        builder.AddLine(from, to);

        return new LineGeometryModel3D
        {
            Geometry = builder.ToLineGeometry3D(),
            Color = colour,
            Thickness = thickness,

            // No bias of its own, while the board carries eight: the line and the board are in
            // the same plane by definition, and without that difference they would fight for
            // every pixel along their whole length.
            IsHitTestVisible = false
        };
    }

    /// <summary>Halfway to the plate's own grey.</summary>
    private static Color Paler(Color colour) => Color.FromRgb(
        (byte)((colour.R + 0xE6) / 2), (byte)((colour.G + 0xE8) / 2), (byte)((colour.B + 0xEC) / 2));

    /// <summary>A border so the printable area is unmistakable even when the plate is empty.</summary>
    private static LineGeometryModel3D Outline(float halfX, float halfY)
    {
        var builder = new LineBuilder();
        var corners = new[]
        {
            new SharpDX.Vector3(-halfX, -halfY, 0), new SharpDX.Vector3(halfX, -halfY, 0),
            new SharpDX.Vector3(halfX, halfY, 0), new SharpDX.Vector3(-halfX, halfY, 0)
        };
        for (int i = 0; i < 4; i++)
            builder.AddLine(corners[i], corners[(i + 1) % 4]);

        return new LineGeometryModel3D
        {
            Geometry = builder.ToLineGeometry3D(),
            Color = Color.FromRgb(0x88, 0x8C, 0x92),
            Thickness = 1.2,
            IsHitTestVisible = false
        };
    }

    /// <summary>
    /// The step between labels, in the unit they are written in: a round 1, 2 or 5 times a power
    /// of ten, giving about four along half the plate - 50 mm on a 200 mm bed, 1 in on the same bed
    /// in inches.
    /// </summary>
    public static float LabelStep(float halfSpanMm, float unitMillimetres)
    {
        float span = halfSpanMm / MathF.Max(unitMillimetres, 1e-6f);
        float rough = span / 4f;
        if (!(rough > 0f)) return 1f;

        float power = MathF.Pow(10f, MathF.Floor(MathF.Log10(rough)));
        foreach (float nice in new[] { 1f, 2f, 5f, 10f })
            if (nice * power >= rough) return nice * power;

        return 10f * power;
    }

    /// <summary>
    /// The distances along the positive X and Y axes, as flat lettering lying on the board: in
    /// front of the X axis and to the left of the Y axis, so they never sit on a line.
    /// </summary>
    private static Mesh Labels(PlateLook look)
    {
        var pieces = new List<Mesh>();
        float halfX = look.Width / 2f, halfY = look.Depth / 2f;
        float height = MathF.Max(MathF.Min(look.Width, look.Depth) / 40f, 2f);
        float gap = height * 0.8f;

        float step = LabelStep(MathF.Max(halfX, halfY), look.UnitMillimetres);
        float stepMm = step * look.UnitMillimetres;

        for (int i = 0; i * stepMm <= halfX + 1e-3f; i++)
            Add(Text(i * step), i * stepMm, -gap - height / 2f, centred: true);

        for (int i = 1; i * stepMm <= halfY + 1e-3f; i++)
            Add(Text(i * step), -gap, i * stepMm, centred: false);

        return Mesh.Combine(pieces);

        string Text(float value) =>
            value.ToString("0.###", CultureInfo.CurrentCulture) + " " + look.UnitLabel;

        void Add(string text, float x, float y, bool centred)
        {
            var glyphs = GlyphOutlines.Build(text, "Segoe UI", height, bold: false);
            if (glyphs.Count == 0) return;

            var shapes = glyphs.Select(g => new TextShape(g.Outline, g.Holes)).ToList();
            var flat = TextSolid.Extrude(shapes, 0f, 0.04f);
            var reach = flat.ComputeBounds();

            // Centred under its tick on the X axis; ending just short of the Y axis beside its own.
            float shiftX = centred ? x - reach.Center.X : x - reach.Max.X;
            float shiftY = y - reach.Center.Y;
            pieces.Add(MeshTransform.Transformed(flat, Numerics.Matrix4x4.CreateTranslation(shiftX, shiftY, 0.02f)));
        }
    }

    private static MeshGeometryModel3D Writing(Mesh mesh) => new()
    {
        Geometry = MeshConverter.ToGeometry(mesh),
        Material = new PhongMaterial
        {
            DiffuseColor = new SharpDX.Color4(0.36f, 0.38f, 0.42f, 1f),
            AmbientColor = new SharpDX.Color4(0.30f, 0.31f, 0.34f, 1f),
            SpecularColor = new SharpDX.Color4(0, 0, 0, 1)
        },
        IsHitTestVisible = false
    };
}
