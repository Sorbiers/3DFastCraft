using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

public class RoundSurfaceTests
{
    private static Mesh Moved(Mesh mesh, Matrix4x4 m) => MeshTransform.Transformed(mesh, m);

    [Fact]
    public void APinsSideGivesItsAxisAndDiameterWhereverTheFacetClickedIs()
    {
        var pin = Moved(Primitives.Prism(2f, 20f, 32), Matrix4x4.CreateTranslation(7, -3, 10));

        foreach (float turn in new[] { 0.1f, 1.3f, 4f })
        {
            var at = new Vector3(7 + 2 * MathF.Cos(turn) * 0.99f, -3 + 2 * MathF.Sin(turn) * 0.99f, 12);
            var round = RoundSurface.Find(pin, at, new Vector3(MathF.Cos(turn), MathF.Sin(turn), 0));

            Assert.NotNull(round);
            Assert.False(round!.IsHole);
            Assert.Equal(1f, round.Axis.Z, 3);
            Assert.Equal(7f, round.Centre.X, 2);
            Assert.Equal(-3f, round.Centre.Y, 2);
            Assert.Equal(10f, round.Centre.Z, 2);
            Assert.InRange(round.Diameter, 3.95f, 4.01f);
        }
    }

    [Fact]
    public void AHoleStraightThroughIsFoundByItsWallAndKnownForAHole()
    {
        var block = Moved(Primitives.Box(30, 30, 10), Matrix4x4.CreateTranslation(0, 0, 5));
        var drill = Moved(Primitives.Prism(3f, 20f, 48), Matrix4x4.CreateTranslation(5, 4, 5));
        var drilled = ManifoldCsg.Subtract(block, drill)!;

        var round = RoundSurface.Find(drilled, new Vector3(8f, 4f, 5f), new Vector3(-1, 0, 0));

        Assert.NotNull(round);
        Assert.True(round!.IsHole);
        Assert.Equal(1f, MathF.Abs(round.Axis.Z), 3);
        Assert.Equal(5f, round.Centre.X, 2);
        Assert.Equal(4f, round.Centre.Y, 2);
        Assert.InRange(round.Diameter, 5.9f, 6.01f);
    }

    [Fact]
    public void ATiltedPinGivesATiltedAxis()
    {
        var turn = Matrix4x4.CreateRotationX(0.5f);
        var pin = Moved(Primitives.Prism(2f, 20f, 32), turn);
        var side = Vector3.Transform(new Vector3(1.99f, 0, 3), turn);

        var round = RoundSurface.Find(pin, side, Vector3.TransformNormal(Vector3.UnitX, turn));

        Assert.NotNull(round);
        var expected = Vector3.TransformNormal(Vector3.UnitZ, turn);
        Assert.Equal(1f, MathF.Abs(Vector3.Dot(round!.Axis, expected)), 3);
        Assert.InRange(round.Centre.Length(), 0f, 0.02f);
    }

    [Fact]
    public void AFlatFaceIsNotRound()
    {
        var box = Primitives.Box(10, 10, 10);
        Assert.Null(RoundSurface.Find(box, new Vector3(5, 1, 1), Vector3.UnitX));

        var pin = Primitives.Prism(2f, 20f, 32);
        Assert.Null(RoundSurface.Find(pin, new Vector3(0.5f, 0.5f, 10), Vector3.UnitZ));
    }
}
