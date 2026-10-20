using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Grow and Shrink move every face by the distance, rather than scaling about the middle.</summary>
public class MeshOffsetTests
{
    /// <summary>An H, 30 wide and 40 deep, its two notches 10 wide and 12 deep, 6 high, standing on the plate.</summary>
    private static Mesh H()
    {
        var left = MeshTransform.Transformed(Primitives.Box(10, 40, 6), Matrix4x4.CreateTranslation(-10, 0, 3));
        var right = MeshTransform.Transformed(Primitives.Box(10, 40, 6), Matrix4x4.CreateTranslation(10, 0, 3));
        var bar = MeshTransform.Transformed(Primitives.Box(12, 16, 6), Matrix4x4.CreateTranslation(0, 0, 3));
        return CsgSolid.Union(CsgSolid.Union(left, right), bar);
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(-0.2f)]
    public void EveryFaceOfABoxMovesByTheDistance(float distance)
    {
        var size = MeshOffset.Offset(Primitives.Box(10, 20, 30), distance).Mesh.ComputeBounds().Size;

        Assert.Equal(10 + 2 * distance, size.X, 4);
        Assert.Equal(20 + 2 * distance, size.Y, 4);
        Assert.Equal(30 + 2 * distance, size.Z, 4);
    }

    [Fact]
    public void AnHShrunkSidewaysKeepsItsHeightAndItsNotchesGetWider()
    {
        var h = H();

        var result = MeshOffset.Offset(h, -1f, alongX: true, alongY: true, alongZ: false);

        Assert.True(result.Changed, result.Refusal);
        var shrunk = result.Mesh;
        Assert.True(shrunk.CheckHealth().IsWatertight);

        var box = shrunk.ComputeBounds();
        Assert.Equal(28f, box.Size.X, 3);
        Assert.Equal(38f, box.Size.Y, 3);
        Assert.Equal(0f, box.Min.Z, 4);
        Assert.Equal(6f, box.Max.Z, 4);

        // Across the top notch at the middle: the legs' inner faces were 10 apart, now 12.
        float inner = float.MaxValue;
        foreach (var p in shrunk.Positions)
            if (p.Y > 10 && MathF.Abs(p.X) < 9) inner = MathF.Min(inner, MathF.Abs(p.X));
        Assert.Equal(6f, inner, 3);
    }

    [Fact]
    public void ShrinkingByMoreThanAWallIsThickIsRefused()
    {
        var result = MeshOffset.Offset(H(), -6f, alongZ: false);

        Assert.False(result.Changed);
        Assert.NotNull(result.Refusal);
    }
}
