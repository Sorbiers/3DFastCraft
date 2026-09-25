using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The panel a generator's settings record becomes, driven as someone typing into it would.</summary>
public class GeneratorViewTests
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

    private static (GeneratorView View, List<Generated?> Previews) Open(object? start = null, GeneratorContext? context = null)
    {
        var previews = new List<Generated?>();
        var view = new GeneratorView(Box, start, context ?? GeneratorContext.Default, (_, made) => previews.Add(made), live: false);
        return (view, previews);
    }

    private static T Find<T>(DependencyObject root, string automationId) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == automationId);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    [Fact]
    public void ItOpensShowingTheDefaultsAndTakesThePreviewAwayWhenStopped() => RunSta(() =>
    {
        var (view, previews) = Open();

        var first = Assert.Single(previews);
        Assert.NotNull(first);
        Assert.True(view.CanInsert);
        Assert.StartsWith("60 x 40 x 30 mm", view.SummaryText);

        view.Stop();
        Assert.Null(previews[^1]);
    });

    [Fact]
    public void ANumberOutsideItsRangeIsSaidAndNothingIsBuiltFromIt() => RunSta(() =>
    {
        var (view, previews) = Open();

        Find<TextBox>(view, "box.open.Width").Text = "900";

        Assert.Single(previews);
        Assert.False(view.CanInsert);
        Assert.Equal("Width goes from 5 to 200 mm.", view.SummaryText);
    });

    [Fact]
    public void SettingsThatCannotBeMadeSayWhyAndTakeThePreviewAway() => RunSta(() =>
    {
        var (view, previews) = Open();

        Find<TextBox>(view, "box.open.Wall").Text = "20";

        Assert.Null(previews[^1]);
        Assert.False(view.CanInsert);
        Assert.Contains("leave no room inside", view.SummaryText);
        Assert.True(view.ShowsProblem, "the reason is said in red");

        Find<TextBox>(view, "box.open.Wall").Text = "1.6";
        Assert.False(view.ShowsProblem);
    });

    [Fact]
    public void InsertHandsBackTheSettingsTypedAndWhatTheyMade() => RunSta(() =>
    {
        var (view, _) = Open();
        bool? finished = null;
        view.Finished += accepted => finished = accepted;

        Find<TextBox>(view, "box.open.Height").Text = "12.5";
        view.PressInsert();

        Assert.True(finished);
        Assert.Equal(Box.Default with { Height = 12.5f }, view.Result);
        Assert.Equal(12.5f, view.Made!.Parts[0].Mesh.ComputeBounds().Size.Z, 3);
    });

    [Fact]
    public void LengthsAreTypedInTheUnitTheTransformBoxesUse() => RunSta(() =>
    {
        var inches = GeneratorContext.Default with { Unit = new DisplayUnit("in", 25.4f) };
        var (view, _) = Open(context: inches);

        var width = Find<TextBox>(view, "box.open.Width");
        Assert.Equal((60f / 25.4f).ToString("0.###"), width.Text);

        width.Text = "4";
        Assert.Equal(101.6f, ((OpenBox.Settings)view.Current).Width, 3);
    });

    [Fact]
    public void AWholeNumberTypedIntoItsBoxIsKeptAsOne() => RunSta(() =>
    {
        // Typed into the gear train's Stages, this came back a decimal, and building the settings
        // record from it threw out of the panel and took the app down.
        var train = new FastCraft3D.Generators.Mechanisms.GearTrain();
        var view = new GeneratorView(train, null, GeneratorContext.Default, (_, _) => { }, live: false);

        Find<TextBox>(view, "mechanism.gear-train.Stages").Text = "3";

        Assert.Equal(3, ((FastCraft3D.Generators.Mechanisms.GearTrain.Settings)view.Current).Stages);
        Assert.True(view.CanInsert);
    });

    [Fact]
    public void ItOpensWhereItWasLastLeft() => RunSta(() =>
    {
        var (view, _) = Open(Box.Default with { Depth = 77f });

        Assert.Equal("77", Find<TextBox>(view, "box.open.Depth").Text);
    });
}
