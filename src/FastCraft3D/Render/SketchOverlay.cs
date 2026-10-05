using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using FastCraft3D.Geometry.Sketches;

namespace FastCraft3D.Render;

/// <summary>
/// What a sketch reads off the plate while a point is placed or dragged: guide lines from the
/// point to the two axes with its X and Y on them, and a tag on each length being drawn.
///
/// Two-dimensional and over the viewport, as the tape measure is, and for the same reason: a tag
/// drawn into the scene is lost behind a model, and the numbers are wanted at exactly the moment
/// something is in the way. It is projected from the camera, so it is redrawn when that moves.
/// </summary>
public sealed class SketchOverlay(Canvas canvas, IScreenProjector projector, Func<float, string> say)
{
    // Darker than the axes themselves: white text has to be read on these.
    private static readonly Color XInk = Color.FromRgb(0xC9, 0x3C, 0x4B);
    private static readonly Color YInk = Color.FromRgb(0x1F, 0x9D, 0x46);
    private static readonly Color Drawing = Color.FromRgb(0xE8, 0x76, 0x2C);

    private Vector2? point;
    private Vector2 bed;
    private IReadOnlyList<Sketch.Dimension> dimensions = [];
    private Point[] drawnAt = [];

    /// <summary>
    /// Puts the tags where the pointer is, with the lengths beside it. A null point takes them
    /// away. <paramref name="bedHalf"/> is half the bed each way: the pointer's own X and Y are
    /// read off the axes only while it is over the bed, since past its edge they point at nothing.
    /// </summary>
    public void Show(Vector2? at, IReadOnlyList<Sketch.Dimension> lengths, Vector2 bedHalf)
    {
        point = at;
        dimensions = lengths;
        bed = bedHalf;
        Draw();
    }

    public void Clear() => Show(null, [], bed);

    /// <summary>
    /// Redraws if the camera has moved the points since they were last drawn. Called on every
    /// frame while sketching, so it projects a few points to find out rather than rebuilding.
    /// </summary>
    public void Reposition()
    {
        if (point is null || !projector.IsReady) return;

        var now = Anchors();
        if (now.Length == drawnAt.Length && now.Zip(drawnAt).All(p => p.First == p.Second)) return;

        Draw();
    }

    /// <summary>Where the origin, a stretch of each axis and the pointer land: enough to notice any move of the camera.</summary>
    private Point[] Anchors()
    {
        var points = new List<Point>();
        foreach (var world in new[] { Vector3.Zero, new Vector3(10, 0, 0), new Vector3(0, 10, 0), new Vector3(point!.Value, 0) })
            points.Add(projector.TryProject(world, out var p) ? p : default);

        return [.. points];
    }

    private void Draw()
    {
        canvas.Children.Clear();
        drawnAt = [];
        if (point is not { } at || !projector.IsReady) return;

        drawnAt = Anchors();

        var onPlate = new Vector3(at, 0);
        var footX = new Vector3(at.X, 0, 0);
        var footY = new Vector3(0, at.Y, 0);
        if (!projector.TryProject(onPlate, out var screen)) return;

        // The guides first, so the tags lie over them.
        bool overBed = MathF.Abs(at.X) <= bed.X && MathF.Abs(at.Y) <= bed.Y;
        if (overBed && projector.TryProject(footX, out var onX))
        {
            Guide(screen, onX, XInk);
            Tag(onX, $"X {say(at.X)}", XInk, Place.Below);
        }

        if (overBed && projector.TryProject(footY, out var onY))
        {
            Guide(screen, onY, YInk);
            Tag(onY, $"Y {say(at.Y)}", YInk, Place.Left);
        }

        foreach (var d in dimensions)
        {
            if (!projector.TryProject(new Vector3(d.From, 0), out var a) || !projector.TryProject(new Vector3(d.To, 0), out var b)) continue;

            string text = (d.Kind == Sketch.DimensionKind.Radius ? "R " : "") + say(d.Millimetres);
            var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            Tag(middle, text, Drawing, d.Kind == Sketch.DimensionKind.Height ? Place.Right : Place.Above);
        }
    }

    private void Guide(Point from, Point to, Color colour)
    {
        if ((from - to).Length < 1) return;

        canvas.Children.Add(new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = new SolidColorBrush(colour),
            StrokeThickness = 1.2,
            StrokeDashArray = [3, 3],
            IsHitTestVisible = false
        });

        var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(colour), IsHitTestVisible = false };
        Canvas.SetLeft(dot, to.X - 3.5);
        Canvas.SetTop(dot, to.Y - 3.5);
        canvas.Children.Add(dot);
    }

    private enum Place { Above, Below, Left, Right }

    /// <summary>A reading on a plate of its own: dark text on a dark part is unreadable, and this is wanted over parts.</summary>
    private void Tag(Point at, string text, Color colour, Place where)
    {
        var plate = new Border
        {
            Background = new SolidColorBrush(colour),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false
            }
        };

        // Measured before placing, so the plate sits beside the point it names and not on it.
        plate.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = plate.DesiredSize.Width, h = plate.DesiredSize.Height;

        var (x, y) = where switch
        {
            Place.Above => (at.X - w / 2, at.Y - h - 8),
            Place.Below => (at.X - w / 2, at.Y + 8),
            Place.Left => (at.X - w - 8, at.Y - h / 2),
            _ => (at.X + 8, at.Y - h / 2)
        };

        Canvas.SetLeft(plate, x);
        Canvas.SetTop(plate, y);
        canvas.Children.Add(plate);
    }
}
