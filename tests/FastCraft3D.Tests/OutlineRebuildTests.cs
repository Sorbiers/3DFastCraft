using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Rebuild by outline keeps the corners, drops every other point, and changes no shape.</summary>
public class OutlineRebuildTests
{
    private static void SameShape(Mesh before, Mesh after)
    {
        var health = after.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.Equal(before.ComputeSignedVolume(), after.ComputeSignedVolume(), 2);
        Assert.Equal(before.ComputeBounds().Min, after.ComputeBounds().Min);
        Assert.Equal(before.ComputeBounds().Max, after.ComputeBounds().Max);

        var had = before.Positions.ToHashSet();
        Assert.All(after.Positions, p => Assert.Contains(p, had));
    }

    [Fact]
    public void ASubdividedBoxComesBackAsTwelveTrianglesOnItsEightCorners()
    {
        var mesh = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 3);

        var result = OutlineRebuild.Rebuild(mesh);

        Assert.True(result.Changed, result.Refusal);
        Assert.Equal(12, result.Mesh.TriangleCount);
        Assert.Equal(8, result.Mesh.Positions.Count);
        SameShape(mesh, result.Mesh);
    }

    [Fact]
    public void AStepAndItsWallsLoseThePointsAlongTheirStraightEdges()
    {
        // A wall with a battlement on top, cut finely: every face is flat and the only points
        // that matter are the corners of the step.
        var wall = MeshSubdivision.Subdivide(Primitives.Box(40, 4, 20), 2);
        var merlon = MeshTransform.Transformed(MeshSubdivision.Subdivide(Primitives.Box(10, 4, 6), 2),
            Matrix4x4.CreateTranslation(0, 0, 13));
        var mesh = CsgSolid.Union(wall, merlon);

        var result = OutlineRebuild.Rebuild(mesh);

        Assert.True(result.Changed, result.Refusal);
        Assert.True(result.Mesh.TriangleCount < mesh.TriangleCount / 4, $"{mesh.TriangleCount} became {result.Mesh.TriangleCount}");
        SameShape(mesh, result.Mesh);
    }

    [Fact]
    public void ACurvedSurfaceIsLeftAlone()
    {
        var mesh = Primitives.Sphere(10, 32, 16);

        var result = OutlineRebuild.Rebuild(mesh);

        Assert.False(result.Changed);
        Assert.Same(mesh, result.Mesh);
    }
}
