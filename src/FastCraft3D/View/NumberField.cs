using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FastCraft3D.ViewModels;

namespace FastCraft3D.View;

/// <summary>
/// What every number box in the app does, wherever it is: the whole value selected when it is
/// reached; "+=5", "-=5", "*=1.5" and "/=2" worked out against what was there; the arrow keys and
/// the wheel nudging it, Shift ten times as far and Ctrl a tenth; Enter putting it into effect;
/// Escape taking back what was typed, and a box left holding something that is not a number going
/// back to what it held.
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

    // What the box held when it was reached, or last put into effect: what Escape goes back to.
    private static readonly DependencyProperty KeptProperty = DependencyProperty.RegisterAttached(
        "Kept", typeof(string), typeof(NumberField), new PropertyMetadata(null));

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
            // The transform boxes too: taking back a typo is the same wherever it was typed.
            if (!box.IsReadOnly) Keep(box);
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
            // Only a box with something to take back keeps Escape: an untouched one lets it
            // through, so a second press still closes the dialog or puts the tool down.
            if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && Revert(box))
            {
                e.Handled = true;
                return;
            }
            // A transform box has been put into effect by the window, which sees Enter first.
            if (e.Key == Key.Enter && box.Tag is "transform" && IsNumber(box.Text)) Keep(box);
            if (Skip(box)) return;
            switch (e.Key)
            {
                case Key.Enter:
                    if (Resolve(box)) box.SelectAll();
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    if (IsNumber(box.Text)) Keep(box);
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

            // Something that is not a number, left behind: what the box held comes back rather
            // than a red box whose value quietly stays the old one anyway. "+=5" is still a number
            // to the transform boxes, which work it out as they lose focus.
            if (!box.IsReadOnly && box.IsEnabled && !IsNumber(box.Text) && !IsChange(box.Text)) Revert(box);
        };
    }

    private static void Keep(TextBox box) => box.SetValue(KeptProperty, box.Text);

    /// <summary>
    /// Puts back what the box held when it was reached, or last put into effect. False when there
    /// was nothing to take back, or the box is not a number box.
    /// </summary>
    /// <remarks>
    /// A bound box that writes back on leaving is read again from its value rather than given its
    /// old text: that text, written back, would round the value to what the box shows.
    /// </remarks>
    public static bool Revert(TextBox box)
    {
        if (!GetIsOn(box) || box.IsReadOnly || box.GetValue(KeptProperty) is not string kept || box.Text == kept)
            return false;

        var binding = box.GetBindingExpression(TextBox.TextProperty);
        if (binding is not null && binding.ParentBinding.UpdateSourceTrigger != UpdateSourceTrigger.PropertyChanged)
            binding.UpdateTarget();
        else
            box.Text = kept;

        box.SelectAll();
        return true;
    }

    private static bool Skip(TextBox box) => box.Tag is "transform" || box.IsReadOnly || !box.IsEnabled;

    // Marked in the layout, or tagged "whole" by the Library's panel, which cannot see this class.
    private static bool IsCount(TextBox box) => GetWhole(box) || box.Tag is "whole";

    // Blank is not an error: a box reading off several objects that differ is blank.
    private static bool IsNumber(string text) =>
        string.IsNullOrWhiteSpace(text) || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double v) && double.IsFinite(v);

    private static bool IsChange(string text) =>
        FieldInput.TryParseRelative(text, out _) || FieldInput.TryParseFactor(text, out _);

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
        Keep(box);
    }
}
