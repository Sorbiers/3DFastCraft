using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.ViewModels;

namespace FastCraft3D.View;

/// <summary>
/// Lengths in the tool panels that fill their boxes from code, shown and typed in the unit the app
/// is set to. The main window's boxes go through <see cref="LengthConverter"/>; these panels set
/// and read <c>Text</c> themselves, so each length box is marked <c>Units.Length</c> in XAML and
/// read and written through here, and its "mm" label is marked <c>Units.Label</c>.
/// </summary>
/// <remarks>
/// The tools still get millimetres: only the text in the box is in the other unit. Boxes that are
/// not lengths - counts, angles, gamma - go through the same calls and are left as typed.
/// </remarks>
public static partial class Units
{
    public static readonly DependencyProperty LengthProperty = DependencyProperty.RegisterAttached(
        "Length", typeof(bool), typeof(Units), new PropertyMetadata(false, OnLength));

    // A starting value written in the XAML is in millimetres: said again in the unit once the box
    // is read in, before the panel's own code fills in anything of its own.
    private static void OnLength(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true) return;

        box.Initialized += (_, _) =>
        {
            if (Unit.Millimetres != 1f
                && float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float mm)
                && float.IsFinite(mm))
                box.Put(mm);
        };
    }

    public static bool GetLength(DependencyObject d) => (bool)d.GetValue(LengthProperty);
    public static void SetLength(DependencyObject d, bool value) => d.SetValue(LengthProperty, value);

    /// <summary>On a TextBlock: the word "mm" in it is said in the app's unit.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.RegisterAttached(
        "Label", typeof(bool), typeof(Units), new PropertyMetadata(false, OnLabel));

    public static bool GetLabel(DependencyObject d) => (bool)d.GetValue(LabelProperty);
    public static void SetLabel(DependencyObject d, bool value) => d.SetValue(LabelProperty, value);

    private static MeasureUnit Unit => LengthConverter.Unit;

    [GeneratedRegex(@"\bmm\b")]
    private static partial Regex Mm();

    private static void OnLabel(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // On Loaded rather than here: the attribute can be read before the Text beside it.
        if (d is TextBlock label && e.NewValue is true)
            label.Loaded += (_, _) => label.Text = Mm().Replace(label.Text, Unit.Label);
    }

    /// <summary>What a box shows for a value: converted from millimetres when it is a length box.</summary>
    public static string Show(TextBox box, float value)
    {
        if (!GetLength(box)) return value.ToString("0.###", CultureInfo.CurrentCulture);
        return Unit.From(value).ToString(Unit.Millimetres <= 1f ? "0.###" : "0.####", CultureInfo.CurrentCulture);
    }

    /// <summary>Fills a box with a value, in its unit.</summary>
    public static void Put(this TextBox box, float value) => box.Text = Show(box, value);

    /// <summary>What a box says, in millimetres when it is a length box. False when it is not a number.</summary>
    public static bool TryRead(this TextBox? box, out float value)
    {
        value = 0f;
        if (box is null
            || !float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float typed)
            || !float.IsFinite(typed))
            return false;

        value = GetLength(box) ? Unit.To(typed) : typed;
        return true;
    }

    /// <summary>What a box says, in millimetres when it is a length box, or <paramref name="otherwise"/>.</summary>
    public static float Read(this TextBox? box, float otherwise) => box.TryRead(out float v) ? v : otherwise;
}
