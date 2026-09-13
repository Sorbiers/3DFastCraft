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
}
