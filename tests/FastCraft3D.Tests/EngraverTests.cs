using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The whole operation, on the shape it was built for: a wall with a pattern cut into one face.
/// </summary>
public class EngraverTests
{
    private static Mesh Wall() => Primitives.Box(100, 100, 10);

    private static FacePatch Top(Mesh mesh) =>
        FacePatch.Find(mesh, new Vector3(0, 0, mesh.ComputeBounds().Max.Z), Vector3.UnitZ)!;

    /// <summary>
    /// Works out how much of the face the stripes cover straight from the pattern's spacing,
    /// with no reference to the cutter or the boolean - so agreeing with it means something.
    /// </summary>
    private static double CoveredLength(FacePatch face, EngraveOptions options)
    {
        var area = Engraver.PatternArea(face);
        double covered = 0;

        for (int k = 0; ; k++)
        {
            float v = area.MinV + k * options.Size;
            if (v > area.MaxV) break;

            double low = Math.Max(v, face.Min.Y);
            double high = Math.Min(v + options.GrooveWidth, face.Max.Y);
            if (high > low) covered += high - low;
        }

        return covered;
    }

    [Fact]
    public void StripesRemoveTheVolumeTheirSpacingAccountsFor()
    {
        var mesh = Wall();
        var face = Top(mesh);
        var options = new EngraveOptions(PatternKind.Stripes, Size: 10, GrooveWidth: 2, Depth: 0.5f);

        var result = Engraver.Engrave(mesh, face, options);

        double removed = mesh.ComputeSignedVolume() - result.Mesh.ComputeSignedVolume();
        double expected = CoveredLength(face, options) * 100 * options.Depth;

        Assert.True(Math.Abs(removed - expected) < expected * 0.005,
            $"removed {removed:N2} mm3, expected about {expected:N2} mm3");
    }

    [Fact]
    public void TheEngravedWallIsStillPrintable()
    {
        var mesh = Wall();

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Brick, 20, 1.5f, 0.6f));

        var health = result.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(result.Mesh.ComputeSignedVolume() > 0, "the result came out inside-out");
    }

    /// <summary>Engraving only takes material away, so the part must not get bigger.</summary>
    [Fact]
    public void TheShapeDoesNotGrow()
    {
        var mesh = Wall();
        var before = mesh.ComputeBounds();

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Brick, 20, 1.5f, 0.6f));
        var after = result.Mesh.ComputeBounds();

        Assert.True(after.Min.X >= before.Min.X - 1e-3f);
        Assert.True(after.Max.X <= before.Max.X + 1e-3f);
        Assert.True(after.Max.Z <= before.Max.Z + 1e-3f, "the cutter's lift leaked into the result");
        Assert.Equal(before.Min.Z, after.Min.Z, 3);
    }

    /// <summary>The pattern belongs to the face that was clicked and nowhere else.</summary>
    [Fact]
    public void OnlyTheChosenFaceIsCut()
    {
        var mesh = Wall();

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Stripes, 8, 2, 0.5f));

        // Every vertex must either lie on a face of the original box or inside the 0.5 mm the
        // cut reaches into the top. The distinction matters: the boolean re-tessellates the
        // sides it splits, so new vertices do appear part way down them - what would be wrong
        // is a vertex floating in the middle of the material, which is material removed.
        var box = mesh.ComputeBounds();
        var strays = result.Mesh.Positions.Where(p =>
            Math.Abs(Math.Abs(p.X) - 50) > 1e-3f &&
            Math.Abs(Math.Abs(p.Y) - 50) > 1e-3f &&
            Math.Abs(p.Z - box.Min.Z) > 1e-3f &&
            p.Z < box.Max.Z - 0.5f - 1e-3f).ToList();

        Assert.True(strays.Count == 0, $"{strays.Count} vertices inside the wall, e.g. {strays.FirstOrDefault()}");
        Assert.True(result.Mesh.Positions.Count(p => Math.Abs(p.Z - box.Min.Z) < 1e-3f) >= 4);
    }

    [Fact]
    public void AVerticalWallGetsLevelCourses()
    {
        var mesh = Wall();
        var face = FacePatch.Find(mesh, new Vector3(0, -50, 0), -Vector3.UnitY)!;

        var result = Engraver.Engrave(
            mesh, face, new EngraveOptions(PatternKind.Brick, 20, 1.5f, 0.6f));

        Assert.True(result.Mesh.CheckHealth().IsWatertight);
        Assert.Equal(0f, face.U.Z, 5); // courses run level, so bed joints come out horizontal
    }

    [Fact]
    public void CuttingDeeperThanTheWallGoesRightThrough()
    {
        var mesh = Primitives.Box(60, 60, 2);

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Stripes, 10, 3, 5f));

        // Still a valid solid, just one with slots in it now.
        var health = result.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, health.Describe());
        Assert.True(result.Mesh.ComputeSignedVolume() < mesh.ComputeSignedVolume() * 0.9);
    }

    [Fact]
    public void MaterialBehindMeasuresWhatThereIsToCutInto()
    {
        var mesh = Wall();
        var side = FacePatch.Find(mesh, new Vector3(50, 0, 0), Vector3.UnitX)!;

        Assert.Equal(10f, Engraver.MaterialBehind(mesh, Top(mesh)), 3);
        Assert.Equal(100f, Engraver.MaterialBehind(mesh, side), 3);
    }


    // --- Wood, which is cut as ribbons rather than rectangles ----------------------

    /// <summary>
    /// The whole reason the grain is held apart. Two ribbons overlapping would leave a wall
    /// inside the material being removed, and the subtraction would tear the wall open.
    /// </summary>
    [Theory]
    [InlineData(3f, 0.5f)]
    [InlineData(5f, 0.7f)]
    [InlineData(9f, 1f)]
    [InlineData(16f, 1.5f)]
    [InlineData(30f, 2f)]
    public void GrainCutsAWatertightWall(float size, float width)
    {
        var mesh = Wall();

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Wood, size, width, 0.6f));

        var health = result.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, $"size {size}: {health.Describe()}");
        Assert.True(result.Mesh.ComputeSignedVolume() > 0);
    }

    [Fact]
    public void GrainRunsOnAnUprightWallAndOnASlopeToo()
    {
        var box = Wall();
        var side = FacePatch.Find(box, new Vector3(0, -50, 0), -Vector3.UnitY)!;
        var wedge = Primitives.Create(PrimitiveKind.Wedge);
        var (point, normal) = WedgeSlope(wedge);
        var slope = FacePatch.Find(wedge, point, normal);
        Assert.NotNull(slope);

        var options = new EngraveOptions(PatternKind.Wood, 4, 0.6f, 0.4f);

        var onSide = Engraver.Engrave(box, side, options);
        var onSlope = Engraver.Engrave(wedge, slope, options);

        Assert.True(onSide.Mesh.CheckHealth().IsWatertight, onSide.Mesh.CheckHealth().Describe());
        Assert.True(onSlope.Mesh.CheckHealth().IsWatertight, onSlope.Mesh.CheckHealth().Describe());
    }

    [Fact]
    public void GrainTakesMaterialAwayWithoutMakingTheShapeBigger()
    {
        var mesh = Wall();
        var before = mesh.ComputeBounds();

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Wood, 6, 0.8f, 0.5f));

        var after = result.Mesh.ComputeBounds();
        Assert.True(result.Mesh.ComputeSignedVolume() < mesh.ComputeSignedVolume());
        Assert.True(after.Max.Z <= before.Max.Z + 1e-3f, "the cutter's lift leaked into the result");
        Assert.True(after.Max.X <= before.Max.X + 1e-3f);
    }

    /// <summary>Whichever way it runs, the grain has to cut cleanly.</summary>
    [Fact]
    public void UprightGrainIsWatertightToo()
    {
        var mesh = Wall();

        var result = Engraver.Engrave(mesh, Top(mesh), new EngraveOptions(
            PatternKind.Wood, 5, 0.7f, 0.5f, PatternDirection.Vertical));

        Assert.True(result.Mesh.CheckHealth().IsWatertight, result.Mesh.CheckHealth().Describe());
    }

    /// <summary>A ribbon wound the wrong way would cut a lump instead of a groove.</summary>
    [Fact]
    public void TheGrainCutterIsWoundOutward()
    {
        var mesh = Wall();
        var face = Top(mesh);
        var options = new EngraveOptions(PatternKind.Wood, 6, 0.8f, 0.5f);

        var cutter = GrooveSolid.Build(Engraver.Grooves(face, options), face, options.Depth);

        Assert.True(cutter.ComputeSignedVolume() > 0, "negative volume means inverted ribbons");
        Assert.True(cutter.CheckHealth().IsWatertight, cutter.CheckHealth().Describe());
    }

    /// <summary>Sliding the pattern must not break it, whichever pattern it is.</summary>
    [Theory]
    [InlineData(PatternKind.Brick)]
    [InlineData(PatternKind.Wood)]
    [InlineData(PatternKind.Stripes)]
    public void AnOffsetPatternStillCutsCleanly(PatternKind kind)
    {
        var mesh = Wall();

        var result = Engraver.Engrave(mesh, Top(mesh), new EngraveOptions(
            kind, 12, 1.2f, 0.5f, OffsetU: 3.7f, OffsetV: -2.4f));

        var health = result.Mesh.CheckHealth();
        Assert.True(health.IsWatertight, $"{kind}: {health.Describe()}");
        Assert.True(result.Mesh.ComputeSignedVolume() < mesh.ComputeSignedVolume());
    }

    /// <summary>The wedge's sloping face: the one triangle tilted in both Y and Z.</summary>
    private static (Vector3 Point, Vector3 Normal) WedgeSlope(Mesh wedge)
    {
        for (int t = 0; t + 2 < wedge.Indices.Count; t += 3)
        {
            Vector3 a = wedge.Positions[wedge.Indices[t]];
            Vector3 b = wedge.Positions[wedge.Indices[t + 1]];
            Vector3 c = wedge.Positions[wedge.Indices[t + 2]];

            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-12f) continue;
            normal = Vector3.Normalize(normal);

            // Any face that is not axis-aligned is the slope, whichever way the wedge is built.
            // Taking the centroid rather than guessing a point keeps this on the face itself.
            bool tilted = Math.Abs(normal.X) < 0.98f && Math.Abs(normal.Y) < 0.98f
                                                     && Math.Abs(normal.Z) < 0.98f;
            if (tilted) return ((a + b + c) / 3f, normal);
        }

        throw new InvalidOperationException("the wedge has no sloping face");
    }

    [Fact]
    public void ADepthOfNothingLeavesTheWallAlmostAlone()
    {
        var mesh = Wall();

        var result = Engraver.Engrave(
            mesh, Top(mesh), new EngraveOptions(PatternKind.Stripes, 10, 2, 0f));

        // Sane() floors the depth rather than dividing by zero, so this is a hairline cut.
        Assert.True(result.Mesh.CheckHealth().IsWatertight);
        // Ten stripes 2 mm wide across a 100 mm face, 0.01 mm deep: about 20 mm3.
        Assert.True(mesh.ComputeSignedVolume() - result.Mesh.ComputeSignedVolume() < 30);
    }
}

/// <summary>The patterns themselves, checked as rectangles before any of it becomes geometry.</summary>
public class GroovePatternTests
{
    private static readonly Rect2 Wall = new(0, 0, 100, 60);

    [Fact]
    public void BrickCoursesAreStaggeredRatherThanStacked()
    {
        var grooves = GroovePattern.Build(
            new EngraveOptions(PatternKind.Brick, Size: 20, GrooveWidth: 1), Wall);

        // The perpend joints are the tall thin ones; two adjacent courses must not share a
        // column, or it stops being masonry and becomes tiling.
        var joints = grooves.Rectangles.Where(g => g.Width < 2).ToList();
        int columns = joints.Select(j => MathF.Round(j.MinU, 2)).Distinct().Count();

        Assert.True(joints.Count > 10, $"only {joints.Count} perpend joints");
        Assert.True(columns > 5, "the joints all line up - the courses are not offset");
    }

    [Fact]
    public void BedJointsRunTheFullWidth()
    {
        var grooves = GroovePattern.Build(
            new EngraveOptions(PatternKind.Brick, Size: 20, GrooveWidth: 1), Wall);

        Assert.Contains(grooves.Rectangles, g => g.MinU <= Wall.MinU && g.MaxU >= Wall.MaxU);
    }

    [Fact]
    public void StripesAreEvenlySpaced()
    {
        var grooves = GroovePattern.Build(
            new EngraveOptions(PatternKind.Stripes, Size: 10, GrooveWidth: 2), Wall);

        var starts = grooves.Rectangles.Select(g => g.MinV).OrderBy(v => v).ToList();
        for (int i = 1; i < starts.Count; i++)
            Assert.Equal(10f, starts[i] - starts[i - 1], 3);

        Assert.All(grooves.Rectangles, g => Assert.Equal(2f, g.Height, 3));
    }

    [Fact]
    public void TurningThePatternSwapsItsAxes()
    {
        var across = GroovePattern.Build(
            new EngraveOptions(PatternKind.Stripes, 10, 2, Direction: PatternDirection.Horizontal), Wall);
        var down = GroovePattern.Build(
            new EngraveOptions(PatternKind.Stripes, 10, 2, Direction: PatternDirection.Vertical), Wall);

        Assert.All(across.Rectangles, g => Assert.True(g.Width > g.Height));
        Assert.All(down.Rectangles, g => Assert.True(g.Height > g.Width));
    }

    /// <summary>
    /// Wood is grain, not boards. It comes back as curves, because a flowing line cannot be made
    /// out of axis-aligned rectangles at any sane cost.
    /// </summary>
    [Fact]
    public void WoodIsCutAsFlowingCurvesRatherThanRectangles()
    {
        var grooves = GroovePattern.Build(new EngraveOptions(PatternKind.Wood, 6, 0.6f), Wall);

        Assert.Empty(grooves.Rectangles);
        Assert.True(grooves.Ribbons.Count > 5, $"only {grooves.Ribbons.Count} grain lines");
        Assert.All(grooves.Ribbons, r => Assert.True(r.Points.Count > 8, "the grain is not curved"));
    }

    [Fact]
    public void TheGrainActuallyWanders()
    {
        var grooves = GroovePattern.Build(new EngraveOptions(PatternKind.Wood, 6, 0.6f), Wall);
        var line = grooves.Ribbons.First(r => !r.Closed);

        float swing = line.Points.Max(p => p.Y) - line.Points.Min(p => p.Y);

        Assert.True(swing > 1f, $"the line only moved {swing:0.##} mm - that is a stripe");
    }

    /// <summary>
    /// The guarantee the cutter rests on. Two ribbons crossing would leave a wall inside the
    /// material being removed and tear the boolean open, so the lines are held apart by
    /// construction however the waves and knots fall.
    /// </summary>
    [Theory]
    [InlineData(3f, 0.5f)]
    [InlineData(6f, 0.6f)]
    [InlineData(12f, 1.2f)]
    [InlineData(20f, 2f)]
    public void GrainLinesNeverCross(float size, float width)
    {
        var lines = GroovePattern.Build(new EngraveOptions(PatternKind.Wood, size, width), Wall)
            .Ribbons.Where(r => !r.Closed)
            .OrderBy(r => r.Points[0].Y)
            .ToList();

        for (int i = 1; i < lines.Count; i++)
        {
            for (int k = 0; k < lines[i].Points.Count; k++)
            {
                float gap = lines[i].Points[k].Y - lines[i - 1].Points[k].Y;
                Assert.True(gap >= width, $"lines {i - 1} and {i} came within {gap:0.###} mm");
            }
        }
    }

    [Fact]
    public void KnotsComeOutAsClosedRings()
    {
        var grooves = GroovePattern.Build(new EngraveOptions(PatternKind.Wood, 4, 0.5f), Wall);

        var rings = grooves.Ribbons.Where(r => r.Closed).ToList();

        Assert.True(rings.Count >= 2, $"{rings.Count} rings - the knots did not form");
        Assert.All(rings, r => Assert.True(r.Points.Count > 10));
    }

    /// <summary>Grain must look unplanned but be the same every time the pattern is applied.</summary>
    [Fact]
    public void TheSameSettingsGrowTheSameGrainTwice()
    {
        var options = new EngraveOptions(PatternKind.Wood, Size: 6, GrooveWidth: 0.6f);

        var first = GroovePattern.Build(options, Wall).Ribbons;
        var second = GroovePattern.Build(options, Wall).Ribbons;

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Points, second[i].Points);
    }

    [Fact]
    public void AnAbsurdlyFinePatternIsCappedRatherThanRunningAway()
    {
        var grooves = GroovePattern.Build(
            new EngraveOptions(PatternKind.Brick, Size: 0.5f, GrooveWidth: 0.1f),
            new Rect2(0, 0, 500, 500));

        Assert.True(grooves.Rectangles.Count <= GroovePattern.MaximumGrooves + 1);
    }

    [Fact]
    public void NonsenseSettingsAreBroughtBackIntoRange()
    {
        var sane = new EngraveOptions(PatternKind.Brick, Size: -5, GrooveWidth: 100, Depth: -1).Sane();

        Assert.True(sane.Size >= EngraveOptions.MinimumSize);
        Assert.True(sane.GrooveWidth < sane.Size);
        Assert.True(sane.Depth > 0);
    }

    [Fact]
    public void TheGrooveCountIsPredictedBeforeAnythingIsBuilt()
    {
        var options = new EngraveOptions(PatternKind.Brick, Size: 20, GrooveWidth: 1.5f);
        int actual = GroovePattern.Build(options, Wall).Count;

        int predicted = GroovePattern.Count(options, Wall);

        // An estimate for the dialog, so it only has to be the right order of magnitude.
        Assert.True(predicted >= actual * 0.5 && predicted <= actual * 2,
            $"predicted {predicted}, built {actual}");
    }

    // --- Offsetting, which is how two walls are made to meet at a corner ------------

    /// <summary>Where in its own repeat a line falls - what an offset actually changes.</summary>
    private static float Phase(float value, float pitch) => ((value % pitch) + pitch) % pitch;

    [Fact]
    public void AnOffsetSlidesTheWholePatternAlong()
    {
        var plain = new EngraveOptions(PatternKind.Stripes, Size: 10, GrooveWidth: 2);

        var original = GroovePattern.Build(plain, Wall).Rectangles;
        var moved = GroovePattern.Build(plain with { OffsetV = 3f }, Wall).Rectangles;

        // The pattern slides inside its repeat rather than gaining or losing stripes, so it is
        // the phase that moves, not the count.
        Assert.All(original, g => Assert.Equal(0f, Phase(g.MinV, 10f), 4));
        Assert.All(moved, g => Assert.Equal(3f, Phase(g.MinV, 10f), 4));
        Assert.Equal(original.Count, moved.Count);
    }

    [Fact]
    public void OffsettingByAWholeRepeatChangesNothing()
    {
        var plain = new EngraveOptions(PatternKind.Stripes, Size: 10, GrooveWidth: 2);

        var shifted = GroovePattern.Build(plain with { OffsetV = 10f }, Wall).Rectangles;

        Assert.Equal(GroovePattern.Build(plain, Wall).Rectangles, shifted);
    }

    [Fact]
    public void BrickSlidesAcrossWithoutLosingItsStagger()
    {
        var plain = new EngraveOptions(PatternKind.Brick, Size: 20, GrooveWidth: 1);

        var moved = GroovePattern.Build(plain with { OffsetU = 7f }, Wall).Rectangles;

        var joints = moved.Where(g => g.Width < 2).ToList();
        int columns = joints.Select(j => MathF.Round(j.MinU, 2)).Distinct().Count();

        Assert.True(joints.Count > 10);
        Assert.True(columns > 5, "sliding the pattern flattened the stagger");
    }

    /// <summary>The offset has to follow the pattern round when it is turned upright.</summary>
    [Fact]
    public void AnOffsetOnATurnedPatternMovesTheRightWay()
    {
        var upright = new EngraveOptions(
            PatternKind.Stripes, 10, 2, Direction: PatternDirection.Vertical);

        var original = GroovePattern.Build(upright, Wall).Rectangles;
        var moved = GroovePattern.Build(upright with { OffsetU = 3f }, Wall).Rectangles;

        // Turned upright, the stripes march across in U, so that is where the offset must land.
        Assert.All(original, g => Assert.Equal(0f, Phase(g.MinU, 10f), 4));
        Assert.All(moved, g => Assert.Equal(3f, Phase(g.MinU, 10f), 4));
    }
}
