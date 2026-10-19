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
    public void MergingTakesAFaceWhosePointsWanderAHairAsOneFace()
    {
        // The top of a finely cut box, its inner points nudged up and down by a few thousandths:
        // at the exact tolerance that is many faces, merged it is one.
        var mesh = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 3);
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            var p = mesh.Positions[i];
            if (p.Z == 10 && MathF.Abs(p.X) < 9.9f && MathF.Abs(p.Y) < 9.9f)
                mesh.Positions[i] = p with { Z = 10 + (i % 2 == 0 ? 0.004f : -0.004f) };
        }

        var exact = OutlineRebuild.Rebuild(mesh);
        var merged = OutlineRebuild.Rebuild(mesh, OutlineRebuild.MergeToleranceMm);

        Assert.True(merged.Changed, merged.Refusal);
        Assert.Equal(12, merged.TrianglesAfter);
        Assert.True(merged.TrianglesAfter < exact.TrianglesAfter, $"exact {exact.TrianglesAfter}, merged {merged.TrianglesAfter}");
        Assert.True(merged.Mesh.CheckHealth().IsWatertight);
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
