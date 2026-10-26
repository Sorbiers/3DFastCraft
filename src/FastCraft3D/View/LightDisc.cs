using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FastCraft3D.View;

/// <summary>
/// Where the key light comes from, as a dot on a disc to drag: the middle is overhead, the rim
/// the horizon, the bottom behind the viewer - or, with the light fixed in the scene, the front
/// of the plate.
///
/// Two number boxes said the same thing and nobody could picture "turn -17, height 23"; a sun on
/// a dome is read at a glance and set with one drag.
/// </summary>
public sealed class LightDisc : FrameworkElement
{
    public static readonly DependencyProperty TurnProperty = DependencyProperty.Register(
        nameof(Turn), typeof(double), typeof(LightDisc),
        new FrameworkPropertyMetadata(-17.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HeightAngleProperty = DependencyProperty.Register(
        nameof(HeightAngle), typeof(double), typeof(LightDisc),
        new FrameworkPropertyMetadata(23.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FollowsViewProperty = DependencyProperty.Register(
        nameof(FollowsView), typeof(bool), typeof(LightDisc),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Degrees round from the bottom of the disc, positive to the right.</summary>
    public double Turn
    {
        get => (double)GetValue(TurnProperty);
        set => SetValue(TurnProperty, value);
    }

    /// <summary>Degrees up from the rim; ninety is the middle.</summary>
    public double HeightAngle
    {
        get => (double)GetValue(HeightAngleProperty);
        set => SetValue(HeightAngleProperty, value);
    }

    /// <summary>Only changes what the bottom of the disc is called.</summary>
    public bool FollowsView
    {
        get => (bool)GetValue(FollowsViewProperty);
        set => SetValue(FollowsViewProperty, value);
    }

    private static readonly Brush Dome = new SolidColorBrush(Color.FromRgb(0xFA, 0xFC, 0xFF));
    private static readonly Pen Rim = new(new SolidColorBrush(Color.FromRgb(0xB8, 0xD2, 0xF0)), 1);
    private static readonly Pen Guide = new(new SolidColorBrush(Color.FromRgb(0xDD, 0xE6, 0xF2)), 1);
    private static readonly Pen Ray = new(new SolidColorBrush(Color.FromArgb(0x90, 0xE0, 0x9A, 0x10)), 1.5);
    private static readonly Brush Sun = new SolidColorBrush(Color.FromRgb(0xF5, 0xB8, 0x22));
    private static readonly Pen SunEdge = new(new SolidColorBrush(Color.FromRgb(0xB0, 0x78, 0x00)), 1);
    private static readonly Brush Label = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x98));

    public LightDisc()
    {
        Width = 112;
        Height = 112;
        Cursor = Cursors.Hand;
        ToolTip = "Drag the sun to where the light should come from: the middle is overhead, the rim the horizon";
    }

    private Point Centre => new(ActualWidth / 2, ActualHeight / 2);

    // Room left round the rim for the label underneath.
    private double Radius => Math.Max(Math.Min(ActualWidth, ActualHeight) / 2 - 12, 4);

    protected override void OnRender(DrawingContext dc)
    {
        var c = Centre;
        double r = Radius;

        dc.DrawEllipse(Dome, Rim, c, r, r);
        dc.DrawEllipse(null, Guide, c, r / 2, r / 2);
        dc.DrawLine(Guide, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(Guide, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));

        var text = new FormattedText(FollowsView ? "you" : "front", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, Label, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(c.X - text.Width / 2, c.Y + r + 1));

        var sun = SunAt(Turn, HeightAngle);
        dc.DrawLine(Ray, c, sun);
        dc.DrawEllipse(Sun, SunEdge, sun, 6, 6);
    }

    private Point SunAt(double turn, double height)
    {
        double along = Radius * (1 - Math.Clamp(height, 0, 90) / 90);
        double angle = turn * Math.PI / 180;
        return new Point(Centre.X + along * Math.Sin(angle), Centre.Y + along * Math.Cos(angle));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        CaptureMouse();
        Follow(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) Follow(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Follow(Point p)
    {
        var d = p - Centre;
        double along = Math.Min(d.Length, Radius);

        // Right over the middle the way round is anything; keep the one it had.
        if (along > 1) SetCurrentValue(TurnProperty, Math.Atan2(d.X, d.Y) * 180 / Math.PI);
        SetCurrentValue(HeightAngleProperty, 90 * (1 - along / Radius));
    }
}
