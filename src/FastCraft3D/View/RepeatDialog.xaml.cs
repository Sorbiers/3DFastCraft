using System.Globalization;
using System.Numerics;
using System.Windows;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks how to repeat the selection.
///
/// It exists because a staircase used to be a dozen boxes typed in one at a time - name, three
/// sizes and three positions apiece - and so did a row of dowels, and a wall of openings. A step
/// and a rise per copy covers all three.
/// </summary>
public partial class RepeatDialog : Window
{
    private readonly IReadOnlyList<SceneObject> subjects;

    public RepeatSettings? Result { get; private set; }

    public RepeatDialog(IReadOnlyList<SceneObject> objects)
    {
        InitializeComponent();

        subjects = objects;
        SubjectText.Text = objects.Count == 1
            ? $"Repeating {objects[0].Name}."
            : $"Repeating {objects.Count} objects.";

        Loaded += (_, _) => { CountBox.SelectAll(); CountBox.Focus(); Describe(); };
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Describe();

    private void Describe()
    {
        if (SummaryText is null) return;

        var settings = Read();
        if (settings is not { } s || s.Copies < 1)
        {
            SummaryText.Text = "Nothing to make.";
            return;
        }

        var span = s.Step * s.Copies;
        int made = s.Copies * subjects.Count;

        string reach = span.Length() < 1e-4f
            ? "stacked in the same place"
            : $"reaching {span.X:0.##}, {span.Y:0.##}, {span.Z:0.##} mm from where it starts";

        string grown = s.Grow.Length() < 1e-4f
            ? ""
            : $" The last one is {s.Grow.X * s.Copies:0.##} x {s.Grow.Y * s.Copies:0.##} x " +
              $"{s.Grow.Z * s.Copies:0.##} mm larger than the first.";

        SummaryText.Text = $"{made} new object(s), {reach}.{grown}";
    }

    private RepeatSettings? Read()
    {
        if (!int.TryParse(CountBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int count))
            return null;

        return new RepeatSettings(
            Math.Clamp(count, 1, RepeatArray.MaximumCopies),
            new Vector3(Number(StepXBox?.Text), Number(StepYBox?.Text), Number(StepZBox?.Text)),
            new Vector3(Number(GrowXBox?.Text), Number(GrowYBox?.Text), Number(GrowZBox?.Text)),
            AnchorBox?.IsChecked == true);
    }

    private static float Number(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : 0f;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Read() is not { } settings) return;

        Result = settings;
        DialogResult = true;
    }
}
