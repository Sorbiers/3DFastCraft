using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Colour is stored as a 0..1 vector but typed as hex or 0..255, so the conversions have to be
/// exact in both directions - a colour that shifts every time the dialog opens is worse than no
/// dialog at all.
/// </summary>
public class PaletteTests
{
    [Fact]
    public void HexRoundTripsThroughTheVector()
    {
        foreach (string hex in new[] { "#000000", "#FFFFFF", "#4D8CD9", "#D9734D", "#23262A" })
        {
            Assert.True(Palette.TryFromHex(hex, out var colour));
            Assert.Equal(hex, Palette.ToHex(colour));
        }
    }

    [Fact]
    public void EveryByteValueSurvivesTheTrip()
    {
        for (int v = 0; v <= 255; v++)
        {
            var colour = Palette.FromBytes((byte)v, (byte)v, (byte)v);
            Assert.Equal(((byte)v, (byte)v, (byte)v), Palette.ToBytes(colour));
        }
    }

    [Fact]
    public void HexIsAcceptedWithoutTheHashAndInShorthand()
    {
        Assert.True(Palette.TryFromHex("4D8CD9", out var bare));
        Assert.Equal("#4D8CD9", Palette.ToHex(bare));

        // #ABC means #AABBCC, not #0A0B0C.
        Assert.True(Palette.TryFromHex("#F0C", out var shorthand));
        Assert.Equal("#FF00CC", Palette.ToHex(shorthand));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("cornflower")]
    public void NonsenseIsRejectedRatherThanGuessedAt(string? text)
    {
        Assert.False(Palette.TryFromHex(text, out _));
    }

    /// <summary>Typing a hex code must not shift the colour as the picker converts it to HSV and back.</summary>
    [Fact]
    public void HsvRoundTripsWithinAByte()
    {
        foreach (var swatch in Palette.Swatches)
        {
            var (h, s, v) = Palette.ToHsv(swatch.Colour);
            var back = Palette.FromHsv(h, s, v);

            Assert.Equal(Palette.ToHex(swatch.Colour), Palette.ToHex(back));
        }
    }

    [Fact]
    public void TheSixPrimaryHuesLandWhereTheyShould()
    {
        Assert.Equal("#FF0000", Palette.ToHex(Palette.FromHsv(0, 1, 1)));
        Assert.Equal("#FFFF00", Palette.ToHex(Palette.FromHsv(60, 1, 1)));
        Assert.Equal("#00FF00", Palette.ToHex(Palette.FromHsv(120, 1, 1)));
        Assert.Equal("#00FFFF", Palette.ToHex(Palette.FromHsv(180, 1, 1)));
        Assert.Equal("#0000FF", Palette.ToHex(Palette.FromHsv(240, 1, 1)));
        Assert.Equal("#FF00FF", Palette.ToHex(Palette.FromHsv(300, 1, 1)));

        // 360 is 0 again rather than falling off the end of the switch.
        Assert.Equal("#FF0000", Palette.ToHex(Palette.FromHsv(360, 1, 1)));
    }

    [Fact]
    public void SaturationAndValueBottomOutAtWhiteAndBlack()
    {
        Assert.Equal("#FFFFFF", Palette.ToHex(Palette.FromHsv(210, 0, 1)));
        Assert.Equal("#000000", Palette.ToHex(Palette.FromHsv(210, 1, 0)));
    }

    /// <summary>Grey reports no hue, which is what lets the picker leave its strip alone.</summary>
    [Fact]
    public void GreyHasNoSaturation()
    {
        var (_, s, v) = Palette.ToHsv(Palette.FromHex("#808080"));

        Assert.Equal(0f, s, 3);
        Assert.Equal(128f / 255f, v, 3);
    }

    [Fact]
    public void OutOfRangeComponentsAreClampedNotWrapped()
    {
        Assert.Equal("#FFFFFF", Palette.ToHex(new Vector3(2f, 5f, 1.0001f)));
        Assert.Equal("#000000", Palette.ToHex(new Vector3(-1f, -0.001f, 0f)));
    }

    /// <summary>The automatic colours must all be pickable by hand afterwards.</summary>
    [Fact]
    public void EveryAutomaticColourAppearsInThePalette()
    {
        foreach (var colour in Palette.Cycle)
            Assert.Contains(Palette.Swatches, s => s.Colour == colour);
    }

    [Fact]
    public void TheSwatchesFillWholeRowsAndAreAllDistinct()
    {
        Assert.Equal(24, Palette.Swatches.Count); // three rows of eight
        Assert.Equal(24, Palette.Swatches.Select(s => Palette.ToHex(s.Colour)).Distinct().Count());
        Assert.Equal(24, Palette.Swatches.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public void DarkTextIsChosenOnlyForPaleColours()
    {
        Assert.True(Palette.PrefersDarkText(Palette.FromHex("#FFFFFF")));
        Assert.True(Palette.PrefersDarkText(Palette.FromHex("#EFD34D")));
        Assert.False(Palette.PrefersDarkText(Palette.FromHex("#000000")));
        Assert.False(Palette.PrefersDarkText(Palette.FromHex("#5C6BD6")));
    }
}

/// <summary>
/// Painting has to undo as one step for the whole selection, the same way a multi-object move
/// does - otherwise recolouring six parts costs six presses of Ctrl+Z.
/// </summary>
public class ColourCommandTests
{
    private static readonly Vector3 Red = Palette.FromHex("#D6455C");
    private static readonly Vector3 Blue = Palette.FromHex("#4D8CD9");

    private static SceneObject Box(string name, Vector3 colour) =>
        new(name, Primitives.Box(10, 10, 10)) { Colour = colour };

    [Fact]
    public void PaintingTheSelectionIsASingleUndoStep()
    {
        var scene = new Scene();
        var a = Box("A", Blue);
        var b = Box("B", Palette.FromHex("#66B873"));
        scene.Objects.Add(a);
        scene.Objects.Add(b);
        var undo = new UndoStack(scene);

        undo.Execute(ColourCommand.CreateIfChanged("Colour 2 objects", [a, b], Red)!);

        Assert.Equal(Red, a.Colour);
        Assert.Equal(Red, b.Colour);

        undo.Undo();

        // Each object goes back to its own colour, not to a shared one.
        Assert.Equal(Blue, a.Colour);
        Assert.Equal(Palette.FromHex("#66B873"), b.Colour);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void RedoPaintsItAgain()
    {
        var scene = new Scene();
        var a = Box("A", Blue);
        scene.Objects.Add(a);
        var undo = new UndoStack(scene);

        undo.Execute(ColourCommand.CreateIfChanged("Colour", [a], Red)!);
        undo.Undo();
        undo.Redo();

        Assert.Equal(Red, a.Colour);
    }

    /// <summary>Clicking the swatch an object already has must not fill the undo stack.</summary>
    [Fact]
    public void RepaintingTheSameColourIsNotAStep()
    {
        var a = Box("A", Blue);
        var b = Box("B", Blue);

        Assert.Null(ColourCommand.CreateIfChanged("Colour", [a, b], Blue));

        // But a mixed selection is a real change even when one of them already matches.
        b.Colour = Red;
        Assert.NotNull(ColourCommand.CreateIfChanged("Colour", [a, b], Blue));
    }

    [Fact]
    public void AnEmptySelectionProducesNothing()
    {
        Assert.Null(ColourCommand.CreateIfChanged("Colour", [], Red));
    }

    /// <summary>Colour survives a copy, so duplicating a painted part keeps the paint.</summary>
    [Fact]
    public void CloningKeepsTheColour()
    {
        var clone = Box("A", Red).Clone();

        Assert.Equal(Red, clone.Colour);
    }
}

/// <summary>
/// The picker's XAML is only parsed when the dialog is first constructed, so a broken binding or
/// a missing resource would otherwise surface as a crash in the user's hands rather than here.
/// Nothing is shown: the window is built, driven and dropped.
/// </summary>
public class ColourDialogTests
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

    [Fact]
    public void TheDialogBuildsAndStartsOnTheColourItWasGiven()
    {
        RunSta(() =>
        {
            var start = Palette.FromHex("#8E63D6");
            var dialog = new ColourDialog(start);

            // Cancelled until Apply is pressed, so a stray close cannot repaint anything.
            Assert.Null(dialog.Result);

            var hex = (TextBox)dialog.FindName("HexBox")!;
            Assert.Equal("#8E63D6", hex.Text);
            Assert.Equal("142", ((TextBox)dialog.FindName("RedBox")!).Text);
            Assert.Equal("99", ((TextBox)dialog.FindName("GreenBox")!).Text);
            Assert.Equal("214", ((TextBox)dialog.FindName("BlueBox")!).Text);
        });
    }

    [Fact]
    public void TypingAHexCodeMovesTheChannelBoxes()
    {
        RunSta(() =>
        {
            var dialog = new ColourDialog(Palette.FromHex("#000000"));

            ((TextBox)dialog.FindName("HexBox")!).Text = "#45BFD6";

            Assert.Equal("69", ((TextBox)dialog.FindName("RedBox")!).Text);
            Assert.Equal("191", ((TextBox)dialog.FindName("GreenBox")!).Text);
            Assert.Equal("214", ((TextBox)dialog.FindName("BlueBox")!).Text);
        });
    }

    [Fact]
    public void TypingChannelsMovesTheHexBox()
    {
        RunSta(() =>
        {
            var dialog = new ColourDialog(Palette.FromHex("#000000"));

            ((TextBox)dialog.FindName("RedBox")!).Text = "217";
            ((TextBox)dialog.FindName("GreenBox")!).Text = "115";
            ((TextBox)dialog.FindName("BlueBox")!).Text = "77";

            Assert.Equal("#D9734D", ((TextBox)dialog.FindName("HexBox")!).Text);
        });
    }

    /// <summary>Half-typed input must leave the last good colour alone rather than jump to black.</summary>
    [Fact]
    public void AnIncompleteHexCodeIsIgnoredWhileItIsBeingTyped()
    {
        RunSta(() =>
        {
            var dialog = new ColourDialog(Palette.FromHex("#45BFD6"));
            var hex = (TextBox)dialog.FindName("HexBox")!;

            // Four and five digits are not a colour - only three or six are - so the channels
            // must hold the last good value rather than resetting.
            hex.Text = "#45BF";
            hex.Text = "#45BFD";

            Assert.Equal("69", ((TextBox)dialog.FindName("RedBox")!).Text);
            Assert.Equal("191", ((TextBox)dialog.FindName("GreenBox")!).Text);
            Assert.Equal("214", ((TextBox)dialog.FindName("BlueBox")!).Text);
        });
    }

    /// <summary>
    /// Sliding the value down to black and back up must return the hue the user chose. Black has
    /// no hue to read back, so the dialog has to remember it.
    /// </summary>
    [Fact]
    public void TheHueSurvivesATripThroughBlack()
    {
        RunSta(() =>
        {
            var dialog = new ColourDialog(Palette.FromHex("#45BFD6"));
            var hex = (TextBox)dialog.FindName("HexBox")!;

            hex.Text = "#000000";
            hex.Text = "#45BFD6";

            Assert.Equal("#45BFD6", hex.Text);
        });
    }

    [Fact]
    public void EveryPaletteSwatchSurvivesBeingLoadedIntoThePicker()
    {
        RunSta(() =>
        {
            foreach (var swatch in Palette.Swatches)
            {
                var dialog = new ColourDialog(swatch.Colour);

                Assert.Equal(
                    Palette.ToHex(swatch.Colour),
                    ((TextBox)dialog.FindName("HexBox")!).Text);
            }
        });
    }
}
