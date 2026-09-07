using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Subtracting with a clearance: the hole comes out bigger than the thing that cut it.
///
/// This is the number that used to be arithmetic by hand. Every fit in the hinged box was two
/// numbers typed in two places and a hope that they stayed related - a 3 mm pin and a 3.4 mm
/// bore, a 25.2 mm knuckle and a 26 mm notch. Change one and nothing changed the other.
/// </summary>
public class ClearanceTests
{
    private static SceneObject Cube(float w, float d, float h) =>
        new("cutter", Primitives.Box(w, d, h)) { Origin = PrimitiveKind.Cube };

    private static SceneObject Pin(float diameter, float length) =>
        new("pin", Primitives.Prism(diameter / 2f, length, 64)) { Origin = PrimitiveKind.Cylinder };

    /// <summary>A clearance is per side, so each dimension grows by twice it.</summary>
    [Fact]
    public void ACubeGrowsByTheClearanceOnEverySide()
    {
        var size = Cube(10, 20, 30).ToWorldMeshGrown(0.2f).ComputeBounds().Size;

        Assert.Equal(10.4f, size.X, 3);
        Assert.Equal(20.4f, size.Y, 3);
        Assert.Equal(30.4f, size.Z, 3);
    }

    /// <summary>The case the whole feature exists for: a 3 mm pin wants a 3.4 mm bore.</summary>
    [Fact]
    public void APinLeavesAHoleTwiceTheClearanceWider()
    {
        var size = Pin(3f, 20f).ToWorldMeshGrown(0.2f).ComputeBounds().Size;

        Assert.Equal(3.4f, size.X, 2);
        Assert.Equal(3.4f, size.Y, 2);
        Assert.Equal(20.4f, size.Z, 2);
    }

    /// <summary>Zero has to mean exactly what it always did, or every old model changes.</summary>
    [Fact]
    public void NoClearanceIsTheSameGeometryAsBefore()
    {
        var pin = Pin(3f, 20f);

        var plain = pin.ToWorldMesh().ComputeBounds();
        var grown = pin.ToWorldMeshGrown(0f).ComputeBounds();

        Assert.Equal(plain.Size.X, grown.Size.X, 5);
        Assert.Equal(plain.Size.Z, grown.Size.Z, 5);
    }

    /// <summary>
    /// Growing happens in the cutter's own frame. A pin lying on its side has to gain the
    /// clearance on its radius and its ends, not on the plate's X and Z.
    /// </summary>
    [Fact]
    public void ATurnedCutterGrowsAlongItsOwnAxes()
    {
        var pin = Pin(3f, 20f);
        pin.Rotation = new Vector3(0, 90, 0);      // now lying along the plate's X

        var size = pin.ToWorldMeshGrown(0.25f).ComputeBounds().Size;

        Assert.Equal(20.5f, size.X, 2);            // the length, plus a clearance at each end
        Assert.Equal(3.5f, size.Y, 2);             // the diameter, plus a clearance all round
        Assert.Equal(3.5f, size.Z, 2);
    }

    /// <summary>It stays centred: a clearance grows a hole, it does not move it.</summary>
    [Fact]
    public void TheCutterDoesNotMove()
    {
        var pin = Pin(3f, 20f);
        pin.PositionX = 17f;
        pin.PositionY = -4f;

        var centre = pin.ToWorldMeshGrown(0.4f).ComputeBounds().Center;

        Assert.Equal(17f, centre.X, 3);
        Assert.Equal(-4f, centre.Y, 3);
    }

    /// <summary>
    /// And the refusal. On a sloped face, growing each dimension leaves the surface nearer than
    /// asked - the clearance comes out as c times the cosine of the slope. A gap smaller than
    /// the number typed is the one direction that jams a printed part, so these are refused
    /// rather than quietly under-delivered.
    /// </summary>
    [Theory]
    [InlineData(PrimitiveKind.Cube, true)]
    [InlineData(PrimitiveKind.Cylinder, true)]
    [InlineData(PrimitiveKind.Sphere, true)]
    [InlineData(PrimitiveKind.Cone, false)]
    [InlineData(PrimitiveKind.Pyramid, false)]
    [InlineData(PrimitiveKind.Wedge, false)]
    [InlineData(PrimitiveKind.Torus, false)]
    public void OnlyTheShapesItIsExactOnAreAllowed(PrimitiveKind kind, bool allowed)
    {
        var o = new SceneObject("x", Primitives.Box(10, 10, 10)) { Origin = kind };

        Assert.Equal(allowed, o.CanTakeClearance);
    }

    /// <summary>A mesh that has been through a boolean no longer knows what it was.</summary>
    [Fact]
    public void SomethingThatHasLostItsShapeIsRefused()
    {
        var made = new SceneObject("Subtract", Primitives.Box(10, 10, 10));

        Assert.Null(made.Origin);
        Assert.False(made.CanTakeClearance);
    }

    /// <summary>
    /// The point of the whole thing, end to end: a pin subtracted from a block with a clearance
    /// leaves a hole the pin drops into rather than one it has to be forced into.
    /// </summary>
    [Fact]
    public void ThePinFitsTheHoleItCut()
    {
        var block = new SceneObject("block", Primitives.Box(40, 40, 20));
        var pin = Pin(3f, 40f);

        var bored = CsgSolid.Subtract(block.ToWorldMesh(), pin.ToWorldMeshGrown(0.2f));

        // The bore is the block's volume less a 3.4 mm cylinder through 20 mm of material.
        double solid = Math.Abs(bored.ComputeSignedVolume());
        double expected = 40 * 40 * 20 - Math.PI * 1.7 * 1.7 * 20;

        Assert.Equal(expected, solid, 0.02 * expected);

        // And the pin itself still passes through what is left.
        double interference = Math.Abs(
            CsgSolid.Intersect(bored, pin.ToWorldMesh()).ComputeSignedVolume());

        Assert.True(interference < 1e-3,
            $"the pin still fouls its own hole by {interference:0.####} mm3");
    }
}
