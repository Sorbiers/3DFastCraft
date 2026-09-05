using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Splitting a mesh into connected pieces is what Ungroup runs on, and what separates a
/// downloaded STL that holds several loose parts.
/// </summary>
public class MeshComponentsTests
{
    private static Mesh At(Mesh mesh, float x) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, 0, 0));

    [Fact]
    public void ASingleSolidIsOnePiece()
    {
        var cube = Primitives.Box(20, 20, 20);

        Assert.Equal(1, MeshComponents.Count(cube));
        Assert.Single(MeshComponents.Split(cube));
    }

    [Fact]
    public void TwoSeparateSolidsSplitIntoTwo()
    {
        var combined = Mesh.Combine([Primitives.Box(20, 20, 20), At(Primitives.Box(10, 10, 10), 60)]);

        var parts = MeshComponents.Split(combined);

        Assert.Equal(2, parts.Count);
        Assert.Equal(2, MeshComponents.Count(combined));
    }

    [Fact]
    public void EachPieceKeepsItsOwnGeometryAndPlace()
    {
        var combined = Mesh.Combine([Primitives.Box(20, 20, 20), At(Primitives.Box(10, 10, 10), 60)]);

        var parts = MeshComponents.Split(combined);

        // Order follows first appearance, so the big cube at the origin comes first.
        Assert.Equal(20f, parts[0].ComputeBounds().Size.X, 3);
        Assert.Equal(0f, parts[0].ComputeBounds().Center.X, 3);
        Assert.Equal(10f, parts[1].ComputeBounds().Size.X, 3);
        Assert.Equal(60f, parts[1].ComputeBounds().Center.X, 3);
    }

    [Fact]
    public void EveryPieceIsStillAClosedSolid()
    {
        var combined = Mesh.Combine([
            Primitives.Create(PrimitiveKind.Sphere),
            At(Primitives.Create(PrimitiveKind.Cylinder), 50),
            At(Primitives.Create(PrimitiveKind.Cone), 100)
        ]);

        var parts = MeshComponents.Split(combined);

        Assert.Equal(3, parts.Count);
        foreach (var part in parts)
            Assert.True(part.CheckHealth().IsWatertight, part.CheckHealth().Describe());
    }

    [Fact]
    public void SplittingLosesNoTriangles()
    {
        var combined = Mesh.Combine([Primitives.Box(20, 20, 20), At(Primitives.Create(PrimitiveKind.Torus), 60)]);

        var parts = MeshComponents.Split(combined);

        Assert.Equal(combined.TriangleCount, parts.Sum(p => p.TriangleCount));
    }

    /// <summary>
    /// Pieces are read off the geometry, so solids that genuinely share vertices are one piece.
    /// That is the limit of Ungroup, and worth pinning down rather than discovering later.
    /// </summary>
    [Fact]
    public void SolidsThatShareVerticesCountAsOnePiece()
    {
        var welded = Mesh.Combine([Primitives.Box(20, 20, 20), At(Primitives.Box(20, 20, 20), 20)]).Welded();

        Assert.Equal(1, MeshComponents.Count(welded));
    }

    [Fact]
    public void AnEmptyMeshHasNoPieces()
    {
        Assert.Empty(MeshComponents.Split(new Mesh()));
        Assert.Equal(0, MeshComponents.Count(new Mesh()));
    }
}
