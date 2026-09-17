using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Text;

namespace FastCraft3D.View;

/// <summary>Asks for lettering to make as an object, showing it on the plate as it is typed.</summary>
public partial class TextDialog : ToolPanel
{
    private readonly Action<Mesh?> preview;
    private readonly bool loading;

    /// <param name="preview">Shows a mesh on the plate, or takes the preview away when given null.</param>
    public TextDialog(TextOptions start, IReadOnlyList<string> fonts, Action<Mesh?> preview)
    {
        this.preview = preview;
        loading = true;
        InitializeComponent();

        FontBox.ItemsSource = fonts;
        FontBox.SelectedItem = fonts.Contains(start.Font) ? start.Font : fonts.FirstOrDefault(f => f == "Segoe UI") ?? fonts.FirstOrDefault();
        TextBox.Text = start.Text;
        BoldBox.IsChecked = start.Bold;
        ItalicBox.IsChecked = start.Italic;
        HeightBox.Text = Format(start.Height);
        DepthBox.Text = Format(start.Depth);
        SpacingBox.Text = Format(start.Spacing);
        UprightBox.IsChecked = start.Upright;

        loading = false;
        Refresh();

        Loaded += (_, _) =>
        {
            TextBox.SelectAll();
            TextBox.Focus();
        };
    }

    /// <summary>Null until the user adds the lettering.</summary>
    public TextOptions? Result { get; private set; }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private TextOptions Read()
    {
        var fallback = new TextOptions();

        return new TextOptions
        {
            Text = TextBox.Text,
            Font = FontBox.SelectedItem as string ?? fallback.Font,
            Bold = BoldBox.IsChecked == true,
            Italic = ItalicBox.IsChecked == true,
            Height = Number(HeightBox, fallback.Height),
            Depth = Number(DepthBox, fallback.Depth),
            Spacing = Number(SpacingBox, 0f),
            Upright = UprightBox.IsChecked == true
        }.Sane();

        static float Number(TextBox box, float otherwise) =>
            float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : otherwise;
    }

    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        var options = Read();
        var mesh = TextObject.Build(options);

        if (mesh.TriangleCount == 0)
        {
            preview(null);
            SummaryText.Text = string.IsNullOrWhiteSpace(options.Text)
                ? "Type the lettering."
                : "This font has none of these letters.";
            AddButton.IsEnabled = false;
            return;
        }

        preview(mesh);
        var size = mesh.ComputeBounds().Size;
        SummaryText.Text = $"{size.X:0.#} x {size.Y:0.#} x {size.Z:0.#} mm, {mesh.TriangleCount:N0} triangles."
                           + (options.Height < 4f ? "\nLetters under about 4 mm tall lose their detail on a 0.4 mm nozzle." : "");
        AddButton.IsEnabled = true;
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnFontChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnClicked(object sender, RoutedEventArgs e) => Refresh();

    protected override void OnClosed(EventArgs e)
    {
        preview(null);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        var options = Read();
        if (TextObject.Build(options).TriangleCount == 0) return;

        Result = options;
        DialogResult = true;
    }
}
