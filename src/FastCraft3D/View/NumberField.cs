using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastCraft3D.ViewModels;

namespace FastCraft3D.View;

/// <summary>
/// What every number box in the app does, wherever it is: the whole value selected when it is
/// reached; "+=5", "-=5", "*=1.5" and "/=2" worked out against what was there; the arrow keys and
/// the wheel nudging it, Shift ten times as far and Ctrl a tenth; Enter putting it into effect.
/// </summary>
/// <remarks>
/// Switched on by the NumberBox and GizmoBox styles, so a box gets it by looking like one - the
/// tool panels, the Library's panel and Settings included. Before, the window's own handlers did
/// this for the boxes that carried a tag, and the arithmetic only for the transform boxes, so most
/// tool panels had none of it and nothing said so.
///
/// The transform boxes keep their own handling in the window: with several objects each changed on
/// its own, "+=5" has to go to each object rather than to the one number the box shows.
/// </remarks>
public static class NumberField
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.RegisterAttached(
        "IsOn", typeof(bool), typeof(NumberField), new PropertyMetadata(false, OnIsOn));

    public static bool GetIsOn(DependencyObject d) => (bool)d.GetValue(IsOnProperty);
    public static void SetIsOn(DependencyObject d, bool value) => d.SetValue(IsOnProperty, value);

    /// <summary>
    /// A box of whole numbers - a count. Said, not guessed: a length can show "10" as well, and
    /// taking that for a count rounded "-=2.5" to a whole number.
    /// </summary>
    public static readonly DependencyProperty WholeProperty = DependencyProperty.RegisterAttached(
        "Whole", typeof(bool), typeof(NumberField), new PropertyMetadata(false));

    public static bool GetWhole(DependencyObject d) => (bool)d.GetValue(WholeProperty);
    public static void SetWhole(DependencyObject d, bool value) => d.SetValue(WholeProperty, value);

    /// <summary>One nudge in the unit the boxes read in. Set by the window from the app's unit.</summary>
    public static Func<double> Step { get; set; } = () => 1.0;

    // The last plain number the box held, which "+=5" is worked out against.
    private static readonly DependencyProperty WasProperty = DependencyProperty.RegisterAttached(
        "Was", typeof(string), typeof(NumberField), new PropertyMetadata(null));

    private static void OnIsOn(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;

        box.TextChanged += (_, _) =>
        {
            // Only a plain number: "+5" is a change by five, not a new starting point.
            if (IsPlain(box.Text)) box.SetValue(WasProperty, box.Text);
        };
        box.GotKeyboardFocus += (_, _) =>
        {
            if (Skip(box)) return;
            if (IsPlain(box.Text)) box.SetValue(WasProperty, box.Text);
            box.SelectAll();
        };
        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // The first click only focuses: left to itself it would put the caret where the pointer
            // was and undo the selection just made. A second click still places the caret.
            if (Skip(box) || box.IsKeyboardFocusWithin) return;
            box.Focus();
            e.Handled = true;
        };
        box.PreviewKeyDown += (_, e) =>
        {
            if (Skip(box)) return;
            switch (e.Key)
            {
                case Key.Enter:
                    if (Resolve(box)) box.SelectAll();
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    break;
                case Key.Up:
                case Key.Down:
                    Nudge(box, e.Key == Key.Up ? 1 : -1);
                    e.Handled = true;
                    break;
            }
        };
        box.PreviewMouseWheel += (_, e) =>
        {
            // Only while the box has focus, so the wheel still scrolls the panel past it.
            if (Skip(box) || !box.IsKeyboardFocusWithin) return;
            Nudge(box, Math.Sign(e.Delta));
            e.Handled = true;
        };
        box.PreviewLostKeyboardFocus += (_, _) =>
        {
            // Before the binding reads the box on its way out.
            if (!Skip(box)) Resolve(box);
        };
    }

    private static bool Skip(TextBox box) => box.Tag is "transform" || box.IsReadOnly || !box.IsEnabled;

    // Marked in the layout, or tagged "whole" by the Library's panel, which cannot see this class.
    private static bool IsCount(TextBox box) => GetWhole(box) || box.Tag is "whole";

    private static bool IsPlain(string text) =>
        !text.TrimStart().StartsWith('+') && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) && double.IsFinite(v);

    /// <summary>
    /// "+=5", "*=1.5" and the rest put into the box as the number they come to, worked out against
    /// the last plain number it held. A count stays whole. False when
    /// the box holds anything else, which is left for the panel to read as it is.
    /// </summary>
    public static bool Resolve(TextBox box)
    {
        string text = box.Text;
        bool times = false;
        if (!FieldInput.TryParseRelative(text, out float change))
        {
            if (!FieldInput.TryParseFactor(text, out change)) return false;
            times = true;
        }

        string was = box.GetValue(WasProperty) as string ?? "";
        if (!double.TryParse(was, NumberStyles.Float, CultureInfo.CurrentCulture, out double before)) return false;

        double after = times ? before * change : before + change;
        if (IsCount(box)) after = Math.Round(after);

        box.Text = after.ToString("0.####", CultureInfo.CurrentCulture);
        box.CaretIndex = box.Text.Length;
        return true;
    }

    /// <summary>
    /// A step up or down, put into effect at once: a nudge is a finished edit on its own. Snapped
    /// onto the step, so repeated nudges tidy a value like 4.37 rather than carry its rounding
    /// along. A count moves by whole ones, since a tenth of a count is no count.
    /// </summary>
    public static void Nudge(TextBox box, int direction)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current)) return;

        double step = Step();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) step *= 10;
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) step /= 10;
        if (IsCount(box)) step = Math.Max(1, Math.Round(step));

        double updated = Math.Round((current + direction * step) / step) * step;
        box.Text = updated.ToString("0.####", CultureInfo.CurrentCulture);
        box.CaretIndex = box.Text.Length;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}
