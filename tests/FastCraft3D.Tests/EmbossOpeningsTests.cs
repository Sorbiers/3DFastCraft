using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

public class EmbossOpeningsTests
{
    private static Mesh Box(float x0, float y0, float z0, float x1, float y1, float z1) =>
        MeshTransform.Transformed(Primitives.Box(x1 - x0, y1 - y0, z1 - z0),
            Matrix4x4.CreateTranslation((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2));

    /// <summary>A wall 60 wide and 40 tall, with a window through it and a doorway up from its foot.</summary>
    private static Mesh Wall()
    {
        var wall = Box(-30, -2, 0, 30, 2, 40);
        wall = ManifoldCsg.Subtract(wall, Box(-20, -5, 15, -8, 5, 28))!;
        return ManifoldCsg.Subtract(wall, Box(5, -5, -1, 17, 5, 25))!;
    }

    /// <summary>Bricks over the face's whole rectangle, as a texture is laid.</summary>
    private static List<TextShape> Bricks()
    {
        var shapes = new List<TextShape>();
        for (float v = -19f; v + 3f <= 19f; v += 4f)
            for (float u = -29f; u + 6f <= 29f; u += 7f)
                shapes.Add(new TextShape([new(u, v), new(u + 6, v), new(u + 6, v + 3), new(u, v + 3)], []));
        return shapes;
    }

    private static bool SeesThrough(Mesh mesh, float x, float z) =>
        HoleCutter.FarSide(mesh, new Vector3(x, -20, z), Vector3.UnitY) is null;

    [Fact]
    public void RaisedBricksLeaveAWindowAndADoorwayOpen()
    {
        var wall = Wall();
        var face = FacePatch.Find(wall, new Vector3(0, -2, 35), -Vector3.UnitY)!;
        var surface = new PlanarSurface(face);

        Assert.True(SeesThrough(wall, -14, 21.5f));
        Assert.True(SeesThrough(wall, 11, 12));

        var result = TextCutter.Apply(wall, Bricks(), surface, raised: true, depthMm: 1f)!;

        Assert.True(result.CheckHealth().IsWatertight);
        Assert.True(SeesThrough(result, -14, 21.5f), "a brick is standing in the window");
        Assert.True(SeesThrough(result, 11, 12), "a brick is standing in the doorway");
        Assert.False(SeesThrough(result, 0, 35));
        Assert.True(result.ComputeSignedVolume() > wall.ComputeSignedVolume());
    }

    [Fact]
    public void RaisedBricksKeptAsTheirOwnPartLeaveTheOpeningsOpenToo()
    {
        var wall = Wall();
        var face = FacePatch.Find(wall, new Vector3(0, -2, 35), -Vector3.UnitY)!;
        var made = TextCutter.Separate(wall, Bricks(), new PlanarSurface(face), raised: true, depthMm: 1f)!.Value;

        Assert.True(SeesThrough(made.Lettering, -14, 21.5f));
        Assert.True(SeesThrough(made.Lettering, 11, 12));
    }
}
