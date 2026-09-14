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
///
/// These call the BSP route directly. The ordinary route asks Manifold first and seldom gets here,
/// but this is still what runs on a PC Manifold cannot load on.
/// </summary>
public class LocalCsgTests(ITestOutputHelper output)
{
    private static Mesh Bore(Vector3 at, float side, float length) =>
        MeshTransform.Transformed(
            Primitives.Box(side, side, length), Matrix4x4.CreateTranslation(at));

    /// <summary>
    /// The surface nothing reaches comes back as the same triangles, not only the same shape.
    ///
    /// Cutting along the six walls of the working box sliced the whole solid, since a plane has no
    /// edges: one 1 mm peg socket near the corner of a rounded box moved 279 corners more than 5 mm
    /// away from it, and every rounded edge came back striped under the light.
    /// </summary>
    [Fact]
    public void ACutMovesNoCornerOfTheSurfaceAwayFromItself()
    {
        var box = RoundedPrimitives.RoundedBox(20, 20, 20, 3f, RoundEdges.All, 45);
        var (top, _) = PlaneSplit.Split(box, Axis.Z, 0f, SplitKeep.Both);

        var socket = MeshTransform.Transformed(Primitives.Prism(0.5f, 6f, 32), Matrix4x4.CreateTranslation(7.9f, 7.9f, 0f));
        var cut = LocalCsg.SubtractByBsp(top!, socket);

        Assert.True(cut.CheckHealth().IsWatertight, "the socket left the half open");

        // Above the working box, the only corners allowed to be new are on triangles that reach down
        // into it - so within its width of the socket. Before, they ran right round the part.
        var before = new HashSet<Vector3>(top!.Positions.Where(p => p.Z > 5f));
        var moved = cut.Positions.Where(p => p.Z > 5f && !before.Contains(p)).ToList();
        float farthest = moved.Count == 0 ? 0f : moved.Max(p => MathF.Max(MathF.Abs(p.X - 7.9f), MathF.Abs(p.Y - 7.9f)));

        output.WriteLine($"new corners above 5 mm: {moved.Count}, the farthest {farthest:0.###} mm across from the socket");
        Assert.True(farthest <= 0.5f + 1.5f + 1e-3f, $"a corner {farthest} mm from the socket was moved");
        Assert.True(moved.Count < 20, $"{moved.Count} corners above the socket were moved");
    }

    [Fact]
    public void ABoreThroughABlockLeavesItClosed()
    {
        var block = Primitives.Box(60, 60, 20);
        var bore = Bore(new Vector3(10, 5, 0), 6, 40);

        var holed = LocalCsg.SubtractByBsp(block, bore);
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
        double local = Math.Abs(LocalCsg.SubtractByBsp(block, bore).ComputeSignedVolume());

        Assert.Equal(whole, local, 1);
    }

    /// <summary>A tool that misses the solid changes nothing and loses nothing.</summary>
    [Fact]
    public void AToolThatMissesLeavesTheSolidAlone()
    {
        var block = Primitives.Box(20, 20, 20);
        var bore = Bore(new Vector3(200, 0, 0), 6, 10);

        var after = LocalCsg.SubtractByBsp(block, bore);

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
            LocalCsg.SubtractByBsp(ball, bore); // warm

            var clock = Stopwatch.StartNew();
            var holed = LocalCsg.SubtractByBsp(ball, bore);
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
