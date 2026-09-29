using System.IO;
using System.Text.Json;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Calibration;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

public class PrinterProfileTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "3dfc-test-" + Guid.NewGuid().ToString("N"), "printers.json");

    [Fact]
    public void SeveralProfilesAreKeptByNameWithTheOneInUse()
    {
        string path = TempFile();
        var pla = new Printer(0.4f, 0.2f, 0.2f) { Name = "MK4 PLA", HoleClearance = 0.15f, BrickFit = -0.1f, ThreadClearance = 0.25f };
        var petg = pla with { Name = "MK4 PETG", XyClearance = 0.3f, BrickFit = -0.15f };

        PrinterProfile.SaveAll(new PrinterProfiles([pla, petg], "MK4 PETG"), path);
        var back = PrinterProfile.LoadAll(path);

        Assert.Equal(2, back.All.Count);
        Assert.Equal(petg, back.InUse);
        Assert.Equal(pla, back.All.Single(p => p.Name == "MK4 PLA"));
    }

    [Fact]
    public void SavingAPrinterReplacesItsNamesakeAndPutsItInUse()
    {
        string path = TempFile();
        var a = new Printer { Name = "A" };
        PrinterProfile.SaveAll(new PrinterProfiles([a, new Printer { Name = "B" }], "B"), path);

        PrinterProfile.Save(a with { HoleClearance = 0.35f }, path);
        var back = PrinterProfile.LoadAll(path);

        Assert.Equal(2, back.All.Count);
        Assert.Equal("A", back.Current);
        Assert.Equal(0.35f, back.InUse.HoleClearance);
    }

    [Fact]
    public void TheOnePrinterKeptBeforeProfilesBecomesTheFirstOfThem()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "printer.json"),
            JsonSerializer.Serialize(new { Nozzle = 0.6f, Layer = 0.3f, XyClearance = 0.25f }));

        var back = PrinterProfile.LoadAll(path);

        var only = Assert.Single(back.All);
        Assert.Equal(0.6f, only.Nozzle);
        Assert.Equal(0.25f, only.XyClearance);
        Assert.Equal(Printer.Default.HoleClearance, only.HoleClearance);
    }

    [Fact]
    public void AClearanceStartsFromThePrintersFitOfItsKind()
    {
        var printer = new Printer(0.4f, 0.2f, 0.3f) { ThreadClearance = 0.45f };
        var thread = new ScrewThread();
        var clearance = thread.Parameters.Single(p => p.Name == "Clearance");

        Assert.Equal(PrinterFit.Thread, clearance.Fit);
        Assert.Equal(0.45f, (float)clearance.DefaultFor(printer), 3);

        var lid = new FastCraft3D.Generators.Boxes.LiddedBox().Parameters.Single(p => p.Name == "Fit");
        Assert.Equal(0.3f, (float)lid.DefaultFor(printer), 3);
    }

    [Fact]
    public void TheProfileTestTriesEachFitEitherSideOfTheProfilesNumber()
    {
        var printer = new Printer(0.4f, 0.2f, 0.25f) { HoleClearance = 0.2f, BrickFit = -0.1f, ThreadClearance = 0.3f };
        var test = new ProfileTest();
        var made = test.Make(test.Default, printer);

        Assert.False(made.IsRefused);
        foreach (var row in new[] { "sliding.", "holes.", "bricks.", "threads." })
            Assert.Contains(made.Parts, p => p.Role!.StartsWith(row, StringComparison.Ordinal));
        Assert.All(made.Parts, p => Assert.True(p.Mesh.CheckHealth().IsWatertight, p.Name));

        // Nothing left to test is refused rather than made empty.
        Assert.True(test.Make(test.Default with { Sliding = false, Holes = false, Bricks = false, Threads = false }, printer).IsRefused);
    }

    [Fact]
    public void TakingUpAPrinterMovesTheToolsFitsToIt()
    {
        var model = new MainViewModel();
        model.UsePrinter(new Printer { HoleClearance = 0.3f, BrickFit = -0.2f });

        Assert.Equal(0.3f, model.ConnectorClearance, 3);
        Assert.Equal(-0.2f, model.ConnectorBrickFit, 3);
        Assert.Equal(-0.2f, model.EngraveStudFit, 3);
    }
}
