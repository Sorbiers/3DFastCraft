using System.Numerics;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Generators.Clips;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.Generators.Mechanisms;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Motion;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// What each generator in the library promises beyond what the sweep holds every one of them to:
/// the ratio a train comes to, the sizes a standard fixes, the motion a mechanism makes.
/// </summary>
public class LibraryGeneratorTests
{
    private static Vector2[] Square(float x0, float y0, float x1, float y1) =>
        [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    [Fact]
    public void AnArmTurningIntoASliderPushesItAlongItsTrack()
    {
        // An arm along plus X from the origin, and a block beside its path that may only slide in X.
        var arm = PlanarBody.Turning("arm", Vector2.Zero, new PlanarLoop(0, Square(0, -1, 20, 1)));
        var block = PlanarBody.Sliding("block", Vector2.UnitX, new PlanarLoop(0, Square(5, 3, 9, 7)));

        var run = PlanarMotion.Drive([arm, block], 0, 0.25);

        // The arm, turning anticlockwise, sweeps up and to the left through where the block was.
        Assert.False(run.Jammed);
        Assert.True(run.Final[1] < -1, $"the block moved {run.Final[1]:0.##} mm");
    }

    [Fact]
    public void AnArmThatMeetsAWallJams()
    {
        var arm = PlanarBody.Turning("arm", Vector2.Zero, new PlanarLoop(0, Square(0, -1, 20, 1)));
        var wall = PlanarBody.Standing("wall", new PlanarLoop(0, Square(5, 3, 30, 5)));

        var run = PlanarMotion.Drive([arm, wall], 0, 0.25);

        Assert.True(run.Jammed);
        Assert.Contains("wall", run.Jam);
    }

    [Fact]
    public void ASolidCutAcrossGivesItsOutline()
    {
        var loops = Sections.At(Primitives.Box(10, 20, 30), 0f);

        var loop = Assert.Single(loops);
        Assert.Equal(200f, MathF.Abs(Polygon2.SignedArea(loop.ToList())) / 2f, 1);
    }

    [Fact]
    public void GridfinityKeepsToThePublishedSizes()
    {
        Assert.Equal((42f, 7f, 4.75f), (GridfinitySpec.Pitch, GridfinitySpec.HeightUnit, GridfinitySpec.FootHeight));
        Assert.Equal(41.5f, GridfinitySpec.Foot[^1].Width);

        var bin = new GridfinityBin();
        var made = bin.Make(bin.Default with { UnitsX = 2, UnitsY = 1, Units = 3 }, Printer.Default);
        var size = Assert.Single(made.Parts).Mesh.ComputeBounds().Size;

        Assert.Equal((83.5f, 41.5f, 21f), (MathF.Round(size.X, 2), MathF.Round(size.Y, 2), MathF.Round(size.Z, 2)));
    }

    [Theory]
    [InlineData(12f, 2)]
    [InlineData(50f, 3)]
    [InlineData(5.5f, 1)]
    public void AGearTrainComesWithinAPercentOfTheRatioAsked(float ratio, int stages)
    {
        var train = new GearTrain();
        var settings = train.Default with { Ratio = ratio, Stages = stages, Largest = 80 };

        double made = GearTrain.Teeth(settings).Aggregate(1.0, (r, b) => r * b / settings.Pinion);
        Assert.InRange(made, ratio * 0.99, ratio * 1.01);
    }

    [Fact]
    public void APlanetarySetWhoseTeethDoNotShareOutSaysWhatSunWould()
    {
        var set = new Planetary();
        var made = set.Make(set.Default with { Sun = 13, Planet = 12, Planets = 3 }, Printer.Default);

        Assert.Empty(made.Parts);
        Assert.Contains("Try a sun of", made.Refusal);
    }

    [Fact]
    public void ASnapFitBentPastWhatItsMaterialTakesIsRefused()
    {
        var snap = new SnapFit();
        var made = snap.Make(snap.Default with { Material = Material.PLA, Length = 8, Thickness = 3, Hook = 2 }, Printer.Default);

        Assert.Empty(made.Parts);
        Assert.Contains("past what PLA takes", made.Refusal);
    }

    [Fact]
    public void AGenevaWheelStepsOneSlotForEachTurnOfItsDriver()
    {
        var geneva = new Geneva();
        var made = geneva.Make(geneva.Default with { Slots = 6 }, Printer.Default);

        Assert.Null(made.Refusal);
        Assert.Equal(2, made.Parts.Count);
        Assert.Contains(made.Notes, n => n.Contains("steps 60 degrees"));
    }

    [Fact]
    public void AFourBarWithTheShortestCrankIsACrankRocker()
    {
        var linkage = new FourBar();
        Assert.StartsWith("A crank-rocker", FourBar.Kind(linkage.Default));
        Assert.StartsWith("No link turns", FourBar.Kind(linkage.Default with { Ground = 100, Crank = 40, Coupler = 45, Rocker = 50 }));
    }

    [Fact]
    public void ACamLiftsItsFollowerByTheRiseAskedAndNoMore()
    {
        var cam = new Cam();
        var s = cam.Default with { Follower = FollowerKind.Flat, Base = 20, Rise = 8 };

        Assert.Equal(0, Cam.Motion(s, 0).Lift, 6);
        Assert.Equal(8, Cam.Motion(s, (s.RiseAngle + s.Dwell / 2) * Math.PI / 180).Lift, 6);

        var part = Assert.Single(cam.Make(s, Printer.Default).Parts, p => p.Role == "cam");
        var profile = MeshTransform.Transformed(part.Mesh, part.Assembled!.Value);
        float reach = profile.Positions.Max(p => new Vector2(p.X, p.Y).Length());
        Assert.Equal(28f, reach, 0);
    }

    [Fact]
    public void ACrankSliderOnItsAxisStrokesTwiceTheCrank()
    {
        var slider = new CrankSlider();
        Assert.Equal(40, CrankSlider.Stroke(slider.Default with { Crank = 20, Offset = 0 }), 1);
    }

    [Fact]
    public void ANewInsertGoesClearOfWhatIsAlreadyOnThePlate()
    {
        var size = new Vector2(64, 64);
        Assert.Equal(Vector2.Zero, FastCraft3D.Model.BedPlacement.Clear(size, [], 200, 200));

        bool Apart(Vector2 c, Bounds b) => c.X - 32 >= b.Max.X || c.X + 32 <= b.Min.X || c.Y - 32 >= b.Max.Y || c.Y + 32 <= b.Min.Y;

        // Room on the plate beside a smaller part: it goes there.
        var small = new Bounds(new Vector3(-20, -20, 0), new Vector3(20, 20, 8));
        var clear = FastCraft3D.Model.BedPlacement.Clear(size, [small], 200, 200);
        Assert.True(Apart(clear, small), $"put at {clear}, on top of what was there");
        Assert.True(MathF.Abs(clear.X) + 32 <= 100 && MathF.Abs(clear.Y) + 32 <= 100, "put off the plate when there was room on it");

        // No room: beside it off the plate's edge, never on top of it.
        var big = new Bounds(new Vector3(-90, -90, 0), new Vector3(90, 90, 8));
        Assert.True(Apart(FastCraft3D.Model.BedPlacement.Clear(size, [big], 200, 200), big));
    }

    [Fact]
    public void ChoosingAWasherSizeFillsInItsIsoNumbers()
    {
        var washer = new Washer();
        var after = (Washer.Settings)washer.Adjusted(washer.Default, washer.Default with { Size = WasherSize.M5 }, "Size");

        Assert.Equal((5.3f, 10f, 1f), (after.Inside, after.Outside, after.Thickness));
    }
}
