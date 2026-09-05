using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FastCraft3D.Model;

namespace FastCraft3D.View;

/// <summary>
/// Picks a colour: a saturation/value square with a hue strip, hex and RGB boxes, and the
/// standard palette underneath.
///
/// Hue, saturation and value are the dialog's state rather than the RGB triple, because grey and
/// black have no hue of their own - deriving it back from RGB would make the marker jump to red
/// the moment the value slid to zero.
/// </summary>
public partial class ColourDialog : Window
{
    private const double MarkerRadius = 6;

    private double hue;
    private double saturation;
    private double value;

    /// <summary>Guards the text boxes against being rewritten by the update they themselves caused.</summary>
    private bool updating;

    public ColourDialog(Vector3 startingColour)
    {
        InitializeComponent();

        SwatchList.ItemsSource = Palette.Swatches;
        BeforeSwatch.Fill = BrushFor(startingColour);

        (hue, saturation, value) = Palette.ToHsv(startingColour);
        Redraw();

        // The markers sit at a fraction of the panels, which have no size until layout runs.
        Loaded += (_, _) => PlaceMarkers();
        SizeChanged += (_, _) => PlaceMarkers();
    }

    /// <summary>The chosen colour, or null when the dialog was cancelled.</summary>
    public Vector3? Result { get; private set; }

    private Vector3 Current => Palette.FromHsv(hue, saturation, value);

    private static SolidColorBrush BrushFor(Vector3 colour)
    {
        var (r, g, b) = Palette.ToBytes(colour);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void Redraw()
    {
        var colour = Current;

        HueFill.Fill = BrushFor(Palette.FromHsv(hue, 1, 1));
        AfterSwatch.Fill = BrushFor(colour);

        // A white marker vanishes on a pale colour and a black one on a dark colour.
        var markerStroke = Palette.PrefersDarkText(colour) ? Brushes.Black : Brushes.White;
        SquareMarker.Stroke = markerStroke;

        updating = true;
        try
        {
            var (r, g, b) = Palette.ToBytes(colour);
            HexBox.Text = Palette.ToHex(colour);
            RedBox.Text = r.ToString();
            GreenBox.Text = g.ToString();
            BlueBox.Text = b.ToString();
        }
        finally
        {
            updating = false;
        }

        PlaceMarkers();
    }

    private void PlaceMarkers()
    {
        if (Square.ActualWidth > 0 && Square.ActualHeight > 0)
        {
            Canvas.SetLeft(SquareMarker, saturation * Square.ActualWidth - MarkerRadius);
            Canvas.SetTop(SquareMarker, (1 - value) * Square.ActualHeight - MarkerRadius);
        }

        if (HueStrip.ActualHeight > 0)
            Canvas.SetTop(HueMarker, hue / 360 * HueStrip.ActualHeight - 2);
    }

    private void SetFrom(Vector3 colour)
    {
        var (h, s, v) = Palette.ToHsv(colour);

        // A colour with no chroma reports hue 0; keeping the strip where the user left it means
        // sliding the value back up returns the hue they had rather than red.
        if (s > 0) hue = h;
        saturation = s;
        value = v;

        Redraw();
    }

    private void OnSquarePressed(object sender, MouseButtonEventArgs e)
    {
        Square.CaptureMouse();
        TrackSquare(e.GetPosition(Square));
    }

    private void OnSquareMoved(object sender, MouseEventArgs e)
    {
        if (Square.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            TrackSquare(e.GetPosition(Square));
    }

    private void OnHuePressed(object sender, MouseButtonEventArgs e)
    {
        HueStrip.CaptureMouse();
        TrackHue(e.GetPosition(HueStrip));
    }

    private void OnHueMoved(object sender, MouseEventArgs e)
    {
        if (HueStrip.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            TrackHue(e.GetPosition(HueStrip));
    }

    /// <summary>Both panels release here, so a drag that ends off the panel still lets go.</summary>
    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        Square.ReleaseMouseCapture();
        HueStrip.ReleaseMouseCapture();
    }

    private void TrackSquare(Point p)
    {
        if (Square.ActualWidth <= 0 || Square.ActualHeight <= 0) return;

        saturation = Math.Clamp(p.X / Square.ActualWidth, 0, 1);
        value = Math.Clamp(1 - p.Y / Square.ActualHeight, 0, 1);
        Redraw();
    }

    private void TrackHue(Point p)
    {
        if (HueStrip.ActualHeight <= 0) return;

        hue = Math.Clamp(p.Y / HueStrip.ActualHeight, 0, 1) * 360;
        Redraw();
    }

    private void OnHexTyped(object sender, TextChangedEventArgs e)
    {
        if (updating) return;
        if (Palette.TryFromHex(HexBox.Text, out var colour)) SetFrom(colour);
    }

    private void OnChannelTyped(object sender, TextChangedEventArgs e)
    {
        if (updating) return;
        if (TryChannel(RedBox, out byte r) && TryChannel(GreenBox, out byte g) && TryChannel(BlueBox, out byte b))
            SetFrom(Palette.FromBytes(r, g, b));
    }

    private static bool TryChannel(TextBox box, out byte channel)
    {
        channel = 0;
        if (!int.TryParse(box.Text, out int parsed)) return false;

        channel = (byte)Math.Clamp(parsed, 0, 255);
        return true;
    }

    private void OnSwatchPicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Vector3 colour }) SetFrom(colour);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Current;
        DialogResult = true;
    }
}
