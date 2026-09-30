using FastCraft3D.Generators;
using FastCraft3D.Generators.Buildings;
using Xunit;

namespace FastCraft3D.Tests;

public class StairBalustradeTests
{
    private static readonly Stair Stair = new();

    [Theory]
    [InlineData(true, RailSide.Both)]
    [InlineData(true, RailSide.Left)]
    [InlineData(false, RailSide.Both)]
    [InlineData(false, RailSide.Right)]
    public void ASolidBalustradeIsTheStairsOwnEdgeNotAddedToItsWidth(bool solid, RailSide side)
    {
        var s = Stair.Default with { Handrail = Handrail.Solid, Side = side, Solid = solid, Width = 20, Run = 15, Rise = 10, Steps = 4 };
        var mesh = Assert.Single(Stair.Make(s, Printer.Default).Parts).Mesh;
        var b = mesh.ComputeBounds();

        Assert.True(mesh.CheckHealth().IsWatertight);
        Assert.Equal(20f, b.Size.Y, 3);
        Assert.Equal(15f, b.Size.X, 3);
        Assert.Equal(0f, b.Min.Z, 3);
    }

    [Fact]
    public void OnASolidStairTheBalustradeGoesDownToTheFloorAtTheTopEnd()
    {
        var s = Stair.Default with { Handrail = Handrail.Solid, Side = RailSide.Both, Solid = true, Width = 20, Run = 15, Rise = 10, Steps = 4 };
        var mesh = Assert.Single(Stair.Make(s, Printer.Default).Parts).Mesh;

        // The back corner of each balustrade at the floor, where its sloped foot used to hang.
        Assert.Contains(mesh.Positions, p => System.MathF.Abs(p.X - 7.5f) < 1e-3f && System.MathF.Abs(p.Y - 10f) < 1e-3f && System.MathF.Abs(p.Z) < 1e-3f);
        Assert.Contains(mesh.Positions, p => System.MathF.Abs(p.X - 7.5f) < 1e-3f && System.MathF.Abs(p.Y + 10f) < 1e-3f && System.MathF.Abs(p.Z) < 1e-3f);
    }
}
