using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Rebuilding the surface from scratch. The point of it is the case patching cannot touch: a
/// mesh whose surface passes through itself, where there is no hole to fill and no face pointing
/// the wrong way, and the geometry is simply self-contradictory.
/// </summary>
public class VoxelRebuildTests
{
    /// <summary>
    /// Two boxes overlapping, exported as one object without ever being unioned. Closed,
    /// valid-looking, and not a solid - which is what an imported model with lettering dropped
    /// onto it usually is.
    /// </summary>
    private static Mesh SelfIntersecting()
    {
        var body = Primitives.Box(40, 40, 40);
        var lump = MeshTransform.Transformed(
            Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(18, 0, 0));

        return Mesh.Combine([body, lump]);
    }

    [Fact]
    public void ARebuiltBoxIsStillABoxAndStillPrintable()
    {
        var box = Primitives.Box(40, 40, 40);

        var rebuilt = VoxelRebuild.Rebuild(box, 96);

        Assert.True(rebuilt.Mesh.CheckHealth().IsWatertight, rebuilt.Mesh.CheckHealth().Describe());
        Assert.True(rebuilt.Mesh.ComputeSignedVolume() > 0, "it came out inside out");

        // Within a voxel or two of the original all round.
        var bounds = rebuilt.Mesh.ComputeBounds();
        Assert.Equal(40f, bounds.Size.X, 0);
        Assert.Equal(40f, bounds.Size.Z, 0);
    }

    [Fact]
    public void TheVolumeSurvivesRoughlyIntact()
    {
        var box = Primitives.Box(40, 40, 40);

        var rebuilt = VoxelRebuild.Rebuild(box, 128);

        double before = box.ComputeSignedVolume();
        double after = rebuilt.Mesh.ComputeSignedVolume();

        Assert.True(after > before * 0.9 && after < before * 1.1,
            $"volume went from {before:N0} to {after:N0} mm3");
    }

    /// <summary>The whole reason the tool exists.</summary>
    [Fact]
    public void ASelfIntersectingModelComesBackWatertight()
    {
        var broken = SelfIntersecting();

        // It passes a watertightness check - each shell is closed - and is still not a solid.
        // That is the whole trap: nothing local is wrong with it, the two shells simply occupy
        // the same space, and no amount of patching triangles can say what the inside is.
        Assert.True(broken.CheckHealth().IsWatertight);
        Assert.Equal(2, MeshComponents.Count(broken));

        var rebuilt = VoxelRebuild.Rebuild(broken, 96);

        Assert.True(rebuilt.Mesh.CheckHealth().IsWatertight, rebuilt.Mesh.CheckHealth().Describe());
        Assert.True(rebuilt.Mesh.ComputeSignedVolume() > 0);
        Assert.Equal(1, MeshComponents.Count(rebuilt.Mesh));
    }

    /// <summary>
    /// Overlapping solids have to come back as their union. Counting crossings by parity rather
    /// than by direction would call the overlap empty and leave a hole where the two meet.
    /// </summary>
    [Fact]
    public void OverlappingSolidsBecomeOneSolidNotAHole()
    {
        var rebuilt = VoxelRebuild.Rebuild(SelfIntersecting(), 96);

        // The union of a 40 cube and a 20 cube overlapping by 10 mm: 64000 + 8000 - 4000.
        double expected = 40 * 40 * 40 + 20 * 20 * 20 - 2 * 20 * 20;
        double actual = rebuilt.Mesh.ComputeSignedVolume();

        Assert.True(actual > expected * 0.9 && actual < expected * 1.1,
            $"expected about {expected:N0} mm3, got {actual:N0}");
        Assert.Equal(1, MeshComponents.Count(rebuilt.Mesh));
    }

    [Fact]
    public void EveryPrimitiveSurvivesARebuild()
    {
        foreach (var kind in Enum.GetValues<PrimitiveKind>())
        {
            var mesh = MeshTransform.Transformed(Primitives.Create(kind), Matrix4x4.CreateScale(3f));

            var rebuilt = VoxelRebuild.Rebuild(mesh, 80);

            Assert.True(rebuilt.Mesh.CheckHealth().IsWatertight,
                $"{kind}: {rebuilt.Mesh.CheckHealth().Describe()}");
            Assert.True(rebuilt.Mesh.ComputeSignedVolume() > 0, $"{kind} came out inside out");
        }
    }

    /// <summary>A hollow shell has to stay hollow, not be filled in.</summary>
    [Fact]
    public void ACavityIsKept()
    {
        var outer = Primitives.Box(60, 60, 60);
        var inner = Primitives.Box(30, 30, 30);
        inner.FlipWinding(); // an inward-facing shell is what a cavity looks like
        var hollow = Mesh.Combine([outer, inner]);

        var rebuilt = VoxelRebuild.Rebuild(hollow, 96);

        double solid = 60.0 * 60 * 60;
        double actual = rebuilt.Mesh.ComputeSignedVolume();

        Assert.True(actual < solid * 0.9, $"the cavity was filled in - {actual:N0} of {solid:N0} mm3");
        Assert.True(rebuilt.Mesh.CheckHealth().IsWatertight);
    }

    [Fact]
    public void FinerSettingsKeepMoreDetail()
    {
        var mesh = Primitives.Create(PrimitiveKind.Torus);

        var coarse = VoxelRebuild.Rebuild(mesh, 40);
        var fine = VoxelRebuild.Rebuild(mesh, 140);

        Assert.True(fine.VoxelSizeMm < coarse.VoxelSizeMm);
        Assert.True(fine.Mesh.TriangleCount > coarse.Mesh.TriangleCount);
        Assert.True(fine.Mesh.CheckHealth().IsWatertight);
    }

    [Fact]
    public void TheResolutionIsHeldInRange()
    {
        var box = Primitives.Box(20, 20, 20);

        Assert.Equal(VoxelRebuild.MinimumResolution, VoxelRebuild.Rebuild(box, 1).Resolution);
        Assert.Equal(VoxelRebuild.MaximumResolution, VoxelRebuild.Rebuild(box, 99999).Resolution);
    }

    [Fact]
    public void AnEmptyMeshRebuildsToNothingRatherThanThrowing()
    {
        var rebuilt = VoxelRebuild.Rebuild(new Mesh());

        Assert.Equal(0, rebuilt.Mesh.TriangleCount);
    }

    /// <summary>
    /// The estimate is what someone decides on, so it has to be close. A cube at 160 voxels is
    /// genuinely a third of a million triangles; guessing an order of magnitude low would have
    /// people choosing a setting that grinds.
    /// </summary>
    [Fact]
    public void TheTriangleEstimateMatchesWhatIsActuallyBuilt()
    {
        var box = Primitives.Box(20, 20, 20);

        foreach (int resolution in new[] { 40, 80, 160 })
        {
            var rebuilt = VoxelRebuild.Rebuild(box, resolution);
            long predicted = VoxelRebuild.EstimateTriangles(box.ComputeSurfaceArea(), rebuilt.VoxelSizeMm);

            Assert.True(predicted > rebuilt.Mesh.TriangleCount * 0.7
                     && predicted < rebuilt.Mesh.TriangleCount * 1.4,
                $"at {resolution}: predicted {predicted:N0}, built {rebuilt.Mesh.TriangleCount:N0}");
        }
    }

    [Fact]
    public void SurfaceAreaIsMeasuredCorrectly()
    {
        Assert.Equal(6 * 20f * 20f, Primitives.Box(20, 20, 20).ComputeSurfaceArea(), 2);
        Assert.Equal(0f, new Mesh().ComputeSurfaceArea(), 5);
    }
}
