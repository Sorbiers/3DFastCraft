using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Generators.Buildings;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.Generators.Mechanisms;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The stair, the thread and the gear as generators: what each had in its own panel before it
/// moved into the library, and still has.
/// </summary>
public class GeneratorToolsTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static readonly Stair Flight = new();
    private static readonly ScrewThread Threaded = new();
    private static readonly Gear Toothed = new();

    private static GeneratedPart Only(Generated made)
    {
        Assert.Null(made.Refusal);
        return Assert.Single(made.Parts);
    }

    [Fact]
    public void AStairStandsOnThePlateWithItsFootprintAboutItsOrigin()
    {
        var bounds = Only(Flight.Make(Flight.Default, Printer.Default)).Mesh.ComputeBounds();

        Assert.Equal(0f, bounds.Min.Z, 4);
        Assert.Equal(26.5f, bounds.Max.Z, 3);
        Assert.Equal(0f, bounds.Center.X, 3);
        Assert.Equal(0f, bounds.Center.Y, 3);
    }

    [Fact]
    public void AStairSaysWhatItWouldBeToClimbAtTheModelsScale()
    {
        var said = Flight.Readouts(Flight.Default, 87f);

        Assert.Contains(said, line => line.Contains("At 1:87, that is 177 mm risers on 252 mm treads"));
        Assert.Contains("A comfortable flight.", said);
    }

    [Fact]
    public void ChoosingAMetricSizeBringsItsPitchAndNutWithIt()
    {
        var before = Threaded.Default;
        var after = (ScrewThread.Settings)Threaded.Adjusted(before, before with { Size = ThreadSize.M4 }, "Size");

        Assert.Equal((4f, 0.7f, 7f, 3.2f, 2.8f), (after.Diameter, after.Pitch, after.AcrossFlats, after.NutHeight, after.HeadHeight));
    }

    [Fact]
    public void ACustomSizeIsMadeToTheDiameterAndPitchTyped()
    {
        var custom = Threaded.Default with { Size = ThreadSize.Custom, Diameter = 7f, Pitch = 1f };
        Assert.Equal((7f, 1f), (ScrewThread.Options(custom).Diameter, ScrewThread.Options(custom).Pitch));

        // A metric size is its own, whatever the hidden boxes still say.
        var metric = custom with { Size = ThreadSize.M5 };
        Assert.Equal((5f, 0.8f), (ScrewThread.Options(metric).Diameter, ScrewThread.Options(metric).Pitch));
    }

    [Fact]
    public void AThreadCutterHangsFromItsMouthAndSaysItIsACutter()
    {
        var part = Only(Threaded.Make(Threaded.Default with { Kind = ThreadKind.HoleCutter, Length = 12 }, Printer.Default));

        Assert.True(part.Cutter);
        Assert.Equal(0f, part.Mesh.ComputeBounds().Max.Z, 4);
        Assert.True(part.Mesh.ComputeBounds().Min.Z < -11f);
    }

    [Fact]
    public void ANutHasNoLengthAndABoltHasAHead()
    {
        bool Shown(ThreadKind kind, string name) =>
            Threaded.Shows(Threaded.Default with { Kind = kind }, Threaded.Parameters.Single(p => p.Name == name));

        Assert.False(Shown(ThreadKind.Nut, "Length"));
        Assert.True(Shown(ThreadKind.Nut, "NutHeight"));
        Assert.True(Shown(ThreadKind.Bolt, "HeadHeight"));
        Assert.False(Shown(ThreadKind.Rod, "AcrossFlats"));
    }

    [Theory]
    [InlineData(GearKind.Gear, false, false, "Rim", false)]
    [InlineData(GearKind.Rack, false, false, "Rim", true)]
    [InlineData(GearKind.Gear, true, false, "Frame", true)]
    [InlineData(GearKind.Gear, true, false, "LockHeight", false)]
    [InlineData(GearKind.Gear, true, true, "LockHeight", true)]
    [InlineData(GearKind.Gear, true, true, "Rim", true)]
    [InlineData(GearKind.Gear, true, true, "HasPartner", false)]
    [InlineData(GearKind.Worm, false, false, "Teeth", false)]
    [InlineData(GearKind.Worm, false, false, "WormDiameter", true)]
    [InlineData(GearKind.Ratchet, false, false, "PressureAngle", false)]
    [InlineData(GearKind.Bevel, false, false, "HubDiameter", false)]
    public void AGearShowsOnlyTheRowsThatMeanSomethingForIt(GearKind kind, bool cutAway, bool frame, string row, bool shown)
    {
        var settings = Toothed.Default with { Kind = kind, Partial = cutAway, Frame = frame };
        Assert.Equal(shown, Toothed.Shows(settings, Toothed.Parameters.Single(p => p.Name == row)));
    }

    [Fact]
    public void AMateStartsWithTheGearsOwnShaft()
    {
        var gear = Toothed.Default with { Bore = BoreShape.DShaft, BoreSize = 6, BoreFlat = 5.4f, HubDiameter = 14, HubHeight = 5, SetScrew = 3 };
        var mated = (Gear.Settings)Toothed.Adjusted(gear, gear with { HasPartner = true }, "HasPartner");

        Assert.Equal((BoreShape.DShaft, 6f, 5.4f, 14f, 5f, 3f),
            (mated.MateBore, mated.MateBoreSize, mated.MateBoreFlat, mated.MateHubDiameter, mated.MateHubHeight, mated.MateSetScrew));
    }

    [Fact]
    public void EachGearOfAPairTurnsAboutItsOwnShaftAtThePlate()
    {
        var made = Toothed.Make(Toothed.Default with { HasPartner = true, PartnerTeeth = 30 }, Printer.Default);
        Assert.Equal(2, made.Parts.Count);

        foreach (var part in made.Parts)
        {
            var bore = part.Anchors!.First(a => a.Kind == AnchorKind.Bore);
            Assert.Equal((bore.At.X, bore.At.Y, 0f), (part.Pivot.X, part.Pivot.Y, part.Pivot.Z));
        }

        // In mesh as they print: the shafts a centre distance apart.
        Assert.Equal(1.5f * (20 + 30) / 2f, Vector3.Distance(made.Parts[0].Pivot, made.Parts[1].Pivot), 2);
    }

    [Fact]
    public void AGearBiggerThanAnyPlateIsRefusedWithTheReason()
    {
        var made = Toothed.Make(Toothed.Default with { Module = 10, Teeth = 100 }, Printer.Default);

        Assert.Empty(made.Parts);
        Assert.Contains("1000 mm across", made.Refusal);
    }

    [Fact]
    public void AHeadingGoesWhenNothingUnderItIsShown() => RunSta(() =>
    {
        var view = new GeneratorView(Toothed, null, GeneratorContext.Default, (_, _) => { }, live: false);

        Assert.False(view.HeadingShown("Bevel"));
        Assert.True(view.HeadingShown("Shaft"));

        view.Choose("Kind", GearKind.Bevel);
        Assert.True(view.HeadingShown("Bevel"));
        Assert.False(view.HeadingShown("Cut away"));
    });

    [Fact]
    public void TurningTheMateOnInThePanelFillsInItsShaft() => RunSta(() =>
    {
        var start = Toothed.Default with { BoreSize = 8 };
        var view = new GeneratorView(Toothed, start, GeneratorContext.Default, (_, _) => { }, live: false);

        view.Choose("HasPartner", true);

        Assert.Equal(8f, ((Gear.Settings)view.Current).MateBoreSize);
    });

    [Fact]
    public void CutIsOfferedOnlyForACutterWithAPartSelected() => RunSta(() =>
    {
        var cutter = Threaded.Default with { Kind = ThreadKind.HoleCutter };
        var into = GeneratorContext.Default with { Target = "Bracket" };

        Assert.True(new GeneratorView(Threaded, cutter, into, (_, _) => { }, live: false).CutOffered);
        Assert.False(new GeneratorView(Threaded, cutter, GeneratorContext.Default, (_, _) => { }, live: false).CutOffered);
        Assert.False(new GeneratorView(Threaded, Threaded.Default, into, (_, _) => { }, live: false).CutOffered);
    });

    [Fact]
    public void TheOldToolsAreSettledAndTheNewOnesAreBeta()
    {
        Assert.False(Flight.IsBeta);
        Assert.False(Threaded.IsBeta);
        Assert.False(Toothed.IsBeta);
        Assert.True(new OpenBox().IsBeta);
    }
}
