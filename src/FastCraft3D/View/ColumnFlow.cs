using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FastCraft3D.View;

/// <summary>
/// A tool panel's body: a plain stack on a narrow side panel, and on a wide one the settings
/// between the title and the buttons flowed down into balanced columns, read top to bottom and
/// then across, like a newspaper. A single long column ran off the bottom of a 1080-line screen
/// with most of a widened panel empty beside it.
/// </summary>
/// <remarks>
/// The title (the first child) and a row of nothing but buttons take the whole width, so Apply
/// and Cancel stay where they were. A bold heading is kept with what follows it. Anything else can
/// be told to span with <c>ColumnFlow.Span</c>.
/// </remarks>
public sealed class ColumnFlow : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(bool), typeof(ColumnFlow),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetSpan(DependencyObject d) => (bool)d.GetValue(SpanProperty);
    public static void SetSpan(DependencyObject d, bool value) => d.SetValue(SpanProperty, value);

    /// <summary>The least a column needs: a label, a box, and its unit.</summary>
    public double ColumnWidth { get; set; } = 270;

    public double Gap { get; set; } = 12;

    private Rect[] placed = [];
    private double measuredWidth = double.NaN;

    protected override Size MeasureOverride(Size available)
    {
        measuredWidth = available.Width;
        return Lay(available.Width, measure: true);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (final.Width != measuredWidth)
        {
            measuredWidth = final.Width;
            Lay(final.Width, measure: true);
        }

        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var r = placed[i];
            // A spanning child, and every child in one column, fills the width it is given.
            InternalChildren[i].Arrange(new Rect(r.X, r.Y, r.Width, r.Height));
        }

        return final;
    }

    private Size Lay(double width, bool measure)
    {
        var children = InternalChildren;
        placed = new Rect[children.Count];

        int columns = double.IsInfinity(width) || double.IsNaN(width)
            ? 1
            : Math.Max(1, (int)((width + Gap) / (ColumnWidth + Gap)));

        double y = 0, widest = 0;
        int i = 0;
        while (i < children.Count)
        {
            if (Spans(i))
            {
                var child = children[i];
                if (measure) child.Measure(new Size(width, double.PositiveInfinity));
                placed[i] = new Rect(0, y, Finite(width, child.DesiredSize.Width), child.DesiredSize.Height);
                y += child.DesiredSize.Height;
                widest = Math.Max(widest, child.DesiredSize.Width);
                i++;
                continue;
            }

            // A run of settings between spanning children, flowed into the columns.
            int start = i;
            while (i < children.Count && !Spans(i)) i++;

            // A run of one - a note under the buttons - is not squeezed into a column of its own.
            int shown = 0;
            for (int k = start; k < i; k++) if (children[k].Visibility != Visibility.Collapsed) shown++;
            int across = Math.Max(1, Math.Min(columns, shown));
            double width1 = across == 1 ? width : Math.Floor((width - Gap * (across - 1)) / across);

            var heights = new double[i - start];
            for (int k = 0; k < heights.Length; k++)
            {
                var child = children[start + k];
                if (measure) child.Measure(new Size(width1, double.PositiveInfinity));
                heights[k] = child.DesiredSize.Height;
                widest = Math.Max(widest, child.DesiredSize.Width);
            }

            var breaks = Balance(heights, across, k => KeptWithNext(children[start + k]));
            double tallest = 0;
            for (int c = 0; c < breaks.Length - 1; c++)
            {
                double x = c * (width1 + Gap), cy = y;
                for (int k = breaks[c]; k < breaks[c + 1]; k++)
                {
                    placed[start + k] = new Rect(x, cy, Finite(width1, children[start + k].DesiredSize.Width), heights[k]);
                    cy += heights[k];
                }

                tallest = Math.Max(tallest, cy - y);
            }

            y += tallest;
        }

        return new Size(columns == 1 || double.IsInfinity(width) ? widest : width, y);
    }

    private static double Finite(double width, double desired) => double.IsInfinity(width) ? desired : width;

    private bool Spans(int i)
    {
        var child = InternalChildren[i];
        if (i == 0 || GetSpan(child)) return true;

        // A row of buttons: Apply, Cancel and the like.
        return child is Panel row
               && row.Children.OfType<UIElement>().Any(c => c.Visibility != Visibility.Collapsed)
               && row.Children.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed)
                   .All(c => c is ButtonBase and not ToggleButton);
    }

    private static bool KeptWithNext(UIElement child) =>
        child is TextBlock { FontWeight: var weight } && weight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();

    /// <summary>
    /// Where each column starts, for the columns to come out as even as they can without a break
    /// after a heading: the least tallest column over every way of cutting the run in order.
    /// Tool panels have a few dozen rows, so trying every cut is cheap.
    /// </summary>
    private static int[] Balance(double[] heights, int columns, Func<int, bool> keptWithNext)
    {
        int n = heights.Length;
        columns = Math.Max(1, Math.Min(columns, n));

        var prefix = new double[n + 1];
        for (int k = 0; k < n; k++) prefix[k + 1] = prefix[k] + heights[k];

        // best[c, j]: the least tallest column putting the first j items into c columns.
        var best = new double[columns + 1, n + 1];
        var cut = new int[columns + 1, n + 1];
        for (int c = 0; c <= columns; c++)
            for (int j = 0; j <= n; j++)
                best[c, j] = double.PositiveInfinity;
        best[0, 0] = 0;

        for (int c = 1; c <= columns; c++)
            for (int j = 0; j <= n; j++)
                for (int k = 0; k <= j; k++)
                {
                    // No column may start just after a heading, leaving it at the foot of the one before.
                    if (k > 0 && k < n && keptWithNext(k - 1) && k != j) continue;
                    if (double.IsInfinity(best[c - 1, k])) continue;

                    double tallest = Math.Max(best[c - 1, k], prefix[j] - prefix[k]);
                    if (tallest < best[c, j] - 0.5)
                    {
                        best[c, j] = tallest;
                        cut[c, j] = k;
                    }
                }

        var breaks = new int[columns + 1];
        breaks[columns] = n;
        for (int c = columns, j = n; c > 0; c--)
        {
            j = cut[c, j];
            breaks[c - 1] = j;
        }

        return breaks;
    }
}
