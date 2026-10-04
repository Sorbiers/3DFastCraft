using System.Globalization;
using System.Windows.Data;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.View;

/// <summary>A texture's name as it reads in the lists: "Roof tiles", "Coursed stone", not "RoofTiles".</summary>
public sealed class TextureNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TextureKind kind ? TextureOptions.NameOf(kind) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
