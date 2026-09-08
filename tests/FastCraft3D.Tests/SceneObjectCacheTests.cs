using System.Diagnostics;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What an object keeps rather than works out again.
///
/// The status bar asks a selected object how healthy it is, and that used to mean transforming the
/// whole mesh and checking the copy - every time anything was raised. One nudge raises a dozen
/// properties, so on a mould of a scan that was four seconds twelve times over, on the UI thread.
/// The app went to Not Responding and stayed there.
/// </summary>
public class SceneObjectCacheTests
{
    private static SceneObject Dense() =>
        new("dense", Primitives.Sphere(20, 200, 100));

    /// <summary>
    /// The regression guard. Asking repeatedly has to cost nothing, because the UI does exactly
    /// that - a fixed millisecond budget rather than a ratio, since the whole point is that the
    /// second answer takes no measurable time at all.
    /// </summary>
    [Fact]
    public void AskingAgainCostsNothing()
    {
        var o = Dense();
        _ = o.Health;

        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) { _ = o.Health; _ = o.VolumeCm3; }
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 50,
            $"200 reads took {clock.ElapsedMilliseconds} ms - the answer is not being kept");
    }

    /// <summary>
    /// Moving something cannot change what its edges do, so the answer stands. This is what makes
    /// keeping it worthwhile at all: a drag drops the bounds every frame and must not drop this.
    /// </summary>
    [Fact]
    public void MovingItDoesNotThrowTheAnswerAway()
    {
        var o = Dense();
        _ = o.Health;

        o.PositionX = 40f;
        o.Rotation = new Vector3(0, 30, 0);

        var clock = Stopwatch.StartNew();
        _ = o.Health;
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 50,
            $"a move cost {clock.ElapsedMilliseconds} ms of rechecking");
    }

    /// <summary>New geometry is a new answer, or the status bar describes the last shape.</summary>
    [Fact]
    public void NewGeometryIsCheckedAgain()
    {
        var o = new SceneObject("box", Primitives.Box(10, 10, 10));
        Assert.Equal(12, o.Health.TriangleCount);

        o.Mesh = Primitives.Sphere(10, 32, 16);

        Assert.Equal(o.Mesh.TriangleCount, o.Health.TriangleCount);
        Assert.True(o.Health.TriangleCount > 12);
    }

    /// <summary>
    /// Volume is the one part of the health a transform does change, so it is scaled rather than
    /// measured again - by the determinant, which is the three scales multiplied.
    /// </summary>
    [Fact]
    public void VolumeFollowsTheTransform()
    {
        var o = new SceneObject("box", Primitives.Box(10, 10, 10));
        Assert.Equal(1d, o.VolumeCm3, 2);          // 1000 mm3

        o.SizeX = 20f;
        Assert.Equal(2d, o.VolumeCm3, 2);

        o.SizeY = 20f;
        o.SizeZ = 20f;
        Assert.Equal(8d, o.VolumeCm3, 2);
    }

    /// <summary>Turning it does not change how much material there is.</summary>
    [Fact]
    public void TurningItDoesNotChangeTheVolume()
    {
        var o = new SceneObject("box", Primitives.Box(10, 20, 30));
        double was = o.VolumeCm3;

        o.Rotation = new Vector3(15, 30, 45);

        Assert.Equal(was, o.VolumeCm3, 4);
    }

    /// <summary>
    /// The bounds are still the real bounds. They are walked now rather than taken from a
    /// transformed copy of the mesh, and the corners of the local box would not do: turned, they
    /// bound something larger than the geometry, and Drop to plate would leave a gap.
    /// </summary>
    [Fact]
    public void TheBoundsAreStillTheGeometrysOwn()
    {
        var o = new SceneObject("box", Primitives.Box(20, 20, 20));
        o.Rotation = new Vector3(0, 0, 45);

        var box = o.WorldBounds;

        // A square turned by 45 degrees is as wide as its diagonal, and no wider.
        Assert.Equal(20f * MathF.Sqrt(2f), box.Size.X, 2);
        Assert.Equal(20f, box.Size.Z, 2);
    }

    [Fact]
    public void TheBoundsMoveWithTheObject()
    {
        var o = new SceneObject("box", Primitives.Box(10, 10, 10));
        o.PositionX = 25f;

        Assert.Equal(20f, o.WorldBounds.Min.X, 3);
        Assert.Equal(30f, o.WorldBounds.Max.X, 3);
    }
}
