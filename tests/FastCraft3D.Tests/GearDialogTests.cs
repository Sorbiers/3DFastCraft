using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The gear panel opens, shows what it starts from, and takes its preview away when it closes.</summary>
public class GearDialogTests
{
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Theory]
    [InlineData(GearKind.Gear, 0)]
    [InlineData(GearKind.Ring, 20)]
    [InlineData(GearKind.Rack, 16)]
    public void ItOpensShowingTheGearItStartsFromAndClearsItOnClosing(GearKind kind, int partner) => RunSta(() =>
    {
        var start = new GearOptions { Kind = kind, Teeth = kind == GearKind.Ring ? 60 : 20, PartnerTeeth = partner, Form = ToothForm.Helical };
        var previews = new List<GearOptions?>();

        var dialog = new GearDialog(start, options =>
        {
            previews.Add(options);
            return options is null ? null : Gears.Build(options);
        });

        var shown = Assert.Single(previews);
        Assert.NotNull(shown);
        Assert.Equal(start.Sane(), shown);

        dialog.Close();
        Assert.Null(previews[^1]);
    });
}
