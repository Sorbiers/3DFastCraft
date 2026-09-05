using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Turning a solid into a shell. The measure that matters is the wall: too thin and it will not
/// print, too thick and there was no point, and it has to be the same thickness all over rather
/// than just along the axes.
/// </summary>
public class MeshHollowTests
{
    [Fact]
    public void ThereIsACavityInsideAfterwards()
    {
        var box = Primitives.Box(40, 40, 40);

        var hollow = MeshHollow.Hollow(box, wallMm: 3f, resolution: 80);

        Assert.True(hollow.VolumeSavedCm3 > 0, "nothing was hollowed out");
        Assert.True(hollow.Mesh.ComputeSignedVolume() < box.ComputeSignedVolume() * 0.7,
            "the shell should weigh far less than the solid");
    }

    [Fact]
    public void TheShellIsStillPrintable()
    {
        var hollow = MeshHollow.Hollow(Primitives.Box(40, 40, 40), 3f, 80);

        var health = hollow.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(hollow.Mesh.ComputeSignedVolume() > 0, "it came out inside out");
    }

    /// <summary>
    /// The cavity is a shell of its own inside the outer one, which is what a hollow part is.
    /// </summary>
    [Fact]
    public void TheOutsideAndTheCavityAreTwoSurfaces()
    {
        var hollow = MeshHollow.Hollow(Primitives.Box(40, 40, 40), 3f, 80);

        Assert.Equal(2, MeshComponents.Count(hollow.Mesh));
    }

    [Fact]
    public void TheOutsideKeepsItsSize()
    {
        var box = Primitives.Box(40, 40, 40);

        var hollow = MeshHollow.Hollow(box, 3f, 80);

        var bounds = hollow.Mesh.ComputeBounds();
        Assert.Equal(40f, bounds.Size.X, 0);
        Assert.Equal(40f, bounds.Size.Y, 0);
        Assert.Equal(40f, bounds.Size.Z, 0);
    }

    /// <summary>
    /// The wall comes out about as thick as asked. A 40 mm cube walled at 4 mm keeps a shell of
    /// roughly 40^3 - 32^3 mm3; measuring the volume is the simplest way to check the thickness
    /// without measuring every face.
    /// </summary>
    [Fact]
    public void TheWallIsAboutTheThicknessAskedFor()
    {
        var hollow = MeshHollow.Hollow(Primitives.Box(40, 40, 40), wallMm: 4f, resolution: 120);

        double expected = Math.Pow(40, 3) - Math.Pow(40 - 2 * 4, 3);
        double actual = hollow.Mesh.ComputeSignedVolume();

        Assert.True(actual > expected * 0.8 && actual < expected * 1.25,
            $"expected a shell of about {expected:N0} mm3, got {actual:N0}");
    }

    [Fact]
    public void AThickerWallLeavesMoreMaterial()
    {
        var thin = MeshHollow.Hollow(Primitives.Box(40, 40, 40), 2f, 80);
        var thick = MeshHollow.Hollow(Primitives.Box(40, 40, 40), 6f, 80);

        Assert.True(thick.Mesh.ComputeSignedVolume() > thin.Mesh.ComputeSignedVolume());
        Assert.True(thick.VolumeSavedCm3 < thin.VolumeSavedCm3);
    }

    /// <summary>A part with nothing to spare is handed back solid rather than destroyed.</summary>
    [Fact]
    public void APartThinnerThanItsWallIsLeftAlone()
    {
        var plate = Primitives.Box(40, 40, 2);

        var hollow = MeshHollow.Hollow(plate, wallMm: 3f, resolution: 80);

        Assert.Same(plate, hollow.Mesh);
        Assert.Equal(0, hollow.VolumeSavedCm3, 6);
    }

    [Fact]
    public void AWallTooThinToPrintIsRaisedToOneThatWill()
    {
        var hollow = MeshHollow.Hollow(Primitives.Box(40, 40, 40), wallMm: 0.05f, resolution: 80);

        Assert.Equal(MeshHollow.MinimumWallMm, hollow.WallMm, 3);
    }

    [Fact]
    public void ItWorksOnSomethingRoundToo()
    {
        var sphere = MeshTransform.Transformed(
            Primitives.Create(PrimitiveKind.Sphere), System.Numerics.Matrix4x4.CreateScale(3f));

        var hollow = MeshHollow.Hollow(sphere, 3f, 80);

        Assert.True(hollow.Mesh.CheckHealth().IsWatertight, hollow.Mesh.CheckHealth().Describe());
        Assert.Equal(2, MeshComponents.Count(hollow.Mesh));
        Assert.True(hollow.Mesh.ComputeSignedVolume() < sphere.ComputeSignedVolume() * 0.8);
    }

    [Fact]
    public void AnEmptyMeshIsHandedStraightBack()
    {
        var empty = new Mesh();

        Assert.Same(empty, MeshHollow.Hollow(empty, 2f).Mesh);
    }
}
