using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The Voronoi panel opens, and goes on working as the choices change.
///
/// Worth its own test because a panel that throws on the way up shows nothing at all - no panel,
/// no clue, just a message box saying an object reference was not set. Shell is checked in the
/// markup, so its Checked handler runs while the panel is still being built and before the
/// sliders it reaches for exist.
/// </summary>
public class VoronoiDialogTests
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

    private static List<SceneObject> OneBall() =>
        [new SceneObject("Ball", Primitives.Sphere(20f, 32, 16)) { Position = new Vector3(0, 0, 20) }];

    [Fact]
    public void ItOpensWithoutThrowing() => RunSta(() =>
    {
        var seen = new List<VoronoiOptions?>();
        var panel = new VoronoiDialog(OneBall(), seen.Add);

        Assert.NotNull(panel);
        Assert.Null(panel.Result);
    });

    /// <summary>
    /// The panel host draws the beta mark off this flag rather than off a heading anyone has to
    /// remember to spell the same way twice. Lithophane sets it in its markup and is not built
    /// here, since it wants a picture file to open at all.
    /// </summary>
    [Fact]
    public void ItSaysItIsStillBeingProvedOut() => RunSta(() =>
    {
        Assert.True(new VoronoiDialog(OneBall(), _ => { }).IsBeta);
    });

    [Fact]
    public void ItAsksForAPreviewOnceItIsUp() => RunSta(() =>
    {
        var seen = new List<VoronoiOptions?>();
        var panel = new VoronoiDialog(OneBall(), seen.Add);

        // The panel describes itself once it is initialised, which is when the web can first be
        // drawn - before that the sliders it reads are not there.
        panel.BeginInit();
        panel.EndInit();

        Assert.NotEmpty(seen);
        Assert.NotNull(seen[^1]);
        Assert.Equal(VoronoiKind.Shell, seen[^1]!.Value.Kind);
    });
}
