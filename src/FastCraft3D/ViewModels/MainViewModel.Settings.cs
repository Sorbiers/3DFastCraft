using System.Windows;
using System.Windows.Input;
using FastCraft3D.Generators;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>The Settings window, and the printer profile reaching the tools outside the Library.</summary>
public sealed partial class MainViewModel
{
    private ICommand? openSettings;

    public ICommand OpenSettingsCommand => openSettings ??= Track(RelayCommand.Simple(OpenSettings, () => NothingInHand));

    /// <summary>The printer in use, as the Settings window or the Library's panel last left it.</summary>
    public Printer Printer => CurrentPrinter;

    private void OpenSettings()
    {
        var dialog = new SettingsDialog(PrinterProfile.LoadAll()) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } profiles) return;

        PrinterProfile.SaveAll(profiles);
        UsePrinter(profiles.InUse);
        Status = $"Printing with {profiles.InUse.Name}: {PrinterProfile.Describe(profiles.InUse)}";

        if (dialog.PrintTest && GeneratorRegistry.Find("calibration.profile") is { } test)
            InsertGeneratedCommand.Execute(test);
    }

    /// <summary>
    /// Takes up the profile kept on this machine. Called by the window as it opens, rather than
    /// by the view model as it is made: a view model made for a test starts from the defaults, not
    /// from whatever printer the machine running the tests was last set to.
    /// </summary>
    public void UseSavedPrinter() => UsePrinter(PrinterProfile.Load());

    /// <summary>
    /// A printer taken up: the tools that keep a fit of their own start from its numbers - Hole
    /// from its hole clearance, the connectors of Split and Connect from the hole clearance and
    /// the brick fit, brick studs from the brick fit. Mold's keys and Keyhole read it as they
    /// open. A number typed into a tool since is replaced: the profile is what was asked for.
    /// </summary>
    public void UsePrinter(Printer chosen)
    {
        printer = chosen;

        lastHole = lastHole with { ExtraClearance = chosen.HoleClearance };
        connectors = connectors with { Clearance = chosen.HoleClearance, BrickFit = chosen.BrickFit };
        Raise(nameof(ConnectorClearance));
        Raise(nameof(ConnectorBrickFit));
        EngraveStudFit = chosen.BrickFit;
        Raise(nameof(Printer));
    }
}
