using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastCraft3D.Generators;

/// <summary>
/// The library as a catalogue: a picture and a name for each generator, under its category, with
/// the ones starred and the ones used last at the top, and a search box that narrows the lot.
///
/// In the side panel, as every tool's settings are, so choosing one goes straight on to its
/// settings in the same place. It replaced a plain menu once the library had outgrown one: a
/// list of names says nothing about which tray is which.
/// </summary>
public sealed class LibraryView : UserControl
{
    private readonly IReadOnlyList<Generator> generators;
    private readonly LibraryMemory memory;
    private readonly LibraryPictures? pictures;

    private readonly TextBox search = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly StackPanel listing = new();

    /// <summary>Pictures already drawn, and the images waiting for each.</summary>
    private readonly Dictionary<string, ImageSource?> drawn = [];
    private readonly HashSet<string> drawing = [];
    private readonly Dictionary<string, List<Image>> waiting = [];

    /// <param name="pictures">Where the pictures come from; null lists the generators without them.</param>
    public LibraryView(IReadOnlyList<Generator> generators, LibraryMemory memory, LibraryPictures? pictures)
    {
        this.generators = generators;
        this.memory = memory;
        this.pictures = pictures;

        AutomationProperties.SetAutomationId(search, "LibrarySearch");
        search.TextChanged += (_, _) => Refresh();
        search.KeyDown += (_, e) =>
        {
            // Enter takes the first thing found, so a name typed is a part made.
            if (e.Key != Key.Enter || Shown().FirstOrDefault() is not { } first) return;
            e.Handled = true;
            Chosen?.Invoke(first);
        };

        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        close.SetResourceReference(StyleProperty, "PanelButton");
        close.Click += (_, _) => Closed?.Invoke();

        var searchRow = new DockPanel();
        var label = new TextBlock { Text = "Search", Width = 52, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(StyleProperty, "FieldLabel");
        DockPanel.SetDock(label, Dock.Left);
        searchRow.Children.Add(label);
        searchRow.Children.Add(search);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Parts made to your numbers. Pick one to set its sizes, with the preview on the plate.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF3A4550),
            Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(searchRow);
        panel.Children.Add(listing);
        panel.Children.Add(close);
        Content = panel;

        Loaded += (_, _) => search.Focus();
        Refresh();
    }

    /// <summary>A generator was picked.</summary>
    public event Action<Generator>? Chosen;

    /// <summary>Closed without picking one.</summary>
    public event Action? Closed;

    /// <summary>The generators listed as things stand, in order, each once - for the tests and for Enter.</summary>
    internal IReadOnlyList<Generator> Shown() =>
        Sections().SelectMany(s => s.Generators).Distinct().ToList();

    /// <summary>The headings shown, in order.</summary>
    internal IReadOnlyList<string> Headings => Sections().Select(s => s.Heading).ToList();

    internal string SearchText
    {
        get => search.Text;
        set => search.Text = value;
    }

    private List<(string Heading, List<Generator> Generators)> Sections()
    {
        string wanted = search.Text.Trim();

        if (wanted.Length > 0)
        {
            var found = generators.Where(g => Matches(g, wanted)).ToList();
            return found.Count == 0 ? [] : [("Found", found)];
        }

        var sections = new List<(string, List<Generator>)>();

        var starred = memory.Favourites.Select(Find).OfType<Generator>().ToList();
        if (starred.Count > 0) sections.Add(("Favourites", starred));

        var recent = memory.Recent.Select(Find).OfType<Generator>().ToList();
        if (recent.Count > 0) sections.Add(("Recently used", recent));

        foreach (var category in generators.GroupBy(g => g.Category))
            sections.Add((category.Key, category.ToList()));

        return sections;
    }

    private Generator? Find(string id) => generators.FirstOrDefault(g => g.Id == id);

    /// <summary>Every word typed somewhere in the name, the category or the description.</summary>
    private static bool Matches(Generator g, string wanted)
    {
        string text = $"{g.Title} {g.Category} {g.Summary} {string.Join(' ', g.Presets.Select(p => p.Name))}";
        return wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, word, CompareOptions.IgnoreCase) >= 0);
    }

    private void Refresh()
    {
        listing.Children.Clear();
        waiting.Clear();

        var sections = Sections();
        if (sections.Count == 0)
        {
            listing.Children.Add(new TextBlock
            {
                Text = $"Nothing in the library matches \"{search.Text.Trim()}\".",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(0xFF5A5F66)
            });
            return;
        }

        foreach (var (heading, found) in sections)
        {
            listing.Children.Add(new TextBlock
            {
                Text = heading,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(0xFF3A4550),
                Margin = new Thickness(0, listing.Children.Count > 0 ? 8 : 0, 0, 2)
            });

            var tiles = new WrapPanel();
            foreach (var g in found) tiles.Children.Add(Tile(g));
            listing.Children.Add(tiles);
        }

        foreach (string id in waiting.Keys.ToList()) _ = Draw(id);
    }

    private FrameworkElement Tile(Generator g)
    {
        var image = new Image { Width = 88, Height = 88, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) };
        if (drawn.TryGetValue(g.Id, out var picture)) image.Source = picture;
        else if (pictures is not null)
        {
            if (!waiting.TryGetValue(g.Id, out var images)) waiting[g.Id] = images = [];
            images.Add(image);
        }

        var words = new StackPanel();
        words.Children.Add(image);
        words.Children.Add(new TextBlock
        {
            Text = g.Title,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = Brush(0xFF2C3037)
        });

        if (g.IsBeta)
            words.Children.Add(new TextBlock
            {
                Text = "beta",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush(0xFF8A6A10)
            });

        var tile = new Button
        {
            Content = words,
            Width = 108,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 4, 4),
            Background = Brushes.White,
            BorderBrush = Brush(0xFFD5DEEA),
            Cursor = Cursors.Hand,
            VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = g.Summary.Length > 0 ? g.Summary : null
        };
        AutomationProperties.SetAutomationId(tile, "Library." + g.Id);
        AutomationProperties.SetName(tile, g.Title);
        tile.Click += (_, _) => Chosen?.Invoke(g);

        // Beside the tile's button rather than inside it, so starring one is not also choosing it.
        var star = new ToggleButton
        {
            Content = memory.IsFavourite(g.Id) ? "★" : "☆",
            IsChecked = memory.IsFavourite(g.Id),
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brush(0xFFC79A1E),
            Cursor = Cursors.Hand,
            ToolTip = "Keep at the top of the library"
        };
        AutomationProperties.SetAutomationId(star, "LibraryStar." + g.Id);
        star.Click += (_, _) =>
        {
            memory.SetFavourite(g.Id, star.IsChecked == true);
            Refresh();
        };

        var cell = new Grid();
        cell.Children.Add(tile);
        cell.Children.Add(star);
        return cell;
    }

    /// <summary>Draws a picture off the UI thread and puts it into every tile waiting for it.</summary>
    private async Task Draw(string id)
    {
        if (pictures is null || Find(id) is not { } g || drawn.ContainsKey(id) || !drawing.Add(id)) return;

        byte[]? png = await Task.Run(() => pictures.Get(g));
        ImageSource? source = null;

        if (png is not null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(png);
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
            }
            catch (Exception)
            {
                // A picture that will not decode leaves the tile without one.
            }
        }

        drawn[id] = source;
        if (waiting.TryGetValue(id, out var images))
            foreach (var image in images) image.Source = source;
    }

    private static SolidColorBrush Brush(uint argb) =>
        new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
}
