using System.Diagnostics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Stopping a long operation part-way.
///
/// Before this there was no way out of one. A boolean between two dense imports, or a rebuild at
/// a resolution typed with an extra digit, took the app away for minutes with a "Working..." in
/// the corner and nothing else - and the only way to take it back was to end the process and
/// lose the model with it.
///
/// Cancelling has to be cooperative, so what these check is that the token is actually looked at
/// inside the work rather than only on the way in.
/// </summary>
public class AbortTests
{
    /// <summary>A token that was cancelled before the work ever started.</summary>
    private static CancellationToken Stopped()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }

    private static Mesh Ball(float radius) => Primitives.Sphere(radius, 48, 24);

    [Fact]
    public void ABooleanStops() =>
        Assert.Throws<OperationCanceledException>(() => CsgSolid.Subtract(
            Primitives.Box(20, 20, 20), Primitives.Box(10, 10, 30), token: Stopped()));

    [Fact]
    public void RepairStops() =>
        Assert.Throws<OperationCanceledException>(
            () => MeshHealer.Heal(Ball(10), token: Stopped()));

    [Fact]
    public void RebuildStops() =>
        Assert.Throws<OperationCanceledException>(
            () => VoxelRebuild.Rebuild(Primitives.Box(20, 20, 20), 48, Stopped()));

    [Fact]
    public void SimplifyStops() =>
        Assert.Throws<OperationCanceledException>(
            () => MeshSimplify.To(Ball(10), 100, Stopped()));

    [Fact]
    public void HollowStops() =>
        Assert.Throws<OperationCanceledException>(() => MeshHollow.Hollow(
            Primitives.Box(30, 30, 30), 2f, 48, OpenSide.None, Stopped()));

    [Fact]
    public void SplitStops() =>
        Assert.Throws<OperationCanceledException>(() => PlaneSplit.Split(
            Primitives.Box(20, 20, 20), Axis.Z, 0f, SplitKeep.Both, Stopped()));

    /// <summary>
    /// The one that matters. A check on the way in alone would mean Abort did nothing at all
    /// until the operation it was meant to stop had finished of its own accord - which is the
    /// state this started from.
    ///
    /// Timed against itself rather than against a number of milliseconds, so it means the same
    /// thing on a slow machine as on a fast one.
    /// </summary>
    [Fact]
    public void AbortLandsWhileTheBooleanIsStillWorking()
    {
        var a = Ball(20);
        var b = Ball(18);

        var clock = Stopwatch.StartNew();
        CsgSolid.Subtract(a, b);
        var whole = clock.Elapsed;

        using var source = new CancellationTokenSource();
        source.CancelAfter(whole / 4);

        clock.Restart();
        Assert.ThrowsAny<OperationCanceledException>(() => CsgSolid.Subtract(a, b, token: source.Token));
        var stopped = clock.Elapsed;

        Assert.True(stopped < whole * 0.75,
            $"aborting took {stopped.TotalMilliseconds:0} ms of the "
            + $"{whole.TotalMilliseconds:0} ms the whole operation takes");
    }

    /// <summary>
    /// And it comes back as itself. The parallel splitter is given the token through its options
    /// precisely so that a cancel is not delivered as an AggregateException holding one of these
    /// per worker - which the caller would have reported to the user as a crash.
    /// </summary>
    [Fact]
    public void AnAbortIsNotDeliveredAsACrash()
    {
        var a = Ball(20);
        var b = Ball(18);

        var clock = Stopwatch.StartNew();
        CsgSolid.Subtract(a, b, parallel: true);
        var whole = clock.Elapsed;

        using var source = new CancellationTokenSource();
        source.CancelAfter(whole / 4);

        var thrown = Record.Exception(() => CsgSolid.Subtract(a, b, parallel: true, token: source.Token));

        Assert.IsNotType<AggregateException>(thrown);
        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    /// <summary>
    /// Why the panel can offer Abort without a warning: what was handed in is still exactly what
    /// it was. Every one of these builds a new mesh and only the caller decides to keep it.
    /// </summary>
    [Fact]
    public void WhatWasHandedInIsUntouched()
    {
        var block = Primitives.Box(20, 20, 20);
        int triangles = block.TriangleCount;
        var bounds = block.ComputeBounds();

        Assert.Throws<OperationCanceledException>(() => MeshHealer.Heal(block, token: Stopped()));

        Assert.Equal(triangles, block.TriangleCount);
        Assert.Equal(bounds.Size, block.ComputeBounds().Size);
    }

    /// <summary>Nothing was asked to stop, so nothing does - the default has to stay the old one.</summary>
    [Fact]
    public void WithoutATokenEverythingRunsToTheEnd()
    {
        var cut = CsgSolid.Subtract(Primitives.Box(20, 20, 20), Primitives.Box(10, 10, 30));

        Assert.True(cut.TriangleCount > 0);
        Assert.True(cut.CheckHealth().IsWatertight);
    }
}
