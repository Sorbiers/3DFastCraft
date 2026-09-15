using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// What one of the shape-bending tools asks for, and how it does it.
/// </summary>
/// <param name="Deform">The tool itself: a solid in plate coordinates, the value, the direction.</param>
/// <param name="Problem">Why this value cannot be used on this solid, or null when it can.</param>
/// <param name="ChoosesAxis">Whether the tool goes one way or the other, as a bend does.</param>
/// <param name="Identity">The value that changes nothing, shown as nothing to do.</param>
public sealed record DeformSpec(
    string Title, string Verb, string Explanation, string ValueLabel, string Unit,
    double Minimum, double Maximum, double Step, double Identity,
    string LowHint, string HighHint, bool ChoosesAxis,
    Func<Mesh, float, Axis, Mesh> Deform,
    Func<Mesh, float, Axis, string?>? Problem = null);

/// <summary>
/// Asks for the one number a twist, taper or bend takes, showing the result on the plate.
///
/// One dialog for the three, because they ask the same kind of question - how much, and for a
/// bend which way - and preview the same way. The preview waits for the number to settle: a large
/// value over a dense part breaks a great many edges, and doing that on every step of a dragged
/// slider made the slider stick.
/// </summary>
public partial class DeformDialog : ToolPanel
{
    private readonly DeformSpec spec;
    private readonly IReadOnlyList<Mesh> worlds;
    private readonly Action<IReadOnlyList<Mesh>?> preview;
    private readonly DispatcherTimer settle;
    private bool updating;
    private bool usable = true;

    /// <param name="worlds">The objects in plate coordinates, as they are before the change.</param>
    /// <param name="preview">Shows changed meshes in plate coordinates, or puts the originals back when given null.</param>
    public DeformDialog(DeformSpec spec, string subject, IReadOnlyList<Mesh> worlds,
                        float start, Axis startAxis, Action<IReadOnlyList<Mesh>?> preview)
    {
        this.spec = spec;
        this.worlds = worlds;
        this.preview = preview;

        // Made before anything can move the slider. Setting its range moves it whenever zero is
        // outside the range - a taper starts at 5% - and the move asks for a preview, which found
        // no timer and threw.
        settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            ShowPreview();
        };

        InitializeComponent();

        Title = spec.Title;
        SubjectText.Text = $"{spec.Verb} {subject}. {spec.Explanation}";
        ValueLabel.Text = spec.ValueLabel;
        UnitText.Text = spec.Unit;
        LowText.Text = spec.LowHint;
        HighText.Text = spec.HighHint;
        AcceptButton.Content = spec.Title;

        AxisRow.Visibility = spec.ChoosesAxis ? Visibility.Visible : Visibility.Collapsed;
        updating = true;
        AxisY.IsChecked = startAxis == Axis.Y;
        AxisX.IsChecked = startAxis != Axis.Y;
        updating = false;

        ValueSlider.Minimum = spec.Minimum;
        ValueSlider.Maximum = spec.Maximum;
        ValueSlider.TickFrequency = spec.Step;
        ValueSlider.SmallChange = spec.Step;
        ValueSlider.LargeChange = spec.Step * 3;

        Value = start;
        ShowPreview();
    }

    /// <summary>Null until the user confirms.</summary>
    public float? Result { get; private set; }

    public Axis ResultAxis => AxisY.IsChecked == true ? Axis.Y : Axis.X;

    private float Value
    {
        get => (float)ValueSlider.Value;
        set
        {
            updating = true;
            ValueSlider.Value = Math.Clamp(value, spec.Minimum, spec.Maximum);
            ValueBox.Text = ValueSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
            updating = false;
        }
    }

    private void ShowPreview()
    {
        var axis = ResultAxis;
        float value = Value;

        string? problem = spec.Problem is null
            ? null
            : worlds.Select(w => spec.Problem(w, value, axis)).FirstOrDefault(p => p is not null);

        usable = problem is null;
        ProblemText.Text = problem ?? "";
        ProblemText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        AcceptButton.IsEnabled = usable;

        if (!usable || Math.Abs(value - spec.Identity) < 1e-3)
        {
            preview(null);
            SummaryText.Text = usable ? "Nothing to change at this value." : "The shape is shown as it is.";
            return;
        }

        var changed = worlds.Select(w => spec.Deform(w, value, axis)).ToList();
        preview(changed);

        int before = worlds.Sum(w => w.TriangleCount);
        int after = changed.Sum(m => m.TriangleCount);
        bool capped = changed.Any(m => m.TriangleCount >= MeshDeform.TriangleBudget);

        SummaryText.Text =
            $"{after:N0} triangles, from {before:N0}. Edges are broken up only where the curve needs them."
            + (capped ? "\nThis is as fine as it goes - the curve will show flats at this much." : "")
            + "\nThe object is rebuilt as it now stands, so its turn and scale are made part of the shape.";
    }

    private void Later()
    {
        settle.Stop();
        settle.Start();
    }

    private void OnValueDragged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || !IsInitialized) return;

        updating = true;
        ValueBox.Text = ValueSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
        updating = false;
        Later();
    }

    private void OnValueTyped(object sender, TextChangedEventArgs e)
    {
        if (updating || !IsInitialized) return;
        if (!float.TryParse(ValueBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float typed)) return;

        // A typed value keeps its own precision; only the slider moves in steps.
        updating = true;
        ValueSlider.IsSnapToTickEnabled = false;
        ValueSlider.Value = Math.Clamp(typed, spec.Minimum, spec.Maximum);
        ValueSlider.IsSnapToTickEnabled = true;
        updating = false;
        Later();
    }

    private void OnAxisChanged(object sender, RoutedEventArgs e)
    {
        if (updating || !IsInitialized) return;
        Later();
    }

    protected override void OnClosed(EventArgs e)
    {
        settle.Stop();
        if (Result is null) preview(null);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        settle.Stop();
        ShowPreview();
        if (!usable) return;

        Result = Value;
        DialogResult = true;
    }
}
