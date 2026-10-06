using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>A hole with flat sides - a window in a wall, a hexagonal pocket - is found by one of its walls.</summary>
public class OpeningSurfaceTests
{
    private static Mesh Moved(Mesh mesh, Matrix4x4 m) => MeshTransform.Transformed(mesh, m);

    /// <summary>A wall 40 wide, 10 thick and 30 high, with a 12 by 8 opening through it, along Y.</summary>
    private static Mesh WallWithOpening(Vector3? at = null) => ManifoldCsg.Subtract(
        Primitives.Box(40, 10, 30),
        Moved(Primitives.Box(12, 20, 8), Matrix4x4.CreateTranslation(at ?? Vector3.Zero)))!;

    private static void AssertTheOpening(RoundSurface? found, Vector3 centre)
    {
        Assert.NotNull(found);
        Assert.True(found!.IsHole);
        Assert.False(found.IsRound);
        Assert.Equal(1f, MathF.Abs(found.Axis.Y), 3);
        Assert.Equal(centre.X, found.Centre.X, 2);
        Assert.Equal(centre.Y, found.Centre.Y, 2);
        Assert.Equal(centre.Z, found.Centre.Z, 2);
        Assert.Equal(12f, found.Wide, 2);
        Assert.Equal(8f, found.Narrow, 2);
        Assert.Equal(10f, found.Length, 2);
    }

    [Fact]
    public void AnyWallOfARectangularOpeningGivesTheMiddleOfTheOpening()
    {
        var at = new Vector3(5, 0, -3);
        var wall = WallWithOpening(at);

        // Clicked on each of its four walls, and not near the middle of any.
        foreach (var (point, normal) in new[]
        {
            (new Vector3(5 + 6, 1.5f, -3 + 2), new Vector3(-1, 0, 0)),
            (new Vector3(5 - 6, -2f, -3 - 1), new Vector3(1, 0, 0)),
            (new Vector3(5 + 3, 3f, -3 + 4), new Vector3(0, 0, -1)),
            (new Vector3(5 - 4, -1f, -3 - 4), new Vector3(0, 0, 1)),
        })
        {
            AssertTheOpening(RoundSurface.FindOpening(wall, point, normal), at);
        }
    }

    [Fact]
    public void ThePatchIsAllFourWallsSoTheViewportCanLightThem()
    {
        var found = RoundSurface.FindOpening(WallWithOpening(), new Vector3(6, 0, 0), new Vector3(-1, 0, 0));

        Assert.NotNull(found);
        // Four walls of ten by eight or twelve: 2 * (12 + 8) * 10 mm squared of surface.
        Assert.InRange(found!.Patch.Area, 399f, 401f);
    }

    [Fact]
    public void AnOpeningInATurnedWallIsFoundJustTheSame()
    {
        var turn = Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(20, 30, 10);
        var wall = Moved(WallWithOpening(), turn);

        var point = Vector3.Transform(new Vector3(6, 1, 1), turn);
        var normal = Vector3.TransformNormal(new Vector3(-1, 0, 0), turn);
        var found = RoundSurface.FindOpening(wall, point, normal);

        Assert.NotNull(found);
        Assert.Equal(Vector3.Transform(Vector3.Zero, turn).X, found!.Centre.X, 2);
        Assert.Equal(Vector3.Transform(Vector3.Zero, turn).Y, found.Centre.Y, 2);
        Assert.Equal(12f, found.Wide, 2);
        Assert.Equal(8f, found.Narrow, 2);
    }

    [Fact]
    public void AHexagonalPocketIsFoundByAcrossFlatsAndAcrossCorners()
    {
        var plate = Primitives.Box(40, 40, 10);
        var hexagon = Primitives.Prism(6f, 20f, 6);
        var drilled = ManifoldCsg.Subtract(plate, hexagon)!;

        // A wall of it: any triangle whose normal lies flat and whose middle is near the hole.
        (Vector3 Point, Vector3 Normal)? wall = null;
        for (int t = 0; t + 2 < drilled.Indices.Count && wall is null; t += 3)
        {
            var a = drilled.Positions[drilled.Indices[t]];
            var b = drilled.Positions[drilled.Indices[t + 1]];
            var c = drilled.Positions[drilled.Indices[t + 2]];
            var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var middle = (a + b + c) / 3f;
            if (MathF.Abs(n.Z) < 0.01f && new Vector2(middle.X, middle.Y).Length() < 6.5f) wall = (middle, n);
        }

        var found = RoundSurface.FindOpening(drilled, wall!.Value.Point, wall.Value.Normal);

        Assert.NotNull(found);
        Assert.True(found!.IsHole);
        Assert.Equal(0f, found.Centre.X, 2);
        Assert.Equal(0f, found.Centre.Y, 2);
        Assert.Equal(12f, found.Wide, 2);
        Assert.Equal(6f * MathF.Sqrt(3f), found.Narrow, 2);
    }

    /// <summary>
    /// A groove in a textured wall that runs into the opening shares a line with its wall, parallel to
    /// the axis, but it is shallow. It must not be taken for part of the opening, or the middle
    /// would be pulled along the groove.
    /// </summary>
    [Fact]
    public void AGrooveRunningIntoTheOpeningDoesNotMoveItsMiddle()
    {
        var groove = Moved(Primitives.Box(30, 1.2f, 2), Matrix4x4.CreateTranslation(-6 - 15, -5 + 0.4f, 0));
        var wall = ManifoldCsg.Subtract(WallWithOpening(), groove)!;

        var found = RoundSurface.FindOpening(wall, new Vector3(-6, 3, 2.5f), new Vector3(1, 0, 0));

        AssertTheOpening(found, Vector3.Zero);
    }

    [Fact]
    public void TheSideOfABlockIsNotAnOpening()
    {
        var block = Primitives.Box(20, 20, 20);

        Assert.Null(RoundSurface.FindOpening(block, new Vector3(10, 2, 3), Vector3.UnitX));
        Assert.Null(RoundSurface.FindOpening(block, new Vector3(1, 10, 3), Vector3.UnitY));
        Assert.Null(RoundSurface.FindOpening(block, new Vector3(1, 3, 10), Vector3.UnitZ));
    }

    [Fact]
    public void TheFaceOfAWallAroundAnOpeningIsNotAnOpening()
    {
        Assert.Null(RoundSurface.FindOpening(WallWithOpening(), new Vector3(15, -5, 10), new Vector3(0, -1, 0)));
    }

    [Fact]
    public void ASlotOpenAtOneEndIsNotAnOpening()
    {
        // Cut in from the top edge: three walls, which do not go all the way round anything.
        var slot = Moved(Primitives.Box(6, 20, 10), Matrix4x4.CreateTranslation(0, 0, 15));
        var notched = ManifoldCsg.Subtract(Primitives.Box(40, 10, 30), slot)!;

        Assert.Null(RoundSurface.FindOpening(notched, new Vector3(3, 0, 12), new Vector3(-1, 0, 0)));
    }

    [Fact]
    public void ARoundHoleIsStillARoundOneAndNotAnOpening()
    {
        var block = Primitives.Box(30, 30, 10);
        var drill = Primitives.Prism(3f, 20f, 48);
        var drilled = ManifoldCsg.Subtract(block, drill)!;

        var found = RoundSurface.Find(drilled, new Vector3(3f, 0, 0), new Vector3(-1, 0, 0));

        Assert.NotNull(found);
        Assert.True(found!.IsRound);
        Assert.Equal(found.Diameter, found.Wide, 3);
        Assert.Equal(found.Diameter, found.Narrow, 3);
    }
}
