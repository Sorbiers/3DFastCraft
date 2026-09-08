using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Io;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Working in something other than millimetres.
///
/// The unit is a display choice and nothing else. An STL carries no unit and every slicer reads
/// one as millimetres, so the thing that must not happen is a chosen unit reaching the file - a
/// part modelled in inches has to export exactly as it would have in millimetres.
/// </summary>
public class MeasureUnitTests
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

    private static MeasureUnit Named(string label) =>
        MeasureUnit.All.Single(u => u.Label == label);

    [Theory]
    [InlineData("mm", 1f)]
    [InlineData("cm", 10f)]
    [InlineData("m", 1000f)]
    [InlineData("in", 25.4f)]
    [InlineData("ft", 304.8f)]
    public void EachUnitKnowsWhatItIsInMillimetres(string label, float millimetres)
    {
        var unit = Named(label);

        Assert.Equal(millimetres, unit.Millimetres, 4);
        Assert.Equal(1f, unit.From(millimetres), 4);
        Assert.Equal(millimetres, unit.To(1f), 4);
    }

    /// <summary>There and back, because a rounding slip here moves the model.</summary>
    [Theory]
    [InlineData("cm")]
    [InlineData("m")]
    [InlineData("in")]
    [InlineData("ft")]
    public void ConvertingBackGivesTheSameMillimetres(string label)
    {
        var unit = Named(label);

        Assert.Equal(137.5f, unit.To(unit.From(137.5f)), 3);
    }

    /// <summary>A 25.4 mm cube is a one inch cube, and the boxes say so.</summary>
    [Fact]
    public void TheBoxesReadInTheChosenUnit() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        model.UniformScale = false;
        model.Scene.Objects[0].SizeX = 25.4f;

        model.Unit = Named("in");

        Assert.Equal(1f, model.ObjectSizeX, 4);
        Assert.Equal("in", model.UnitLabel);
    });

    /// <summary>And typing one in sets the millimetres behind it.</summary>
    [Fact]
    public void TypingInInchesSetsTheMillimetres() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        model.UniformScale = false;
        model.Unit = Named("in");
        model.ObjectSizeZ = 2f;

        Assert.Equal(50.8f, model.Scene.Objects[0].SizeZ, 3);
    });

    [Fact]
    public void PositionFollowsTheUnitTheSameWay() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        model.Unit = Named("cm");
        model.ObjectPositionX = 3f;

        Assert.Equal(30f, model.Scene.Objects[0].PositionX, 3);
    });

    /// <summary>
    /// The one that matters. Whatever unit is on screen, the file is millimetres - an STL has no
    /// unit of its own, so a part modelled in inches and exported would otherwise arrive at the
    /// slicer twenty-five times too small.
    /// </summary>
    [Fact]
    public void TheUnitNeverReachesTheExport() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        model.UniformScale = false;
        model.Scene.Objects[0].SizeX = 25.4f;
        model.Unit = Named("in");

        var written = new MemoryStream();
        StlWriter.WriteBinary(written, model.Scene.Objects[0].ToWorldMesh());

        long length = written.Length;
        written.Position = 0;
        var read = StlReader.Read(written, length);

        Assert.Equal(25.4f, read.ComputeBounds().Size.X, 2);
    });

    /// <summary>One press of an arrow is a sensible amount in whatever unit is showing.</summary>
    [Theory]
    [InlineData("mm", 1f)]
    [InlineData("cm", 0.5f)]
    [InlineData("m", 0.01f)]
    [InlineData("in", 0.25f)]
    [InlineData("ft", 0.05f)]
    public void TheNudgeIsARoundNumberInEachUnit(string label, float step)
    {
        Assert.Equal(step, Named(label).Step, 4);
    }

    /// <summary>
    /// A model is drawn small and stands for something large, so the reading at scale wants a big
    /// unit - and it has to be a big unit of the same system. Feet beside inches, metres beside
    /// millimetres; metres beside feet is two systems at once.
    /// </summary>
    [Theory]
    [InlineData("mm", "m")]
    [InlineData("cm", "m")]
    [InlineData("m", "m")]
    [InlineData("in", "ft")]
    [InlineData("ft", "ft")]
    public void ImperialReadsAtScaleInFeet(string label, string real)
    {
        Assert.Equal(real, Named(label).Real.Label);
    }

    /// <summary>A metre of model at 1:87 is 87 metres; in imperial it is the same length in feet.</summary>
    [Fact]
    public void TheReadingAtScaleIsInTheSameSystem() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");

        model.UniformScale = false;
        model.Scene.Objects[0].SizeX = 1000f;
        model.ModelScale = 87f;

        model.Unit = Named("m");
        Assert.Equal(87f, model.RealW, 2);
        Assert.Contains("m at 1:87", model.RealUnit);

        model.Unit = Named("ft");
        Assert.Equal(1000f * 87f / 304.8f, model.RealW, 2);
        Assert.Contains("ft at 1:87", model.RealUnit);
    });

    /// <summary>
    /// With more than one thing picked the readings come off the selection in millimetres, not off
    /// the boxes - those read in the display unit now, and taking them would convert twice.
    /// </summary>
    [Fact]
    public void AGroupReadsAtScaleWithoutConvertingTwice() => RunSta(() =>
    {
        var model = new MainViewModel();
        model.InsertCommand.Execute("Cube");
        model.InsertCommand.Execute("Cube");
        model.SelectAllCommand.Execute(null);

        model.ModelScale = 100f;
        model.Unit = Named("cm");

        // Whatever the boxes are in, a hundred millimetres of model at 1:100 is ten metres.
        float millimetres = model.GroupSizeX * 10f;
        Assert.Equal(millimetres * 100f / 1000f, model.RealW, 2);
    });

    /// <summary>The picker draws and announces the same thing, which DisplayMemberPath will not do.</summary>
    [Fact]
    public void AUnitReadsAsItsOwnName()
    {
        Assert.Equal("ft", Named("ft").ToString());
    }
}
