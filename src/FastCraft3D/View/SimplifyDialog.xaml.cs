using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Asks how much of a mesh to keep.
///
/// Stated as a proportion rather than a triangle count, because that is the judgement being made
/// - how much detail this shape can spare - and the count varies wildly between objects. Both
/// numbers are shown, since the count is what will be printed and passed around.
///
/// No live preview here, unlike smoothing and rounding. Simplifying a rebuilt model means
/// hundreds of thousands of edge collapses, which is a second or two of work rather than the
/// instant a slider needs, so it happens once when the dialog is accepted.
/// </summary>
public partial class SimplifyDialog : Window
{
    private readonly int triangles;

    public SimplifyDialog(IReadOnlyList<SceneObject> objects)
    {
        InitializeComponent();

        triangles = objects.Sum(o => o.Mesh.TriangleCount);

        SubjectText.Text = objects.Count == 1
            ? $"Simplifying {objects[0].Name}, {triangles:N0} triangles."
            : $"Simplifying {objects.Count} objects, {triangles:N0} triangles between them.";

        // Somewhere useful to start: a quarter is a big saving and rarely visible on the sort of
        // dense mesh anyone reaches for this with.
        AmountSlider.Value = 25;
        Describe();
    }

    /// <summary>The fraction to keep, or null when the dialog was cancelled.</summary>
    public float? Result { get; private set; }

    private float Keep => (float)(AmountSlider.Value / 100.0);

    private void Describe()
    {
        if (!IsInitialized) return;

        int after = Math.Max((int)(triangles * Keep), 4);

        AmountText.Text = $"{AmountSlider.Value:0}%";
        SummaryText.Text = $"{triangles:N0} triangles becomes about {after:N0}."
                           + (Keep < 0.05f
                               ? " That is a severe reduction, and it will show."
                               : Keep > 0.8f
                                   ? " Barely a reduction - there is little point below about 80%."
                                   : "");
    }

    private void OnAmountChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Describe();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Keep;
        DialogResult = true;
    }
}
