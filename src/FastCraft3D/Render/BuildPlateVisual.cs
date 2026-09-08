using System.Windows.Media;
using FastCraft3D.Geometry;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using Numerics = System.Numerics;

namespace FastCraft3D.Render;

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

    public static IEnumerable<Element3D> Create(float plateSize = Model.Scene.PlateSize)
    {
        var light = new Mesh();
        var dark = new Mesh();

        int squares = (int)MathF.Ceiling(plateSize / SquareSize);
        float half = plateSize / 2f;

        for (int ix = 0; ix < squares; ix++)
        {
            for (int iy = 0; iy < squares; iy++)
            {
                float x0 = -half + ix * SquareSize;
                float y0 = -half + iy * SquareSize;
                float x1 = MathF.Min(x0 + SquareSize, half);
                float y1 = MathF.Min(y0 + SquareSize, half);

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
        yield return Outline(plateSize);
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
                AmbientColor = new SharpDX.Color4(0.55f, 0.55f, 0.58f, 1f)
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

    /// <summary>A border so the printable area is unmistakable even when the plate is empty.</summary>
    private static LineGeometryModel3D Outline(float plateSize)
    {
        float half = plateSize / 2f;
        var builder = new LineBuilder();
        var corners = new[]
        {
            new SharpDX.Vector3(-half, -half, 0), new SharpDX.Vector3(half, -half, 0),
            new SharpDX.Vector3(half, half, 0), new SharpDX.Vector3(-half, half, 0)
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
}
