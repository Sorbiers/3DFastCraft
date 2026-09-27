using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

public class LithophaneLampTests
{
    private static Greyscale Gradient(int width, int height) =>
        new(width, height, Enumerable.Range(0, width * height).Select(i => (i % width) / (float)(width - 1)).ToArray());

    [Fact]
    public void AFourSidedLampIsOneClosedSquareBoxWhateverSizeItsPicturesAre()
    {
        var options = new LithophaneOptions { Shape = LithophaneShape.Lamp, Width = 60, Pitch = 1f };
        var lamp = Lithophane.BuildLamp([Gradient(40, 50), Gradient(30, 60), Gradient(40, 50), Gradient(10, 10)], options);

        Assert.True(lamp.CheckHealth().IsWatertight, lamp.CheckHealth().Describe());
        var size = lamp.ComputeBounds().Size;
        Assert.Equal(size.X, size.Y, 2);
        Assert.Equal(0f, lamp.ComputeBounds().Min.Z, 3);
    }

    /// <summary>
    /// Every side count, the socket on for odd ones, at three widths in turn. Every combination of
    /// the three was swept once and all came out closed; this is enough to keep them so without a
    /// minute's wait every run.
    /// </summary>
    public static IEnumerable<object[]> Lamps() =>
        Enumerable.Range(3, 10).Select(sides => new object[] { sides, sides % 2 == 1, new[] { 30f, 50f, 83f }[sides % 3] })
            .Append([4, true, 83f])
            .Append([3, false, 30f]);

    [Theory]
    [MemberData(nameof(Lamps))]
    public void ALampOfAnySidesIsOneClosedSolidStandingOnThePlate(int sides, bool socket, float width)
    {
        var options = new LithophaneOptions { Shape = LithophaneShape.Lamp, Width = width, Pitch = 1f, LampSides = sides, Socket = socket };
        var lamp = Lithophane.BuildLamp([Gradient(40, 50), Gradient(30, 60)], options);

        Assert.True(lamp.CheckHealth().IsWatertight, lamp.CheckHealth().Describe());
        var bounds = lamp.ComputeBounds();
        Assert.Equal(0f, bounds.Min.Z, 3);

        // The pictures stand on the socket's base, as tall as it is over the plain frame.
        var plain = Lithophane.BuildLamp([Gradient(40, 50)], options with { Socket = false }).ComputeBounds();
        Assert.Equal(plain.Size.Z + (socket ? options.SocketBase : 0f), bounds.Size.Z, 1);
    }
}
