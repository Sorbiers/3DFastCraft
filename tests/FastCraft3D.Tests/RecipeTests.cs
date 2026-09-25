using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// A generated part remembers how it was made for exactly as long as it is still what was made,
/// carries that through the project file, and is made again where it stood.
/// </summary>
public class RecipeTests
{
    private static readonly OpenBox Box = new();

    private static SceneObject Made(OpenBox.Settings settings, string? role = "box", string? set = null)
    {
        var part = Box.Make(settings, Printer.Default).Parts[0];
        return new SceneObject("Box", part.Mesh) { Recipe = Recipes.For(Box, settings, role, set) }.CentredOn(Vector3.Zero);
    }

    [Fact]
    public void ARecipeStaysThroughAMoveATurnAndAResize()
    {
        var o = Made(Box.Default);

        o.Position = new Vector3(10, 20, 0);
        o.Rotation = new Vector3(0, 0, 45);
        o.Scale = new Vector3(2, 2, 2);

        Assert.NotNull(o.Recipe);
    }

    [Fact]
    public void ANewMeshDropsTheRecipeAndPuttingTheOldOneBackRestoresIt()
    {
        var o = Made(Box.Default);
        var before = o.Mesh;

        // What a smoothing preview does: its own mesh in, then the old one back on cancel.
        o.Mesh = before.Clone();
        Assert.Null(o.Recipe);

        o.Mesh = before;
        Assert.NotNull(o.Recipe);
    }

    [Fact]
    public void MovingThePivotKeepsTheRecipeAndWhereTheGeneratorsOriginIs()
    {
        var o = Made(Box.Default);
        var originOnThePlate = Vector3.Transform(o.Recipe!.Origin, o.Transform);

        o.CentredOn(new Vector3(30, 20, 30));

        Assert.NotNull(o.Recipe);
        Assert.Equal(new Vector3(-30, -20, -30), o.Recipe!.Origin);
        Assert.Equal(originOnThePlate, Vector3.Transform(o.Recipe.Origin, o.Transform));
    }

    [Fact]
    public void ACopyCanBeEditedAsTheOriginalCan()
    {
        var o = Made(Box.Default);
        Assert.Equal(o.Recipe, o.Clone().Recipe);
    }

    [Fact]
    public void TheRecipeIsKeptInTheProjectFileAndAnOldFileHasNone()
    {
        var scene = new Scene();
        var made = Made(Box.Default with { Width = 77 }, set: "abc");
        made.CentredOn(new Vector3(1, 2, 3));
        scene.Objects.Add(made);
        scene.Objects.Add(new SceneObject("Plain", Primitives.Box(10, 10, 10)));

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + SceneSerializer.Extension);
        try
        {
            SceneSerializer.Save(path, scene);
            var loaded = SceneSerializer.Load(path);

            Assert.Equal(made.Recipe, loaded[0].Recipe);
            Assert.Null(loaded[1].Recipe);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SettingsComeBackOutOfARecipeAsTheyWentIn()
    {
        var settings = Box.Default with { Width = 71.5f, Radius = 0 };
        Assert.Equal(settings, Recipes.Read(Box, Recipes.Write(Box, settings)));
    }

    [Theory]
    [InlineData("""{"Width":80}""", 80f)]
    [InlineData("""{"Width":80,"Retired":true}""", 80f)]
    [InlineData("""{"Width":"wide"}""", 60f)]
    [InlineData("""{"Width":9000}""", 200f)]
    [InlineData("""not json""", 60f)]
    public void ARecipeReadsForgivinglyWithAnythingMissingOrWrongAtItsDefault(string json, float width)
    {
        var read = (OpenBox.Settings)Recipes.Read(Box, json);

        Assert.Equal(width, read.Width);
        Assert.Equal(Box.Default.Depth, read.Depth);
    }

    [Theory]
    [InlineData(0.4f, 1.6f)]
    [InlineData(0.6f, 1.8f)]
    [InlineData(0.25f, 1.75f)]
    public void AWallStartsAtWholeNozzleWidthsNoThinnerThanTheGeneratorAsks(float nozzle, float wall)
    {
        var settings = (OpenBox.Settings)Box.Defaults(Printer.Default with { Nozzle = nozzle });
        Assert.Equal(wall, settings.Wall, 4);
    }

    [Fact]
    public void ThePrinterIsRememberedAndADamagedFileGivesTheDefault()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var printer = new Printer(0.6f, 0.3f, 0.15f);
            PrinterProfile.Save(printer, path);
            Assert.Equal(printer, PrinterProfile.Load(path));

            File.WriteAllText(path, "{ not json");
            Assert.Equal(Printer.Default, PrinterProfile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AWiderBoxIsMadeAgainAboutWhereTheOldOneStoodWithItsNameAndTurn()
    {
        var old = Made(Box.Default);
        old.Name = "Parts tray";
        old.Colour = new Vector3(1, 0, 0);
        old.Position = new Vector3(40, -30, 0);
        old.Rotation = new Vector3(0, 0, 90);

        // Its pivot moved since, as the pivot tool would: the generator's origin has to be found
        // through the recipe, not assumed to be the object's origin.
        old.CentredOn(new Vector3(10, 5, 15));

        var wider = Box.Default with { Width = 100 };
        var produced = MainViewModel.Remade([old], Box, wider, Box.Make(wider, Printer.Default), () => Vector3.One);

        var o = Assert.Single(produced);
        Assert.Equal(("Parts tray", new Vector3(1, 0, 0), new Vector3(0, 0, 90)), (o.Name, o.Colour, o.Rotation));
        Assert.Equal(new Vector3(40, -30, 0), Vector3.Transform(o.Recipe!.Origin, o.Transform));
        Assert.Equal(wider, Recipes.Read(Box, o.Recipe.Settings));

        // Turned a quarter, the wider box is longer along Y and still centred on the same spot.
        var bounds = o.WorldBounds;
        Assert.Equal(100f, bounds.Size.Y, 3);
        Assert.Equal(-30f, bounds.Center.Y, 3);
        Assert.Equal(0f, bounds.Min.Z, 3);
    }

    /// <summary>Two parts, and a third when asked: a set that grows a part between makes.</summary>
    private sealed class Pair : Generator<Pair.Settings>
    {
        public override string Id => "test.pair";
        public override int Version => 1;
        public override string Category => "Test";
        public override string Title => "Pair";

        public sealed record Settings([Toggle("Third")] bool Third = false);

        protected override Generated Build(Settings s, Printer printer, CancellationToken token)
        {
            var parts = new List<GeneratedPart>
            {
                new("Base", Primitives.Box(20, 20, 10), Role: "base"),
                new("Lid", Primitives.Box(20, 20, 2), Role: "lid")
            };

            if (s.Third) parts.Add(new("Insert", Primitives.Box(10, 10, 5), Role: "insert"));
            return new Generated(parts, []);
        }
    }

    [Fact]
    public void EveryPartOfASetIsMadeAgainInItsOwnPlaceAndANewPartGoesBeside()
    {
        var pair = new Pair();
        var first = pair.Make(pair.Default, Printer.Default);
        var members = first.Parts.Select((p, i) => new SceneObject(p.Name, p.Mesh)
        {
            Recipe = Recipes.For(pair, pair.Default, p.Role, "set"),
            Position = new Vector3(i * 50, 0, 0)
        }.CentredOn(Vector3.Zero)).ToList();

        var three = new Pair.Settings(Third: true);
        var produced = MainViewModel.Remade(members, pair, three, pair.Make(three, Printer.Default), () => Vector3.One);

        Assert.Equal(["Base", "Lid", "Insert"], produced.Select(o => o.Name));
        Assert.Equal(new Vector3(0, 0, 0), produced[0].Position);
        Assert.Equal(new Vector3(50, 0, 0), produced[1].Position);
        Assert.True(produced[2].WorldBounds.Min.X > produced[1].WorldBounds.Max.X, "the new part is beside the set, not inside it");
        Assert.All(produced, o => Assert.Equal("set", o.Recipe!.Set));
    }
}

/// <summary>The panel's printer section, and the settings that follow it.</summary>
public class GeneratorPrinterTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static readonly OpenBox Box = new();

    private static System.Windows.Controls.TextBox Box_(GeneratorView view, string id) =>
        Descendants(view).OfType<System.Windows.Controls.TextBox>()
            .Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == id);

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    [Fact]
    public void AWallFollowsTheNozzleUntilSomebodyTypesOne() => RunSta(() =>
    {
        var previews = new List<Generated?>();
        var view = new GeneratorView(Box, null, GeneratorContext.Default, (_, made) => previews.Add(made), live: false);
        Printer? remembered = null;
        view.PrinterChanged += p => remembered = p;

        Assert.EndsWith("from the printer", view.UnitText("Wall"));

        Box_(view, "Printer.Nozzle").Text = "0.6";
        Assert.Equal("1.8", Box_(view, "box.open.Wall").Text);
        Assert.Equal(1.8f, ((OpenBox.Settings)view.Current).Wall, 4);
        Assert.Equal(0.6f, remembered?.Nozzle);

        Box_(view, "box.open.Wall").Text = "2.5";
        Assert.Equal("mm", view.UnitText("Wall"));

        Box_(view, "Printer.Nozzle").Text = "0.4";
        Assert.Equal("2.5", Box_(view, "box.open.Wall").Text);
    });

    [Fact]
    public void APartBeingEditedKeepsTheNumbersItWasMadeWith() => RunSta(() =>
    {
        var made = Box.Default with { Wall = 1.2f };
        var view = new GeneratorView(Box, made, GeneratorContext.Default, (_, _) => { }, "Apply", live: false);

        Assert.Equal("mm", view.UnitText("Wall"));

        Box_(view, "Printer.Nozzle").Text = "0.8";
        Assert.Equal(1.2f, ((OpenBox.Settings)view.Current).Wall);
    });

    [Fact]
    public void GoingBackToTheDefaultPutsAWallOnThePrinterAgain() => RunSta(() =>
    {
        var view = new GeneratorView(Box, Box.Default with { Wall = 3 }, GeneratorContext.Default with { Printer = new Printer(Nozzle: 0.5f) }, (_, _) => { }, live: false);

        view.Reset(Box.Parameters.ToList().FindIndex(p => p.Name == "Wall"));

        Assert.Equal(2f, ((OpenBox.Settings)view.Current).Wall, 4);
        Assert.EndsWith("from the printer", view.UnitText("Wall"));
    });
}
