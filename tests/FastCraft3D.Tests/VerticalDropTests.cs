using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

public class VerticalDropTests
{
    private static Mesh Box(float size, Vector3 at, float turnX = 0, float turnY = 0) =>
        MeshTransform.Transformed(Primitives.Box(size, size, size),
            Matrix4x4.CreateRotationX(turnX * MathF.PI / 180f) * Matrix4x4.CreateRotationY(turnY * MathF.PI / 180f) * Matrix4x4.CreateTranslation(at));

    [Fact]
    public void ABoxOverAnotherFallsTheGapBetweenThem()
    {
        var still = Box(20, new Vector3(0, 0, 10));
        var moving = Box(10, new Vector3(3, -2, 33));

        Assert.Equal(8f, VerticalDrop.Distance(moving, still)!.Value, 3);
    }

    [Fact]
    public void RidgesCrossedAtRightAnglesMeetWhereNeitherHasACorner()
    {
        // Each a box stood on an edge; the lower one's ridge runs along X, the upper one's along Y.
        float half = 10 * MathF.Sqrt(2) / 2;
        var still = Box(10, Vector3.Zero, turnX: 45);
        var moving = Box(10, new Vector3(0, 0, 20), turnY: 45);

        Assert.Equal(20 - 2 * half, VerticalDrop.Distance(moving, still)!.Value, 2);
    }

    [Fact]
    public void APartAlreadyALittleIntoTheOtherIsRestingOnItNotFallingThrough()
    {
        var still = Box(20, new Vector3(0, 0, 10));
        var moving = Box(10, new Vector3(0, 0, 24.8f));

        Assert.Equal(-0.2f, VerticalDrop.Distance(moving, still)!.Value, 3);
    }

    [Fact]
    public void NothingUnderneathIsNothingToLandOn()
    {
        var still = Box(10, new Vector3(0, 0, 5));

        Assert.Null(VerticalDrop.Distance(Box(10, new Vector3(30, 0, 25)), still));
        Assert.Null(VerticalDrop.Distance(Box(4, new Vector3(0, 0, 2)), Box(4, new Vector3(0, 0, 20))));
    }
}
