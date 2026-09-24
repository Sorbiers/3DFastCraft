using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Knurling, ribs and the rest, as outlines.
///
/// Two of these matter more than the look of the result. The pads must never overlap, because the
/// retiling that lays a pattern into a face without a boolean refuses crossing outlines and falls
/// back to a boolean over a thousand little diamonds. And they must all sit inside the rectangle
/// they were given, because the same retiling needs a margin of untouched face to hold the face's
/// own outline together.
/// </summary>
public class SurfaceTextureTests
{
    private static TextureOptions Options(TextureKind kind) =>
        TextureOptions.Default with { Kind = kind, PitchMm = 2f, LineMm = 0.6f };

    private static (Vector2 Low, Vector2 High) Box(TextShape shape)
    {
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);

        foreach (var point in shape.Outline)
        {
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }

        return (low, high);
    }

    /// <summary>
    /// Whether two convex outlines actually meet, by separating axes.
    ///
    /// Bounding boxes will not do, and trusting them is how a wrong honeycomb got through. On
    /// the interlocking lattice a honeycomb uses, neighbouring cells have boxes that overlap by a
    /// good fraction of their width while the cells themselves only come within a groove of each
    /// other - so a box test has to be loose enough to pass that, and once it is that loose it
    /// passes cells genuinely running into one another as well.
    /// </summary>
    private static bool Overlap(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
    {
        const float Slack = 1e-4f;

        foreach (var loop in new[] { a, b })
            for (int i = 0; i < loop.Count; i++)
            {
                var edge = loop[(i + 1) % loop.Count] - loop[i];
                var axis = new Vector2(-edge.Y, edge.X);
                if (axis.LengthSquared() < 1e-12f) continue;

                axis = Vector2.Normalize(axis);
                var (aLow, aHigh) = Span(a, axis);
                var (bLow, bHigh) = Span(b, axis);

                if (aHigh <= bLow + Slack || bHigh <= aLow + Slack) return false;
            }

        return true;
    }

    private static (float Low, float High) Span(IReadOnlyList<Vector2> loop, Vector2 axis)
    {
        float low = float.MaxValue, high = float.MinValue;

        foreach (var point in loop)
        {
            float along = Vector2.Dot(point, axis);
            low = MathF.Min(low, along);
            high = MathF.Max(high, along);
        }

        return (low, high);
    }

    [Theory]
    [InlineData(TextureKind.Knurl)]
    [InlineData(TextureKind.Ribs)]
    [InlineData(TextureKind.Hex)]
    [InlineData(TextureKind.Dots)]
    [InlineData(TextureKind.Tread)]
    [InlineData(TextureKind.Brick)]
    [InlineData(TextureKind.Tiles)]
    public void NoTwoPadsEverTouch(TextureKind kind)
    {
        // Both ends of the range. A fine groove on a coarse pitch is where a honeycomb laid on
        // the wrong lattice ran into itself, and it is the setting anybody reaches for first:
        // big cells with a thin line between them.
        foreach (var o in new[]
        {
            Options(kind),
            Options(kind) with { PitchMm = 10f, LineMm = 0.2f },
            Options(kind) with { PitchMm = 1.4f, LineMm = 0.5f }
        })
        {
            var made = SurfaceTexture.Over(40f, 34f, o, seamless: false);
            Assert.NotEmpty(made);

            for (int i = 0; i < made.Count; i++)
                for (int j = i + 1; j < made.Count; j++)
                    Assert.False(Overlap(made[i].Outline, made[j].Outline),
                        $"{kind} at {o.PitchMm:0.#}/{o.LineMm:0.#}: pads {i} and {j} run into each other");
        }
    }

    [Theory]
    [InlineData(TextureKind.Knurl)]
    [InlineData(TextureKind.Ribs)]
    [InlineData(TextureKind.Hex)]
    [InlineData(TextureKind.Dots)]
    [InlineData(TextureKind.Tread)]
    [InlineData(TextureKind.Brick)]
    [InlineData(TextureKind.Tiles)]
    public void EveryPadSitsInsideTheFaceItWasGiven(TextureKind kind)
    {
        var made = SurfaceTexture.Over(30f, 24f, Options(kind), seamless: false);

        foreach (var shape in made)
            foreach (var point in shape.Outline)
            {
                Assert.True(MathF.Abs(point.X) <= 15f + 1e-3f, $"{kind}: {point.X:0.###} is off the face");
                Assert.True(MathF.Abs(point.Y) <= 12f + 1e-3f, $"{kind}: {point.Y:0.###} is off the face");
            }
    }

    /// <summary>
    /// Round a barrel the two ends of the rectangle are the same place. Half a groove of margin at
    /// each end means exactly one groove where they meet - any other figure is a seam, and it lands
    /// down the side of a knob where it is the first thing anybody sees.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Knurl)]
    [InlineData(TextureKind.Tread)]
    public void WrappedRoundAPartTheEndsMeetWithOneGrooveBetweenThem(TextureKind kind)
    {
        var o = Options(kind);

        // A circumference deliberately not a whole number of pitches: 47.1 mm is a 15 mm knob.
        var made = SurfaceTexture.Over(47.1f, 20f, o, seamless: true);
        Assert.NotEmpty(made);

        Assert.Equal(-47.1f / 2f + o.LineMm / 2f, made.Min(x => Box(x).Low.X), 2);
        Assert.Equal(47.1f / 2f - o.LineMm / 2f, made.Max(x => Box(x).High.X), 2);
    }

    /// <summary>
    /// A honeycomb cannot do quite as well and it is worth saying why. Its cells are wider than
    /// their columns are apart - that is what interlocking means - so a field that goes exactly a
    /// whole number of columns round a barrel has a cell hanging over the join at each end. Either
    /// they are laid and overlap once the face closes on itself, or the field stops short. It stops
    /// short, so the seam is a little wider than the other grooves and nothing is laid twice.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Hex)]
    [InlineData(TextureKind.Dots)]
    public void AnInterlockingFieldMeetsItselfWithinOneCell(TextureKind kind)
    {
        var o = Options(kind);
        var made = SurfaceTexture.Over(47.1f, 20f, o, seamless: true);
        Assert.NotEmpty(made);

        float left = made.Min(x => Box(x).Low.X);
        float right = made.Max(x => Box(x).High.X);

        // Centred, so the seam is one gap rather than a gap at one end and none at the other.
        Assert.Equal(-left, right, 2);

        float seam = (47.1f / 2f - right) * 2f;

        Assert.True(seam >= o.LineMm - 0.01f, $"{kind}: the ends overlap by {o.LineMm - seam:0.##} mm");
        // Short by less than the room one more cell would have needed, which is the most it can be.
        Assert.True(seam <= o.PitchMm + o.LineMm,
            $"{kind}: a {seam:0.##} mm seam where one more cell needs {o.PitchMm + o.LineMm:0.##} mm");
    }

    /// <summary>
    /// Ribs running up the face are the ones with two ends to reconcile. Across, the seam runs the
    /// other way and the pitch is whatever was typed.
    /// </summary>
    [Fact]
    public void RibsAcrossAreNotForcedToDivideTheWayRound()
    {
        var o = Options(TextureKind.Ribs) with { Across = true };

        var made = SurfaceTexture.Over(47.1f, 20f, o, seamless: true);

        Assert.NotEmpty(made);
        Assert.Equal(10, made.Count);
    }

    [Fact]
    public void ATextureTooFineToPrintIsOpenedUpRatherThanRefused()
    {
        var asked = new TextureOptions(TextureKind.Knurl, PitchMm: 0.2f, LineMm: 0.05f);
        var settled = asked.Sane();

        Assert.Equal(TextureOptions.LeastLineMm, settled.LineMm, 3);
        Assert.True(settled.PitchMm >= settled.LineMm + TextureOptions.LeastPadMm,
            $"{settled.PitchMm:0.##} mm leaves no pad between the grooves");
    }

    [Fact]
    public void AKnurlLeansTheWayItIsAskedTo()
    {
        var steep = SurfaceTexture.Over(30f, 24f, Options(TextureKind.Knurl) with { AngleDegrees = 70f }, false);
        var shallow = SurfaceTexture.Over(30f, 24f, Options(TextureKind.Knurl) with { AngleDegrees = 20f }, false);

        // A steep groove crosses a cell's width in little height, so the diamonds come out squat
        // and there are more rows of them.
        float steepTall = steep.Max(s => Box(s).High.Y - Box(s).Low.Y);
        float shallowTall = shallow.Max(s => Box(s).High.Y - Box(s).Low.Y);

        Assert.True(steepTall < shallowTall,
            $"a 70 degree lean gave {steepTall:0.##} mm diamonds where 20 degrees gave {shallowTall:0.##}");
    }

    /// <summary>
    /// The whole way through, on the shape the tool exists for: a knurled knob.
    ///
    /// Worth its own test because everything above only proves the outlines are well behaved. What
    /// matters is whether a thousand little diamonds wrapped round a barrel come back as something
    /// that can be printed, and that is the boolean's answer, not the pattern's.
    /// </summary>
    [Fact]
    public void AKnurlGoesRoundAKnobAndComesBackPrintable()
    {
        const float Radius = 9f;
        const float Tall = 12f;

        var knob = MeshTransform.Transformed(
            Primitives.Prism(Radius, Tall, 64), Matrix4x4.CreateTranslation(0, 0, Tall / 2f));

        var surface = new CylinderSurface(new Vector3(0, 0, Tall / 2f), Radius);
        var shapes = SurfaceTexture.Over(
            MathF.Tau * Radius, Tall - 2f, Options(TextureKind.Knurl), seamless: true);

        Assert.True(shapes.Count > 100, $"only {shapes.Count} diamonds went round a 18 mm knob");

        var cut = TextCutter.Apply(knob, shapes, surface, raised: false, depthMm: 0.5f, bevelMm: 0f);

        Assert.NotNull(cut);
        Assert.True(cut!.CheckHealth().IsWatertight, $"the knurl came back {cut.CheckHealth().Describe()}");
        Assert.True(cut.ComputeSignedVolume() < knob.ComputeSignedVolume(), "nothing was taken off");
    }

    /// <summary>
    /// Raised on a flat face, which is the one case that never reaches the boolean: the face is
    /// retiled round the pads instead, so it is watertight by construction and quick with it.
    /// </summary>
    [Fact]
    public void RaisedOnAFlatFaceTheTextureIsBuiltIntoTheFace()
    {
        var plate = MeshTransform.Transformed(Primitives.Box(40, 30, 4), Matrix4x4.CreateTranslation(0, 0, 2));
        var top = FacePatch.Find(plate, new Vector3(0, 0, 4), Vector3.UnitZ)!;

        var shapes = SurfaceTexture.Over(40f - 0.2f, 30f - 0.2f, Options(TextureKind.Hex), seamless: false);
        Assert.NotEmpty(shapes);

        var raised = TextCutter.Apply(
            plate, shapes, new PlanarSurface(top), raised: true, depthMm: 0.6f, bevelMm: 0f);

        Assert.NotNull(raised);
        Assert.True(raised!.CheckHealth().IsWatertight, $"the honeycomb came back {raised.CheckHealth().Describe()}");
        Assert.True(raised.ComputeSignedVolume() > plate.ComputeSignedVolume(), "nothing was added");
    }

    /// <summary>
    /// The field is centred on nothing and covers what it was given.
    ///
    /// It is laid out about the origin because that is where the surface puts it, and the
    /// surface is anchored to the middle of the part. Anchored anywhere else - to the spot that
    /// was clicked, as a single stamp is - half the field hangs off one end of a barrel and
    /// leaves a bare band round the other.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Knurl)]
    [InlineData(TextureKind.Hex)]
    [InlineData(TextureKind.Dots)]
    public void TheFieldIsCentredAndCoversWhatItWasGiven(TextureKind kind)
    {
        var made = SurfaceTexture.Over(47.1f, 30f, Options(kind), seamless: true);

        float lowest = made.Min(s => Box(s).Low.Y);
        float highest = made.Max(s => Box(s).High.Y);

        Assert.True(MathF.Abs(highest + lowest) < 1f,
            $"{kind}: the field runs {lowest:0.#} to {highest:0.#} and is not centred");
        Assert.True(highest - lowest > 30f * 0.9f,
            $"{kind}: {highest - lowest:0.#} mm of a 30 mm face is covered");
    }

    /// <summary>
    /// A course that runs off the end of a wrapped face is laid as the two halves it really is.
    ///
    /// Every other course of a running bond starts half a piece along, so where the two ends of a
    /// barrel meet there is always a brick straddling the join. Dropping it leaves a notch in
    /// every other course, down the one seam anybody will look at.
    /// </summary>
    [Fact]
    public void ABrickOnTheSeamIsLaidAsTwoHalvesThatCloseUp()
    {
        const float Round = 47.1f;
        var o = Options(TextureKind.Brick) with { PitchMm = 8f, LineMm = 0.8f };

        var made = SurfaceTexture.Over(Round, 30f, o, seamless: true);

        // Every piece keeps half a joint of margin at the ends of the run, the halves of the piece
        // on the join included, so every course reaches the same two places.
        float edge = Round / 2f - o.LineMm / 2f;

        var courses = made
            .GroupBy(x => MathF.Round(Box(x).Low.Y, 2))
            .OrderBy(g => g.Key)
            .ToList();

        Assert.True(courses.Count > 3, $"only {courses.Count} courses to look at");

        foreach (var course in courses)
        {
            Assert.Equal(-edge, course.Min(x => Box(x).Low.X), 2);
            Assert.Equal(edge, course.Max(x => Box(x).High.X), 2);
        }

        // And the staggered courses are the ones with a piece more in them, since the one on the
        // join is laid as two.
        var counts = courses.Select(c => c.Count()).Distinct().OrderBy(n => n).ToList();

        Assert.Equal(2, counts.Count);
        Assert.Equal(counts[0] + 1, counts[1]);
    }

    /// <summary>What separates a wall from a tiled one: whether alternate courses shift.</summary>
    [Fact]
    public void BrickStaggersItsCoursesAndTilesDoNot()
    {
        var tiles = SurfaceTexture.Over(60f, 40f, Options(TextureKind.Tiles) with { PitchMm = 8f }, false);
        var lefts = tiles
            .GroupBy(x => MathF.Round(Box(x).Low.Y, 2))
            .Select(g => MathF.Round(g.Min(x => Box(x).Low.X), 2))
            .Distinct()
            .ToList();

        Assert.True(tiles.Count > 8, "not enough tiles to tell");
        Assert.Single(lefts);

        // Brick keeps a half piece at the end of every staggered course, so not every piece is
        // the same width and the courses do not all begin in the same place.
        var brick = SurfaceTexture.Over(60f, 40f, Options(TextureKind.Brick) with { PitchMm = 8f }, false);
        var widths = brick.Select(x => MathF.Round(Box(x).High.X - Box(x).Low.X, 2)).Distinct().ToList();

        Assert.True(widths.Count > 1, "every brick came out the same width, so nothing is staggered");
    }

    /// <summary>
    /// Laying a roof as brick is how the first one came out with 39 x 13 cm tiles. Each bond has
    /// its own proportions and they have to survive the trip through the outlines.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Brick, 3f)]
    [InlineData(TextureKind.Tiles, 1f)]
    public void EachBondKeepsItsOwnProportions(TextureKind kind, float aspect)
    {
        var made = SurfaceTexture.Over(120f, 90f, Options(kind) with { PitchMm = 10f, LineMm = 0.4f }, false);
        Assert.NotEmpty(made);

        // The widest piece is a whole one; the halves at the ends of a course are not.
        var whole = made.OrderByDescending(x => Box(x).High.X - Box(x).Low.X).First();
        var (low, high) = Box(whole);

        // Measured across the module - the piece and its joint - rather than across the piece
        // alone. The proportion describes how the wall is set out; a floorboard eight times its
        // own depth is 1.25 mm deep at this pitch, and taking a 0.4 mm joint off that alone
        // reads as eleven to one while nothing about the setting out has changed.
        float joint = 0.4f;
        float measured = (high.X - low.X + joint) / (high.Y - low.Y + joint);

        Assert.True(MathF.Abs(measured - aspect) < aspect * 0.25f,
            $"{kind} came out {measured:0.##} to 1 where it should be about {aspect:0.##}");
    }

    /// <summary>
    /// Brick round a tower, raised, which is the case the seam split has to survive.
    ///
    /// The two halves of a brick on the join meet exactly where the face closes on itself, so
    /// their walls end up coincident. Coplanar faces are what a boolean is worst at, and raising
    /// is the direction that has to union them onto the model rather than cut them out of it.
    /// </summary>
    [Fact]
    public void BrickGoesRoundATowerAndComesBackPrintable()
    {
        const float Radius = 15f;
        const float Tall = 24f;

        var tower = MeshTransform.Transformed(
            Primitives.Prism(Radius, Tall, 64), Matrix4x4.CreateTranslation(0, 0, Tall / 2f));

        var o = Options(TextureKind.Brick) with { PitchMm = 8f, LineMm = 0.8f };
        var shapes = SurfaceTexture.Over(MathF.Tau * Radius, Tall - 2f, o, seamless: true);
        Assert.NotEmpty(shapes);

        var surface = new CylinderSurface(new Vector3(0, 0, Tall / 2f), Radius);
        var laid = TextCutter.Apply(tower, shapes, surface, raised: true, depthMm: 0.6f, bevelMm: 0f);

        Assert.NotNull(laid);
        Assert.True(laid!.CheckHealth().IsWatertight, $"the brickwork came back {laid.CheckHealth().Describe()}");
    }

    /// <summary>
    /// A course ends in a half brick with a joint on its outer side, not in a brick run flush to
    /// the edge. Flush, the last piece has no joint at all on one side and the course reads as
    /// running out rather than as ending.
    /// </summary>
    [Fact]
    public void ACourseEndsInAPieceWithAJointBesideIt()
    {
        var o = Options(TextureKind.Brick) with { PitchMm = 8f, LineMm = 0.8f };
        var made = SurfaceTexture.Over(60f, 40f, o, seamless: false);

        float margin = o.LineMm / 2f;

        Assert.Equal(-30f + margin, made.Min(x => Box(x).Low.X), 2);
        Assert.Equal(30f - margin, made.Max(x => Box(x).High.X), 2);

        // Both kinds of course reach both ends: the staggered ones by way of a half brick.
        foreach (var course in made.GroupBy(x => MathF.Round(Box(x).Low.Y, 2)))
        {
            Assert.Equal(-30f + margin, course.Min(x => Box(x).Low.X), 2);
            Assert.Equal(30f - margin, course.Max(x => Box(x).High.X), 2);
        }
    }

    /// <summary>
    /// A wall on a flat face, raised, which is how the bonds arrive and the one path that never
    /// reaches the boolean: the face is retiled round the bricks instead, watertight by
    /// construction, at a size where a boolean would have taken seconds.
    /// </summary>
    [Fact]
    public void BrickOnAFlatFaceIsBuiltIntoTheFace()
    {
        var wall = MeshTransform.Transformed(Primitives.Box(50, 50, 50), Matrix4x4.CreateTranslation(0, 0, 25));
        var face = FacePatch.Find(wall, new Vector3(0, 0, 50), Vector3.UnitZ)!;

        var o = Options(TextureKind.Brick) with { PitchMm = 4f, LineMm = 0.6f };
        var shapes = SurfaceTexture.Over(49.8f, 49.8f, o, seamless: false);

        Assert.True(shapes.Count > 100, $"only {shapes.Count} bricks");

        var laid = TextCutter.Apply(wall, shapes, new PlanarSurface(face), raised: true, depthMm: 0.4f, bevelMm: 0f);

        Assert.NotNull(laid);
        Assert.True(laid!.CheckHealth().IsWatertight, $"the brickwork came back {laid.CheckHealth().Describe()}");

        // Raised, so the wall grows by the bricks and nothing is taken out of it.
        Assert.True(laid.ComputeSignedVolume() > wall.ComputeSignedVolume(), "nothing was added");
    }

    /// <summary>
    /// The field covers what it was given. A lattice whose cells are taller than the step
    /// between them loses its outer rows to the fitting check and leaves a third of the face
    /// bare, which is how the honeycomb looked before it was relaid.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Hex)]
    [InlineData(TextureKind.Dots)]
    [InlineData(TextureKind.Knurl)]
    [InlineData(TextureKind.Brick)]
    public void ACoarsePitchStillFillsTheFace(TextureKind kind)
    {
        var o = Options(kind) with { PitchMm = 10f, LineMm = 0.2f };
        var made = SurfaceTexture.Over(40f, 40f, o, seamless: false);

        Assert.NotEmpty(made);

        float low = made.Min(x => Box(x).Low.Y);
        float high = made.Max(x => Box(x).High.Y);
        float left = made.Min(x => Box(x).Low.X);
        float right = made.Max(x => Box(x).High.X);

        Assert.True(high - low > 40f * 0.75f, $"{kind}: {high - low:0.#} mm of 40 covered up the face");
        Assert.True(right - left > 40f * 0.75f, $"{kind}: {right - left:0.#} mm of 40 covered across it");
    }

    /// <summary>
    /// A piece exactly at the floor is a piece, not a sliver. It is a floor, and something has to
    /// be allowed to sit on it.
    /// </summary>
    [Fact]
    public void APieceExactlyAtTheNarrowestPrintableWidthIsKept()
    {
        // Courses deep enough that the depth is pinned to the floor, so every piece is exactly as
        // shallow as a piece is allowed to be.
        var o = Options(TextureKind.Brick) with { PitchMm = 4f, LineMm = 0.4f, Aspect = 20f };
        var made = SurfaceTexture.Over(60f, 40f, o, seamless: false);

        Assert.NotEmpty(made);

        float shallowest = made.Min(x => Box(x).High.Y - Box(x).Low.Y);

        Assert.True(shallowest >= TextureOptions.LeastPadMm - 1e-3f,
            $"a {shallowest:0.###} mm course got through where {TextureOptions.LeastPadMm} is the floor");
        Assert.True(made.Max(x => Box(x).High.X) - made.Min(x => Box(x).Low.X) > 50f,
            "the courses did not reach across the face");
    }

    /// <summary>
    /// Brick is flat and the roof and the boarding are not, which is the whole of the answer to
    /// the fair complaint that the three were one pattern with the numbers changed. A lap, a
    /// slope and a grain are heights, not outlines, so those two leave this path altogether and
    /// are built as solids by ReliefField instead.
    /// </summary>
    [Fact]
    public void BrickIsFlatAndARoofAndBoardingAreNot()
    {
        var o = Options(TextureKind.Brick);

        Assert.False(o.IsProfiled);
        Assert.False((o with { Kind = TextureKind.Tiles }).IsProfiled);

        Assert.True((o with { Kind = TextureKind.RoofTiles }).IsProfiled);
        Assert.True((o with { Kind = TextureKind.Planks }).IsProfiled);
        Assert.True((o with { Kind = TextureKind.Siding }).IsProfiled);

        // A shaped one lays no outlines at all, and has a profile where a flat one has none.
        Assert.Empty(SurfaceTexture.Over(60f, 60f, o with { Kind = TextureKind.Siding }, false));
        Assert.NotEmpty(SurfaceTexture.Over(60f, 60f, o, false));

        Assert.Null(SurfaceTexture.ProfileOf(o, 0.8f));
        Assert.NotNull(SurfaceTexture.ProfileOf(o with { Kind = TextureKind.Siding }, 0.8f));
    }

    [Fact]
    public void NoTextureMeansNoOutlines()
    {
        Assert.Empty(SurfaceTexture.Over(30f, 24f, TextureOptions.Default with { Kind = TextureKind.None }, false));
    }

    /// <summary>
    /// The count follows the room. Nothing else about the pattern is allowed to, since a knurl on
    /// a large face and a small one should feel the same under a thumb.
    /// </summary>
    [Fact]
    public void ABiggerFaceTakesMoreOfTheSamePattern()
    {
        var small = SurfaceTexture.Over(20f, 20f, Options(TextureKind.Dots), false);
        var large = SurfaceTexture.Over(40f, 20f, Options(TextureKind.Dots), false);

        float smallPad = Box(small[0]).High.X - Box(small[0]).Low.X;
        float largePad = Box(large[0]).High.X - Box(large[0]).Low.X;

        Assert.True(large.Count > small.Count * 1.8f, $"{large.Count} against {small.Count}");
        Assert.Equal(smallPad, largePad, 3);
    }
}
