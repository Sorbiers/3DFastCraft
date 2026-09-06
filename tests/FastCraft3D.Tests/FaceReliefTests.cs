using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Retiling a face around a raised pattern, instead of unioning a solid onto it.
///
/// The point of it is the fourth wall of a box. Each union splits geometry all over the model
/// with its own infinite planes, and by the fourth face the pattern has three walls' worth of
/// mismatched edges to land on. Retiling touches nothing but the face itself.
/// </summary>
public class FaceReliefTests
{
    private static readonly EngraveOptions Brick = EngraveOptions.Default with
    {
        Kind = PatternKind.Brick, Size = 5f, GrooveWidth = 0.6f, Depth = 0.6f, Raised = true
    };

    private static FacePatch FaceOf(Mesh mesh, Vector3 normal)
    {
        var bounds = mesh.ComputeBounds();
        var at = new Vector3(
            MathF.Abs(normal.X) > 0.5f ? (normal.X > 0 ? bounds.Max.X : bounds.Min.X) : bounds.Center.X,
            MathF.Abs(normal.Y) > 0.5f ? (normal.Y > 0 ? bounds.Max.Y : bounds.Min.Y) : bounds.Center.Y,
            MathF.Abs(normal.Z) > 0.5f ? (normal.Z > 0 ? bounds.Max.Z : bounds.Min.Z) : bounds.Center.Z);

        return FacePatch.Find(mesh, at, normal)!;
    }

    private static Mesh? Retile(Mesh mesh, FacePatch face, EngraveOptions options) =>
        FaceRelief.Apply(
            mesh, face, Engraver.RaisedArea(face), Engraver.Pattern(face, options), options.Depth);

    [Fact]
    public void ARetiledFaceComesBackWatertightWithoutAnyRepair()
    {
        var wall = Primitives.Box(60, 8, 40);
        var built = Retile(wall, FaceOf(wall, -Vector3.UnitY), Brick);

        Assert.NotNull(built);
        Assert.True(built!.CheckHealth().IsWatertight, built.CheckHealth().Describe());
    }

    /// <summary>The bricks stand off the face, so the model gets both bigger and heavier.</summary>
    [Fact]
    public void ThePatternStandsProudOfTheFace()
    {
        var wall = Primitives.Box(60, 8, 40);
        var built = Retile(wall, FaceOf(wall, -Vector3.UnitY), Brick)!;

        Assert.Equal(-4f - Brick.Depth, built.ComputeBounds().Min.Y, 3);
        Assert.True(built.ComputeSignedVolume() > wall.ComputeSignedVolume());
    }

    /// <summary>
    /// The material added is exactly the bricks and nothing else - the best single check that
    /// the right cells were raised and that none of them came out inside out.
    /// </summary>
    [Fact]
    public void WhatIsAddedIsExactlyTheBricks()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);
        var pieces = Engraver.Pattern(face, Brick).Rectangles;

        double expected = pieces.Sum(p => (double)p.Width * p.Height) * Brick.Depth;
        double added = Retile(wall, face, Brick)!.ComputeSignedVolume() - wall.ComputeSignedVolume();

        Assert.Equal(expected, added, expected * 1e-3);
    }

    /// <summary>
    /// Nothing outside the face moves. The five other walls of the box are carried over vertex
    /// for vertex, which is the whole reason this exists.
    /// </summary>
    [Fact]
    public void TheRestOfTheModelIsLeftAlone()
    {
        var wall = Primitives.Box(60, 8, 40);
        var built = Retile(wall, FaceOf(wall, -Vector3.UnitY), Brick)!;

        var untouched = new Vector3(30, 4, 20);
        Assert.Contains(built.Positions, p => (p - untouched).Length() < 1e-5f);

        // The far wall is still the two triangles it started as: no plane of the pattern
        // reached across the model to split it.
        var far = FacePatch.Find(built, new Vector3(0, 4, 0), Vector3.UnitY)!;
        Assert.Equal(2, far.Triangles.Count);
    }

    /// <summary>
    /// A face that is not a filled rectangle is handed back, and the boolean takes it. A
    /// pyramid's side is a triangle, so it fills half its own bounding rectangle.
    /// </summary>
    [Fact]
    public void AFaceThatIsNotARectangleIsDeclined()
    {
        var pyramid = Primitives.Pyramid(40, 40);
        var side = FacePatch.Find(
            pyramid, new Vector3(0, -40f / 3f, -20f / 3f), new Vector3(0, -2, 1))!;

        Assert.NotNull(side);

        Assert.Null(Retile(pyramid, side, Brick));
    }

    /// <summary>
    /// Grain is curves, so it cannot go through the cell grid - a flowing line would need a
    /// column per sample point and the grid is columns times rows. It takes the vertical
    /// decomposition instead, and comes back just as watertight.
    /// </summary>
    [Fact]
    public void AGrainPatternIsRetiledToo()
    {
        var wall = Primitives.Box(60, 8, 40);
        var wood = Brick with { Kind = PatternKind.Wood, Size = 4f, GrooveWidth = 0.5f };

        var built = Retile(wall, FaceOf(wall, -Vector3.UnitY), wood);

        Assert.NotNull(built);
        Assert.True(built!.CheckHealth().IsWatertight, built.CheckHealth().Describe());
        Assert.True(built.ComputeSignedVolume() > wall.ComputeSignedVolume(), "nothing was added");
    }

    /// <summary>A face of a mesh other than the one being retiled is not something to guess at.</summary>
    [Fact]
    public void AFaceFromAnotherMeshIsDeclined()
    {
        var wall = Primitives.Box(60, 8, 40);
        var other = Primitives.Box(60, 8, 40);

        Assert.Null(Retile(other, FaceOf(wall, -Vector3.UnitY), Brick));
    }

    /// <summary>
    /// A face already carrying a pattern is still a filled rectangle, so it can be retiled
    /// again - which is what makes the second, third and fourth walls work.
    /// </summary>
    [Fact]
    public void AFaceNextToAPatternedOneIsStillARectangle()
    {
        var box = Primitives.Box(60, 60, 60);
        var first = Engraver.Engrave(box, FaceOf(box, -Vector3.UnitY), Brick);

        Assert.True(first.IsPrintable, first.Health.Describe());

        var next = FaceOf(first.Mesh, -Vector3.UnitX);
        Assert.NotNull(Retile(first.Mesh, next, Brick));
    }
}
