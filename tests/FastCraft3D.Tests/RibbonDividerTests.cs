using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// The dividers between groups of ribbon buttons, at every mode.
///
/// A divider belongs to the group it opens, so it has to be hidden whenever that group is. Get it
/// wrong and the tab shows a line with nothing beside it - or, when a whole group moves up a mode,
/// two lines together with a gap between them. That has now been reported twice, from two
/// different causes, and by eye it is almost invisible until somebody points at it.
///
/// Read out of the markup rather than out of a running window: what decides this is which
/// visibility binding sits on which element, and that is a fact about the file.
/// </summary>
public class RibbonDividerTests(ITestOutputHelper log)
{
    private static readonly Regex Tabs =
        new(@"<TabItem Header=""(?<name>[^""]+)"">(?<body>.*?)</TabItem>", RegexOptions.Singleline);

    private static readonly Regex Items =
        new(@"<(?<kind>Separator|Button|ToggleButton)\b(?<attrs>.*?)/>", RegexOptions.Singleline);

    private static string Markup()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    /// <summary>Which mode an element first appears in: 0 Classic, 1 Advanced, 2 Extended.</summary>
    private static int LevelOf(string attributes) =>
        attributes.Contains("IsExtendedMode") ? 2
        : attributes.Contains("IsAdvancedMode") ? 1
        : 0;

    [Theory]
    [InlineData(0, "Classic")]
    [InlineData(1, "Advanced")]
    [InlineData(2, "Extended")]
    public void NoTabShowsADividerWithNothingBesideIt(int level, string mode)
    {
        string markup = Markup();
        var tabs = Tabs.Matches(markup);

        Assert.True(tabs.Count >= 6, $"only {tabs.Count} tabs found - the markup is not being read");

        var faults = new List<string>();

        foreach (Match tab in tabs)
        {
            string name = tab.Groups["name"].Value;

            var shown = Items.Matches(tab.Groups["body"].Value)
                .Select(m => (Kind: m.Groups["kind"].Value, Level: LevelOf(m.Groups["attrs"].Value)))
                .Where(item => item.Level <= level)
                .Select(item => item.Kind)
                .ToList();

            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i] != "Separator") continue;

                if (i == 0) faults.Add($"{name}: a divider opens the tab");
                else if (i == shown.Count - 1) faults.Add($"{name}: a divider closes the tab");
                else if (shown[i - 1] == "Separator" || shown[i + 1] == "Separator")
                    faults.Add($"{name}: two dividers together, with an empty group between them");
            }
        }

        foreach (string fault in faults) log.WriteLine($"{mode}: {fault}");

        Assert.Empty(faults);
    }
}
