using System.Numerics;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
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

    private readonly bool loading;
    private readonly string unitLabel;
    private readonly float unitMillimetres;
    private readonly float modelScale;
    private Dictionary<DrawingView, ViewLines>? views;

    public DrawingWindow(IReadOnlyList<Mesh> meshes, string title, string unitLabel, float unitMillimetres, float modelScale = 1f)
    {
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

    private void Refresh()
    {
        if (loading || views is null) return;

        var options = Options();
        var paper = ChosenPaper();
        var sheet = DrawingSheet.Layout(views, paper, options);

        Sheet.Show(DrawingRenderer.Render(sheet, options, DateTime.Now), DrawingRenderer.PageSize(paper));

        int visible = views.Values.Sum(v => v.Visible.Count);
        int hidden = views.Values.Sum(v => v.Hidden.Count);
        SummaryText.Text = $"Scale {sheet.ScaleText} on {PaperBox.SelectedItem} - the largest standard scale the views fit at."
                           + $"\n{visible:N0} lines seen, {hidden:N0} hidden."
                           + (sheet.Scale < 1f / 20f ? "\nA part this big is small on the page: a bigger sheet draws it larger." : "");

        last = (options, PaperBox.SelectedIndex, LandscapeBox.IsChecked == true);
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnPaperChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnPrint(object sender, RoutedEventArgs e)
    {
        if (views is null) return;

        var paper = Papers[Math.Max(0, PaperBox.SelectedIndex)];
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageMediaSize = new PageMediaSize(paper.Media);
        dialog.PrintTicket.PageOrientation = LandscapeBox.IsChecked == true ? PageOrientation.Landscape : PageOrientation.Portrait;

        if (dialog.ShowDialog() != true) return;

        // Laid out again on the page the printer actually has, which the dialog may have changed.
        const double perMillimetre = 96.0 / 25.4;
        var page = new Vector2((float)(dialog.PrintableAreaWidth / perMillimetre), (float)(dialog.PrintableAreaHeight / perMillimetre));
        var options = Options();
        var sheet = DrawingSheet.Layout(views, page, options);

        dialog.PrintVisual(DrawingRenderer.Render(sheet, options, DateTime.Now), options.Title);
    }
}
