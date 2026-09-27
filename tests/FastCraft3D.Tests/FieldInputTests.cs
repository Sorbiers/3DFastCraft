using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

public class FieldInputTests
{
    [Theory]
    [InlineData("+=5", 5f)]
    [InlineData("-=5", -5f)]
    [InlineData("+5", 5f)]
    [InlineData(" +=  2.5 ", 2.5f)]
    [InlineData("+-5", -5f)]
    public void AnAmountToChangeByIsRecognised(string typed, float expected)
    {
        Assert.True(FieldInput.TryParseRelative(typed, out float delta));
        Assert.Equal(expected, delta, 4);
    }

    /// <summary>
    /// A plain number sets the value - a negative one especially, since positions below zero are
    /// normal on a plate centred on the origin.
    /// </summary>
    [Theory]
    [InlineData("-5")]
    [InlineData("20")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("+=")]
    [InlineData("+=abc")]
    public void EverythingElseIsNotAChange(string? typed) =>
        Assert.False(FieldInput.TryParseRelative(typed, out _));

    [Theory]
    [InlineData("*=2", 2f)]
    [InlineData("*1.5", 1.5f)]
    [InlineData("/=4", 0.25f)]
    [InlineData("/2", 0.5f)]
    [InlineData("*=150%", 1.5f)]
    public void AFactorToMultiplyByIsRecognised(string typed, float expected)
    {
        Assert.True(FieldInput.TryParseFactor(typed, out float factor));
        Assert.Equal(expected, factor, 4);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("+=2")]
    [InlineData("*=0")]
    [InlineData("/=abc")]
    public void EverythingElseIsNotAFactor(string typed) =>
        Assert.False(FieldInput.TryParseFactor(typed, out _));
}
