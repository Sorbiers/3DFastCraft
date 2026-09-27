using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

public class LithophaneCubeTests
{
    private static Greyscale Gradient(int width, int height) =>
        new(width, height, Enumerable.Range(0, width * height).Select(i => (i % width) / (float)(width - 1)).ToArray());

    [Fact]
    public void ACubeLampIsOneClosedSquareBoxWhateverSizeItsPicturesAre()
    {
        var options = new LithophaneOptions { Shape = LithophaneShape.Cube, Width = 60, Pitch = 1f };
        var lamp = Lithophane.BuildCube([Gradient(40, 50), Gradient(30, 60), Gradient(40, 50), Gradient(10, 10)], options);

        Assert.True(lamp.CheckHealth().IsWatertight, lamp.CheckHealth().Describe());
        var size = lamp.ComputeBounds().Size;
        Assert.Equal(size.X, size.Y, 2);
        Assert.Equal(0f, lamp.ComputeBounds().Min.Z, 3);
    }
}
