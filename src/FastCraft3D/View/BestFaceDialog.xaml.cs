using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Which way up to print the part, with the numbers behind each answer.
///
/// A list rather than a single button, because "best" is not one thing: least support, shortest
/// print and firmest footing are three different faces on plenty of parts, and the person holding
/// the printer knows which they are short of. Hovering a row tips the part over on the plate, so
/// the answer is seen rather than read - the numbers say what it costs, the model says whether it
/// is the shape anyone wanted.
/// </summary>
public partial class BestFaceDialog : ToolPanel
{
    private readonly List<StandingChoice> measured;
    private readonly Action<StandingChoice?> show;

    /// <summary>How many rows are worth reading. Past this the answers repeat in all but numbers.</summary>
    private const int MostOffered = 5;

    public BestFaceDialog(string subject, IReadOnlyList<StandingChoice> choices, Action<StandingChoice?> show)
    {
        this.show = show;
        measured = choices.ToList();
        InitializeComponent();

        SubjectText.Text = measured.Count == 1
            ? $"{subject} can stand one way up."
            : $"{subject} can stand {measured.Count} ways up. Hover one to see it, click to put it down.";

        Fill();
    }

    /// <summary>The face picked, or null when the panel was cancelled.</summary>
    public StandingChoice? Result { get; private set; }

    private FaceChoice Favour => FavourBox.SelectedIndex switch
    {
        1 => FaceChoice.ShorterPrint,
        2 => FaceChoice.FirmerFooting,
        _ => FaceChoice.LessSupport
    };

    /// <summary>One row as the list shows it. A plain shape, because the template binds to it.</summary>
    private sealed record Row(string Heading, string Detail, string Mark, string Hint, Brush Shade, StandingChoice Choice);

    private void Fill()
    {
        if (!IsInitialized) return;

        var ordered = BestFace.Order(measured, Favour);

        // The face it is standing on now is always shown, even when it is nobody's idea of a good
        // one: without it the list says where to go and not what it saves, and the saving is the
        // whole reason to turn a part over.
        var shown = ordered.Take(MostOffered).ToList();
        var standing = ordered.FirstOrDefault(c => c.IsCurrent);
        if (standing is not null && !shown.Contains(standing)) shown.Add(standing);

        var best = ordered.Count > 0 ? ordered[0] : null;
        ChoiceList.ItemsSource = shown.Select(c => Describe(c, best)).ToList();

        SummaryText.Text = Summarise(best, standing);
    }

    private static Row Describe(StandingChoice choice, StandingChoice? best)
    {
        string support = choice.SupportMm2 < 1.0
            ? "no support"
            : $"{choice.SupportMm2 / 100.0:0.#} cm² support";

        string alike = choice.Alike > 1 ? $" · {choice.Alike} alike" : "";

        return new Row(
            Heading: support,
            Detail: $"{choice.HeightMm:0.#} mm tall · {choice.FootprintMm2 / 100f:0.#} cm² on the bed "
                  + $"· tips at {choice.TipDegrees:0}°{alike}",
            Mark: choice.IsCurrent ? "standing on it" : ReferenceEquals(choice, best) ? "best" : "",
            Hint: choice.TipDegrees < 10f
                ? "Narrow for its height - it stands, but it would not take a knock."
                : "Click to turn the part onto this face.",
            Shade: choice.IsCurrent
                ? new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFA))
                : Brushes.White,
            Choice: choice);
    }

    private static string Summarise(StandingChoice? best, StandingChoice? standing)
    {
        if (best is null) return "There is nothing here that will stand up.";

        if (standing is null)
            return "The part is not resting on any of these faces at the moment, so there is "
                 + "nothing to compare against - it is lying somewhere it would not stay.";

        if (ReferenceEquals(best, standing))
            return "It is already the best way up by this measure.";

        double saved = standing.SupportMm2 - best.SupportMm2;
        if (saved < 10.0)
            return $"The best face saves next to no support, but it stands {standing.HeightMm - best.HeightMm:0.#} mm "
                 + "lower, which is most of the print time.";

        return $"Turning it over saves {saved / 100.0:0.#} cm² of support, "
             + $"{(best.HeightMm <= standing.HeightMm ? "and it stands lower too" : "though it stands taller")}.";
    }

    private void OnFavourChanged(object sender, SelectionChangedEventArgs e) => Fill();

    private void OnHover(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Row row }) show(row.Choice);
    }

    private void OnLeave(object sender, RoutedEventArgs e) => show(null);

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Row row }) return;

        Result = row.Choice;
        DialogResult = true;
    }
}
