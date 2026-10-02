using System.Numerics;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Generators.Clips;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.Generators.Mechanisms;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
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

    /// <summary>The whole of a set as one mesh: a door or a window with its glass in it.</summary>
    private static Mesh Whole(Generated made) => Mesh.Combine(made.Parts.Select(p => p.Mesh));

    [Fact]
    public void AWindowsGlassIsAPartOfItsOwnInTheFrameFillingEveryPane()
    {
        var window = new FastCraft3D.Generators.Buildings.Window();
        var s = window.Default with { Columns = 3, Rows = 2, GlassThickness = 0.4f, Flat = true };
        var made = window.Make(s, Printer.Default);
        var bare = Assert.Single(window.Make(s with { Glass = false }, Printer.Default).Parts);

        Assert.Equal(["frame", "glass"], made.Parts.Select(p => p.Role));
        var glass = made.Parts[1];
        Assert.Equal(FastCraft3D.Generators.Buildings.Glazing.Filament, glass.Filament);

        double panes = FastCraft3D.Generators.Buildings.Window.Panes(s).Sum(p => (p.X1 - p.X0) * (p.Y1 - p.Y0));
        // Within the hair each pane is drawn in by, so as not to share the frame's corners.
        Assert.InRange(glass.Mesh.ComputeSignedVolume(), panes * 0.4 * 0.97, panes * 0.4);
        Assert.Equal(bare.Mesh.ComputeSignedVolume(), made.Parts[0].Mesh.ComputeSignedVolume(), 3);
        Assert.True(glass.Mesh.CheckHealth().IsWatertight);

        // One pane of glass in each opening, lying on the plate at the back of the frame.
        var pieces = MeshComponents.Split(glass.Mesh);
        Assert.Equal(3 * 2, pieces.Count);
        Assert.All(pieces, p => Assert.Equal(0f, p.ComputeBounds().Min.Z, 4));
    }

    /// <summary>
    /// The sets whose parts print where they go together - a window or a door and its glass, a
    /// dormer and its glass, a hinge printed in one, the clearance plate with its pins in it - go
    /// down as an assembly, not as loose parts nor grouped into one object.
    /// </summary>
    [Theory]
    [InlineData("building.window")]
    [InlineData("building.door")]
    [InlineData("building.dormer")]
    [InlineData("hinge.knuckle")]
    [InlineData("calibration.clearance")]
    public void ASetThatPrintsWhereItGoesTogetherGoesDownAsAnAssembly(string id)
    {
        var generator = GeneratorRegistry.Find(id)!;
        var settings = generator.Defaults();
        if (settings is FastCraft3D.Generators.Buildings.Door.Settings door)
            settings = door with { Leaf = FastCraft3D.Generators.Buildings.DoorLeaf.Glazed };

        var made = generator.Make(settings, Printer.Default);

        Assert.True(made.Parts.Count > 1, $"{id} made {made.Parts.Count} part");
        Assert.False(made.LaidOut, $"{id} is laid out as loose parts");
        Assert.All(made.Parts, p => Assert.True(p.Mesh.CheckHealth().IsWatertight, $"{p.Name} is not closed"));
    }

    [Theory]
    [InlineData(FastCraft3D.Generators.Buildings.RoofShape.Gable, FastCraft3D.Generators.Buildings.RoofCovering.Tiles)]
    [InlineData(FastCraft3D.Generators.Buildings.RoofShape.Hip, FastCraft3D.Generators.Buildings.RoofCovering.Slates)]
    [InlineData(FastCraft3D.Generators.Buildings.RoofShape.Mansard, FastCraft3D.Generators.Buildings.RoofCovering.Smooth)]
    [InlineData(FastCraft3D.Generators.Buildings.RoofShape.Gable, FastCraft3D.Generators.Buildings.RoofCovering.Corrugated)]
    public void DormersAreMergedIntoTheRoofAsOneSoundSolid(FastCraft3D.Generators.Buildings.RoofShape shape, FastCraft3D.Generators.Buildings.RoofCovering covering)
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var s = roof.Default with { Shape = shape, Covering = covering, Width = 60, Length = 90, Dormers = 2, DormerHeight = 6, DormerSetBack = 0 };
        var bare = Assert.Single(roof.Make(s with { Dormers = 0 }, Printer.Default).Parts).Mesh;
        var with = Assert.Single(roof.Make(s, Printer.Default).Parts).Mesh;

        Assert.True(with.CheckHealth().IsWatertight);
        Assert.True(with.ComputeSignedVolume() > bare.ComputeSignedVolume() + 4 * 100);
    }

    [Fact]
    public void DormersOnEveryRoofShapeAndCoveringAreRefusedOrSound()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var faults = new List<string>();
        int made = 0;

        foreach (var shape in Enum.GetValues<FastCraft3D.Generators.Buildings.RoofShape>().Where(r => r != FastCraft3D.Generators.Buildings.RoofShape.Flat))
            foreach (var covering in Enum.GetValues<FastCraft3D.Generators.Buildings.RoofCovering>())
                foreach (var (count, sides, width, height, back) in new[]
                         {
                             (1, FastCraft3D.Generators.Buildings.DormerSides.One, 14f, 8f, 2f),
                             (3, FastCraft3D.Generators.Buildings.DormerSides.Both, 10f, 6f, 0f),
                             (2, FastCraft3D.Generators.Buildings.DormerSides.Both, 20f, 12f, 6f)
                         })
                {
                    var s = roof.Default with
                    {
                        Shape = shape, Covering = covering, Width = 70, Length = 110,
                        Dormers = count, DormerSides = sides, DormerWidth = width, DormerHeight = height, DormerSetBack = back
                    };
                    var result = roof.Make(s, Printer.Default);
                    if (result.IsRefused) continue;

                    made++;
                    var mesh = Assert.Single(result.Parts).Mesh;
                    if (!mesh.CheckHealth().IsWatertight) faults.Add($"{shape} {covering} {count}x{width}: not closed");
                    if (mesh.ComputeBounds().Min.Z < -1e-3f) faults.Add($"{shape} {covering} {count}x{width}: below the plate");
                }

        Assert.Empty(faults);
        Assert.True(made > 40, $"Only {made} made - the sweep is mostly refusals");
    }

    [Fact]
    public void MoreDormersThanASlopeHasRoomForAreRefused()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var made = roof.Make(roof.Default with { Length = 40, Dormers = 5 }, Printer.Default);

        Assert.True(made.IsRefused);
        Assert.Contains("Fewer, or narrower", made.Refusal);
    }

    [Fact]
    public void AWindowSaysItsRealSizeAtTheModelsScale()
    {
        var window = new FastCraft3D.Generators.Buildings.Window();
        var said = window.Readouts(window.Default with { Width = 14, Height = 16 }, 87f);

        Assert.Contains("At 1:87, a window 1218 x 1392 mm.", said);
    }

    [Fact]
    public void ADoorIsGlazedOnlyInTheTopPanelsAsked()
    {
        var door = new FastCraft3D.Generators.Buildings.Door();
        var s = door.Default with { Leaf = FastCraft3D.Generators.Buildings.DoorLeaf.Glazed, Panels = 3, Glazed = 1, Flat = true };
        var made = door.Make(s, Printer.Default);

        // The glass is a part of its own, one pane in the top panel.
        var glass = Assert.Single(MeshComponents.Split(Assert.Single(made.Parts, p => p.Role == "glass").Mesh));
        var top = FastCraft3D.Generators.Buildings.Door.Panels(s)[0];
        var bounds = glass.ComputeBounds();
        Assert.Equal(top.Y0, bounds.Min.Y, 1);
        Assert.Equal(top.Y1, bounds.Max.Y, 1);

        Assert.Single(MeshComponents.Split(Assert.Single(door.Make(door.Default, Printer.Default).Parts).Mesh));
    }

    [Fact]
    public void ADoorsGlassIsOptionalAndWithoutItTheGlazedPanelsAreOpen()
    {
        var door = new FastCraft3D.Generators.Buildings.Door();
        var s = door.Default with { Leaf = FastCraft3D.Generators.Buildings.DoorLeaf.Glazed, Panels = 3, Glazed = 2, PaneColumns = 2, Flat = true };

        var glazed = Whole(door.Make(s, Printer.Default));
        var open = Assert.Single(door.Make(s with { Glass = false }, Printer.Default).Parts).Mesh;

        // The door alone, one piece, with the openings where the glass was: the glass is all
        // that went.
        Assert.Single(MeshComponents.Split(open));
        Assert.True(open.CheckHealth().IsWatertight);
        var panes = MeshComponents.Split(glazed).Where(p => p.ComputeBounds().Max.Z <= s.GlassThickness + 1e-3f).ToList();
        Assert.Equal(2 * 2, panes.Count);
        Assert.Equal(glazed.ComputeSignedVolume() - panes.Sum(p => p.ComputeSignedVolume()), open.ComputeSignedVolume(), 2);
    }

    /// <summary>
    /// The glass can be as thick as the leaf is deep and no thicker - the old limit of 2 mm had
    /// nothing to do with the door it was in.
    /// </summary>
    [Fact]
    public void ADoorsGlassIsLimitedByTheLeafNotByTwoMillimetres()
    {
        var door = new FastCraft3D.Generators.Buildings.Door();
        var s = door.Default with { Depth = 6f, SetBack = 0.5f, Leaf = FastCraft3D.Generators.Buildings.DoorLeaf.Glazed, Panels = 2, Flat = true };

        var thick = door.Make(s with { GlassThickness = 5.5f }, Printer.Default);
        Assert.False(thick.IsRefused, thick.Refusal);
        var pane = Assert.Single(MeshComponents.Split(Assert.Single(thick.Parts, p => p.Role == "glass").Mesh));
        Assert.Equal(5.5f, pane.ComputeBounds().Max.Z, 3);

        Assert.True(door.Make(s with { GlassThickness = 5.6f }, Printer.Default).IsRefused);
    }

    /// <summary>
    /// The back is flat: panes lie in their openings flush with the back of the door, or as one
    /// clear sheet across the whole back with the door standing on it - which closes the gap
    /// between two leaves as well.
    /// </summary>
    [Theory]
    [InlineData(FastCraft3D.Generators.Buildings.DoorGlass.Panes)]
    [InlineData(FastCraft3D.Generators.Buildings.DoorGlass.Sheet)]
    public void ADoorsBackIsFlatWithItsGlassOnIt(FastCraft3D.Generators.Buildings.DoorGlass glassAs)
    {
        var door = new FastCraft3D.Generators.Buildings.Door();
        var s = door.Default with
        {
            Type = FastCraft3D.Generators.Buildings.DoorType.French, Width = 18, Leaves = 2,
            Leaf = FastCraft3D.Generators.Buildings.DoorLeaf.Glazed, Panels = 1, Glazed = 1, PaneColumns = 2, PaneRows = 3,
            GlassAs = glassAs, GlassThickness = 0.6f, Flat = true
        };
        var made = Whole(door.Make(s, Printer.Default));
        Assert.True(made.CheckHealth().IsWatertight, made.CheckHealth().Describe());

        var pieces = MeshComponents.Split(made);
        var glass = pieces.Where(p => p.ComputeBounds().Max.Z <= 0.6f + 1e-3f).ToList();
        Assert.All(glass, p => Assert.Equal(0f, p.ComputeBounds().Min.Z, 4));

        if (glassAs == FastCraft3D.Generators.Buildings.DoorGlass.Panes)
        {
            // Two leaves of six panes each, and the door on the plate beside them.
            Assert.Equal(2 * 2 * 3, glass.Count);
            Assert.Equal(0f, pieces.Except(glass).Min(p => p.ComputeBounds().Min.Z), 4);
            return;
        }

        // One sheet the size of the door, and the door standing on it a hair clear.
        var sheet = Assert.Single(glass);
        var body = Assert.Single(pieces.Except(glass));
        Assert.Equal(18f, sheet.ComputeBounds().Size.X, 1);
        Assert.Equal(s.Height, sheet.ComputeBounds().Size.Y, 1);
        Assert.InRange(body.ComputeBounds().Min.Z, 0.6f, 0.6f + 0.02f);
        Assert.Equal(s.Depth, body.ComputeBounds().Max.Z, 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(2.5f)]
    public void AnArchedDoorIsTheSquareOneWithItsTopCornersRoundedOff(float rise)
    {
        var door = new FastCraft3D.Generators.Buildings.Door();
        var square = door.Default with { Leaf = FastCraft3D.Generators.Buildings.DoorLeaf.Glazed, Panels = 3, Glazed = 1, PaneColumns = 2, PaneRows = 2, Flat = true };
        var arched = square with { Shape = FastCraft3D.Generators.Buildings.DoorShape.Arched, Rise = rise };

        var made = door.Make(arched, Printer.Default);
        Assert.False(made.IsRefused, made.Refusal);
        var mesh = Whole(made);
        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());

        // As tall as asked at its crown, and nothing standing at its top corners.
        var bounds = mesh.ComputeBounds();
        Assert.Equal(square.Height, bounds.Size.Y, 2);
        Assert.DoesNotContain(mesh.Positions, p => MathF.Abs(p.X) > square.Width / 2f - 0.5f && p.Y > square.Height - 0.5f);

        // A round arch takes more off than a shallow one, and both less than the whole top.
        double cut = Whole(door.Make(square, Printer.Default)).ComputeSignedVolume() - mesh.ComputeSignedVolume();
        float w = square.Width / 2f;
        Assert.True(cut > 0.05 * w * w * square.Depth, $"only {cut:0.0} mm3 came off");

        // The top panel follows the arch round, inside the frame and the rail.
        var top = FastCraft3D.Generators.Buildings.Door.Panels(arched)[0];
        Assert.True(top.Y1 < square.Height - square.Frame - 0.5f);
    }

    [Fact]
    public void AnArchRisingPastHalfTheWidthIsRefused()
    {
        var door = new FastCraft3D.Generators.Buildings.Door();
        var made = door.Make(door.Default with { Shape = FastCraft3D.Generators.Buildings.DoorShape.Arched, Width = 11, Rise = 6 }, Printer.Default);

        Assert.True(made.IsRefused);
        Assert.Contains("half the door's width", made.Refusal);
    }

    [Fact]
    public void ADoorTooLowForPeopleAtTheModelsScaleSaysSo()
    {
        var door = new FastCraft3D.Generators.Buildings.Door();

        Assert.Contains(door.Readouts(door.Default with { Height = 18 }, 87f), line => line.Contains("people would stoop"));
        Assert.DoesNotContain(door.Readouts(door.Default with { Height = 25 }, 87f), line => line.Contains("stoop"));
    }

    [Fact]
    public void ARoofSitsOnItsWallsWithItsEavesOverhangingAndItsRidgeWhereThePitchPutsIt()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var s = roof.Default with { Width = 60, Length = 80, Pitch = 40, Eaves = 3, Verge = 2, Fascia = 1.5f,
            Hollow = false, Covering = FastCraft3D.Generators.Buildings.RoofCovering.Smooth, Ridge = false };
        var bounds = Assert.Single(roof.Make(s, Printer.Default).Parts).Mesh.ComputeBounds();

        Assert.Equal(66f, bounds.Size.X, 2);
        Assert.Equal(84f, bounds.Size.Y, 2);
        Assert.Equal(0f, bounds.Min.Z, 3);
        Assert.Equal(1.5f + 33f * MathF.Tan(40f * MathF.PI / 180f), bounds.Max.Z, 2);
    }

    [Fact]
    public void AHipRoofIsTheGableCutBackAtBothEnds()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var gable = roof.Default with { Hollow = false, Covering = FastCraft3D.Generators.Buildings.RoofCovering.Smooth, Ridge = false, Verge = 3 };
        double Volume(FastCraft3D.Generators.Buildings.RoofShape shape) =>
            roof.Make(gable with { Shape = shape }, Printer.Default).Parts[0].Mesh.ComputeSignedVolume();

        double full = Volume(FastCraft3D.Generators.Buildings.RoofShape.Gable);
        Assert.True(Volume(FastCraft3D.Generators.Buildings.RoofShape.HalfHip) < full);
        Assert.True(Volume(FastCraft3D.Generators.Buildings.RoofShape.Hip) < Volume(FastCraft3D.Generators.Buildings.RoofShape.HalfHip));
    }

    [Fact]
    public void AHollowRoofIsAShellAndItsTilesAreLaidOnIt()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var smooth = roof.Default with { Covering = FastCraft3D.Generators.Buildings.RoofCovering.Smooth, Ridge = false };
        double Volume(FastCraft3D.Generators.Buildings.Roof.Settings s) => roof.Make(s, Printer.Default).Parts[0].Mesh.ComputeSignedVolume();

        double solid = Volume(smooth with { Hollow = false });
        double shell = Volume(smooth);
        Assert.True(shell < solid / 3, $"the shell is {shell:0} mm3 of {solid:0}");
        Assert.True(Volume(smooth with { Covering = FastCraft3D.Generators.Buildings.RoofCovering.Tiles }) > shell);
    }

    /// <summary>
    /// The slabs stand on the whole of each slope, not half of it. Laid on a face made of two
    /// triangles that did not share their corners, they stopped at the diagonal, and every test
    /// that only asked whether the roof grew passed.
    /// </summary>
    [Theory]
    [InlineData(FastCraft3D.Generators.Buildings.RoofCovering.Tiles)]
    [InlineData(FastCraft3D.Generators.Buildings.RoofCovering.Slates)]
    [InlineData(FastCraft3D.Generators.Buildings.RoofCovering.Shingles)]
    public void TilesCoverTheWholeOfEverySlope(FastCraft3D.Generators.Buildings.RoofCovering covering)
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var (course, width, relief) = FastCraft3D.Generators.Buildings.Roof.Proportions(covering);
        var smooth = roof.Default with { Hollow = false, Ridge = false, Covering = FastCraft3D.Generators.Buildings.RoofCovering.Smooth };
        var tiled = smooth with { Covering = covering, Course = course, TileWidth = width, Relief = relief };
        double Volume(FastCraft3D.Generators.Buildings.Roof.Settings s) => roof.Make(s, Printer.Default).Parts[0].Mesh.ComputeSignedVolume();

        // Two slopes 84 mm long and 33 / cos 40 up, most of it under slabs a relief thick.
        double slopes = 2 * 84 * 33 / Math.Cos(40 * Math.PI / 180);
        double added = Volume(tiled) - Volume(smooth);
        Assert.True(added > 0.6 * slopes * relief, $"the {covering} added {added:0} mm3, under {0.6 * slopes * relief:0} for two whole slopes");
    }

    [Fact]
    public void ChoosingACoveringBringsItsOwnProportions()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        var start = roof.Default;
        var slates = (FastCraft3D.Generators.Buildings.Roof.Settings)roof.Adjusted(start,
            start with { Covering = FastCraft3D.Generators.Buildings.RoofCovering.Slates }, "Covering");

        Assert.NotEqual((start.Course, start.TileWidth, start.Relief), (slates.Course, slates.TileWidth, slates.Relief));
    }

    [Fact]
    public void ARoofSaysHowHighItsRidgeIsAtTheModelsScale()
    {
        var roof = new FastCraft3D.Generators.Buildings.Roof();
        Assert.Contains(roof.Readouts(roof.Default, 87f), line => line.Contains("m at 1:87"));
    }

    [Theory]
    [InlineData(MotionTeeth.Small)]
    [InlineData(MotionTeeth.Standard)]
    [InlineData(MotionTeeth.Large)]
    public void MotionWorkIsTwelveToOneWithBothPairsTheSameDistanceApart(MotionTeeth set)
    {
        var (cannon, minute, pinion, hour) = MotionWork.Teeth(set);

        Assert.Equal(12.0, (double)minute / cannon * hour / pinion, 9);
        Assert.Equal(cannon + minute, pinion + hour);
    }

    [Fact]
    public void EachFitTestPlateHasItsFitCutIntoItsTop()
    {
        var test = new FastCraft3D.Generators.Calibration.FitTest();
        var made = test.Make(test.Default, Printer.Default);

        Assert.Equal(4, made.Parts.Count);
        var plain = BrickStuds.FitCoupon(-0.2f, 1)!;
        double cut = plain.ComputeSignedVolume() - made.Parts[0].Mesh.ComputeSignedVolume();
        Assert.True(cut > 1, $"only {cut:0.##} mm3 was cut for the label");
        Assert.Equal(BrickStuds.PlateHeight + BrickStuds.StudHeight, made.Parts[0].Mesh.ComputeBounds().Size.Z, 2);
    }

    [Fact]
    public void ARetractionTestSaysWhatToRetractAtEachHeight()
    {
        var test = new FastCraft3D.Generators.Calibration.RetractionTest();
        var made = test.Make(test.Default with { From = 0.4f, Step = 0.4f, Bands = 3, BandHeight = 5, Base = 1 }, Printer.Default);

        Assert.Contains("Z 1–6 mm: 0.4 mm", made.Notes);
        Assert.Contains("Z 11–16 mm: 1.2 mm", made.Notes);
        Assert.Equal(16f, made.Parts[0].Mesh.ComputeBounds().Max.Z, 3);
    }

    [Fact]
    public void ATemperatureTowerHasABandForEachTemperatureAndATowerForEachSpeed()
    {
        var tower = new FastCraft3D.Generators.Calibration.TemperatureTower();
        var made = tower.Make(tower.Default with { From = 230, Step = -5, Bands = 6, Speeds = 3 }, Printer.Default);

        Assert.Equal(3, made.Parts.Count);
        Assert.Contains("Z 1.2–9.2 mm: 230 °C", made.Notes);
        Assert.Contains("Z 41.2–49.2 mm: 205 °C", made.Notes);
        Assert.Contains(made.Parts, p => p.Name == "Tower 100 mm/s");
    }

    [Fact]
    public void ChoosingAWasherSizeFillsInItsIsoNumbers()
    {
        var washer = new Washer();
        var after = (Washer.Settings)washer.Adjusted(washer.Default, washer.Default with { Size = WasherSize.M5 }, "Size");

        Assert.Equal((5.3f, 10f, 1f), (after.Inside, after.Outside, after.Thickness));
    }
}
