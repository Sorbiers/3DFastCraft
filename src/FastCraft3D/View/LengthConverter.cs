using System.Globalization;
using System.Windows.Data;
using FastCraft3D.ViewModels;

namespace FastCraft3D.View;

/// <summary>
/// A length the model keeps in millimetres, shown and typed in the unit the app is set to. For the
/// tool panels' boxes, which said "mm" whatever the transform boxes beside them were reading.
///
/// Formatted here rather than by the binding's StringFormat: two places fit a length in
/// millimetres, and 0.8 mm in inches is 0.0315, which "N2" rounds to nothing.
/// </summary>
public sealed class LengthConverter : IValueConverter
{
    /// <summary>The unit every box reads in. Set by the view model when the unit changes.</summary>
    public static MeasureUnit Unit { get; set; } = MeasureUnit.Default;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double mm = value switch { float f => f, double d => d, _ => double.NaN };
        if (!double.IsFinite(mm)) return value?.ToString() ?? "";

        double shown = Unit.From((float)mm);
        return shown.ToString(Unit.Millimetres <= 1f ? "0.###" : "0.####", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString() ?? "";
        if (!double.TryParse(text, NumberStyles.Float, culture, out double typed)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out typed))
            return Binding.DoNothing;

        double mm = Unit.To((float)typed);
        return targetType == typeof(double) ? mm : (object)(float)mm;
    }
}
