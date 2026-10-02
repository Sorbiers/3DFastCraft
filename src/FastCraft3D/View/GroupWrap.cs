using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FastCraft3D.View;

/// <summary>
/// Groups of buttons in a row, with the ribbon's divider between neighbours, that wrap a whole
/// group at a time when the row is too narrow - and a group too wide for a line of its own wraps
/// its own buttons, given as a WrapPanel.
///
/// A WrapPanel over the buttons themselves was the obvious way and is the wrong one: it breaks a
/// line in the middle of a group, and a divider it carries as a child ends up opening or closing a
/// line with nothing beside it. So the dividers are not children here at all. They are drawn
/// between two groups that ended up on the same line, and nowhere else.
/// </summary>
public sealed class GroupWrap : Panel
{
    /// <summary>Room for a divider: as the ribbon's, eight either side of a one-pixel line.</summary>
    private const double Gap = 17;

    /// <summary>How far the line stops short of the top and bottom, as on the ribbon.</summary>
    private const double Inset = 8;

    private static readonly Brush Line = Frozen(Color.FromRgb(0xC8, 0xCC, 0xD2));

    private Rect[] placed = [];
    private readonly List<Rect> dividers = [];

    /// <summary>Where the dividers go, as of the last layout.</summary>
    public IReadOnlyList<Rect> Dividers => dividers;

    /// <summary>The dividers as last drawn, to tell whether a layout moved them.</summary>
    private List<Rect> drawn = [];

    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(available.Width, double.PositiveInfinity));

        return Lay(available.Width);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Lay(final.Width);

        for (int i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(placed[i]);

        // Only when they moved. Asking to be drawn again also asks to be arranged again, so
        // asking every time is a layout pass that never ends.
        if (!drawn.SequenceEqual(dividers)) InvalidateVisual();
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        foreach (var d in dividers) dc.DrawRectangle(Line, null, d);
        drawn = dividers.ToList();
    }

    /// <summary>Lines of groups, each as far left as it goes, and a divider wherever two meet.</summary>
    private Size Lay(double width)
    {
        var children = InternalChildren;
        placed = new Rect[children.Count];
        dividers.Clear();

        double x = 0, y = 0, lineHeight = 0, widest = 0;
        var line = new List<int>();

        void EndLine()
        {
            // Every divider on the line runs its full height, whichever group is the taller.
            for (int k = 1; k < line.Count; k++)
            {
                double at = Math.Round(placed[line[k]].X - Gap / 2);
                dividers.Add(new Rect(at, y + Inset, 1, Math.Max(0, lineHeight - 2 * Inset)));
            }

            widest = Math.Max(widest, x);
            y += lineHeight;
            x = 0;
            lineHeight = 0;
            line.Clear();
        }

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Visibility == Visibility.Collapsed)
            {
                placed[i] = new Rect(0, y, 0, 0);
                continue;
            }

            var size = child.DesiredSize;
            double start = line.Count == 0 ? 0 : x + Gap;
            if (line.Count > 0 && start + size.Width > width)
            {
                EndLine();
                start = 0;
            }

            placed[i] = new Rect(start, y, size.Width, size.Height);
            x = start + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            line.Add(i);
        }

        if (line.Count > 0) EndLine();

        return new Size(double.IsInfinity(width) ? widest : Math.Min(widest, width), y);
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
