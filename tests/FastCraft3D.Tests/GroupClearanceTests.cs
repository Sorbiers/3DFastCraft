using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Subtracting a group with a clearance - a row of dowel pins taken out of the part they go into.
///
/// It was refused outright: grouping throws its members away, so nothing was left to say the
/// group had been six cylinders, and a clearance is only exact on shapes whose faces are square
/// to their own axes. The group carries that one fact now.
/// </summary>
public class GroupClearanceTests
{
    /// <summary>Two 10 mm pins, 40 mm apart, as Group leaves them: one mesh, two shells.</summary>
    private static SceneObject TwoPins()
    {
        var left = MeshTransform.Transformed(
            Primitives.Box(10, 10, 10), Matrix4x4.CreateTranslation(-20, 0, 0));
        var right = MeshTransform.Transformed(
            Primitives.Box(10, 10, 10), Matrix4x4.CreateTranslation(20, 0, 0));

        return new SceneObject("Group", Mesh.Combine([left, right])) { PiecesTakeClearance = true };
    }

    [Fact]
    public void AGroupWhosePiecesCanAllTakeAClearanceIsAllowedOne()
    {
        Assert.True(TwoPins().CanTakeClearance);

        var plain = new SceneObject("Group", Primitives.Box(10, 10, 10));
        Assert.False(plain.CanTakeClearance); // no Origin, and nothing said its pieces were safe
    }

    /// <summary>
    /// The failure this is really about: growing the group as one lump would have moved the pins
    /// apart as well as fattened them, and the holes would have come out in the wrong places.
    /// </summary>
    [Fact]
    public void EachPieceGrowsAboutItsOwnCentre()
    {
        var pieces = MeshComponents.Split(TwoPins().ToWorldMeshGrown(1f))
            .OrderBy(p => p.ComputeBounds().Center.X)
            .ToList();

        Assert.Equal(2, pieces.Count);

        var left = pieces[0].ComputeBounds();
        var right = pieces[1].ComputeBounds();

        // A millimetre on every side: 10 mm across becomes 12.
        Assert.Equal(12f, left.Size.X, 3);
        Assert.Equal(12f, left.Size.Y, 3);
        Assert.Equal(12f, left.Size.Z, 3);

        // And exactly where they were.
        Assert.Equal(-20f, left.Center.X, 3);
        Assert.Equal(20f, right.Center.X, 3);
    }

    [Fact]
    public void WithoutAClearanceTheGroupIsUntouched()
    {
        var group = TwoPins();
        Assert.Equal(group.ToWorldMesh().Positions.Count, group.ToWorldMeshGrown(0f).Positions.Count);
        Assert.Equal(50f, group.ToWorldMeshGrown(0f).ComputeBounds().Size.X, 3);
    }
}
