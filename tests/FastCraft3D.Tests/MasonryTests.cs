using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

public class MasonryTests
{
    private const float Module = 6f, Aspect = 3f, Joint = 0.2f, Depth = 0.4f;

    /// <summary>A box 40 wide, 30 deep and 20 tall, standing on the plate round the origin.</summary>
    private static Mesh Box() =>
        MeshTransform.Transformed(Primitives.Box(40, 30, 20), Matrix4x4.CreateTranslation(0, 0, 10));

    /// <summary>The walls picked at <paramref name="pick"/>, carried on to the wall facing each of <paramref name="more"/>.</summary>
    private static WallRun Walls(Mesh part, Vector3 pick, Vector3 facing, params Vector3[] more)
    {
        var face = FacePatch.Find(part, pick, facing)!;
        var loop = WallLoop.Around(part, face, pick, out var why);
        Assert.True(loop is not null, why);

        int Wall(Vector3 at, Vector3 way) =>
            loop!.WallNear(new Vector2(at.X, at.Y), Vector2.Normalize(new Vector2(way.X, way.Y)), out _);

        var run = WallRun.Of(loop!, part, Wall(pick, facing), out why);
        foreach (var way in more)
        {
            var bounds = part.ComputeBounds();
            run = run!.With(part, Wall(bounds.Center + way * (bounds.Size / 2f), way), out why);
        }

        Assert.True(run is not null, why);
        return run!;
    }

    private static readonly Vector3[] TheOtherThree = [Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY];

    /// <summary>Where a ray first meets the mesh, or null when it never does.</summary>
    private static float? Hit(Mesh mesh, Vector3 origin, Vector3 dir)
    {
        float best = float.MaxValue;
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            var p0 = mesh.Positions[mesh.Indices[t]];
            var e1 = mesh.Positions[mesh.Indices[t + 1]] - p0;
            var e2 = mesh.Positions[mesh.Indices[t + 2]] - p0;
            var h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-12f) continue;

            float f = 1f / det;
            var s = origin - p0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0 || u > 1) continue;

            var q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(dir, q);
            if (v < 0 || u + v > 1) continue;

            float along = f * Vector3.Dot(e2, q);
            if (along > 0 && along < best) best = along;
        }

        return best == float.MaxValue ? null : best;
    }

    /// <summary>The length of a brick on a wall, fitted so a whole number go corner to corner, and the end of one.</summary>
    private static (float Brick, float Header) Fitted(float wall)
    {
        int modules = Math.Max(2, (int)MathF.Round((wall + Joint) / Module));
        float brick = (wall + Joint) / modules - Joint;
        return (brick, (brick - Joint) / 2f);
    }

    /// <summary>
    /// Round all four walls of a box, each wall a whole number of bricks and each corner one brick
    /// that shows its length on one face and its end on the other - swapping faces course by course,
    /// which is what staggers the courses by half a brick.
    /// </summary>
    [Fact]
    public void EveryCornerOfABoxIsTurnedByAWholeBrickThatSwapsFacesCourseByCourse()
    {
        var box = Box();
        var run = Walls(box, new Vector3(20, 0, 10), Vector3.UnitX, TheOtherThree);
        Assert.True(run.Closed);
        Assert.Null(BrickBond.Refusal(run));

        var bricks = BrickBond.Build(run, Module, Aspect, Joint, Depth, out int count);
        Assert.True(bricks.CheckHealth().IsWatertight, bricks.CheckHealth().Describe());

        // Ten courses of two; per course every wall's bricks between its corners - one more on the
        // walls showing ends at their corners than on those showing lengths - and four corner bricks.
        Assert.Equal(10 * (2 * (5 - 2) + 2 * (7 - 1) + 4), count);
        Assert.Equal(count, MeshComponents.Split(bricks).Count);

        // The joint just past the end of a brick: on the +X wall from the corner at y = +15, on the
        // +Y wall from the same corner at x = +20. Where the wall shows the corner brick's length
        // there is brick there; where it shows the end, the joint.
        var (_, endX) = Fitted(30f);
        var (_, endY) = Fitted(40f);
        bool? before = null;

        for (int k = 0; k < 10; k++)
        {
            float z = 1f + 2f * k;
            var onX = Hit(bricks, new Vector3(25, 15 - endX - Joint / 2f, z), -Vector3.UnitX);
            var onY = Hit(bricks, new Vector3(20 - endY - Joint / 2f, 25, z), -Vector3.UnitY);

            Assert.True(onX.HasValue ^ onY.HasValue, $"course {k}: one face shows the length, the other the end");
            if (onX is { } x) Assert.Equal(5f - Depth, x, 3);
            if (onY is { } y) Assert.Equal(10f - Depth, y, 3);

            if (before is { } was) Assert.NotEqual(was, onX.HasValue);
            before = onX.HasValue;
        }

        var built = ManifoldCsg.Union(box, TextCutter.KeptOn(bricks, new WallsSurface(run), Depth));
        Assert.True(built is { } b && b.CheckHealth().IsWatertight, built?.CheckHealth().Describe());
        Assert.Single(MeshComponents.Split(built!));
    }

    /// <summary>
    /// Two walls picked: brickwork on those two and round the corner between them, and at the far
    /// edge of each a square end of whole and half bricks by turns, stopping at the edge - the other
    /// two walls bare.
    /// </summary>
    [Fact]
    public void TwoWallsEndSquareInWholeAndHalfBricksByTurns()
    {
        var box = Box();
        var run = Walls(box, new Vector3(20, 0, 10), Vector3.UnitX, Vector3.UnitY);
        Assert.Equal(2, run.Count);

        var bricks = BrickBond.Build(run, Module, Aspect, Joint, Depth, out _);
        Assert.True(bricks.CheckHealth().IsWatertight, bricks.CheckHealth().Describe());

        var bounds = bricks.ComputeBounds();
        Assert.True(bounds.Min.Y > -15f && bounds.Min.Y < -14.9f, $"{bounds.Min.Y}");
        Assert.True(bounds.Min.X > -20f && bounds.Min.X < -19.9f, $"{bounds.Min.X}");

        // The free end of the +X wall is at y = -15: past an end, the joint; past a whole brick, brick.
        var (_, header) = Fitted(30f);
        bool? before = null;
        for (int k = 0; k < 10; k++)
        {
            bool brick = Hit(bricks, new Vector3(25, -15 + header + Joint / 2f, 1f + 2f * k), -Vector3.UnitX).HasValue;
            if (before is { } was) Assert.NotEqual(was, brick);
            before = brick;
        }

        var built = ManifoldCsg.Union(box, TextCutter.KeptOn(bricks, new WallsSurface(run), Depth));
        Assert.True(built is { } b && b.CheckHealth().IsWatertight, built?.CheckHealth().Describe());
    }

    /// <summary>
    /// An L-shaped building: five outside corners and one inside, where a whole brick of one wall
    /// runs into the corner and the other wall's end stops a joint short of it, by turns. No two
    /// bricks anywhere take up the same room.
    /// </summary>
    [Fact]
    public void AnInsideCornerInterlocksWithNoTwoBricksInTheSamePlace()
    {
        var wing = MeshTransform.Transformed(Primitives.Box(20, 40, 20), Matrix4x4.CreateTranslation(-10, 10, 10));
        var part = ManifoldCsg.Union(Box(), wing)!;

        var face = FacePatch.Find(part, new Vector3(20, 0, 10), Vector3.UnitX)!;
        var loop = WallLoop.Around(part, face, new Vector3(20, 0, 10), out var why)!;
        Assert.Equal(6, loop.Count);
        var run = WallRun.Round(loop, part, out why);
        Assert.True(run is not null, why);
        Assert.Null(BrickBond.Refusal(run!));

        var bricks = BrickBond.Build(run!, Module, Aspect, Joint, Depth, out int count);
        Assert.True(bricks.CheckHealth().IsWatertight, bricks.CheckHealth().Describe());

        var boxes = MeshComponents.Split(bricks).Select(b => b.ComputeBounds()).ToList();
        Assert.Equal(count, boxes.Count);
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
                Assert.False(Overlap(boxes[i], boxes[j]), $"bricks at {boxes[i].Center} and {boxes[j].Center} overlap");

        var built = ManifoldCsg.Union(part, TextCutter.KeptOn(bricks, new WallsSurface(run!), Depth));
        Assert.True(built is { } b && b.CheckHealth().IsWatertight, built?.CheckHealth().Describe());
    }

    /// <summary>
    /// Whether two boxes share any room. An outside corner brick's box takes in the corner of the
    /// wall behind it, where no other brick is, so boxes are as good as the bricks themselves here.
    /// </summary>
    private static bool Overlap(Bounds a, Bounds b) =>
        a.Min.X < b.Max.X - 1e-4f && b.Min.X < a.Max.X - 1e-4f &&
        a.Min.Y < b.Max.Y - 1e-4f && b.Min.Y < a.Max.Y - 1e-4f &&
        a.Min.Z < b.Max.Z - 1e-4f && b.Min.Z < a.Max.Z - 1e-4f;

    /// <summary>A stone field round the walls of a run with its quoins set in, as Masonry builds it.</summary>
    private static (SurfaceProfiles.Quoins Quoins, WallsSurface Surface) Quoined(WallRun run, TextureKind kind)
    {
        var options = new TextureOptions(kind, kind == TextureKind.Rubble ? 7f : 8f, 0.2f).Sane();
        var surface = new WallsSurface(run);
        var field = SurfaceTexture.ProfileOf(options, 0.8f, 0.4f, run.Closed ? run.Length : 0f)!;
        var lined = new LinedRelief(new SlidRelief(field, Vector2.Zero), surface);
        return (new SurfaceProfiles.Quoins(lined, run, kind, options.PitchMm, options.Courses, 0.2f, 0.8f, 0.4f), surface);
    }

    /// <summary>
    /// Rubble and castle walling round a box: a stack of quoins at every corner, each turning the
    /// corner, its long face on one wall and its short face on the other, swapping course by course;
    /// and the whole one closed solid with the box.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Rubble)]
    [InlineData(TextureKind.Castle)]
    public void QuoinsTurnEveryOutsideCornerLongAndShortByTurns(TextureKind kind)
    {
        var box = Box();
        var run = Walls(box, new Vector3(20, 0, 10), Vector3.UnitX, TheOtherThree);
        var (quoins, surface) = Quoined(run, kind);

        Assert.Equal(4, quoins.Corners);
        Assert.True(quoins.Courses >= 2, $"{quoins.Courses} courses");

        // Just past the end of a short face: inside the quoin where its long face is on that side,
        // in the joint beside it where its short face is.
        foreach (float corner in quoins.CornerXs)
        {
            bool? before = null;
            for (int j = 0; j < quoins.Courses; j++)
            {
                float y = -10f + (j + 0.5f) * quoins.CourseMm;
                float back = quoins.Height(new Vector2(corner - quoins.ShortMm - 0.1f, y));
                float on = quoins.Height(new Vector2(corner + quoins.ShortMm + 0.1f, y));
                bool longBefore = quoins.LongBefore(corner, j);

                Assert.True(longBefore ? back > 0.2f && on == 0f : on > 0.2f && back == 0f,
                    $"corner at {corner}, course {j}: {back} before, {on} after");
                if (before is { } was) Assert.NotEqual(was, longBefore);
                before = longBefore;
            }
        }

        var field = ReliefField.Build(surface, quoins, run.Length, 20f - 0.2f, false, true);
        var built = ManifoldCsg.Union(box, TextCutter.KeptOn(field, surface, 0.8f));
        Assert.True(built is { } b && b.CheckHealth().IsWatertight, built?.CheckHealth().Describe());
        Assert.Single(MeshComponents.Split(built!));
    }

    /// <summary>
    /// Quoins go at outside corners only: the corner between two walls picked, and a corner where
    /// the walls picked stop - but not an inside corner, which on a building is two walls meeting.
    /// </summary>
    [Fact]
    public void QuoinsGoOnOutsideCornersOnly()
    {
        var (two, _) = Quoined(Walls(Box(), new Vector3(20, 0, 10), Vector3.UnitX, Vector3.UnitY), TextureKind.Castle);
        Assert.Equal(3, two.Corners);

        var wing = MeshTransform.Transformed(Primitives.Box(20, 40, 20), Matrix4x4.CreateTranslation(-10, 10, 10));
        var part = ManifoldCsg.Union(Box(), wing)!;
        var face = FacePatch.Find(part, new Vector3(20, 0, 10), Vector3.UnitX)!;
        var loop = WallLoop.Around(part, face, new Vector3(20, 0, 10), out _)!;
        var (all, _) = Quoined(WallRun.Round(loop, part, out _)!, TextureKind.Rubble);
        Assert.Equal(5, all.Corners);
    }

    /// <summary>A hexagonal tower has no square corner to turn, and is told so rather than bricked wrong.</summary>
    [Fact]
    public void ACornerThatIsNotSquareIsRefused()
    {
        var tower = Primitives.Prism(20, 20, 6);
        var low = tower.ComputeBounds().Min.Z;
        tower = MeshTransform.Transformed(tower, Matrix4x4.CreateTranslation(0, 0, -low));

        var bounds = tower.ComputeBounds();
        var face = FacePatch.FromTriangle(tower, Enumerable.Range(0, tower.TriangleCount)
            .Select(t => t * 3)
            .First(t =>
            {
                var n = Vector3.Cross(
                    tower.Positions[tower.Indices[t + 1]] - tower.Positions[tower.Indices[t]],
                    tower.Positions[tower.Indices[t + 2]] - tower.Positions[tower.Indices[t]]);
                return MathF.Abs(Vector3.Normalize(n).Z) < 0.1f;
            }))!;

        var pick = face.ToLocal((face.Min + face.Max) / 2f);
        var loop = WallLoop.Around(tower, face, pick, out var why);
        Assert.True(loop is not null, why);
        var run = WallRun.Round(loop!, tower, out why);
        Assert.True(run is not null, why);

        Assert.Contains("square corners", BrickBond.Refusal(run!));
        Assert.Equal(0, BrickBond.Build(run!, Module, Aspect, Joint, Depth, out int count).TriangleCount);
        Assert.Equal(0, count);
        Assert.True(bounds.Size.Z > 0);
    }
}
