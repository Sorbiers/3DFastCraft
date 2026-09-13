using System.Globalization;
using System.Windows.Data;

namespace FastCraft3D.View;

/// <summary>
/// A number for a box, or nothing when the selected objects do not share one.
///
/// The view model says "they differ" with NaN. Shown as it stands that reads "NaN", and shown as
/// a 0 it reads as a value that is not true; empty is the only honest thing to put in the box.
/// </summary>
public sealed class MixedNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is float number && float.IsFinite(number)
            ? number.ToString(parameter as string ?? "0.####", culture)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && float.TryParse(text, NumberStyles.Float, culture, out float number)
            ? number
            : Binding.DoNothing;
}
