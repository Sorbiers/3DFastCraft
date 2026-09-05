using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Cutting the triangle count down while keeping the shape. The measure of success is not the
/// count - that is the easy half - but whether the thing still looks like what it was.
/// </summary>
public class MeshSimplifyTests
{
    private static Mesh Dense() => MeshSubdivision.Subdivide(Primitives.Create(PrimitiveKind.Sphere), 2);

    [Fact]
    public void ItActuallyReducesTheCount()
    {
        var dense = Dense();

        var simpler = MeshSimplify.To(dense, dense.TriangleCount / 4);

        Assert.True(simpler.TriangleCount < dense.TriangleCount * 0.6,
            $"{dense.TriangleCount:N0} only became {simpler.TriangleCount:N0}");
    }

    [Fact]
    public void TheResultIsStillPrintable()
    {
        var dense = Dense();

        var simpler = MeshSimplify.To(dense, dense.TriangleCount / 4);

        var health = simpler.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(simpler.ComputeSignedVolume() > 0, "it came out inside out");
    }

    /// <summary>The point of the quadric measure: the shape survives, not just the topology.</summary>
    [Fact]
    public void TheShapeIsKept()
    {
        var dense = Dense();
        double before = dense.ComputeSignedVolume();

        var simpler = MeshSimplify.To(dense, dense.TriangleCount / 6);
        double after = simpler.ComputeSignedVolume();

        Assert.True(after > before * 0.9 && after < before * 1.1,
            $"volume went from {before:N0} to {after:N0} mm3");
    }

    [Fact]
    public void TheBoundingBoxBarelyMoves()
    {
        var dense = Dense();
        var before = dense.ComputeBounds();

        var after = MeshSimplify.To(dense, dense.TriangleCount / 6).ComputeBounds();

        Assert.Equal(before.Size.X, after.Size.X, 0);
        Assert.Equal(before.Size.Y, after.Size.Y, 0);
        Assert.Equal(before.Size.Z, after.Size.Z, 0);
    }

    /// <summary>
    /// Flat panels should thin out and creases should not, which is exactly what the quadric
    /// measure buys. A subdivided cube is all flat panel and a few hard edges, so it should come
    /// back to something near its original twelve triangles without losing its corners.
    /// </summary>
    [Fact]
    public void FlatFacesCollapseAndCornersSurvive()
    {
        var cube = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 3);
        Assert.True(cube.TriangleCount > 700);

        var simpler = MeshSimplify.To(cube, 40);

        Assert.True(simpler.TriangleCount <= 80,
            $"a flat-sided box should reduce a long way, got {simpler.TriangleCount}");

        // Still 20 mm across: the corners have not been rounded off.
        var bounds = simpler.ComputeBounds();
        Assert.Equal(20f, bounds.Size.X, 1);
        Assert.Equal(20f, bounds.Size.Y, 1);
        Assert.Equal(20f, bounds.Size.Z, 1);
    }

    [Fact]
    public void AskingForMoreThanThereIsChangesNothing()
    {
        var sphere = Primitives.Create(PrimitiveKind.Sphere);

        Assert.Same(sphere, MeshSimplify.To(sphere, sphere.TriangleCount));
        Assert.Same(sphere, MeshSimplify.To(sphere, sphere.TriangleCount * 2));
    }

    [Fact]
    public void ItNeverReducesToNothing()
    {
        var simpler = MeshSimplify.To(Dense(), 1);

        Assert.True(simpler.TriangleCount >= 4, "a solid needs at least four faces");
        Assert.True(simpler.ComputeSignedVolume() > 0);
    }

    [Fact]
    public void AFractionIsTheSameAsACount()
    {
        var dense = Dense();

        var byCount = MeshSimplify.To(dense, dense.TriangleCount / 2);
        var byFraction = MeshSimplify.ByFraction(dense, 0.5f);

        Assert.Equal(byCount.TriangleCount, byFraction.TriangleCount);
    }

    /// <summary>A hole must keep its shape rather than being pulled shut.</summary>
    [Fact]
    public void TheRimOfAnOpenMeshIsLeftAlone()
    {
        var box = MeshSubdivision.Subdivide(Primitives.Box(20, 20, 20), 2);
        var open = new Mesh(box.Positions, box.Indices.Take(box.Indices.Count - 3 * 20).ToList());

        int rimBefore = open.CheckHealth().BoundaryEdges;
        Assert.True(rimBefore > 0);

        var simpler = MeshSimplify.To(open, open.TriangleCount / 3);

        Assert.Equal(rimBefore, simpler.CheckHealth().BoundaryEdges);
    }

    [Fact]
    public void AnEmptyMeshIsHandedStraightBack()
    {
        var empty = new Mesh();

        Assert.Same(empty, MeshSimplify.To(empty, 10));
    }

    /// <summary>The pairing that matters: a rebuilt model is heavy, and this is what lightens it.</summary>
    [Fact]
    public void ARebuiltModelCanBeBroughtBackToASensibleSize()
    {
        var rebuilt = VoxelRebuild.Rebuild(Primitives.Box(20, 20, 20), 64).Mesh;
        Assert.True(rebuilt.TriangleCount > 20_000);

        var simpler = MeshSimplify.To(rebuilt, 2000);

        Assert.True(simpler.TriangleCount < 4000, $"still {simpler.TriangleCount:N0}");
        Assert.True(simpler.CheckHealth().IsWatertight, simpler.CheckHealth().Describe());

        double before = rebuilt.ComputeSignedVolume();
        double after = simpler.ComputeSignedVolume();
        Assert.True(after > before * 0.9 && after < before * 1.1);
    }
}
