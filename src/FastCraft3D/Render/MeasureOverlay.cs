using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FastCraft3D.Render;

/// <summary>
/// Draws the measuring tape: two marked points, the line between them, and the distance.
///
/// Entirely in two dimensions, over the viewport rather than in it. A tape measure that
/// disappeared behind the model would be useless - the whole point is to read it - so the line is
/// projected and drawn on top, and is never occluded.
/// </summary>
public sealed class MeasureOverlay(Canvas canvas, IScreenProjector projector)
{
    private static readonly Color Ink = Color.FromRgb(0x1A, 0x73, 0xE8);

    private Vector3? from;
    private Vector3? to;

    /// <summary>Where the tape is anchored, in world millimetres. Null when nothing is measured.</summary>
    public Vector3? From => from;

    /// <summary>How near the cursor has to be to an end to take hold of it, in pixels.</summary>
    private const double Reach = 11;

    /// <summary>
    /// Which end of the tape is under <paramref name="screen"/>: 0 for the first, 1 for the
    /// second, -1 for neither. The far end wins a tie, because it is the one just put down and
    /// so the one most likely to be wrong.
    /// </summary>
    public int EndAt(Point screen)
    {
        if (!projector.IsReady) return -1;

        if (to is { } b && projector.TryProject(b, out var pb) && Near(screen, pb)) return 1;
        if (from is { } a && projector.TryProject(a, out var pa) && Near(screen, pa)) return 0;

        return -1;
    }

    private static bool Near(Point cursor, Point marker) =>
        (cursor - marker).Length <= Reach;

    public void Show(Vector3? start, Vector3? end)
    {
        from = start;
        to = end;
        Reposition();
    }

    public void Clear() => Show(null, null);

    /// <summary>
    /// Redraws at the current camera. Cheap enough to call on every frame the camera moved,
    /// which is what keeps the tape stuck to the model rather than to the screen.
    /// </summary>
    public void Reposition()
    {
        canvas.Children.Clear();
        if (!projector.IsReady || from is not { } a) return;

        if (!projector.TryProject(a, out var pa)) return;

        if (to is not { } b)
        {
            AddMarker(pa);
            return;
        }

        if (!projector.TryProject(b, out var pb)) return;

        canvas.Children.Add(new Line
        {
            X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y,
            Stroke = new SolidColorBrush(Ink),
            StrokeThickness = 1.8,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false
        });

        AddMarker(pa);
        AddMarker(pb);
        AddLabel(new Point((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2), Vector3.Distance(a, b));
    }

    private void AddMarker(Point at)
    {
        // A transparent disc behind the visible one, so there is something to take hold of. The
        // marker is drawn 9 pixels across because that is the right size to look at; a 9-pixel
        // target is not the right size to hit, and the first attempt at dragging an end missed
        // by four pixels and looked like the drag was not implemented at all.
        Add(at, Reach * 2, Brushes.Transparent, null);
        Add(at, 9, Brushes.White, new SolidColorBrush(Ink));
    }

    private void Add(Point at, double size, Brush fill, Brush? stroke)
    {
        var marker = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2,
            Cursor = Cursors.SizeAll
        };

        Canvas.SetLeft(marker, at.X - size / 2);
        Canvas.SetTop(marker, at.Y - size / 2);
        canvas.Children.Add(marker);
    }

    /// <summary>
    /// The reading, on a plate rather than bare text: it sits over the model, and dark text on a
    /// dark part is unreadable at exactly the moment it is wanted.
    /// </summary>
    private void AddLabel(Point at, float millimetres)
    {
        var text = new TextBlock
        {
            Text = millimetres.ToString("0.## mm", CultureInfo.CurrentCulture),
            Foreground = Brushes.White,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false
        };

        var plate = new Border
        {
            Background = new SolidColorBrush(Ink),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(7, 3, 7, 3),
            Child = text,
            IsHitTestVisible = false
        };

        // Measured before placing, so the plate is centred on the line rather than hanging off it.
        plate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Canvas.SetLeft(plate, at.X - plate.DesiredSize.Width / 2);
        Canvas.SetTop(plate, at.Y - plate.DesiredSize.Height - 10);
        canvas.Children.Add(plate);
    }
}
