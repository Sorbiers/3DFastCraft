using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Clone makes a face into a thin solid standing on it.</summary>
public class SurfaceCloneTests
{
    [Fact]
    public void TheTopOfABoxBecomesAPlateATenthThickLyingOnIt()
    {
        var box = MeshSubdivision.Subdivide(Primitives.Box(20, 10, 10), 2);
        var face = FacePatch.Find(box, new Vector3(0.3f, 0.2f, 5), Vector3.UnitZ)!;

        var plate = SurfaceClone.Extrude(face);

        Assert.NotNull(plate);
        Assert.True(plate!.CheckHealth().IsWatertight);
        Assert.True(plate.ComputeSignedVolume() > 0, "the plate is inside out");
        Assert.Equal(20 * 10 * 0.1, plate.ComputeSignedVolume(), 2);
        Assert.Equal(5f, plate.ComputeBounds().Min.Z, 4);
        Assert.Equal(5.1f, plate.ComputeBounds().Max.Z, 4);
    }

    [Theory]
    [InlineData(1f, 22 * 12)]
    [InlineData(-1f, 18 * 8)]
    public void GrowingTheOutlineMovesEveryEdgeOutByTheAmount(float grow, float area)
    {
        var box = MeshSubdivision.Subdivide(Primitives.Box(20, 10, 10), 2);
        var face = FacePatch.Find(box, new Vector3(0.3f, 0.2f, 5), Vector3.UnitZ)!;

        var plate = SurfaceClone.Extrude(face, 0.5f, grow);

        Assert.NotNull(plate);
        Assert.True(plate!.CheckHealth().IsWatertight);
        Assert.Equal(area * 0.5, plate.ComputeSignedVolume(), 2);
        Assert.Equal(5f, plate.ComputeBounds().Min.Z, 4);
    }

    [Fact]
    public void GrowingClosesAHoleSmallerThanTheAmountInsteadOfFailing()
    {
        var holed = Geometry.Csg.CsgSolid.Subtract(Primitives.Box(20, 20, 10), Primitives.Prism(1, 40, 24));
        var face = FacePatch.Find(holed, new Vector3(8, 8, 5), Vector3.UnitZ)!;

        var plate = SurfaceClone.Extrude(face, 0.5f, 2f);

        Assert.NotNull(plate);
        Assert.True(plate!.CheckHealth().IsWatertight);
        Assert.Equal(24 * 24 * 0.5, plate.ComputeSignedVolume(), 1);
    }

    [Fact]
    public void ShrinkingAnOutlineToNothingIsRefused()
    {
        var box = Primitives.Box(4, 4, 4);
        var face = FacePatch.Find(box, new Vector3(0.3f, 0.2f, 2), Vector3.UnitZ)!;

        Assert.Null(SurfaceClone.Extrude(face, 0.5f, -3f));
    }

    [Fact]
    public void AFaceWithAHoleKeepsItsHole()
    {
        var holed = Geometry.Csg.CsgSolid.Subtract(Primitives.Box(20, 20, 10), Primitives.Prism(4, 40, 24));
        var face = FacePatch.Find(holed, new Vector3(8, 8, 5), Vector3.UnitZ)!;

        var plate = SurfaceClone.Extrude(face);

        Assert.NotNull(plate);
        Assert.True(plate!.CheckHealth().IsWatertight);
        Assert.Equal(face.Area * 0.1, plate.ComputeSignedVolume(), 1);
    }
}
