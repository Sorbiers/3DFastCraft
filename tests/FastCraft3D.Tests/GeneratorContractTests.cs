using FastCraft3D.Generators;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>A settings record is read as its author meant, and a mistake in one is caught with its reason.</summary>
public class GeneratorContractTests
{
    public enum Lid { None, SlidingLid, [ShownAs("Snap on")] Snap }

    public sealed record Good(
        [Length("Width", 10, 100, Group = "Size")] float Width = 50f,
        [Count("Slots", 3, 8, UnitText = "slots")] int Slots = 4,
        [Choice("Lid")] Lid Lid = Lid.SlidingLid,
        [Toggle("Label")] bool Label = true,
        [Length("Snap depth", 0.5, 3), ShowWhen(nameof(Lid), Lid.Snap)] float SnapDepth = 1f);

    public readonly record struct AStruct([Length("Width", 1, 2)] float Width = 1f);

    public sealed record Unlabelled(float Width = 1f);

    public sealed record NoDefault([Length("Width", 1, 2)] float Width);

    public sealed record WrongType([Length("Width", 1, 2)] int Width = 1);

    public sealed record OutOfRange([Length("Width", 1, 2)] float Width = 5f);

    public sealed record ShownByNothing([Length("Width", 1, 2), ShowWhen("Missing", true)] float Width = 1f);

    [Fact]
    public void ASettingsRecordIsReadWithItsLabelsRangesAndDefaults()
    {
        var shape = SettingsShape.Of(typeof(Good));

        Assert.Equal(["Width", "Slots", "Lid", "Label", "SnapDepth"], shape.Parameters.Select(p => p.Name));

        var width = shape.Parameters[0];
        Assert.Equal((ParameterKind.Length, "Width", "Size", 10.0, 100.0, "mm"), (width.Kind, width.Label, width.Group, width.Min, width.Max, width.Unit));
        Assert.Equal(50f, width.Default);

        // An enum's default comes back from reflection as a number and has to be turned back.
        Assert.Equal(Lid.SlidingLid, shape.Parameters[2].Default);
        Assert.Equal(new Good(), shape.Defaults());
    }

    [Fact]
    public void AChoiceIsListedInWordsOrAsItsOwnLabelSays()
    {
        var lid = SettingsShape.Of(typeof(Good)).Parameters[2];
        Assert.Equal(["None", "Sliding lid", "Snap on"], lid.Choices.Select(c => c.Label));
    }

    [Fact]
    public void ASettingIsChangedByBuildingTheRecordAgainWithEverythingElseKept()
    {
        var shape = SettingsShape.Of(typeof(Good));
        var changed = (Good)shape.With(new Good(), "Slots", 6);

        Assert.Equal(new Good() with { Slots = 6 }, changed);
    }

    [Fact]
    public void NumbersOutsideTheirRangeAreBroughtInBeforeAnythingIsMade()
    {
        var shape = SettingsShape.Of(typeof(Good));
        var sane = (Good)shape.Sane(new Good(Width: 500f, Slots: 1));

        Assert.Equal(100f, sane.Width);
        Assert.Equal(3, sane.Slots);
    }

    [Fact]
    public void AFieldShownOnlyForOneChoiceIsHiddenForTheOthers()
    {
        var shape = SettingsShape.Of(typeof(Good));
        var snap = shape.Parameters[4];

        Assert.False(shape.Shows(new Good(), snap));
        Assert.True(shape.Shows(new Good(Lid: Lid.Snap), snap));
    }

    [Theory]
    [InlineData(typeof(AStruct), "record class")]
    [InlineData(typeof(Unlabelled), "says nothing about how it is shown")]
    [InlineData(typeof(NoDefault), "no default")]
    [InlineData(typeof(WrongType), "cannot be")]
    [InlineData(typeof(OutOfRange), "outside")]
    [InlineData(typeof(ShownByNothing), "there is no Missing")]
    public void AMistakeInASettingsRecordIsCaughtWithItsReason(Type type, string reason)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SettingsShape.Of(type));
        Assert.Contains(reason, error.Message);
    }
}
