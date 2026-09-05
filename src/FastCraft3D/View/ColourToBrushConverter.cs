using System.Globalization;
using System.Numerics;
using System.Windows.Data;
using System.Windows.Media;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Paints a swatch from the 0..1 vector the scene stores. Frozen brushes, because the swatch
/// grid rebuilds one per object on every selection change.
/// </summary>
public sealed class ColourToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Vector3 colour) return Brushes.Transparent;

        var (r, g, b) = Palette.ToBytes(colour);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
