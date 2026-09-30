using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Generators;

namespace FastCraft3D.View;

/// <summary>
/// The app's settings: for now the printer profiles, each a printer and filament with the fits a
/// test print found for it. Edited as a copy and handed back whole, so Cancel leaves them as they were.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly List<Printer> printers;
    private int showing = -1;
    private bool filling;

    public SettingsDialog(PrinterProfiles profiles)
    {
        InitializeComponent();

        printers = profiles.All.ToList();
        FillList(Math.Max(0, printers.FindIndex(p => p.Name == profiles.Current)));
    }

    /// <summary>The profiles as left, the one showing in use; null when cancelled.</summary>
    public PrinterProfiles? Result { get; private set; }

    /// <summary>Whether the test print was asked for, to open once the settings are kept.</summary>
    public bool PrintTest { get; private set; }

    private void FillList(int select)
    {
        filling = true;
        ProfileList.Items.Clear();
        foreach (var p in printers) ProfileList.Items.Add(p.Name);
        ProfileList.SelectedIndex = select;
        filling = false;

        Show(select);
    }

    private void Show(int index)
    {
        showing = index;
        var p = printers[index];
        NameBox.Text = p.Name;
        NozzleBox.Text = Say(p.Nozzle);
        LayerBox.Text = Say(p.Layer);
        SlidingBox.Text = Say(p.XyClearance);
        HoleBox.Text = Say(p.HoleClearance);
        PressBox.Text = Say(p.PressFit);
        BrickBox.Text = Say(p.BrickFit);
        ThreadBox.Text = Say(p.ThreadClearance);
        DeleteButton.IsEnabled = printers.Count > 1;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private static string Say(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>What the boxes say, as the profile showing; null, with the reason shown, when a box is not a number.</summary>
    private Printer? Read()
    {
        var p = printers[showing];
        float? Number(TextBox box, float least, float most, string what)
        {
            if (float.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out float v) && float.IsFinite(v) && v >= least && v <= most)
                return v;

            ErrorText.Text = $"{what} has to be a number from {least:0.##} to {most:0.##}.";
            ErrorText.Visibility = Visibility.Visible;
            box.Focus();
            return null;
        }

        if (Number(NozzleBox, Printer.LeastNozzle, Printer.MostNozzle, "Nozzle") is not { } nozzle
            || Number(LayerBox, Printer.LeastLayer, Printer.MostLayer, "Layer height") is not { } layer
            || Number(SlidingBox, Printer.LeastClearance, Printer.MostClearance, "Sliding clearance") is not { } sliding
            || Number(HoleBox, Printer.LeastClearance, Printer.MostClearance, "Hole clearance") is not { } hole
            || Number(PressBox, Printer.LeastClearance, Printer.MostClearance, "Press fit") is not { } press
            || Number(BrickBox, Printer.LeastBrickFit, Printer.MostBrickFit, "Brick fit") is not { } brick
            || Number(ThreadBox, Printer.LeastClearance, Printer.MostClearance, "Thread clearance") is not { } thread)
            return null;

        string name = string.IsNullOrWhiteSpace(NameBox.Text) ? p.Name : NameBox.Text.Trim();
        if (printers.Where((_, i) => i != showing).Any(q => q.Name == name))
        {
            ErrorText.Text = $"There is a profile called {name} already.";
            ErrorText.Visibility = Visibility.Visible;
            NameBox.Focus();
            return null;
        }

        return new Printer(nozzle, layer, sliding)
        {
            Name = name, HoleClearance = hole, PressFit = press, BrickFit = brick, ThreadClearance = thread
        }.Saned();
    }

    /// <summary>Keeps what the boxes say in the profile showing, before another is shown or the window closes.</summary>
    private bool Keep()
    {
        if (Read() is not { } p) return false;
        printers[showing] = p;
        return true;
    }

    private void OnProfileChosen(object sender, SelectionChangedEventArgs e)
    {
        if (filling || ProfileList.SelectedIndex < 0 || ProfileList.SelectedIndex == showing) return;

        int wanted = ProfileList.SelectedIndex;
        if (!Keep())
        {
            // Stays on the one with the mistake in it, so it can be put right.
            filling = true;
            ProfileList.SelectedIndex = showing;
            filling = false;
            return;
        }

        FillList(wanted);
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (!Keep()) return;

        var copy = printers[showing];
        string name = copy.Name;
        for (int n = 2; printers.Any(p => p.Name == name); n++) name = $"{copy.Name} {n}";

        printers.Add(copy with { Name = name });
        FillList(printers.Count - 1);
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (printers.Count < 2) return;
        printers.RemoveAt(showing);
        FillList(Math.Min(showing, printers.Count - 1));
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (!Keep()) return;
        Result = new PrinterProfiles(printers.ToList(), printers[showing].Name);
        DialogResult = true;
    }

    private void OnPrintTest(object sender, RoutedEventArgs e)
    {
        PrintTest = true;
        OnAccept(sender, e);
    }
}
