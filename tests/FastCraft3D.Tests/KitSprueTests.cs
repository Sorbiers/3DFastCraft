using System.Numerics;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>Parts bound into a model kit's sprue: laid out in rows inside a frame, each held by gates.</summary>
public class KitSprueTests
{
    private static Mesh At(Mesh mesh, float x, float y, float z) =>
        MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(x, y, z));

    private static readonly Mesh[] Parts =
    [
        At(Primitives.Box(10, 10, 10), 0, 0, 5),
        At(Primitives.Box(24, 6, 3), 30, 0, 20),
        At(Primitives.Create(PrimitiveKind.Cylinder, size: 12), -40, 10, 6),
        At(Primitives.Box(4, 4, 2), 0, 0, 1)
    ];

    [Fact]
    public void TheSprueIsOneClosedSolidOnThePlateHoldingEveryPartFromEverySide()
    {
        var layout = new KitSprue(Parts).Lay(KitSprueOptions.Default);

        var health = layout.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.False(health.IsInsideOut);

        var b = layout.Mesh.ComputeBounds();
        Assert.Equal(0, b.Min.Z, 3);
        Assert.Equal(KitSprueOptions.Default.Height, b.Max.Z, 3);

        Assert.Empty(layout.Loose);
        Assert.Equal(4 * Parts.Length, layout.Gates);
    }

    [Fact]
    public void ThePartsStandOnThePlateApartFromEachOtherAndInsideTheFrame()
    {
        var layout = new KitSprue(Parts).Lay(KitSprueOptions.Default);
        var frame = layout.Mesh.ComputeBounds();

        var placed = Parts.Select((p, i) =>
        {
            var b = p.ComputeBounds();
            return new Bounds(b.Min + layout.Moves[i], b.Max + layout.Moves[i]);
        }).ToList();

        foreach (var p in placed)
        {
            Assert.Equal(0, p.Min.Z, 3);
            Assert.True(p.Min.X > frame.Min.X && p.Max.X < frame.Max.X && p.Min.Y > frame.Min.Y && p.Max.Y < frame.Max.Y);
        }

        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
                Assert.True(placed[i].Max.X <= placed[j].Min.X || placed[j].Max.X <= placed[i].Min.X
                         || placed[i].Max.Y <= placed[j].Min.Y || placed[j].Max.Y <= placed[i].Min.Y);
    }

    [Fact]
    public void AtTheCornersASquarePartIsHeldByEachCornerFromBothSidesAndARoundOneStillFromTheMiddle()
    {
        var corners = KitSprueOptions.Default with { Corners = true };

        Assert.Equal(8, new KitSprue([Primitives.Box(20, 12, 5)]).Lay(corners).Gates);
        Assert.Equal(4, new KitSprue([Primitives.Create(PrimitiveKind.Cylinder, size: 20, segments: 100)]).Lay(corners).Gates);

        var diamond = MeshTransform.Transformed(Primitives.Box(14, 14, 5), Matrix4x4.CreateRotationZ(MathF.PI / 4));
        Assert.Equal(4, new KitSprue([diamond]).Lay(corners).Gates);

        var all = new KitSprue(Parts).Lay(corners);
        Assert.Empty(all.Loose);
        Assert.True(all.Mesh.CheckHealth().IsWatertight);
    }

    [Fact]
    public void ANarrowSprueTakesMoreRows()
    {
        var sprue = new KitSprue(Parts);

        Assert.Equal(1, sprue.Lay(KitSprueOptions.Default).Rows);
        Assert.True(sprue.Lay(KitSprueOptions.Default with { Width = 50 }).Rows > 1);
        Assert.True(sprue.Lay(KitSprueOptions.Default with { Width = 50 }).Mesh.CheckHealth().IsWatertight);
    }
}
