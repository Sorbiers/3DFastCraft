using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The catalogue: what it remembers, the pictures it keeps, and what it lists.</summary>
public class LibraryTests
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

    private static string Temporary(string extension) => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + extension);

    [Fact]
    public void StarsRecentUseAndPresetsAreRememberedBetweenSessions()
    {
        string path = Temporary(".json");
        try
        {
            var memory = LibraryMemory.Load(path);
            memory.SetFavourite("box.open", true);
            memory.Used("a");
            memory.Used("b");
            memory.SavePreset(Box, "Screws", Box.Default with { Width = 33 });

            var again = LibraryMemory.Load(path);
            Assert.True(again.IsFavourite("box.open"));
            Assert.Equal(["b", "a"], again.Recent);
            Assert.Equal(["Screws"], again.PresetNames("box.open"));
            Assert.Equal(33f, ((OpenBox.Settings)again.Preset(Box, "Screws")!).Width);

            again.ForgetPreset("box.open", "Screws");
            Assert.Empty(LibraryMemory.Load(path).PresetNames("box.open"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecentUseKeepsTheLastFewMostRecentFirstEachOnce()
    {
        var memory = LibraryMemory.Temporary();
        foreach (string id in new[] { "a", "b", "c", "a", "d", "e", "f", "g" }) memory.Used(id);

        Assert.Equal(["g", "f", "e", "d", "a", "c"], memory.Recent);
    }

    [Fact]
    public void ADamagedMemoryFileStartsWithNothingRemembered()
    {
        string path = Temporary(".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            var memory = LibraryMemory.Load(path);

            Assert.Empty(memory.Favourites);
            Assert.Empty(memory.Recent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void APictureIsDrawnOnceAndReadFromDiskAfterThat()
    {
        string folder = Temporary("");
        try
        {
            var pictures = new LibraryPictures(folder);

            var png = pictures.Get(Box);
            Assert.NotNull(png);
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png!.Take(4));
            Assert.EndsWith("box.open.v1.png", pictures.PathOf(Box));

            // What is on disk is what comes back: nothing is drawn a second time.
            File.WriteAllBytes(pictures.PathOf(Box), [1, 2, 3]);
            Assert.Equal([1, 2, 3], pictures.Get(Box));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void TheCatalogueListsByCategoryWithStarredAndRecentAtTheTop() => RunSta(() =>
    {
        var memory = LibraryMemory.Temporary();
        var view = new LibraryView(GeneratorRegistry.All, memory, null);

        Assert.Equal(GeneratorRegistry.All.Select(g => g.Category).Distinct(), view.Headings);

        Find<ToggleButton>(view, "LibraryStar.box.open").IsChecked = true;
        Find<ToggleButton>(view, "LibraryStar.box.open").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        memory.Used("box.open");

        view.SearchText = "";
        Assert.Equal(["Favourites", "Recently used"], view.Headings.Take(2));
        Assert.True(memory.IsFavourite("box.open"));
    });

    [Theory]
    [InlineData("box", true)]
    [InlineData("BOX open", true)]
    [InlineData("pen pot", true)]
    [InlineData("gearbox", false)]
    public void SearchFindsEveryWordInTheNameCategoryDescriptionOrPresets(string wanted, bool found) => RunSta(() =>
    {
        var view = new LibraryView(GeneratorRegistry.All, LibraryMemory.Temporary(), null) { };
        view.SearchText = wanted;

        Assert.Equal(found, view.Shown().Any(g => g.Id == "box.open"));
    });

    [Fact]
    public void ChoosingATileSaysWhichGeneratorWasChosen() => RunSta(() =>
    {
        var view = new LibraryView(GeneratorRegistry.All, LibraryMemory.Temporary(), null);
        Generator? chosen = null;
        view.Chosen += g => chosen = g;

        Find<Button>(view, "Library.box.open").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Assert.Equal("box.open", chosen?.Id);
    });

    [Fact]
    public void APresetFillsEveryBoxAndOneOfYourOwnIsKeptForNextTime() => RunSta(() =>
    {
        var memory = LibraryMemory.Temporary();
        var context = GeneratorContext.Default with { Memory = memory };
        var view = new GeneratorView(Box, null, context, (_, _) => { }, live: false);

        Assert.Contains("Pen pot", view.PresetLabels);
        view.ChoosePreset("Pen pot");
        Assert.Equal((70f, 70f, 100f, 35f), Sizes((OpenBox.Settings)view.Current));
        Assert.True(view.CanInsert);

        view.KeepPreset("  My pot ");
        Assert.Contains("My pot (mine)", view.PresetLabels);

        var next = new GeneratorView(Box, null, context, (_, _) => { }, live: false);
        next.ChoosePreset("My pot (mine)");
        Assert.Equal((70f, 70f, 100f, 35f), Sizes((OpenBox.Settings)next.Current));

        static (float, float, float, float) Sizes(OpenBox.Settings s) => (s.Width, s.Depth, s.Height, s.Radius);
    });

    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(e => AutomationProperties.GetAutomationId(e) == id);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }
}
