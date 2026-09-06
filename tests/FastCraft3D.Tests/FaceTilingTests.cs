using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Retiling a face around outlines that are not rectangles - a ring, a curve, a drawing.
///
/// The cell grid cannot take these: it changes state only at a rectangle's edge, so a curve
/// would need a column per sample and the grid is the product of its columns and rows. The
/// vertical decomposition splits the face at every corner instead, which for a flowing line is a
/// column per point rather than a column per point squared.
/// </summary>
public class FaceTilingTests
{
    private static FacePatch FaceOf(Mesh mesh, Vector3 normal)
    {
        var bounds = mesh.ComputeBounds();
        var at = new Vector3(
            MathF.Abs(normal.X) > 0.5f ? (normal.X > 0 ? bounds.Max.X : bounds.Min.X) : bounds.Center.X,
            MathF.Abs(normal.Y) > 0.5f ? (normal.Y > 0 ? bounds.Max.Y : bounds.Min.Y) : bounds.Center.Y,
            bounds.Center.Z);

        return FacePatch.Find(mesh, at, normal)!;
    }

    private static List<Vector2> Circle(Vector2 at, float radius, int steps = 48)
    {
        var loop = new List<Vector2>(steps);

        for (int i = 0; i < steps; i++)
        {
            float angle = i / (float)steps * MathF.Tau;
            loop.Add(at + new Vector2(radius * MathF.Cos(angle), radius * MathF.Sin(angle)));
        }

        return loop;
    }

    /// <summary>
    /// The middle of the face in its own frame. A face measures from whichever vertex seeded it,
    /// so its rectangle is nowhere near the origin and a shape placed at (0,0) would miss it.
    /// </summary>
    private static Vector2 Middle(FacePatch face) => (face.Min + face.Max) * 0.5f;

    private static Rect2 Room(FacePatch face) => new(
        face.Min.X + Engraver.RaisedInset, face.Min.Y + Engraver.RaisedInset,
        face.Max.X - Engraver.RaisedInset, face.Max.Y - Engraver.RaisedInset);

    /// <summary>Twice a loop's area, which is what the added volume has to match.</summary>
    private static float Area(List<Vector2> loop) => MathF.Abs(Polygon2.SignedArea(loop)) * 0.5f;

    [Fact]
    public void ARingComesBackWatertightWithExactlyItsOwnVolumeAdded()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        var outer = Circle(Middle(face), 15f);
        var inner = Circle(Middle(face), 8f);

        var built = FaceRelief.Apply(wall, face, Room(face), [outer, inner], 0.8f);

        Assert.NotNull(built);
        Assert.True(built!.CheckHealth().IsWatertight, built.CheckHealth().Describe());

        double added = built.ComputeSignedVolume() - wall.ComputeSignedVolume();
        Assert.Equal((Area(outer) - Area(inner)) * 0.8f, added, added * 1e-3);
    }

    /// <summary>Shapes standing apart from each other, which is the ordinary case.</summary>
    [Fact]
    public void SeveralSeparateShapesAllStandUp()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        var shapes = new List<IReadOnlyList<Vector2>>
        {
            Circle(Middle(face) + new Vector2(-18, 0), 6f),
            Circle(Middle(face), 6f, 7),
            Circle(Middle(face) + new Vector2(18, 0), 6f, 3)
        };

        var built = FaceRelief.Apply(wall, face, Room(face), shapes, 0.6f);

        Assert.NotNull(built);
        Assert.True(built!.CheckHealth().IsWatertight, built.CheckHealth().Describe());

        double added = built.ComputeSignedVolume() - wall.ComputeSignedVolume();
        double expected = shapes.Sum(s => (double)Area(s.ToList())) * 0.6f;
        Assert.Equal(expected, added, expected * 1e-3);
    }

    /// <summary>
    /// Nothing else in the model is touched. A boolean would have split the far wall with the
    /// planes of every one of these edges; retiling never looks at it.
    /// </summary>
    [Fact]
    public void TheRestOfTheModelIsLeftAlone()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        var built = FaceRelief.Apply(
            wall, face, Room(face), [Circle(Middle(face), 12f)], 0.6f)!;

        var far = FacePatch.Find(built, new Vector3(0, 4, 0), Vector3.UnitY)!;
        Assert.Equal(2, far.Triangles.Count);
    }

    /// <summary>
    /// A shape reaching past the area has nothing under its overhang, so it is handed back for
    /// the boolean rather than left standing in mid air.
    /// </summary>
    [Fact]
    public void AShapeThatRunsOffTheFaceIsDeclined()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        Assert.Null(FaceRelief.Apply(
            wall, face, Room(face), [Circle(Middle(face), 25f)], 0.6f));
    }

    /// <summary>
    /// Two shapes drawn over each other are handed back rather than guessed at.
    ///
    /// Not laziness: which loops are holes is decided by containment, and for outlines that
    /// cross, "is this loop inside that one" has no answer.
    ///
    /// The boolean it falls back to does not much like them either - it builds each shape on its
    /// own, so the walls of one end up inside the other - which is why a drawing whose shapes
    /// overlap is worth merging in the drawing program before bringing it in.
    /// </summary>
    [Fact]
    public void OverlappingShapesAreHandedBack()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        var left = Circle(Middle(face) + new Vector2(-3, 0), 8f);
        var right = Circle(Middle(face) + new Vector2(3, 0), 8f);

        Assert.Null(FaceRelief.Apply(wall, face, Room(face), [left, right], 0.6f));
    }

    /// <summary>
    /// Lettering raised on a plain flat face takes the same road, so a stamp is exact rather
    /// than lucky - and cannot dice the rest of the object on its way through.
    /// </summary>
    [Fact]
    public void RaisedLetteringOnAFlatFaceSkipsTheBoolean()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);

        var stamp = new TextShape(Circle(Vector2.Zero, 10f), [Circle(Vector2.Zero, 5f)]);
        var built = TextCutter.Apply(wall, [stamp], new PlanarSurface(face), raised: true, 0.7f);

        Assert.NotNull(built);
        Assert.True(built!.CheckHealth().IsWatertight, built.CheckHealth().Describe());

        var far = FacePatch.Find(built, new Vector3(0, 4, 0), Vector3.UnitY)!;
        Assert.Equal(2, far.Triangles.Count);
    }

    /// <summary>
    /// A bevel is sloping walls, which the retiler has no way to express - it knows two levels
    /// and a step between them. So a bevelled stamp still goes the long way round, and comes out
    /// holding less than an upright one of the same footprint.
    /// </summary>
    [Fact]
    public void ABevelledStampIsStillBuiltAsASolid()
    {
        var wall = Primitives.Box(60, 8, 40);
        var face = FaceOf(wall, -Vector3.UnitY);
        var stamp = new TextShape(Circle(Vector2.Zero, 10f), []);

        var upright = TextCutter.Apply(wall, [stamp], new PlanarSurface(face), true, 0.7f);
        var sloped = TextCutter.Apply(wall, [stamp], new PlanarSurface(face), true, 0.7f, 0.2f);

        Assert.NotNull(sloped);
        Assert.True(sloped!.CheckHealth().IsWatertight, sloped.CheckHealth().Describe());
        Assert.True(sloped.ComputeSignedVolume() < upright!.ComputeSignedVolume(),
            "the bevel took nothing off the walls");
    }
}
