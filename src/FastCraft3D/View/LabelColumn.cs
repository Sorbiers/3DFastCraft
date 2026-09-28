using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FastCraft3D.View;

/// <summary>
/// Makes the label column of every tool panel under the element as wide as its longest label,
/// so no label is cut short, and wraps labels once they would take more than about half the side
/// panel. A label is the first child of a horizontal row, given a width of its own in XAML; that
/// width is kept as the least, and labels of the same width in one tool panel stay in line.
/// </summary>
/// <remarks>
/// Done here rather than by turning seventy XAML rows into grids with a shared column: the rows
/// stay as they are, and a tool panel added later is covered without anyone remembering to.
/// </remarks>
public static class LabelColumn
{
    public static readonly DependencyProperty FitProperty = DependencyProperty.RegisterAttached(
        "Fit", typeof(bool), typeof(LabelColumn), new PropertyMetadata(false, OnFitChanged));

    public static bool GetFit(DependencyObject d) => (bool)d.GetValue(FitProperty);
    public static void SetFit(DependencyObject d, bool value) => d.SetValue(FitProperty, value);

    // The width the label was given in XAML, remembered before it is first changed.
    private static readonly DependencyProperty LeastProperty = DependencyProperty.RegisterAttached(
        "Least", typeof(double), typeof(LabelColumn), new PropertyMetadata(double.NaN));

    private const double MostShare = 0.45;

    private static void OnFitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host || e.NewValue is not true) return;

        bool watching = false;
        void Refit(object? sender, EventArgs args)
        {
            var labels = Fit(host);
            if (watching) return;

            // A label bound to the view model can say something longer later.
            watching = true;
            var text = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            foreach (var label in labels) text.AddValueChanged(label, (_, _) => Fit(host));
        }

        host.Loaded += (s, a) => Refit(s, a);
        host.SizeChanged += (s, a) => Refit(s, a);
    }

    private static List<TextBlock> Fit(FrameworkElement host)
    {
        var all = new List<TextBlock>();
        foreach (var panel in LogicalTreeHelper.GetChildren(host).OfType<DependencyObject>())
        {
            var labels = new List<TextBlock>();
            Collect(panel, labels);
            all.AddRange(labels);

            foreach (var column in labels.GroupBy(Least))
            {
                double least = column.Key;
                double most = Math.Max(least, host.ActualWidth * MostShare);
                double widest = column.Max(Natural);
                double width = Math.Min(Math.Max(least, widest), most);

                foreach (var label in column)
                {
                    label.TextWrapping = TextWrapping.Wrap;
                    if (label.Width != width) label.Width = width;
                }
            }
        }

        return all;
    }

    private static void Collect(DependencyObject node, List<TextBlock> labels)
    {
        if (node is StackPanel { Orientation: Orientation.Horizontal } row
            && row.Children.Count > 1
            && row.Children[0] is TextBlock label
            && (!double.IsNaN((double)label.GetValue(LeastProperty)) || !double.IsNaN(label.Width)))
        {
            if (double.IsNaN((double)label.GetValue(LeastProperty))) label.SetValue(LeastProperty, label.Width);
            labels.Add(label);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            Collect(child, labels);
    }

    private static double Least(TextBlock label) => (double)label.GetValue(LeastProperty);

    /// <summary>The label's text on one line, measured whether or not its panel is showing.</summary>
    private static double Natural(TextBlock label)
    {
        var text = new FormattedText(label.Text ?? "", CultureInfo.CurrentUICulture, label.FlowDirection,
            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
            label.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip);
        return Math.Ceiling(text.WidthIncludingTrailingWhitespace + label.Padding.Left + label.Padding.Right) + 1;
    }
}
