using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;

namespace FastCraft3D.View;

/// <summary>
/// Asks for a gear, a ring gear or a rack, showing it on the plate as the numbers change.
///
/// Boxes that mean nothing for what is chosen are greyed rather than hidden, as in Custom shape,
/// so the panel does not jump about. The shaft's boxes apply to a gear, and to the gear made to
/// mesh with a ring or a rack; a ring or a rack has no shaft of its own.
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

        GearKindBox.IsChecked = start.Kind == GearKind.Gear;
        RingKindBox.IsChecked = start.Kind == GearKind.Ring;
        RackKindBox.IsChecked = start.Kind == GearKind.Rack;
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
        PartnerBox.IsChecked = start.PartnerTeeth > 0;
        PartnerTeethBox.Text = (start.PartnerTeeth > 0 ? start.PartnerTeeth : 2 * start.Teeth)
            .ToString(CultureInfo.CurrentCulture);

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
            Kind = RingKindBox.IsChecked == true ? GearKind.Ring : RackKindBox.IsChecked == true ? GearKind.Rack : GearKind.Gear,
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
            PartnerTeeth = PartnerBox.IsChecked == true ? Whole(PartnerTeethBox, 2 * fallback.Teeth) : 0
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
        bool shaft = gear.Kind == GearKind.Gear || PartnerBox.IsChecked == true;
        bool hub = gear.HubDiameter > 0 && gear.HubHeight > 0;

        HelixBox.IsEnabled = gear.Form != ToothForm.Straight;
        RimBox.IsEnabled = gear.Kind != GearKind.Gear;
        RimLabel.Text = gear.Kind == GearKind.Rack ? "Base" : "Rim";
        BoreBox.IsEnabled = shaft;
        BoreSizeBox.IsEnabled = shaft && gear.Bore != BoreShape.None;
        FlatBox.IsEnabled = shaft && gear.Bore == BoreShape.DShaft;
        HubDiameterBox.IsEnabled = HubHeightBox.IsEnabled = shaft;
        SetScrewBox.IsEnabled = shaft && hub;
        PartnerTeethBox.IsEnabled = PartnerBox.IsChecked == true;

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

    private void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnChoice(object sender, RoutedEventArgs e) => Refresh();

    private void OnBoreChanged(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnPartnerClicked(object sender, RoutedEventArgs e) => Refresh();

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
