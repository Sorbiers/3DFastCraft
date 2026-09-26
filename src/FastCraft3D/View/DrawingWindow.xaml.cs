using System.Numerics;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Drawings;

namespace FastCraft3D.View;

/// <summary>
/// A three-view drawing of the part with its overall dimensions, previewed on a page and printed.
///
/// A window of its own rather than a panel beside the model: it is a page to look at, and the
/// bigger it is the better, while nothing about it needs the scene. The views are drawn once, in
/// the background - hidden lines on a dense model take a moment - and everything after that is
/// only laying them out again, which is instant.
/// </summary>
public partial class DrawingWindow : Window
{
    private readonly record struct Paper(string Name, float Long, float Short, PageMediaSizeName Media);

    private static readonly Paper[] Papers =
    [
        new("A4", 297f, 210f, PageMediaSizeName.ISOA4),
        new("A3", 420f, 297f, PageMediaSizeName.ISOA3),
        new("Letter", 279.4f, 215.9f, PageMediaSizeName.NorthAmericaLetter),
        new("Tabloid", 431.8f, 279.4f, PageMediaSizeName.NorthAmericaTabloid)
    ];

    /// <summary>What the window was last left at, for the next drawing this session.</summary>
    private static (DrawingOptions Options, int Paper, bool Landscape) last = (new DrawingOptions(), 0, true);

    private readonly IReadOnlyList<Mesh> meshes;
    private readonly IReadOnlyList<string> names;
    private List<DrawingRenderer.PartEntry>? parts;
    private int page;

    private readonly bool loading;
    private readonly string unitLabel;
    private readonly float unitMillimetres;
    private readonly float modelScale;
    private Dictionary<DrawingView, ViewLines>? views;

    public DrawingWindow(IReadOnlyList<Mesh> meshes, string title, string unitLabel, float unitMillimetres, float modelScale = 1f,
                         IReadOnlyList<string>? names = null)
    {
        this.meshes = meshes;
        this.names = names ?? meshes.Select((_, i) => $"Part {i + 1}").ToList();
        this.unitLabel = unitLabel;
        this.unitMillimetres = unitMillimetres;
        this.modelScale = modelScale;

        loading = true;
        InitializeComponent();

        foreach (var paper in Papers) PaperBox.Items.Add(paper.Name);
        PaperBox.SelectedIndex = last.Paper;
        LandscapeBox.IsChecked = last.Landscape;
        PortraitBox.IsChecked = !last.Landscape;
        FirstAngleBox.IsChecked = last.Options.Projection == Projection.FirstAngle;
        ThirdAngleBox.IsChecked = last.Options.Projection == Projection.ThirdAngle;
        HiddenBox.IsChecked = last.Options.HiddenLines;
        DimensionsBox.IsChecked = last.Options.Dimensions;
        AllDimensionsBox.IsChecked = last.Options.AllDimensions;
        IsometricBox.IsChecked = last.Options.Isometric;
        FillBox.IsChecked = last.Options.FillPage;
        HideSmallBox.IsChecked = last.Options.SmallFigures == SmallFigures.Hide;
        HideOverlapsBox.IsChecked = last.Options.HideOverlaps;
        ModelOnlyBox.IsChecked = last.Options.ScaleFormat == ScaleFormat.ModelOnly;
        ModelWithRealBox.IsChecked = last.Options.ScaleFormat == ScaleFormat.ModelWithReal;
        RealWithModelBox.IsChecked = last.Options.ScaleFormat == ScaleFormat.RealWithModel;
        RealOnlyBox.IsChecked = last.Options.ScaleFormat == ScaleFormat.RealOnly;
        TitleBox.Text = title;
        loading = false;

        // The choice of wording only matters once the model stands for something else.
        ScaleFormatPanel.Visibility = modelScale > 1.001f ? Visibility.Visible : Visibility.Collapsed;

        Loaded += async (_, _) =>
        {
            views = await Task.Run(() => Enum.GetValues<DrawingView>()
                .ToDictionary(view => view, view => ViewDrawing.Build(meshes, view)));

            WorkingText.Visibility = Visibility.Collapsed;
            PrintButton.IsEnabled = true;
            PngButton.IsEnabled = true;
            Refresh();
        };
    }

    private DrawingOptions Options() => new()
    {
        Projection = ThirdAngleBox.IsChecked == true ? Projection.ThirdAngle : Projection.FirstAngle,
        HiddenLines = HiddenBox.IsChecked == true,
        Dimensions = DimensionsBox.IsChecked == true,
        AllDimensions = AllDimensionsBox.IsChecked == true,
        Isometric = IsometricBox.IsChecked == true,
        FillPage = FillBox.IsChecked == true,
        SmallFigures = HideSmallBox.IsChecked == true ? SmallFigures.Hide : SmallFigures.Callout,
        HideOverlaps = HideOverlapsBox.IsChecked == true,
        Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Untitled" : TitleBox.Text.Trim(),
        UnitLabel = unitLabel,
        UnitMillimetres = unitMillimetres,
        ModelScale = modelScale,
        ScaleFormat = RealOnlyBox.IsChecked == true ? ScaleFormat.RealOnly
                    : RealWithModelBox.IsChecked == true ? ScaleFormat.RealWithModel
                    : ModelOnlyBox.IsChecked == true ? ScaleFormat.ModelOnly
                    : ScaleFormat.ModelWithReal
    };

    private Vector2 ChosenPaper()
    {
        var paper = Papers[Math.Max(0, PaperBox.SelectedIndex)];
        return LandscapeBox.IsChecked == true ? new Vector2(paper.Long, paper.Short) : new Vector2(paper.Short, paper.Long);
    }

    private bool PartsMode => PartsModeBox.IsChecked == true;

    /// <summary>
    /// One entry per object for the parts list: its isometric and what it measures. Drawn the first
    /// time the list is asked for, in the background, since a hidden-line isometric of every part
    /// of a big scene takes a moment.
    /// </summary>
    private async Task<List<DrawingRenderer.PartEntry>> Parts()
    {
        if (parts is not null) return parts;

        WorkingText.Visibility = Visibility.Visible;
        parts = await Task.Run(() => meshes.Select((mesh, i) =>
        {
            var bounds = mesh.ComputeBounds();
            string Size(float mm) => (mm / MathF.Max(unitMillimetres, 1e-6f)).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
            var details = new List<string>
            {
                $"{Size(bounds.Size.X)} x {Size(bounds.Size.Y)} x {Size(bounds.Size.Z)} {unitLabel}",
                $"{Math.Abs(mesh.ComputeSignedVolume()) / 1000.0:0.##} cm3, {mesh.TriangleCount:N0} triangles"
            };
            if (modelScale > 1.001f)
                details.Add($"Stands for {bounds.Size.X * modelScale / 1000f:0.##} x {bounds.Size.Y * modelScale / 1000f:0.##} x {bounds.Size.Z * modelScale / 1000f:0.##} m at 1:{modelScale:0}");
            return new DrawingRenderer.PartEntry(names[i], ViewDrawing.Build([mesh], DrawingView.Isometric), details);
        }).ToList());
        WorkingText.Visibility = Visibility.Collapsed;
        return parts;
    }

    private int PageCount(Vector2 paper) =>
        parts is null ? 1 : Math.Max(1, (parts.Count + DrawingRenderer.PartsPerPage(paper) - 1) / DrawingRenderer.PartsPerPage(paper));

    /// <summary>Every page there is to print or save, as pictures, laid out on this paper.</summary>
    private async Task<List<Visual>> Pages(Vector2 paper, DrawingOptions options)
    {
        if (!PartsMode) return [DrawingRenderer.Render(DrawingSheet.Layout(views!, paper, options), options, DateTime.Now)];

        var list = await Parts();
        return Enumerable.Range(0, PageCount(paper)).Select(i => (Visual)DrawingRenderer.RenderParts(list, i, paper, options, DateTime.Now)).ToList();
    }

    private async void Refresh()
    {
        if (loading || views is null) return;

        var options = Options();
        var paper = ChosenPaper();

        PagePanel.Visibility = PartsMode ? Visibility.Visible : Visibility.Collapsed;
        if (PartsMode)
        {
            var list = await Parts();
            int count = PageCount(paper);
            page = Math.Clamp(page, 0, count - 1);
            Sheet.Show(DrawingRenderer.RenderParts(list, page, paper, options, DateTime.Now), DrawingRenderer.PageSize(paper));
            PageText.Text = $"Page {page + 1} of {count}";
            SummaryText.Text = $"{list.Count} object{(list.Count == 1 ? "" : "s")}, {DrawingRenderer.PartsPerPage(paper)} to a page on {PaperBox.SelectedItem}.";
            last = (options, PaperBox.SelectedIndex, LandscapeBox.IsChecked == true);
            return;
        }

        var sheet = DrawingSheet.Layout(views, paper, options);

        Sheet.Show(DrawingRenderer.Render(sheet, options, DateTime.Now), DrawingRenderer.PageSize(paper));

        int visible = views.Values.Sum(v => v.Visible.Count);
        int hidden = views.Values.Sum(v => v.Hidden.Count);
        SummaryText.Text = $"Scale {sheet.ScaleText} on {PaperBox.SelectedItem} - the largest {(options.FillPage ? "" : "standard ")}scale the views fit at."
                           + $"\n{visible:N0} lines seen, {hidden:N0} hidden."
                           + (sheet.Scale < 1f / 20f ? "\nA part this big is small on the page: a bigger sheet draws it larger." : "");

        last = (options, PaperBox.SelectedIndex, LandscapeBox.IsChecked == true);
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnPaperChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnPreviousPage(object sender, RoutedEventArgs e)
    {
        page = Math.Max(0, page - 1);
        Refresh();
    }

    private void OnNextPage(object sender, RoutedEventArgs e)
    {
        page++;
        Refresh();
    }

    /// <summary>The page, or each page of a parts list, as a PNG at 300 dots to the inch.</summary>
    private async void OnSavePng(object sender, RoutedEventArgs e)
    {
        if (views is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG picture (*.png)|*.png",
            FileName = string.Join("_", (string.IsNullOrWhiteSpace(TitleBox.Text) ? "Blueprint" : TitleBox.Text.Trim()).Split(System.IO.Path.GetInvalidFileNameChars())) + ".png"
        };
        if (dialog.ShowDialog(this) != true) return;

        var paper = ChosenPaper();
        var pages = await Pages(paper, Options());
        const double dpi = 300;
        var size = DrawingRenderer.PageSize(paper);

        for (int i = 0; i < pages.Count; i++)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Round(size.Width * dpi / 96), (int)Math.Round(size.Height * dpi / 96), dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(pages[i]);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            string file = pages.Count == 1 ? dialog.FileName
                : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dialog.FileName)!, $"{System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}-{i + 1}.png");
            using var stream = System.IO.File.Create(file);
            encoder.Save(stream);
        }

        SummaryText.Text = pages.Count == 1 ? $"Saved {System.IO.Path.GetFileName(dialog.FileName)}." : $"Saved {pages.Count} pages, numbered -1 to -{pages.Count}.";
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        if (views is null) return;

        var paper = Papers[Math.Max(0, PaperBox.SelectedIndex)];
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(paper.Media);
        dialog.PrintTicket.PageOrientation = LandscapeBox.IsChecked == true ? PageOrientation.Landscape : PageOrientation.Portrait;

        if (dialog.ShowDialog() != true) return;

        // Laid out again on the page the printer actually has, which the dialog may have changed.
        const double perMillimetre = 96.0 / 25.4;
        var printable = new Vector2((float)(dialog.PrintableAreaWidth / perMillimetre), (float)(dialog.PrintableAreaHeight / perMillimetre));
        var options = Options();
        var pages = await Pages(printable, options);

        if (pages.Count == 1) dialog.PrintVisual(pages[0], options.Title);
        else dialog.PrintDocument(new VisualPages(pages, DrawingRenderer.PageSize(printable)), options.Title);
    }
}

/// <summary>Pages already drawn, handed to the printer one after another.</summary>
internal sealed class VisualPages(IReadOnlyList<Visual> pages, Size size) : System.Windows.Documents.DocumentPaginator
{
    public override System.Windows.Documents.DocumentPage GetPage(int pageNumber) =>
        new(pages[pageNumber], size, new Rect(size), new Rect(size));

    public override bool IsPageCountValid => true;
    public override int PageCount => pages.Count;
    public override Size PageSize { get => size; set { } }
    public override System.Windows.Documents.IDocumentPaginatorSource? Source => null;
}
