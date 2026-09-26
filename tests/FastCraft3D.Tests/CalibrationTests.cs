using FastCraft3D.Generators;
using FastCraft3D.Generators.Calibration;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>What each test print promises beyond what the sweep holds every generator to.</summary>
public class CalibrationTests
{
    [Fact]
    public void EachClearancePinHasTheGapItIsLabelledWith()
    {
        var test = new ClearanceTest();
        var made = test.Make(test.Default with { From = 0.1f, Step = 0.1f, Pins = 3, Diameter = 6 }, Printer.Default);

        var pins = Assert.Single(made.Parts, p => p.Role == "pins").Mesh.ComputeBounds();
        var plate = Assert.Single(made.Parts, p => p.Role == "plate").Mesh.ComputeBounds();

        // The first pin 6 mm less twice its 0.1 mm gap across; the pins standing proud of the plate.
        Assert.Equal(5.8f, pins.Size.Y, 2);
        Assert.True(pins.Max.Z > plate.Max.Z);
    }

    [Fact]
    public void ABridgeLadderRunsFromItsFirstSpanToItsLast()
    {
        var test = new BridgeTest();
        var made = test.Make(test.Default with { From = 10, Step = 10, Bridges = 4 }, Printer.Default);

        Assert.Contains("Spans 10, 20, 30, 40 mm, each cut on the base beside it.", made.Notes);
    }

    [Fact]
    public void TheCalibrationCubeIsTheSizeAskedEachWay()
    {
        var cube = new CalibrationCube();
        var size = cube.Make(cube.Default with { Size = 20 }, Printer.Default).Parts[0].Mesh.ComputeBounds().Size;

        Assert.Equal(20f, size.X, 3);
        Assert.Equal(20f, size.Y, 3);
        Assert.Equal(20f, size.Z, 3);
    }

    [Fact]
    public void AThreadFitTestIsABoltAndANutForEveryClearance()
    {
        var test = new ThreadFitTest();
        var made = test.Make(test.Default with { Pairs = 3 }, Printer.Default);

        Assert.Equal(6, made.Parts.Count);
        Assert.Contains(made.Parts, p => p.Name == "M8 bolt 0.3");
        Assert.Contains(made.Parts, p => p.Name == "M8 nut 0.1");
    }

    [Fact]
    public void TheHoleGaugeLeavesOutWhatIsTurnedOff()
    {
        var gauge = new HoleGauge();
        double Volume(HoleGauge.Settings s) => gauge.Make(s, Printer.Default).Parts[0].Mesh.ComputeSignedVolume();

        var all = gauge.Default;
        Assert.True(Volume(all with { Pegs = false }) < Volume(all));
        Assert.True(Volume(all with { Sideways = false }) < Volume(all));
    }
}
