using System.Diagnostics;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// A boolean done only where the tool can reach.
///
/// The engine is quadratic in the triangle count and nearly all of that is building a tree over
/// the solid, which is wasted on a pour hole that could not touch nine tenths of it. The answer
/// has to be the same as the whole-solid boolean would give, and it has to come back closed.
/// </summary>
public class LocalCsgTests(ITestOutputHelper output)
{
    private static Mesh Bore(Vector3 at, float side, float length) =>
        MeshTransform.Transformed(
            Primitives.Box(side, side, length), Matrix4x4.CreateTranslation(at));

    [Fact]
    public void ABoreThroughABlockLeavesItClosed()
    {
        var block = Primitives.Box(60, 60, 20);
        var bore = Bore(new Vector3(10, 5, 0), 6, 40);

        var holed = LocalCsg.Subtract(block, bore);
        var health = holed.CheckHealth();

        Assert.True(health.IsWatertight, health.Describe());
    }

    /// <summary>
    /// The same answer as the whole-solid boolean, to the volume the bore takes out.
    /// </summary>
    [Fact]
    public void ItTakesAwayWhatTheWholeBooleanWouldHave()
    {
        var block = Primitives.Box(60, 60, 20);
        var bore = Bore(new Vector3(10, 5, 0), 6, 40);

        double whole = Math.Abs(CsgSolid.Subtract(block, bore).ComputeSignedVolume());
        double local = Math.Abs(LocalCsg.Subtract(block, bore).ComputeSignedVolume());

        Assert.Equal(whole, local, 1);
    }

    /// <summary>A tool that misses the solid changes nothing and loses nothing.</summary>
    [Fact]
    public void AToolThatMissesLeavesTheSolidAlone()
    {
        var block = Primitives.Box(20, 20, 20);
        var bore = Bore(new Vector3(200, 0, 0), 6, 10);

        var after = LocalCsg.Subtract(block, bore);

        Assert.Equal(block.TriangleCount, after.TriangleCount);
    }

    /// <summary>
    /// The point of the exercise: the cost follows the tool, not the solid. A bore through a
    /// dense shell should cost about what it costs through a coarse one.
    /// </summary>
    [Fact]
    public void TheCostFollowsTheToolRatherThanTheSolid()
    {
        var bore = Bore(new Vector3(0, 0, 30), 5, 40);

        long Time(int segments, int rings)
        {
            var ball = Primitives.Sphere(20, segments, rings);
            LocalCsg.Subtract(ball, bore); // warm

            var clock = Stopwatch.StartNew();
            var holed = LocalCsg.Subtract(ball, bore);
            clock.Stop();

            output.WriteLine($"{ball.TriangleCount,7:N0} triangles: {clock.ElapsedMilliseconds,6:N0} ms, "
                + holed.CheckHealth().Describe());
            return clock.ElapsedMilliseconds;
        }

        Time(32, 16);
        long dense = Time(180, 90);

        // Whole-solid, 32,400 triangles would be minutes. Anything in seconds is the point made.
        Assert.True(dense < 4000, $"a bore through 32,400 triangles took {dense} ms");
    }
}
