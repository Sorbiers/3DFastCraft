using System.Globalization;
using System.Numerics;
using System.Windows;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Moulding;

namespace FastCraft3D.View;

/// <summary>
/// Asks how to cut a mould, having already worked out how it wants to be cut.
///
/// The study is done before this opens, so the panel at the top is an answer rather than a
/// prompt: which way the part comes out, how much of it is undercut, and where the air will sit.
/// Everything below it is the arrangement, and the recommendation is only the starting position.
/// </summary>
public partial class MouldDialog : Window
{
    private readonly MouldStudy study;
    private readonly int triangles;
    private readonly Bounds bounds;

    /// <summary>
    /// What the detail choices mean in cells along the block's longest side. Named rather than
    /// numbered, because the number says nothing on its own - what it works out to in millimetres
    /// for this particular model is shown beside it.
    /// </summary>
    private static readonly (string Name, int Cells)[] Details =
    [
        ("Draft", 256),
        ("Normal", 512),
        ("Fine", 768),
    ];

    /// <summary>The study with the cuts the user settled on.</summary>
    public MouldStudy Chosen { get; private set; }

    public MouldOptions Options { get; private set; } = MouldOptions.Default;

    public MouldDialog(string name, MouldStudy study, Mesh model)
    {
        InitializeComponent();

        this.study = study;
        triangles = model.TriangleCount;
        bounds = model.ComputeBounds();
        Chosen = study;

        SubjectText.Text = $"A mould for {name}, to pour silicone into.";
        AdviceText.Text = study.Summary;

        AxisText.Text = string.Join("    ", study.Pulls.Select(p =>
            $"{p.Axis}: {(p.LiftsStraightOut ? "clear" : $"{p.TrappedShare:P0} undercut")}, "
            + $"opening {p.OpeningArea / 100f:0.#} cm2"));

        // Only as many pieces as there are axes to cut on.
        foreach (int pieces in new[] { 2, 4, 8 }.Take(study.Pulls.Count))
            PiecesBox.Items.Add(pieces);

        foreach (var (label, _) in Details) DetailBox.Items.Add(label);
        DetailBox.SelectedIndex = 1;

        // The detail only decides anything on a model too heavy to cut exactly.
        DetailRow.Visibility = triangles > MouldBuilder.ExactLimit
            ? Visibility.Visible
            : Visibility.Collapsed;

        PiecesBox.SelectedItem = 1 << Math.Max(study.Cuts.Count, 1);
        if (PiecesBox.SelectedItem is null && PiecesBox.Items.Count > 0)
            PiecesBox.SelectedIndex = 0;

        Loaded += (_, _) => { WallBox.SelectAll(); WallBox.Focus(); Describe(); };
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Describe();

    private void Describe()
    {
        if (SummaryText is null) return;

        Read();

        int cuts = Chosen.Cuts.Count;
        string where = string.Join(", ", Chosen.Cuts.Select(c => $"{c.Axis} at {c.At:0.#} mm"));

        string vents = Options.AddVents && study.Vents.Count > 0
            ? $" {study.Vents.Count} vent(s)."
            : study.Vents.Count > 0 ? " Vents off - air may stay in." : "";

        string keys = Options.KeyRadius > 0f
            ? $" {4 * cuts} key(s)."
            : " No keys - the faces are plain.";

        SummaryText.Text = $"{1 << cuts} piece(s), cut on {where}.{keys}{vents}";
    }

    /// <summary>
    /// Cuts are taken off the study's own ranking, best axis first, so asking for four pieces
    /// adds the second-best axis rather than an arbitrary one.
    /// </summary>
    private void Read()
    {
        int pieces = PiecesBox?.SelectedItem as int? ?? 2;
        int cuts = Math.Max(1, (int)Math.Log2(pieces));

        Chosen = study with
        {
            Cuts = study.Pulls.Take(cuts).Select(p => new MouldCut(p.Axis, p.SplitAt)).ToList()
        };

        int cells = Details[Math.Max(DetailBox?.SelectedIndex ?? 1, 0)].Cells;

        Options = new MouldOptions(
            Resolution: cells,
            Wall: Number(WallBox?.Text, 8f),
            SprueRadius: Number(SprueBox?.Text, 10f) / 2f,
            VentRadius: Number(VentBox?.Text, 2f) / 2f,
            AddVents: VentsBox?.IsChecked == true,
            KeyRadius: Number(KeyBox?.Text, 8f) / 2f,
            KeyClearance: Number(ClearanceBox?.Text, 0.2f));

        // Which of the two routes this model will take, and what it costs.
        if (RouteText is not null)
        {
            var span = bounds.Size + new Vector3(Options.Wall * 2f);
            float longest = MathF.Max(span.X, MathF.Max(span.Y, span.Z));
            float cell = cells > 0 ? longest / cells : 0f;

            RouteText.Text = triangles <= MouldBuilder.ExactLimit
                ? $"{triangles:N0} triangles - cut exactly, so the cavity keeps every one of them."
                : $"{triangles:N0} triangles is too many to cut exactly, so the model is sampled "
                  + $"at {cell:0.##} mm. Simplify it below {MouldBuilder.ExactLimit:N0} first if you "
                  + "want the cavity exact.";

            if (DetailHint is not null)
                DetailHint.Text = cell > 0f ? $"{cell:0.##} mm" : "";
        }

        if (PiecesHint is not null)
            PiecesHint.Text = Chosen.Cuts.Count > study.Cuts.Count
                ? "more than it needs"
                : Chosen.Cuts.Count < study.Cuts.Count ? "fewer than it asked for" : "as recommended";
    }

    private static float Number(string? text, float fallback) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) && v >= 0f
            ? v
            : fallback;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Read();
        DialogResult = true;
    }
}
