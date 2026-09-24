using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a gear, a ring, a rack, a bevel, a worm or a ratchet, showing it on the plate as the
/// numbers change.
///
/// A row that means nothing for the kind in hand is taken away rather than greyed. Greying it was
/// tried first, so that the panel would not jump about as the kind changed - but six kinds share
/// this panel and no one of them uses half of its rows, so what it mostly did was make the panel
/// longer than the window. The shaft's rows belong to whatever has a shaft: the gear itself, or
/// the one made to mesh with a ring, a rack or a worm.
/// </summary>
public partial class GearDialog : ToolPanel
{
    private static readonly (BoreShape Shape, string Name)[] Bores =
        [(BoreShape.None, "None"), (BoreShape.Round, "Round"), (BoreShape.DShaft, "D-shaft"), (BoreShape.Hex, "Hexagon")];

    private readonly Func<GearOptions?, GearResult?> preview;
    private readonly bool loading;

    /// <param name="preview">Shows the gear on the plate and says what was made, or takes it away when given null.</param>
    public GearDialog(GearOptions start, Func<GearOptions?, GearResult?> preview)
    {
        this.preview = preview;
        loading = true;
        InitializeComponent();

        foreach (var (_, name) in Bores) BoreBox.Items.Add(name);
        foreach (var (_, name) in Bores) MateBoreBox.Items.Add(name);

        GearKindBox.IsChecked = start.Kind == GearKind.Gear;
        RingKindBox.IsChecked = start.Kind == GearKind.Ring;
        RackKindBox.IsChecked = start.Kind == GearKind.Rack;
        BevelKindBox.IsChecked = start.Kind == GearKind.Bevel;
        WormKindBox.IsChecked = start.Kind == GearKind.Worm;
        RatchetKindBox.IsChecked = start.Kind == GearKind.Ratchet;
        StraightBox.IsChecked = start.Form == ToothForm.Straight;
        HelicalBox.IsChecked = start.Form == ToothForm.Helical;
        HerringboneBox.IsChecked = start.Form == ToothForm.Herringbone;

        ModuleBox.Text = Format(start.Module);
        TeethBox.Text = start.Teeth.ToString(CultureInfo.CurrentCulture);
        ThicknessBox.Text = Format(start.Thickness);
        PressureBox.Text = Format(start.PressureAngle);
        HelixBox.Text = Format(start.HelixAngle);
        BacklashBox.Text = Format(start.Backlash);
        RimBox.Text = Format(start.Rim);
        BoreBox.SelectedIndex = Array.FindIndex(Bores, b => b.Shape == start.Bore);
        BoreSizeBox.Text = Format(start.BoreSize);
        FlatBox.Text = Format(start.BoreFlat);
        HubDiameterBox.Text = Format(start.HubDiameter);
        HubHeightBox.Text = Format(start.HubHeight);
        SetScrewBox.Text = Format(start.SetScrew);
        ChamferBox.Text = Format(start.Chamfer);
        PartialBox.IsChecked = start.KeptTeeth > 0;
        KeptTeethBox.Text = (start.KeptTeeth > 0 ? start.KeptTeeth : Math.Max(2, start.Teeth / 4))
            .ToString(CultureInfo.CurrentCulture);
        FrameBox.IsChecked = start.Frame;
        RoundEndsBox.IsChecked = start.FrameEnds == FrameEnds.Round;
        SquareEndsBox.IsChecked = start.FrameEnds == FrameEnds.Square;
        ClearanceBox.Text = Format(start.FrameClearance);
        LockBox.Text = Format(start.LockHeight);
        ConeBox.Text = Format(start.ConeAngle);
        WormDiameterBox.Text = Format(start.WormDiameter);
        UndercutBox.Text = Format(start.Undercut);
        PawlBox.IsChecked = start.WithPawl;
        PartnerBox.IsChecked = start.PartnerTeeth > 0;
        PartnerTeethBox.Text = (start.PartnerTeeth > 0 ? start.PartnerTeeth : 2 * start.Teeth)
            .ToString(CultureInfo.CurrentCulture);

        // The mate opens on whatever it would have been built to anyway: its own numbers where it
        // has them, the gear's where it has not.
        MateLengthBox.Text = Format(start.MateThickness ?? 0f);
        MateBoreBox.SelectedIndex = Array.FindIndex(Bores, b => b.Shape == (start.MateBore ?? start.Bore));
        MateBoreSizeBox.Text = Format(start.MateBoreSize ?? start.BoreSize);
        MateFlatBox.Text = Format(start.MateBoreFlat ?? start.BoreFlat);
        MateHubDiameterBox.Text = Format(start.MateHubDiameter ?? start.HubDiameter);
        MateHubHeightBox.Text = Format(start.MateHubHeight ?? start.HubHeight);
        MateSetScrewBox.Text = Format(start.MateSetScrew ?? start.SetScrew);

        loading = false;
        Refresh();
    }

    /// <summary>Null until the user adds the gear.</summary>
    public GearOptions? Result { get; private set; }

    private static string Format(float value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>What the boxes say, with anything unreadable left at the default.</summary>
    private GearOptions Read()
    {
        var fallback = new GearOptions();

        return new GearOptions
        {
            Kind = RingKindBox.IsChecked == true ? GearKind.Ring
                 : RackKindBox.IsChecked == true ? GearKind.Rack
                 : BevelKindBox.IsChecked == true ? GearKind.Bevel
                 : WormKindBox.IsChecked == true ? GearKind.Worm
                 : RatchetKindBox.IsChecked == true ? GearKind.Ratchet
                 : GearKind.Gear,
            Form = HelicalBox.IsChecked == true ? ToothForm.Helical : HerringboneBox.IsChecked == true ? ToothForm.Herringbone : ToothForm.Straight,
            Module = Number(ModuleBox, fallback.Module),
            Teeth = Whole(TeethBox, fallback.Teeth),
            Thickness = Number(ThicknessBox, fallback.Thickness),
            PressureAngle = Number(PressureBox, fallback.PressureAngle),
            HelixAngle = Number(HelixBox, fallback.HelixAngle),
            Backlash = Number(BacklashBox, fallback.Backlash),
            Rim = Number(RimBox, fallback.Rim),
            Bore = BoreBox.SelectedIndex >= 0 ? Bores[BoreBox.SelectedIndex].Shape : fallback.Bore,
            BoreSize = Number(BoreSizeBox, fallback.BoreSize),
            BoreFlat = Number(FlatBox, fallback.BoreFlat),
            HubDiameter = Number(HubDiameterBox, 0f),
            HubHeight = Number(HubHeightBox, 0f),
            SetScrew = Number(SetScrewBox, 0f),
            Chamfer = Number(ChamferBox, 0f),
            PartnerTeeth = PartnerBox.IsChecked == true ? Whole(PartnerTeethBox, 2 * fallback.Teeth) : 0,
            KeptTeeth = PartialBox.IsChecked == true ? Whole(KeptTeethBox, 0) : 0,
            Frame = FrameBox.IsChecked == true,
            FrameEnds = SquareEndsBox.IsChecked == true ? FrameEnds.Square : FrameEnds.Round,
            FrameClearance = Number(ClearanceBox, fallback.FrameClearance),
            LockHeight = Number(LockBox, fallback.LockHeight),
            ConeAngle = Number(ConeBox, fallback.ConeAngle),
            WormDiameter = Number(WormDiameterBox, 0f),

            // Nought means the gear's own width - or, for a worm wheel, the width the worm asks
            // for. The rest the mate always states for itself once the panel has been through it.
            MateThickness = Number(MateLengthBox, 0f) > 0 ? Number(MateLengthBox, 0f) : null,
            MateBore = MateBoreBox.SelectedIndex >= 0 ? Bores[MateBoreBox.SelectedIndex].Shape : fallback.Bore,
            MateBoreSize = Number(MateBoreSizeBox, fallback.BoreSize),
            MateBoreFlat = Number(MateFlatBox, fallback.BoreFlat),
            MateHubDiameter = Number(MateHubDiameterBox, 0f),
            MateHubHeight = Number(MateHubHeightBox, 0f),
            MateSetScrew = Number(MateSetScrewBox, 0f),
            Undercut = Number(UndercutBox, fallback.Undercut),
            WithPawl = PawlBox.IsChecked == true
        };

        static float Number(TextBox box, float otherwise) =>
            float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) ? v : otherwise;

        static int Whole(TextBox box, int otherwise) =>
            int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int v) ? v : otherwise;
    }

    private void Refresh()
    {
        if (loading || !IsInitialized) return;

        var asked = Read();
        var gear = asked.Sane();
        bool cut = gear.Kind is GearKind.Gear or GearKind.Ring or GearKind.Rack;

        // The frame is the cut-away gear's own mate, so it stands in for a meshing gear.
        bool cutAway = gear.Kind == GearKind.Gear && gear.KeptTeeth > 0;
        bool framed = cutAway && FrameBox.IsChecked == true;
        bool shaft = gear.Kind is GearKind.Gear or GearKind.Bevel or GearKind.Ratchet or GearKind.Worm
                  || PartnerBox.IsChecked == true;
        bool hub = gear.HubDiameter > 0 && gear.HubHeight > 0;

        // A worm is drawn from its own diameter and its length; the teeth box belongs to its wheel.
        bool wheel = gear.Kind == GearKind.Worm;
        bool hubbed = shaft && gear.Kind != GearKind.Bevel;

        Show(TeethRow, !wheel);
        Show(FormRow, cut);
        Show(HelixRow, cut && gear.Form != ToothForm.Straight);
        Show(PressureRow, gear.Kind != GearKind.Ratchet);
        Show(BacklashRow, gear.Kind != GearKind.Ratchet);
        Show(RimRow, gear.Kind is GearKind.Ring or GearKind.Rack || framed);
        Show(ChamferRow, cut);
        RimLabel.Text = gear.Kind == GearKind.Rack ? "Base" : "Rim";
        ThicknessLabel.Text = wheel ? "Length" : "Thickness";

        Show(PartialRow, gear.Kind == GearKind.Gear);
        KeptTeethBox.IsEnabled = PartialBox.IsChecked == true;
        KeptText.Text = gear.KeptTeeth > 0 ? $"of {gear.Teeth} ({360f * gear.KeptTeeth / gear.Teeth:0} deg)" : "teeth";

        Show(FrameRow, cutAway);
        Show(FrameEndsRow, framed);
        Show(ClearanceRow, framed);
        Show(LockRow, framed);

        // With a mate asked for, a bevel's cones come from the two tooth counts instead.
        Show(ConeRow, gear.Kind == GearKind.Bevel && PartnerBox.IsChecked != true);
        Show(WormRow, wheel);
        Show(RatchetRow, gear.Kind == GearKind.Ratchet);

        Show(PartnerRow, gear.Kind != GearKind.Ratchet && !framed);
        PartnerBox.Content = gear.Kind switch
        {
            GearKind.Worm => "Wheel with",
            GearKind.Bevel => "Mating bevel with",
            GearKind.Ring or GearKind.Rack => "Gear inside it with",
            _ => "Meshing gear with"
        };
        PartnerTeethBox.IsEnabled = PartnerBox.IsChecked == true;

        // A frame is a mate too, but one with no shaft in it.
        bool mate = framed || (PartnerBox.IsChecked == true && gear.Kind != GearKind.Ratchet);
        bool mateShaft = mate && !framed && gear.Kind != GearKind.Bevel;
        Show(MateHeader, mate);
        Show(MateLengthRow, mate);
        Show(MateBoreRow, mate && !framed);
        Show(MateFlatRow, mate && !framed && (gear.MateBore ?? gear.Bore) == BoreShape.DShaft);
        Show(MateHubRow, mateShaft);
        Show(MateSetScrewRow, mateShaft && (gear.MateHubDiameter ?? 0f) > 0 && (gear.MateHubHeight ?? 0f) > 0);
        MateLengthLabel.Text = wheel ? "Width" : "Thickness";

        Show(ShaftHeader, shaft);
        Show(BoreRow, shaft);
        Show(FlatRow, shaft && gear.Bore == BoreShape.DShaft);
        Show(HubRow, hubbed);
        Show(SetScrewRow, hubbed && hub);
        BoreSizeBox.IsEnabled = gear.Bore != BoreShape.None;

        var result = preview(gear);

        var lines = Gears.Describe(gear);
        if (asked.Teeth != gear.Teeth)
            lines.Add($"Teeth are held to {gear.Teeth}.");
        if (asked.Chamfer > gear.Chamfer + 1e-3f)
            lines.Add($"The chamfer is held to {gear.Chamfer:0.##} mm, half the module.");
        if (result is not null)
        {
            if (result.Refusal is not null) lines.Add(result.Refusal);
            lines.AddRange(result.Notes);
            if (result.Parts.Count > 0)
                lines.Add($"{result.Parts.Sum(p => p.Mesh.TriangleCount):N0} triangles.");
        }

        SummaryText.Text = string.Join("\n", lines);
        AddButton.IsEnabled = result?.Parts.Count > 0;
    }

    private static void Show(UIElement row, bool wanted) =>
        row.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnChoice(object sender, RoutedEventArgs e) => Refresh();

    private void OnBoreChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    /// <summary>
    /// The mate's own numbers start as copies of the gear's, so a pair that wants two of a kind
    /// needs nothing typed and a pair that does not has somewhere to type it.
    /// </summary>
    private void OnPartnerClicked(object sender, RoutedEventArgs e)
    {
        if (PartnerBox.IsChecked == true)
        {
            MateBoreBox.SelectedIndex = BoreBox.SelectedIndex;
            MateBoreSizeBox.Text = BoreSizeBox.Text;
            MateFlatBox.Text = FlatBox.Text;
            MateHubDiameterBox.Text = HubDiameterBox.Text;
            MateHubHeightBox.Text = HubHeightBox.Text;
            MateSetScrewBox.Text = SetScrewBox.Text;
        }

        Refresh();
    }

    protected override void OnClosed(EventArgs e)
    {
        preview(null);
        base.OnClosed(e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Result = Read().Sane();
        DialogResult = true;
    }
}
