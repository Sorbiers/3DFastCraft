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

    private static Bounds Bed(float width, float depth, float height) =>
        new(new Vector3(-width / 2f, -depth / 2f, 0f), new Vector3(width / 2f, depth / 2f, height));

    /// <summary>Applies the offsets, as the command does, and reports the resulting bounds.</summary>
    private static List<Bounds> Apply(List<SceneObject> objects, Axis axis, AlignMode mode, Bounds? bed = null)
    {
        var offsets = AlignTools.Offsets(objects, axis, mode, bed);
        for (int i = 0; i < objects.Count; i++) objects[i].Position += offsets[i];
        return objects.Select(o => o.WorldBounds).ToList();
    }

    /// <summary>
    /// The anchor - the last object handed in, the app's own convention for which of a selection
    /// stays put - is deliberately the one sitting in the middle, neither the nearest nor the
    /// furthest edge: aligning used to go to whichever object happened to be most extreme, which
    /// made the result depend on where things already stood rather than on what was picked.
    /// </summary>
    [Fact]
    public void AligningToTheMinimumMovesEveryoneToMatchTheLastObjectPicked()
    {
        var objects = new List<SceneObject>
        {
            Box("Left", 10, new Vector3(0, 0, 0)),      // spans -5..5
            Box("Right", 10, new Vector3(40, 0, 0)),    // spans 35..45
            Box("Anchor", 10, new Vector3(20, 0, 0))    // spans 15..25 - picked last, neither extreme
        };

        var result = Apply(objects, Axis.X, AlignMode.Minimum);

        Assert.Equal(15f, result[0].Min.X, 3);
        Assert.Equal(15f, result[1].Min.X, 3);
        Assert.Equal(15f, result[2].Min.X, 3); // the anchor itself, unmoved
    }

    [Fact]
    public void AligningToTheMaximumMovesEveryoneToMatchTheLastObjectPicked()
    {
        var objects = new List<SceneObject>
        {
            Box("Left", 10, new Vector3(0, 0, 0)),
            Box("Right", 10, new Vector3(40, 0, 0)),
            Box("Anchor", 10, new Vector3(20, 0, 0))    // spans 15..25, its far edge at 25
        };

        var result = Apply(objects, Axis.X, AlignMode.Maximum);

        Assert.Equal(25f, result[0].Max.X, 3);
        Assert.Equal(25f, result[1].Max.X, 3);
        Assert.Equal(25f, result[2].Max.X, 3);
    }

    [Fact]
    public void AligningToTheCentreLevelsEveryoneWithTheLastObjectPicked()
    {
        var objects = new List<SceneObject>
        {
            Box("A", 20, new Vector3(0, 0, 0)),
            Box("B", 10, new Vector3(40, 0, 0))  // the anchor, handed in last
        };

        var result = Apply(objects, Axis.X, AlignMode.Centre);

        Assert.Equal(40f, result[0].Center.X, 3);
        Assert.Equal(40f, result[1].Center.X, 3);
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
    public void ASingleObjectHasNothingToAlignAgainstWithoutABed()
    {
        var objects = new List<SceneObject> { Box("Only", 20, new Vector3(7, 3, 1)) };

        var offsets = AlignTools.Offsets(objects, Axis.X, AlignMode.Centre);

        Assert.Equal(Vector3.Zero, offsets[0]);
    }

    [Fact]
    public void ASingleObjectLinesUpWithTheBedsEdgesAndMiddleWhenOneIsGiven()
    {
        var objects = new List<SceneObject> { Box("Only", 20, new Vector3(7, 3, 50)) };
        var bed = Bed(200, 200, 200);

        Assert.Equal(-100f, Apply(objects, Axis.X, AlignMode.Minimum, bed)[0].Min.X, 3);
        Assert.Equal(100f, Apply(objects, Axis.X, AlignMode.Maximum, bed)[0].Max.X, 3);
        Assert.Equal(0f, Apply(objects, Axis.Y, AlignMode.Centre, bed)[0].Center.Y, 3);

        // The bed's own surface, not its middle height.
        Assert.Equal(0f, Apply(objects, Axis.Z, AlignMode.Minimum, bed)[0].Min.Z, 3);
    }

    [Fact]
    public void ASingleObjectCentresOnTheBedsMiddleHeightNotJustItsSurface()
    {
        var objects = new List<SceneObject> { Box("Only", 20, new Vector3(0, 0, 50)) };
        var bed = Bed(200, 200, 240);

        var result = Apply(objects, Axis.Z, AlignMode.Centre, bed);

        Assert.Equal(120f, result[0].Center.Z, 3);
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

    // --- OffsetToPoint: Align to face ---------------------------------------------------

    /// <summary>
    /// Two objects with a fixed gap between them, moved together against a target point, keep
    /// that gap - the whole group is one rigid body, not each object aligning on its own.
    /// </summary>
    [Fact]
    public void MovingAGroupToAPointKeepsItsMembersArrangedTheSame()
    {
        var a = Box("A", 10, new Vector3(0, 0, 0));   // spans -5..5
        var b = Box("B", 10, new Vector3(30, 0, 0));  // spans 25..35, 25 mm from A
        var group = a.WorldBounds.Union(b.WorldBounds);

        var offset = AlignTools.OffsetToPoint(group, new Vector3(100, 0, 0), AlignMode.Centre, null, null);
        a.Position += offset;
        b.Position += offset;

        Assert.Equal(100f, a.WorldBounds.Union(b.WorldBounds).Center.X, 3);
        Assert.Equal(30f, b.Position.X - a.Position.X, 3); // the 30 mm apart they started
    }

    [Fact]
    public void MinimumBringsTheGroupsNearEdgeToThePoint()
    {
        var a = Box("A", 10, new Vector3(0, 0, 0));
        var b = Box("B", 20, new Vector3(20, 0, 0));   // group spans -5..30
        var group = a.WorldBounds.Union(b.WorldBounds);

        var offset = AlignTools.OffsetToPoint(group, new Vector3(50, 0, 0), AlignMode.Minimum, null, null);

        Assert.Equal(50f, group.Min.X + offset.X, 3);
    }

    [Fact]
    public void MaximumBringsTheGroupsFarEdgeToThePoint()
    {
        var a = Box("A", 10, new Vector3(0, 0, 0));
        var b = Box("B", 20, new Vector3(20, 0, 0));
        var group = a.WorldBounds.Union(b.WorldBounds);

        var offset = AlignTools.OffsetToPoint(group, new Vector3(50, 0, 0), AlignMode.Maximum, null, null);

        Assert.Equal(50f, group.Max.X + offset.X, 3);
    }

    /// <summary>An axis given no mode is left exactly where it was.</summary>
    [Fact]
    public void AnAxisWithNoModeIsLeftAlone()
    {
        var box = Box("Only", 10, new Vector3(3, 7, 11));

        var offset = AlignTools.OffsetToPoint(box.WorldBounds, new Vector3(100, 100, 100), null, null, null);

        Assert.Equal(Vector3.Zero, offset);
    }

    /// <summary>Each axis reads its own mode independently, against the same target point.</summary>
    [Fact]
    public void EachAxisCanBeGivenItsOwnMode()
    {
        var box = Box("Only", 10, new Vector3(0, 0, 0)); // spans -5..5 on every axis
        var target = new Vector3(50, 60, 70);

        var offset = AlignTools.OffsetToPoint(box.WorldBounds, target, AlignMode.Minimum, null, AlignMode.Maximum);

        Assert.Equal(55f, offset.X, 3);  // Min (-5) onto 50
        Assert.Equal(0f, offset.Y, 3);   // Y left alone
        Assert.Equal(65f, offset.Z, 3);  // Max (5) onto 70
    }

    [Fact]
    public void AnEmptyGroupHasNothingToMove()
    {
        var offset = AlignTools.OffsetToPoint(Bounds.Empty, new Vector3(10, 10, 10), AlignMode.Centre, AlignMode.Centre, AlignMode.Centre);

        Assert.Equal(Vector3.Zero, offset);
    }
}
