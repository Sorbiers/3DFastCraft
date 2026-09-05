using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Aligning works on world bounding boxes, not object positions, because that is what "flush
/// left" means to someone looking at the plate: a large and a small part line up when their
/// edges agree, not their centres.
/// </summary>
public class AlignToolsTests
{
    private static SceneObject Box(string name, float size, Vector3 position) =>
        new(name, Primitives.Box(size, size, size)) { Position = position };

    /// <summary>Applies the offsets, as the command does, and reports the resulting bounds.</summary>
    private static List<Bounds> Apply(List<SceneObject> objects, Axis axis, AlignMode mode)
    {
        var offsets = AlignTools.Offsets(objects, axis, mode);
        for (int i = 0; i < objects.Count; i++) objects[i].Position += offsets[i];
        return objects.Select(o => o.WorldBounds).ToList();
    }

    [Fact]
    public void AligningToTheMinimumMakesTheNearEdgesAgree()
    {
        var objects = new List<SceneObject>
        {
            Box("Big", 20, new Vector3(0, 0, 0)),
            Box("Small", 10, new Vector3(40, 0, 0))
        };

        var result = Apply(objects, Axis.X, AlignMode.Minimum);

        // The big box spans -10..10, so both must now start at -10.
        Assert.Equal(-10f, result[0].Min.X, 3);
        Assert.Equal(-10f, result[1].Min.X, 3);
    }

    [Fact]
    public void AligningToTheMaximumMakesTheFarEdgesAgree()
    {
        var objects = new List<SceneObject>
        {
            Box("Big", 20, new Vector3(0, 0, 0)),
            Box("Small", 10, new Vector3(40, 0, 0))
        };

        var result = Apply(objects, Axis.X, AlignMode.Maximum);

        // The small box reaches 45, which is the furthest edge.
        Assert.Equal(45f, result[0].Max.X, 3);
        Assert.Equal(45f, result[1].Max.X, 3);
    }

    [Fact]
    public void AligningToTheCentreLevelsTheCentres()
    {
        var objects = new List<SceneObject>
        {
            Box("A", 20, new Vector3(0, 0, 0)),
            Box("B", 10, new Vector3(40, 0, 0))
        };

        var result = Apply(objects, Axis.X, AlignMode.Centre);

        Assert.Equal(result[0].Center.X, result[1].Center.X, 3);
    }

    /// <summary>Only the chosen axis may move.</summary>
    [Fact]
    public void AligningLeavesTheOtherAxesAlone()
    {
        var objects = new List<SceneObject>
        {
            Box("A", 20, new Vector3(0, 5, 10)),
            Box("B", 10, new Vector3(40, -7, 25))
        };

        Apply(objects, Axis.X, AlignMode.Centre);

        Assert.Equal(5f, objects[0].Position.Y, 3);
        Assert.Equal(10f, objects[0].Position.Z, 3);
        Assert.Equal(-7f, objects[1].Position.Y, 3);
        Assert.Equal(25f, objects[1].Position.Z, 3);
    }

    [Fact]
    public void AligningWorksOnAnyAxis()
    {
        var objects = new List<SceneObject>
        {
            Box("A", 20, new Vector3(0, 0, 40)),
            Box("B", 20, new Vector3(0, 0, 0))
        };

        var result = Apply(objects, Axis.Z, AlignMode.Minimum);

        Assert.Equal(-10f, result[0].Min.Z, 3);
        Assert.Equal(-10f, result[1].Min.Z, 3);
    }

    /// <summary>Gaps are equalised, not centres, so mixed sizes end up looking evenly spaced.</summary>
    [Fact]
    public void DistributingEqualisesTheGapsBetweenObjects()
    {
        var objects = new List<SceneObject>
        {
            Box("Left", 10, new Vector3(0, 0, 0)),     // spans -5..5
            Box("Fat", 30, new Vector3(20, 0, 0)),     // spans 5..35, deliberately crowded left
            Box("Right", 10, new Vector3(95, 0, 0))    // spans 90..100
        };

        var result = Apply(objects, Axis.X, AlignMode.Distribute);

        // The outermost two hold their ground.
        Assert.Equal(-5f, result[0].Min.X, 3);
        Assert.Equal(100f, result[2].Max.X, 3);

        double gapLeft = result[1].Min.X - result[0].Max.X;
        double gapRight = result[2].Min.X - result[1].Max.X;
        Assert.Equal(gapLeft, gapRight, 3);
    }

    [Fact]
    public void DistributingUsesPositionOrderNotSelectionOrder()
    {
        // Handed over out of order on purpose.
        var objects = new List<SceneObject>
        {
            Box("Middle", 10, new Vector3(20, 0, 0)),
            Box("Right", 10, new Vector3(100, 0, 0)),
            Box("Left", 10, new Vector3(0, 0, 0))
        };

        var result = Apply(objects, Axis.X, AlignMode.Distribute);

        // The ends stay put and nothing leapfrogs.
        Assert.Equal(-5f, result[2].Min.X, 3);
        Assert.Equal(105f, result[1].Max.X, 3);
        Assert.InRange(result[0].Center.X, result[2].Center.X, result[1].Center.X);
    }

    [Fact]
    public void ASingleObjectHasNothingToAlignAgainst()
    {
        var objects = new List<SceneObject> { Box("Only", 20, new Vector3(7, 3, 1)) };

        var offsets = AlignTools.Offsets(objects, Axis.X, AlignMode.Centre);

        Assert.Equal(Vector3.Zero, offsets[0]);
    }

    [Fact]
    public void TwoObjectsCannotBeDistributed()
    {
        var objects = new List<SceneObject>
        {
            Box("A", 10, new Vector3(0, 0, 0)),
            Box("B", 10, new Vector3(50, 0, 0))
        };

        var offsets = AlignTools.Offsets(objects, Axis.X, AlignMode.Distribute);

        // There is no space between them to share out, so nothing moves.
        Assert.All(offsets, o => Assert.Equal(Vector3.Zero, o));
    }

    [Fact]
    public void AligningIsIdempotent()
    {
        var objects = new List<SceneObject>
        {
            Box("A", 20, new Vector3(0, 0, 0)),
            Box("B", 10, new Vector3(40, 0, 0))
        };

        Apply(objects, Axis.X, AlignMode.Centre);
        var offsets = AlignTools.Offsets(objects, Axis.X, AlignMode.Centre);

        Assert.All(offsets, o => Assert.True(o.Length() < 1e-4f, "aligning twice moved things again"));
    }
}
