using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.View;

/// <summary>
/// Draws the outlines a stamp is made of, so the panel can show the shape rather than a file
/// name. The same loops that will be cut or raised, which is the point: what is on screen is
/// what will be on the object, holes and all.
/// </summary>
public sealed class OutlinesToGeometryConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<TextShape> shapes) return null;

        var drawn = new StreamGeometry { FillRule = FillRule.EvenOdd };
        bool any = false;

        using (var context = drawn.Open())
            foreach (var shape in shapes)
            {
                any |= Trace(context, shape.Outline);

                foreach (var hole in shape.Holes)
                    Trace(context, hole);
            }

        if (!any) return null;

        drawn.Freeze();
        return drawn;
    }

    private static bool Trace(StreamGeometryContext context, IReadOnlyList<Vector2> loop)
    {
        if (loop.Count < 3) return false;

        // Millimetres measure up the page and the screen measures down it.
        context.BeginFigure(new Point(loop[0].X, -loop[0].Y), isFilled: true, isClosed: true);

        for (int i = 1; i < loop.Count; i++)
            context.LineTo(new Point(loop[i].X, -loop[i].Y), isStroked: false, isSmoothJoin: false);

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
