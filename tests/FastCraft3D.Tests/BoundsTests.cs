using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Bounds accumulate by folding unions over a starting empty box, so "empty" has to actually be
/// empty. When it was not, every selection stretched from the origin to the geometry, which put
/// the manipulator handles halfway to the plate centre and blew up the rotation rings.
/// </summary>
public class BoundsTests
{
    [Fact]
    public void TheEmptyBoxIsEmpty()
    {
        Assert.True(Bounds.Empty.IsEmpty);
    }

    /// <summary>C# zeroes structs; the default value must land on empty, not on a box at the origin.</summary>
    [Fact]
    public void TheDefaultValueIsAlsoEmpty()
    {
        Bounds uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }

    [Fact]
    public void UnionWithEmptyKeepsTheOtherBoxExactly()
    {
        var box = new Bounds(new Vector3(50, 50, 0), new Vector3(70, 70, 20));

        var fromEmpty = Bounds.Empty.Union(box);
        var withEmpty = box.Union(Bounds.Empty);

        Assert.Equal(box.Min, fromEmpty.Min);
        Assert.Equal(box.Max, fromEmpty.Max);
        Assert.Equal(box.Min, withEmpty.Min);
        Assert.Equal(box.Max, withEmpty.Max);
    }

    /// <summary>The exact failure: accumulating from empty must not drag the origin in.</summary>
    [Fact]
    public void AccumulatingFromEmptyDoesNotIncludeTheOrigin()
    {
        var far = new Bounds(new Vector3(50, 50, 0), new Vector3(70, 70, 20));

        var accumulated = Bounds.Empty.Union(far);

        Assert.Equal(new Vector3(60, 60, 10), accumulated.Center);
        Assert.Equal(new Vector3(20, 20, 20), accumulated.Size);
    }

    [Fact]
    public void UnionOfTwoBoxesSpansBoth()
    {
        var a = new Bounds(new Vector3(0, 0, 0), new Vector3(10, 10, 10));
        var b = new Bounds(new Vector3(-5, 2, 4), new Vector3(6, 20, 8));

        var union = a.Union(b);

        Assert.Equal(new Vector3(-5, 0, 0), union.Min);
        Assert.Equal(new Vector3(10, 20, 10), union.Max);
    }

    [Fact]
    public void AnEmptyBoxHasNoSize()
    {
        Assert.Equal(Vector3.Zero, Bounds.Empty.Size);
        Assert.Equal(0f, Bounds.Empty.Diagonal);
    }

    [Fact]
    public void PointsWithNoMembersGiveAnEmptyBox()
    {
        Assert.True(Bounds.FromPoints([]).IsEmpty);
    }

    /// <summary>
    /// The scene-level consequence, which is what the manipulator reads. An object away from
    /// the origin must report its own centre, not a point halfway back to the plate centre.
    /// </summary>
    [Fact]
    public void SelectionBoundsOfAnOffCentreObjectAreCentredOnThatObject()
    {
        var scene = new Scene();
        var o = new SceneObject("Cube", Primitives.Box(20, 20, 20))
        {
            Position = new Vector3(60, 40, 10),
            IsSelected = true
        };
        scene.Objects.Add(o);

        var bounds = Bounds.Empty;
        foreach (var selected in scene.Selection)
            bounds = bounds.Union(selected.WorldBounds);

        Assert.Equal(60f, bounds.Center.X, 3);
        Assert.Equal(40f, bounds.Center.Y, 3);
        Assert.Equal(10f, bounds.Center.Z, 3);
        Assert.Equal(20f, bounds.Size.X, 3);
    }

    [Fact]
    public void SceneBoundsSpanOnlyTheObjectsPresent()
    {
        var scene = new Scene();
        scene.Objects.Add(new SceneObject("A", Primitives.Box(10, 10, 10))
        {
            Position = new Vector3(80, 0, 5)
        });

        var bounds = scene.ComputeBounds();

        Assert.Equal(80f, bounds.Center.X, 3);
        Assert.Equal(10f, bounds.Size.X, 3);
    }
}
