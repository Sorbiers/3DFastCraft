using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

public class CsgTests
{
    private static Mesh CubeAt(float size, Vector3 position) =>
        MeshTransform.Transformed(Primitives.Box(size, size, size), Matrix4x4.CreateTranslation(position));

    [Fact]
    public void UnionOfDisjointCubesKeepsBothVolumes()
    {
        var a = CubeAt(20, new Vector3(-30, 0, 0));
        var b = CubeAt(20, new Vector3(30, 0, 0));

        var result = CsgSolid.Union(a, b);
        var health = result.CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(16000.0, health.SignedVolume, 1);
    }

    [Fact]
    public void UnionOfOverlappingCubesMergesTheOverlap()
    {
        // Two 20 mm cubes offset by 10 mm share a 10x20x20 region.
        var a = CubeAt(20, Vector3.Zero);
        var b = CubeAt(20, new Vector3(10, 0, 0));

        var health = CsgSolid.Union(a, b).CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(8000 + 8000 - 4000, health.SignedVolume, 1);
    }

    [Fact]
    public void IntersectKeepsOnlyTheOverlap()
    {
        var a = CubeAt(20, Vector3.Zero);
        var b = CubeAt(20, new Vector3(10, 0, 0));

        var health = CsgSolid.Intersect(a, b).CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(10 * 20 * 20, health.SignedVolume, 1);
    }

    [Fact]
    public void SubtractRemovesTheOverlap()
    {
        var a = CubeAt(20, Vector3.Zero);
        var b = CubeAt(20, new Vector3(10, 0, 0));

        var health = CsgSolid.Subtract(a, b).CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(8000 - 4000, health.SignedVolume, 1);
    }

    /// <summary>The headline use case: drilling a hole through a block.</summary>
    [Fact]
    public void SubtractingAThroughCylinderLeavesAWatertightHole()
    {
        var block = Primitives.Box(20, 20, 20);
        var drill = Primitives.Prism(5, 40, 32); // longer than the block, so it passes clean through

        var result = CsgSolid.Subtract(block, drill);
        var health = result.CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());

        // Block minus the 32-gon prism cross-section.
        double prismArea = 0.5 * 32 * 25 * Math.Sin(Math.Tau / 32);
        Assert.Equal(8000 - prismArea * 20, health.SignedVolume, 1);

        // The outer envelope is untouched by an interior hole.
        var bounds = result.ComputeBounds();
        Assert.Equal(20f, bounds.Size.X, 3);
        Assert.Equal(20f, bounds.Size.Y, 3);
        Assert.Equal(20f, bounds.Size.Z, 3);
    }

    /// <summary>
    /// Coplanar faces are the classic CSG failure mode: subtracting a box whose face lies
    /// exactly on another face makes vertex classification ambiguous.
    /// </summary>
    [Fact]
    public void CoplanarFacesDoNotLeakTheSolid()
    {
        var a = Primitives.Box(20, 20, 20);
        var b = CubeAt(20, new Vector3(20, 0, 0)); // touches face-to-face, no overlap

        var health = CsgSolid.Subtract(a, b).CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(8000.0, health.SignedVolume, 1);
    }

    [Fact]
    public void SubtractingAnEnclosingSolidLeavesNothing()
    {
        var small = Primitives.Box(10, 10, 10);
        var large = Primitives.Box(40, 40, 40);

        var result = CsgSolid.Subtract(small, large);

        Assert.Equal(0, result.TriangleCount);
    }

    /// <summary>
    /// The parallel split path must reproduce the serial ordering exactly. Order decides the
    /// shape of the BSP tree, which decides how many fragments come out - so a mismatch here
    /// would make results non-reproducible run to run.
    /// </summary>
    [Fact]
    public void ParallelSplittingMatchesSerialExactly()
    {
        // A 32x16 sphere is ~1024 triangles, comfortably over the parallel threshold.
        var sphere = Primitives.Sphere(12, 32, 16);
        var cube = Primitives.Box(20, 20, 20);

        var serial = CsgSolid.Subtract(cube, sphere, parallel: false);
        var parallel = CsgSolid.Subtract(cube, sphere, parallel: true);

        Assert.Equal(serial.Positions.Count, parallel.Positions.Count);
        Assert.Equal(serial.Indices, parallel.Indices);
        for (int i = 0; i < serial.Positions.Count; i++)
            Assert.Equal(serial.Positions[i], parallel.Positions[i]);
    }

    [Fact]
    public void ParallelPathIsActuallyExercised()
    {
        var sphere = Primitives.Sphere(12, 32, 16);

        Assert.True(sphere.TriangleCount > PolygonSplitterProbe.ParallelThreshold,
            "test fixture is too small to reach the parallel path");
    }

    /// <summary>
    /// Deep BSP recursion is why CSG runs on a dedicated big-stack thread. A high-resolution
    /// sphere builds a tree deep enough to overflow the default 1 MB stack.
    /// </summary>
    [Fact]
    public void DeepRecursionDoesNotOverflowTheStack()
    {
        var a = Primitives.Sphere(12, 64, 32);   // ~4k triangles
        var b = Primitives.Sphere(12, 64, 32);

        var health = CsgSolid.Subtract(a, MeshTransform.Transformed(b, Matrix4x4.CreateTranslation(6, 0, 0)))
            .CheckHealth();

        Assert.True(health.TriangleCount > 0);
        Assert.True(health.IsWatertight, health.Describe());
    }

    [Fact]
    public void BooleanResultsSurviveBeingChained()
    {
        var block = Primitives.Box(20, 20, 20);
        var hole = Primitives.Prism(4, 40, 32);

        var once = CsgSolid.Subtract(block, hole);
        var twice = CsgSolid.Subtract(once,
            MeshTransform.Transformed(hole, Matrix4x4.CreateRotationX(MathF.PI / 2)));

        var health = twice.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(health.SignedVolume < once.ComputeSignedVolume());
    }
}

/// <summary>Exposes the internal threshold so a test can prove it is being crossed.</summary>
internal static class PolygonSplitterProbe
{
    public const int ParallelThreshold = 512;
}
