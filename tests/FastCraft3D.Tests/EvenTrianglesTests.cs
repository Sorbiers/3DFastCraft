using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Even triangles cuts a mesh finer, to about one size, without changing its shape.</summary>
public class EvenTrianglesTests
{
    private static float LongestEdge(Mesh mesh)
    {
        float longest = 0;
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            for (int k = 0; k < 3; k++)
                longest = MathF.Max(longest, Vector3.Distance(mesh.Positions[mesh.Indices[i + k]], mesh.Positions[mesh.Indices[i + (k + 1) % 3]]));
        return longest;
    }

    [Theory]
    [InlineData(PrimitiveKind.Cube)]
    [InlineData(PrimitiveKind.Cylinder)]
    [InlineData(PrimitiveKind.Sphere)]
    [InlineData(PrimitiveKind.Wedge)]
    public void EveryEdgeComesOutNearTheSizeAndTheShapeStaysExactly(PrimitiveKind kind)
    {
        var mesh = Primitives.Create(kind);

        var result = EvenTriangles.Refine(mesh, 2f);

        Assert.True(result.Changed, result.Refusal);
        Assert.True(result.TrianglesAfter > result.TrianglesBefore);
        Assert.True(LongestEdge(result.Mesh) <= 2f * 4f / 3f + 1e-3f, $"longest edge {LongestEdge(result.Mesh)}");

        var health = result.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(mesh.ComputeSignedVolume(), result.Mesh.ComputeSignedVolume(), 1);
        Assert.True(Vector3.Distance(mesh.ComputeBounds().Min, result.Mesh.ComputeBounds().Min) < 1e-4f);
        Assert.True(Vector3.Distance(mesh.ComputeBounds().Max, result.Mesh.ComputeBounds().Max) < 1e-4f);
    }

    [Fact]
    public void ASizeThatWouldMakeTooManyTrianglesIsRefused()
    {
        var mesh = Primitives.Box(200, 200, 200);

        var result = EvenTriangles.Refine(mesh, 0.1f);

        Assert.False(result.Changed);
        Assert.NotNull(result.Refusal);
        Assert.Same(mesh, result.Mesh);
    }
}
