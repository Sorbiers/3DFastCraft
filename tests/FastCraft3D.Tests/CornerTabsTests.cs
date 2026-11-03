using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Tabs on the corners that lift, gated thin at the foot, out to a frame or to pads of their own.</summary>
public class CornerTabsTests
{
    private static Mesh At(Mesh mesh, float x, float y, float z) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    private static Mesh Standing(params Vector2[] outline) => TextSolid.Extrude([new TextShape(outline, [])], 3f, 13f);

    private static void Closed(CornerTabLayout layout, float foot)
    {
        Assert.NotNull(layout.Mesh);
        var health = layout.Mesh!.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.False(health.IsInsideOut);
        Assert.Equal(foot, layout.Mesh.ComputeBounds().Min.Z, 3);
    }

    [Fact]
    public void ABoxIsHeldByATabOffEachSideOfEveryCornerOutToAFrame()
    {
        var box = At(Primitives.Box(30, 20, 10), 40, -10, 5);
        var tabs = new CornerTabs(box);

        Assert.Equal(4, tabs.Lifting.Count);
        var layout = tabs.Lay(CornerTabOptions.Default);

        Closed(layout, 0f);
        Assert.Equal((8, 4, 0), (layout.Tabs, layout.Held, layout.Blocked));

        // The frame stands the reach clear of the box all round.
        var o = CornerTabOptions.Default;
        Assert.Equal(30 + 2 * (o.Reach + o.FrameWidth), layout.Size.X, 2);
        Assert.Equal(20 + 2 * (o.Reach + o.FrameWidth), layout.Size.Y, 2);

        // Only the gates go into the box: the rest stands clear of it.
        var shared = ManifoldCsg.Intersect(layout.Mesh!, box);
        double inside = shared is null ? 0 : shared.ComputeSignedVolume();
        Assert.InRange(inside, 0.1, 8 * o.GateWidth * o.GateHeight * 0.5);
    }

    [Fact]
    public void OneTabACornerStandsOutOfItsPointAndWithoutAFrameEndsInAPad()
    {
        var tabs = new CornerTabs(Primitives.Box(20, 20, 10));
        var o = CornerTabOptions.Default with { PerCorner = TabsPerCorner.One, Frame = false };

        var layout = tabs.Lay(o);

        Closed(layout, -5f);
        Assert.Equal(4, layout.Tabs);

        // Out along the diagonal: the pad's far side is past the corner by the reach and the pad.
        float far = 10 + (o.Reach + o.Pad) / MathF.Sqrt(2) + o.Pad / 2 * (1 - 1 / MathF.Sqrt(2));
        Assert.Equal(far, layout.Mesh!.ComputeBounds().Max.X, 1);
    }

    [Fact]
    public void AnLIsHeldAtItsFiveOutsideCornersAndNotAtTheOneInside()
    {
        var tabs = new CornerTabs(Standing(new(0, 0), new(30, 0), new(30, 10), new(10, 10), new(10, 30), new(0, 30)));

        Assert.Equal(5, tabs.Corners.Count);
        Assert.Null(tabs.CornerNear(new Vector2(10, 10), 3f));

        var layout = tabs.Lay(CornerTabOptions.Default);
        Closed(layout, 3f);
        Assert.Equal((10, 5), (layout.Tabs, layout.Held));
    }

    [Fact]
    public void ARoundPartHasNoCornersAndATurnedBoxHasItsOwn()
    {
        Assert.Empty(new CornerTabs(Primitives.Create(PrimitiveKind.Cylinder, size: 30)).Corners);

        var turned = MeshTransform.Transformed(Primitives.Box(20, 20, 10), Matrix4x4.CreateRotationZ(MathF.PI / 6));
        var tabs = new CornerTabs(turned);
        Assert.Equal(4, tabs.Lifting.Count);
        Assert.NotNull(tabs.CornerNear(Vector2.Transform(new Vector2(10, 10), Matrix3x2.CreateRotation(MathF.PI / 6)), 0.5f));
        Closed(tabs.Lay(CornerTabOptions.Default), -5f);

        // Corners rounded off small still lift as sharp ones do; rounded off large they are a curve.
        static Vector2[] Rounded(float radius) => [.. new[] { (15f, 10f, 0f), (-15f, 10f, 90f), (-15f, -10f, 180f), (15f, -10f, 270f) }
            .SelectMany(c => Enumerable.Range(0, 9).Select(k =>
            {
                float a = (c.Item3 + 90f * k / 8) * MathF.PI / 180f;
                return new Vector2(c.Item1 - MathF.Sign(c.Item1) * radius + radius * MathF.Cos(a), c.Item2 - MathF.Sign(c.Item2) * radius + radius * MathF.Sin(a));
            }))];

        var small = new CornerTabs(Standing(Rounded(2f)));
        Assert.Equal(4, small.Lifting.Count);
        Assert.NotNull(small.CornerNear(new Vector2(13f + 2f * MathF.Cos(MathF.PI / 4), 8f + 2f * MathF.Sin(MathF.PI / 4)), 0.3f));
        Assert.Empty(new CornerTabs(Standing(Rounded(9f))).Lifting);
    }

    [Fact]
    public void OnlyTheCornersPickedAreHeldAndATabThatWouldRunIntoThePartIsLeftOff()
    {
        // A U: a tab off the inside of one arm, from the corner at its tip, would run into the other arm.
        var u = new CornerTabs(Standing(new(0, 0), new(30, 0), new(30, 30), new(22, 30), new(22, 8), new(8, 8), new(8, 30), new(0, 30)));
        int tip = u.CornerNear(new Vector2(22, 30), 1f)!.Value;

        var layout = u.Lay(CornerTabOptions.Default, [tip]);

        Assert.Equal((1, 1, 1), (layout.Tabs, layout.Held, layout.Blocked));
        Assert.Null(u.Lay(CornerTabOptions.Default, []).Mesh);
    }
}
