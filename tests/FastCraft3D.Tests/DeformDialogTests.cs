using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The twist, taper and bend dialog opens whatever range its tool has.
///
/// Taper's range starts at 5%, so setting it moved the slider off zero before the preview timer
/// existed, and the dialog threw as it opened. Twist and Bend start below zero and never showed it.
/// </summary>
public class DeformDialogTests
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
    [InlineData(-720, 720, 0, 90)]
    [InlineData(5, 300, 100, 50)]
    [InlineData(-360, 360, 0, 45)]
    public void ItOpensWithARangeThatDoesNotIncludeZero(double minimum, double maximum, double identity, float start) => RunSta(() =>
    {
        var spec = new DeformSpec(
            "Test", "Testing", "", "Value", "", minimum, maximum, 5, identity, "low", "high", false,
            (world, value, _) => MeshDeform.Taper(world, value / 100f));

        var cube = MeshTransform.Transformed(Primitives.Box(20, 20, 20), Matrix4x4.CreateTranslation(0, 0, 10));
        IReadOnlyList<Mesh>? shown = null;

        var dialog = new DeformDialog(spec, "a cube", [cube], start, Axis.X, meshes => shown = meshes);

        Assert.NotNull(shown);
        dialog.Close();
    });
}
