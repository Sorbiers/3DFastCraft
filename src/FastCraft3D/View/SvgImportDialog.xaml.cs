using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Io;

namespace FastCraft3D.View;

/// <summary>
/// Asks how big and how thick to make an imported drawing. Width and depth are tied together by
/// the drawing's own proportions, so typing either sets both.
/// </summary>
public partial class SvgImportDialog : Window
{
    private readonly float aspect;
    private readonly int shapes;
    private bool updating;

    /// <param name="aspect">How wide the drawing is for each unit it is tall.</param>
    /// <param name="shapes">How many filled shapes it has, for the summary.</param>
    public SvgImportDialog(string file, float aspect, int shapes, SvgImportOptions start)
    {
        this.aspect = aspect;
        this.shapes = shapes;
        InitializeComponent();

        SubjectText.Text = $"{Path.GetFileName(file)} - {shapes} filled shape{(shapes == 1 ? "" : "s")}. "
                         + "Lines with no fill, and text not turned into paths, are left out.";

        updating = true;
        WidthBox.Text = Format(start.Width);
        DepthBox.Text = Format(start.Width / aspect);
        ThicknessBox.Text = Format(start.Thickness);
        SeparateBox.IsChecked = start.Separate;
        updating = false;

        Summarise();
    }

    /// <summary>Null until the user confirms.</summary>
    public SvgImportOptions? Result { get; private set; }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private static float? Read(TextBox box) =>
        float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) && v > 0 ? v : null;

    private void OnWidthTyped(object sender, TextChangedEventArgs e)
    {
        if (updating || !IsInitialized || Read(WidthBox) is not { } width) return;

        updating = true;
        DepthBox.Text = Format(width / aspect);
        updating = false;
        Summarise();
    }

    private void OnDepthTyped(object sender, TextChangedEventArgs e)
    {
        if (updating || !IsInitialized || Read(DepthBox) is not { } depth) return;

        updating = true;
        WidthBox.Text = Format(depth * aspect);
        updating = false;
        Summarise();
    }

    private void OnThicknessTyped(object sender, TextChangedEventArgs e)
    {
        if (!updating && IsInitialized) Summarise();
    }

    private void Summarise()
    {
        float width = Read(WidthBox) ?? 0f;
        float thickness = Read(ThicknessBox) ?? 0f;

        SummaryText.Text = width < SvgImport.MinimumSize || thickness < SvgImport.MinimumThickness
            ? $"Give a width of at least {SvgImport.MinimumSize} mm and a thickness of at least {SvgImport.MinimumThickness} mm."
            : $"{width:0.##} x {width / aspect:0.##} x {thickness:0.##} mm, lying flat on the plate"
              + (shapes > 1 && SeparateBox.IsChecked == true ? $", as {shapes} objects." : ".");
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Read(WidthBox) is not { } width || width < SvgImport.MinimumSize
            || Read(ThicknessBox) is not { } thickness || thickness < SvgImport.MinimumThickness)
        {
            Summarise();
            return;
        }

        Result = new SvgImportOptions(width, thickness, SeparateBox.IsChecked == true);
        DialogResult = true;
    }
}
