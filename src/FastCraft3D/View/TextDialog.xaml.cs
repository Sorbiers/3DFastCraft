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
        LayoutBox.SelectedIndex = (int)start.Layout;
        RadiusBox.Text = Format(start.Radius);
        InwardBox.IsChecked = start.Inward;

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
            Layout = (TextLayout)Math.Max(LayoutBox.SelectedIndex, 0),
            Radius = Number(RadiusBox, fallback.Radius),
            Inward = InwardBox.IsChecked == true
        }.Sane();

        static float Number(TextBox box, float otherwise) =>
            float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : otherwise;
    }

    /// <summary>
    /// What a bent layout is about to do, which the size of the result does not say: lettering
    /// longer than the circle is round comes back over itself, and there is nothing in a bounding
    /// box to tell you that has happened.
    /// </summary>
    private static string Round(TextOptions options)
    {
        if (!options.IsRound) return "";

        // How far the lettering runs, measured straight, against how far there is to run.
        float along = TextObject.Build(options with { Layout = TextLayout.Flat }).ComputeBounds().Size.X;
        float round = 2f * MathF.PI * options.Radius;

        string said = along > round
            ? $"The lettering is {along:0.#} mm long and the circle only {round:0.#} mm round, so it "
              + $"laps itself. A radius of {along / (2f * MathF.PI):0.#} mm or more would hold it."
            : $"{along:0.#} mm of lettering on a {round:0.#} mm circle - {along / round:P0} of the way round.";

        // Facing in, the tops of the letters point at the middle - and past it, if the radius is
        // the smaller of the two.
        if (options is { Layout: TextLayout.Circle, Inward: true } && options.Height >= options.Radius)
            said += Environment.NewLine
                  + $"{options.Height:0.#} mm letters facing in do not fit a {options.Radius:0.#} mm "
                  + "radius: their tops reach the middle and fold through it. A bigger radius, or "
                  + "shorter letters.";

        return Environment.NewLine + said;
    }

    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        var options = Read();
        RadiusRow.Visibility = options.IsRound ? Visibility.Visible : Visibility.Collapsed;
        InwardRow.Visibility = options.Layout == TextLayout.Circle ? Visibility.Visible : Visibility.Collapsed;

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
                           + Round(options)
                           + (options.Height < 4f ? "\nLetters under about 4 mm tall lose their detail on a 0.4 mm nozzle." : "");
        AddButton.IsEnabled = true;
    }

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e) => Refresh();

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
