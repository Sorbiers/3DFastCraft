using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Repeating the selection along a line.
///
/// The case it was written for is a staircase: a dozen boxes, each one tread further along and
/// one riser taller than the last. That used to be a hundred numbers typed in by hand, and the
/// house build got two of them wrong without noticing.
/// </summary>
public class RepeatArrayTests
{
    private static SceneObject Box(string name, float sx, float sy, float sz) =>
        new(name, Primitives.Box(sx, sy, sz));

    private static List<SceneObject> Made(SceneObject source, RepeatSettings settings)
    {
        int n = 0;
        return RepeatArray.Make([source], settings, name => $"{name} {++n}");
    }

    [Fact]
    public void ARowOfCopiesStepsAlongTheLine()
    {
        var made = Made(Box("dowel", 2.6f, 2.6f, 3f),
            new RepeatSettings(3, new Vector3(20, 0, 0), Vector3.Zero, false));

        Assert.Equal(3, made.Count);
        Assert.Equal(20f, made[0].PositionX, 3);
        Assert.Equal(40f, made[1].PositionX, 3);
        Assert.Equal(60f, made[2].PositionX, 3);

        // Nothing was asked to change size, so nothing did.
        Assert.All(made, m => Assert.Equal(2.6f, m.SizeX, 3));
    }

    /// <summary>The names have to be ones the scene will take, or the copies collide.</summary>
    [Fact]
    public void EveryCopyIsNamedThroughTheSceneItJoins()
    {
        var made = Made(Box("step", 10, 10, 10),
            new RepeatSettings(4, new Vector3(0, 5, 0), Vector3.Zero, false));

        Assert.Equal(4, made.Select(m => m.Name).Distinct().Count());
    }

    /// <summary>
    /// A flight of steps: one tread along and one riser taller each time, measured from the face
    /// so they stack rather than leaving half the growth as a gap.
    /// </summary>
    [Fact]
    public void AFlightOfStepsComesOutAsAFlightOfSteps()
    {
        var first = Box("step", 14f, 2.9f, 2.04f);
        first.Position = new Vector3(0, 0, 1.02f);   // standing on z = 0

        var made = Made(first, new RepeatSettings(
            11, new Vector3(0, 2.9f, 0), new Vector3(0, 0, 2.04f), FromTheFace: true));

        Assert.Equal(11, made.Count);

        for (int i = 0; i < made.Count; i++)
        {
            var step = made[i];
            int n = i + 2;   // the original is step one

            // Each tread starts where the last one ended.
            Assert.Equal((n - 1) * 2.9f, step.PositionY, 2);

            // And each is one riser taller, still standing on z = 0.
            Assert.Equal(n * 2.04f, step.SizeZ, 2);
            Assert.Equal(0f, step.PositionZ - step.SizeZ / 2f, 2);
        }
    }

    /// <summary>Stepped by the middle instead, the growth opens a gap - which is sometimes wanted.</summary>
    [Fact]
    public void SteppedByTheMiddleTheGrowthIsCentred()
    {
        var made = Made(Box("plate", 10, 10, 2),
            new RepeatSettings(2, new Vector3(0, 0, 10), new Vector3(0, 0, 2), FromTheFace: false));

        Assert.Equal(10f, made[0].PositionZ, 3);
        Assert.Equal(4f, made[0].SizeZ, 3);
    }

    [Fact]
    public void NothingSillyGetsThrough()
    {
        Assert.Empty(Made(Box("a", 1, 1, 1), new RepeatSettings(0, Vector3.One, Vector3.Zero, false)));
        Assert.Empty(RepeatArray.Make([], new RepeatSettings(5, Vector3.One, Vector3.Zero, false), n => n));

        // A copy shrunk past nothing is held at a hair rather than turning inside out.
        var shrunk = Made(Box("a", 4, 4, 4),
            new RepeatSettings(3, new Vector3(5, 0, 0), new Vector3(-2, 0, 0), false));

        Assert.All(shrunk, m => Assert.True(m.SizeX > 0));
    }
}
