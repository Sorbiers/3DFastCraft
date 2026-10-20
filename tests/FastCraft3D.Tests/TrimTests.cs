using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Trim cuts a part back round its outer edge, leaving everything inside it as it was.</summary>
public class TrimTests
{
    /// <summary>A 100 x 80 floor 2 thick, with a 2 mm partition across it end to end, 12 high.</summary>
    private static Mesh Insert()
    {
        var floor = MeshTransform.Transformed(Primitives.Box(100, 80, 2), Matrix4x4.CreateTranslation(0, 0, 1));
        var partition = MeshTransform.Transformed(Primitives.Box(2, 80, 12), Matrix4x4.CreateTranslation(10, 0, 6));
        return CsgSolid.Union(floor, partition);
    }

    [Theory]
    [InlineData(3f, true, true, false, 94f, 74f, 12f)]
    [InlineData(3f, true, false, false, 94f, 80f, 12f)]
    [InlineData(0.5f, false, false, true, 100f, 80f, 11f)]
    public void TheOuterEdgeIsCutBackAlongTheAxesChosen(float by, bool x, bool y, bool z, float width, float depth, float height)
    {
        var result = EdgeTrim.Trim(Insert(), by, x, y, z);

        Assert.NotNull(result.Mesh);
        Assert.True(result.Mesh!.CheckHealth().IsWatertight);

        var size = result.Mesh.ComputeBounds().Size;
        Assert.Equal(width, size.X, 3);
        Assert.Equal(depth, size.Y, 3);
        Assert.Equal(height, size.Z, 3);
    }

    [Fact]
    public void APartitionInsideKeepsItsThickness()
    {
        var trimmed = EdgeTrim.Trim(Insert(), 3f, true, true, false).Mesh!;

        float near = float.MaxValue, far = float.MinValue;
        foreach (var p in trimmed.Positions)
        {
            if (p.Z < 3) continue;
            near = MathF.Min(near, p.X);
            far = MathF.Max(far, p.X);
        }

        Assert.Equal(2f, far - near, 3);
    }
}
