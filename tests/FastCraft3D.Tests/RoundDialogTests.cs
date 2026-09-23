using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>The round-edges panel previews what it is about to build, and Bevel is really a flat cut.</summary>
public class RoundDialogTests
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

    private static SceneObject Cube => new("Cube", Primitives.Box(20, 20, 20)) { Origin = PrimitiveKind.Cube };

    /// <summary>
    /// Round and Bevel are the same sweep at a different step count, so the only way to be sure
    /// the panel is actually asking for the flat version - rather than a curve too coarse to
    /// notice - is to check the shape it hands back is the exact chamfer, not an approximation.
    /// </summary>
    [Fact]
    public void CheckingBevelSwitchesThePreviewToTheExactFlatCut() => RunSta(() =>
    {
        List<Mesh>? shown = null;
        var dialog = new RoundDialog([Cube], meshes => shown = meshes is null ? null : new List<Mesh>(meshes));

        // Sides only, so the exact volume below - four vertical wedges - is the whole story;
        // with the top and bottom left in too the corners are chamfered as well, which this sum
        // does not account for.
        ((CheckBox)dialog.FindName("EdgeTop")!).IsChecked = false;
        ((CheckBox)dialog.FindName("EdgeBottom")!).IsChecked = false;

        var bevel = (RadioButton)dialog.FindName("StyleBevel")!;
        bevel.IsChecked = true;

        Assert.NotNull(shown);
        double bevelled = Assert.Single(shown!).ComputeSignedVolume();

        // The panel opens at a quarter of the maximum radius, which for a 20 mm cube rounded on
        // its sides alone is a quarter of 10 mm: 2.5 mm. At that radius a bevel removes four
        // exact vertical wedges - see RoundedPrimitiveTests.ABevelledBoxCutsAFlatWedgeNotACurve
        // for the same sum done directly against RoundedPrimitives, with nothing panel-shaped in
        // the way.
        const double radius = 2.5;
        double exact = 20.0 * 20.0 * 20.0 - 4.0 * (radius * radius / 2.0) * 20.0;

        Assert.Equal(exact, bevelled, 1);

        // Bevel is only settled once the panel is actually accepted - clicking through with
        // Round still checked must not leave a stale true behind from ticking the radio button.
        ((Button)dialog.FindName("AcceptButton")!).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(dialog.Bevel);
    });

    [Fact]
    public void TheAcceptButtonNamesTheStyleItIsAboutToApply() => RunSta(() =>
    {
        var dialog = new RoundDialog([Cube], _ => { });

        var accept = (Button)dialog.FindName("AcceptButton")!;
        Assert.Equal("Round", accept.Content);

        var bevel = (RadioButton)dialog.FindName("StyleBevel")!;
        bevel.IsChecked = true;
        Assert.Equal("Bevel", accept.Content);

        dialog.Close();
    });
}
