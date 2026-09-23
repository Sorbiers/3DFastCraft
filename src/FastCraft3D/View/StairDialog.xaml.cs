using System.Globalization;
using System.Windows;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <param name="Rise">Floor to floor, in millimetres on the model.</param>
/// <param name="Run">How far it travels along the floor.</param>
/// <param name="Width">Across the flight.</param>
/// <param name="Steps">Risers, the last of which is the floor it arrives at.</param>
public readonly record struct StairSettings(float Rise, float Run, float Width, int Steps);

/// <summary>
/// Asks for a flight of steps, showing it on the plate as the numbers change, and says what it
/// would be to climb.
///
/// The numbers are the whole of the job: a riser somewhere between 150 and 190 mm on a going of
/// 240 or more is a stair, and anything else is a ladder or a ramp. At the model's scale that is
/// arithmetic nobody should be doing on paper, so the dialog does it and reports in real
/// millimetres as the numbers are typed.
/// </summary>
public partial class StairDialog : ToolPanel
{
    private readonly float scale;
    private readonly Action<StairSettings?, Mesh?> preview;

    public StairSettings? Result { get; private set; }

    /// <param name="preview">Shows the flight on the plate, or takes it away when given nulls.</param>
    public StairDialog(float modelScale, Action<StairSettings?, Mesh?> preview)
    {
        this.preview = preview;
        InitializeComponent();

        scale = modelScale <= 0 ? 1f : modelScale;
        Loaded += (_, _) => { RiseBox.SelectAll(); RiseBox.Focus(); Describe(); };
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Describe();

    private void Describe()
    {
        if (SummaryText is null) return;

        if (Read() is not { } s)
        {
            SummaryText.Text = "Fill in the four numbers.";
            preview(null, null);
            return;
        }

        var check = StairBuilder.Measure(s.Rise, s.Run, s.Steps, scale);

        string real = scale > 1.5f
            ? $" At 1:{scale:0}, that is {check.RiserMm:0} mm risers on {check.GoingMm:0} mm treads."
            : $" Risers of {check.RiserMm:0.#} mm on treads of {check.GoingMm:0.#} mm.";

        SummaryText.Text = $"{s.Steps} risers of {s.Rise / s.Steps:0.##} mm."
                         + real
                         + (check.Advice.Length > 0 ? " " + check.Advice
                            : check.IsClimbable ? " A comfortable flight." : "");

        preview(s, StairBuilder.Build(s.Rise, s.Run, s.Width, s.Steps));
    }

    private StairSettings? Read()
    {
        if (!int.TryParse(StepsBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int steps))
            return null;

        float rise = Number(RiseBox?.Text), run = Number(RunBox?.Text), width = Number(WidthBox?.Text);
        if (rise <= 0 || run <= 0 || width <= 0 || steps < 1) return null;

        return new StairSettings(rise, run, width, Math.Clamp(steps, 1, StairBuilder.MaximumSteps));
    }

    private static float Number(string? text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : 0f;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Read() is not { } settings) return;

        Result = settings;
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        preview(null, null);
        base.OnClosed(e);
    }
}
