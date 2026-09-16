using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Engrave's studs and brick underside: the 8 mm grid of the common building bricks.
/// </summary>
public class BrickStudsTests
{
    private static double Volume(Mesh mesh)
    {
        double v = 0;
        for (int i = 0; i < mesh.Indices.Count; i += 3)
        {
            var a = mesh.Positions[mesh.Indices[i]];
            var b = mesh.Positions[mesh.Indices[i + 1]];
            var c = mesh.Positions[mesh.Indices[i + 2]];
            v += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        return v;
    }

    /// <summary>A brick body n by m studs, 9.6 mm tall, sitting on the plate.</summary>
    private static Mesh Brick(int across, int along) =>
        MeshTransform.Transformed(
            Primitives.Box(across * 8f - 0.2f, along * 8f - 0.2f, 9.6f),
            Matrix4x4.CreateTranslation(0, 0, 4.8f));

    private static EngraveOptions Studs(float fit = 0f) =>
        EngraveOptions.Default with { Kind = PatternKind.Studs, StudFit = fit };

    private static EngraveOptions Underside(float depth = BrickStuds.BrickHollow, float fit = 0f) =>
        EngraveOptions.Default with { Kind = PatternKind.StudUnderside, Depth = depth, StudFit = fit };

    private static double Cylinder(float radius, float height) => Math.PI * radius * radius * height;

    [Fact]
    public void TheGripSizesComeOutAtThePublishedOnes()
    {
        Assert.Equal(6.51f, 2 * BrickStuds.TubeRadius(0f), 0.01f);
        Assert.Equal(3.2f, 2 * BrickStuds.RodRadius(0f), 0.01f);
        Assert.Equal(1.5f, BrickStuds.Wall(0f), 0.01f);
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(1, 1)]
    [InlineData(1, 6)]
    public void AFaceTakesAStudForEveryEightMillimetres(int across, int along)
    {
        var brick = Brick(across, along);
        var top = FacePatch.Find(brick, new Vector3(0, 0, 9.6f), Vector3.UnitZ)!;

        var sorted = new[] { across, along }.Order().ToArray();
        var counted = new[] { BrickStuds.Count(top).Across, BrickStuds.Count(top).Along }.Order().ToArray();
        Assert.Equal(sorted, counted);
    }

    [Fact]
    public void StudsStandOnTheTopOfATwoByFourAndAddTheirOwnVolume()
    {
        var brick = Brick(2, 4);
        var top = FacePatch.Find(brick, new Vector3(0, 0, 9.6f), Vector3.UnitZ)!;

        var result = BrickStuds.Apply(brick, top, Studs());

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.Equal(8, result.Grooves);
        Assert.Equal(9.6f + BrickStuds.StudHeight, result.Mesh.ComputeBounds().Max.Z, 0.01f);

        // A 48-sided stud is a hair smaller than a true cylinder.
        double added = Volume(result.Mesh) - Volume(brick);
        double studs = 8 * Cylinder(2.4f, BrickStuds.StudHeight);
        Assert.Equal(studs, added, studs * 0.01);
    }

    [Fact]
    public void TheUndersideOfATwoByFourIsHollowWithThreeTubes()
    {
        var brick = Brick(2, 4);
        var bottom = FacePatch.Find(brick, new Vector3(0, 0, 0), -Vector3.UnitZ)!;

        var result = BrickStuds.Apply(brick, bottom, Underside());

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.Equal(3, result.Grooves);

        const float depth = BrickStuds.BrickHollow;
        double hollow = (15.8 - 3.0) * (31.8 - 3.0) * depth;
        double tubes = 3 * (Cylinder(3.255f, depth) - Cylinder(2.4f, depth));
        double expected = Volume(brick) - hollow + tubes;

        Assert.Equal(expected, Volume(result.Mesh), expected * 0.01);
    }

    [Fact]
    public void AOneByFourGetsRodsRatherThanTubes()
    {
        var brick = Brick(1, 4);
        var bottom = FacePatch.Find(brick, new Vector3(0, 0, 0), -Vector3.UnitZ)!;

        var supports = BrickStuds.Supports(bottom, Underside());
        var result = BrickStuds.Apply(brick, bottom, Underside());

        Assert.Equal(3, supports.Count);
        Assert.All(supports, s => Assert.Equal(0f, s.Inner));
        Assert.True(result.IsPrintable, result.Health.Describe());
    }

    [Fact]
    public void AFaceTooSmallForAStudIsLeftAlone()
    {
        // Smaller than a stud: a 5 mm square does take one, since a 4.8 mm circle fits inside it.
        var chip = MeshTransform.Transformed(Primitives.Box(4, 4, 3), Matrix4x4.CreateTranslation(0, 0, 1.5f));
        var top = FacePatch.Find(chip, new Vector3(0, 0, 3), Vector3.UnitZ)!;

        var result = BrickStuds.Apply(chip, top, Studs());

        Assert.Equal(0, result.Grooves);
        Assert.Same(chip, result.Mesh);
    }

    [Fact]
    public void FitMakesStudsFatterAndTheUndersideTighter()
    {
        var brick = Brick(2, 2);
        var top = FacePatch.Find(brick, new Vector3(0, 0, 9.6f), Vector3.UnitZ)!;

        double standard = Volume(BrickStuds.Apply(brick, top, Studs()).Mesh);
        double fatter = Volume(BrickStuds.Apply(brick, top, Studs(0.2f)).Mesh);
        Assert.True(fatter > standard);

        Assert.True(BrickStuds.TubeRadius(0.2f) > BrickStuds.TubeRadius(0f));
        Assert.True(BrickStuds.Wall(0.2f) > BrickStuds.Wall(0f));
    }

    /// <summary>The top of a round post: no rectangle anywhere, and the studs follow the circle.</summary>
    private static (Mesh Post, FacePatch Top) Post(float radius = 20f)
    {
        var post = MeshTransform.Transformed(Primitives.Prism(radius, 12f, 96), Matrix4x4.CreateTranslation(0, 0, 6f));
        return (post, FacePatch.Find(post, new Vector3(0, 0, 12f), Vector3.UnitZ)!);
    }

    [Fact]
    public void StudsFillARoundFaceWhereverAWholeStudFits()
    {
        var (post, top) = Post();
        var outline = FaceOutline.Of(top);
        var centres = BrickStuds.StudCentres(top, Studs());

        Assert.NotEmpty(centres);
        Assert.All(centres, c => Assert.True(outline.Holds(c, BrickStuds.StudRadius(0f)), $"stud at {c} runs off the face"));

        // Every grid point with room for a stud has one: nothing inside is left out. A 40 mm face
        // spans five studs' worth, an odd number, so the grid has a stud in the very middle.
        var middle = (top.Min + top.Max) * 0.5f;
        int room = 0;
        for (int i = -4; i <= 4; i++)
            for (int j = -4; j <= 4; j++)
            {
                var c = middle + new Vector2(i * 8f, j * 8f);
                if (outline.Holds(c, BrickStuds.StudRadius(0f) + 0.1f)) room++;
            }
        Assert.Equal(13, room);
        Assert.True(centres.Count >= room, $"{centres.Count} studs where {room} fit");

        var result = BrickStuds.Apply(post, top, Studs());
        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.Equal(centres.Count, result.Grooves);
    }

    /// <summary>
    /// A brick's hollow is set by its studs and not by its outline: the cells they stand in,
    /// brought in by a wall and joined up between neighbours, and no further out than the face.
    /// </summary>
    [Fact]
    public void ARoundUndersideIsHollowedOnTheStudGridWithTubesInside()
    {
        var (post, _) = Post();
        var bottom = FacePatch.Find(post, new Vector3(0, 0, 0), -Vector3.UnitZ)!;
        const float depth = 8f;

        var result = BrickStuds.Apply(post, bottom, Underside(depth));

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.True(result.Grooves > 0, "no tube fitted in a 40 mm hollow");

        // The cells, and the cavity measured by sampling them: in a cell brought in by a wall, or
        // in the run between two neighbouring cells, and never nearer the face's edge than a wall.
        float wall = BrickStuds.Wall(0f);
        var outline = FaceOutline.Of(bottom);
        var middle = (bottom.Min + bottom.Max) * 0.5f;
        var cells = new List<Vector2>();
        for (int i = -4; i <= 4; i++)
            for (int j = -4; j <= 4; j++)
            {
                var c = middle + new Vector2(i * BrickStuds.Pitch, j * BrickStuds.Pitch);
                if (outline.Contains(c)) cells.Add(c);
            }

        Assert.NotEmpty(cells);

        const float step = 0.1f;
        double area = 0;
        for (float u = bottom.Min.X; u <= bottom.Max.X; u += step)
            for (float v = bottom.Min.Y; v <= bottom.Max.Y; v += step)
            {
                var p = new Vector2(u, v);
                if (!outline.Contains(p) || outline.DistanceToEdge(p) < wall) continue;
                if (Hollowed(p)) area += step * step;
            }

        double tubes = result.Grooves * (Cylinder(BrickStuds.TubeRadius(0f), depth) - Cylinder(2.4f, depth));
        double expected = Volume(post) - area * depth + tubes;

        Assert.Equal(expected, Volume(result.Mesh), expected * 0.02);

        // Nothing broke through the side: the outside is as wide as it was.
        Assert.Equal(40f, result.Mesh.ComputeBounds().Size.X, 0.05f);

        bool Hollowed(Vector2 p)
        {
            float half = BrickStuds.Pitch / 2f;
            foreach (var c in cells)
            {
                var away = Vector2.Abs(p - c);
                if (away.X <= half - wall && away.Y <= half - wall) return true;

                foreach (var stepTo in new[] { new Vector2(BrickStuds.Pitch, 0f), new Vector2(0f, BrickStuds.Pitch) })
                {
                    if (!cells.Any(o => Vector2.DistanceSquared(o, c + stepTo) < 0.01f)) continue;

                    var fromRun = Vector2.Abs(p - (c + stepTo * 0.5f));
                    var run = stepTo.X > 0
                        ? new Vector2(half, half - wall)
                        : new Vector2(half - wall, half);
                    if (fromRun.X <= run.X && fromRun.Y <= run.Y) return true;
                }
            }

            return false;
        }
    }

    [Fact]
    public void EngraveSendsStudsToTheirOwnBuilder()
    {
        var brick = Brick(2, 2);
        var top = FacePatch.Find(brick, new Vector3(0, 0, 9.6f), Vector3.UnitZ)!;

        var result = Engraver.Engrave(brick, top, Studs());

        Assert.True(result.IsPrintable, result.Health.Describe());
        Assert.Equal(4, result.Grooves);
        Assert.Equal(4, Engraver.Pattern(top, Studs()).Ribbons.Count);
    }
}
