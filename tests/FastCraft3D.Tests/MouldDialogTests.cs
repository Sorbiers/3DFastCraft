using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Moulding;
using FastCraft3D.View;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>The Mold tool's dialog opens, and a mold is made, for a part with a hollow in it.</summary>
public class MouldDialogTests(ITestOutputHelper log)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheMouldDialogOpensForAHollowPartAndAMoldIsMadeFromIt(int kind)
    {
        // A square tube (walls only) and a box open at the top.
        var outer = MeshTransform.Transformed(Primitives.Box(60, 60, 40), Matrix4x4.CreateTranslation(0, 0, 20));
        var inner = MeshTransform.Transformed(Primitives.Box(50, 50, kind == 0 ? 60 : 40), Matrix4x4.CreateTranslation(0, 0, kind == 0 ? 20 : 25));
        var model = ManifoldCsg.Subtract(outer, inner)!;
        log.WriteLine($"{model.TriangleCount} triangles, watertight {model.CheckHealth().IsWatertight}");

        MouldStudy study = MouldAnalysis.Study(model, 64, default);
        log.WriteLine($"pulls {study.Pulls.Count}, cuts {study.Cuts.Count}, {study.Summary}");

        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new MouldDialog("Cube", study, model, 0.2f);
                log.WriteLine("dialog built");
                var result = MouldBuilder.Build(model, dialog.Chosen, dialog.Options, default, null);
                log.WriteLine($"built {result.Parts.Count} parts: {result.Summary}");
                Assert.Equal(2, result.Parts.Count);
                Assert.All(result.Parts, p => Assert.True(p.Watertight));
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
