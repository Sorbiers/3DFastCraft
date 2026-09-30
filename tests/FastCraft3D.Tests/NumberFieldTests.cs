using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

public class NumberFieldTests
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

    private static TextBox Box(string text, bool whole = false)
    {
        var box = new TextBox();
        NumberField.SetIsOn(box, true);
        NumberField.SetWhole(box, whole);
        box.Text = text;
        return box;
    }

    [Theory]
    [InlineData("10", "+=5", "15")]
    [InlineData("10", "-=2.5", "7.5")]
    [InlineData("10", "+5", "15")]
    [InlineData("10.5", "*=2", "21")]
    [InlineData("10.5", "/=2", "5.25")]
    [InlineData("10.5", "*=150%", "15.75")]
    public void ChangeByAndTimesAreWorkedOutAgainstWhatWasThere(string was, string typed, string comes)
    {
        RunSta(() =>
        {
            var box = Box(was);
            box.Text = typed;

            Assert.True(NumberField.Resolve(box));
            Assert.Equal(double.Parse(comes, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(box.Text, System.Globalization.CultureInfo.CurrentCulture), 4);
        });
    }

    [Fact]
    public void ACountStaysAWholeNumber()
    {
        RunSta(() =>
        {
            var box = Box("5", whole: true);
            box.Text = "/=2";
            Assert.True(NumberField.Resolve(box));
            Assert.DoesNotMatch(new Regex(@"[.,]"), box.Text);
        });
    }

    [Fact]
    public void APlainNumberIsLeftAsTyped()
    {
        RunSta(() =>
        {
            var box = Box("10");
            box.Text = "-4";
            Assert.False(NumberField.Resolve(box));
            Assert.Equal("-4", box.Text);
        });
    }

    [Fact]
    public void ANudgeMovesByTheStepAndSnapsToIt()
    {
        RunSta(() =>
        {
            NumberField.Step = () => 1.0;
            var box = Box("10");
            NumberField.Nudge(box, 1);
            Assert.Equal("11", box.Text);
            NumberField.Nudge(box, -1);
            NumberField.Nudge(box, -1);
            Assert.Equal("9", box.Text);
        });
    }

    /// <summary>
    /// Every number box gets the behaviour through its style, so a box has to have one of the two.
    /// A box tagged as a number with neither was the gap that left most tool panels without it.
    /// </summary>
    [Fact]
    public void EveryNumberBoxInTheLayoutsHasAStyleThatSwitchesItOn()
    {
        var up = new DirectoryInfo(AppContext.BaseDirectory);
        while (up is not null && !Directory.Exists(Path.Combine(up.FullName, "src", "FastCraft3D"))) up = up.Parent;
        string root = Path.Combine(up!.FullName, "src", "FastCraft3D");
        var files = Directory.GetFiles(Path.Combine(root, "View"), "*.xaml").Append(Path.Combine(root, "MainWindow.xaml"));

        var bare = new List<string>();
        foreach (var file in files)
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"<TextBox\b[^>]*>", RegexOptions.Singleline))
                if (m.Value.Contains("Tag=\"number\"") && !Regex.IsMatch(m.Value, @"Resource (NumberBox|GizmoBox)\}"))
                    bare.Add($"{Path.GetFileName(file)}: {m.Value[..Math.Min(80, m.Value.Length)]}");

        Assert.Empty(bare);
    }
}
