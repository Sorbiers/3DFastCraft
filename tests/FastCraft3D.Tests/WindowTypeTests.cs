using FastCraft3D.Generators;
using FastCraft3D.Generators.Buildings;
using FastCraft3D.Geometry;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The window's types - fixed, single hung, sliding, casement - and the trim and shutters any
/// square one can have. All built lying on the back with the facade up, as they print.
/// </summary>
public class WindowTypeTests
{
    private static readonly Window Generator = new();

    private static Window.Settings Of(WindowType type, Func<Window.Settings, Window.Settings>? change = null)
    {
        var s = Generator.Default with { Type = type, Flat = true };
        s = (Window.Settings)Generator.Adjusted(Generator.Default, s, nameof(Window.Settings.Type));
        return change is null ? s : change(s);
    }

    private static Generated Made(Window.Settings s)
    {
        var made = Generator.Make(s, Printer.Default);
        Assert.False(made.IsRefused, made.Refusal);
        return made;
    }

    private static bool Shows(Window.Settings s, string parameter) =>
        Generator.Shows(s, Generator.Parameters.Single(p => p.Name == parameter));

    // --- The fixed window is the one there was --------------------------------------------

    [Fact]
    public void AFixedWindowIsAsItWasStandingOnItsSillWithItsGlass()
    {
        var made = Made(Generator.Default);

        var frame = made.Parts.Single(p => p.Role == "frame").Mesh.ComputeBounds();
        Assert.Equal(-7.4f, frame.Min.X, 3);
        Assert.Equal(7.4f, frame.Max.X, 3);
        Assert.Equal(-2.4f, frame.Min.Y, 3);
        Assert.Equal(0f, frame.Max.Y, 3);
        Assert.Equal(0f, frame.Min.Z, 3);
        Assert.Equal(16.8f, frame.Max.Z, 3);
        Assert.Equal(2, made.Parts.Count);
    }

    [Fact]
    public void AWindowSavedBeforeTypesReadsAsFixedAtItsOldNumbers()
    {
        var old = Recipes.Read(Generator, "{\"Width\":18,\"Height\":20,\"Columns\":3,\"Rows\":4,\"Shape\":\"Arched\"}");

        var s = (Window.Settings)old;
        Assert.Equal(WindowType.Fixed, s.Type);
        Assert.Equal(WindowShape.Arched, s.Shape);
        Assert.Equal(18f, s.Width);
        Assert.Equal(3, s.Columns);
        Assert.Equal(WindowTrim.None, s.Trim);
        Assert.Equal(WindowShutters.None, s.Shutters);
    }

    // --- The form -------------------------------------------------------------------------

    [Fact]
    public void TheFormBeginsWithTheTypeAndNothingIsHeadedSizeTwice()
    {
        Assert.Equal(nameof(Window.Settings.Type), Generator.Parameters[0].Name);

        // A heading is printed wherever the group changes, so a group that comes round again
        // is a second heading of the same name.
        string?[] groups = [.. Generator.Parameters.Select(p => p.Group)];
        var seen = new HashSet<string>();
        string? last = null;
        foreach (var group in groups)
        {
            if (group is not null && group != last) Assert.True(seen.Add(group), $"\"{group}\" is headed twice");
            last = group;
        }
    }

    [Fact]
    public void EachTypeAsksForItsOwnSettingsAndNotTheOthers()
    {
        var fixedOne = Of(WindowType.Fixed);
        Assert.True(Shows(fixedOne, nameof(Window.Settings.Shape)));
        Assert.True(Shows(fixedOne, nameof(Window.Settings.Columns)));
        Assert.False(Shows(fixedOne, nameof(Window.Settings.Sash)));
        Assert.False(Shows(fixedOne, nameof(Window.Settings.LightsAcross)));

        var hung = Of(WindowType.SingleHung);
        Assert.False(Shows(hung, nameof(Window.Settings.Shape)));
        Assert.False(Shows(hung, nameof(Window.Settings.Columns)));
        Assert.True(Shows(hung, nameof(Window.Settings.LightsAcross)));
        Assert.True(Shows(hung, nameof(Window.Settings.Step)));
        Assert.True(Shows(hung, nameof(Window.Settings.UpperFront)));
        Assert.False(Shows(hung, nameof(Window.Settings.LeftFront)));
        Assert.False(Shows(hung, nameof(Window.Settings.Casements)));

        var sliding = Of(WindowType.Sliding);
        Assert.True(Shows(sliding, nameof(Window.Settings.LeftFront)));
        Assert.False(Shows(sliding, nameof(Window.Settings.UpperFront)));

        var casement = Of(WindowType.Casement);
        Assert.True(Shows(casement, nameof(Window.Settings.Casements)));
        Assert.False(Shows(casement, nameof(Window.Settings.Step)));
        Assert.Equal(casement.Casements > 1, Shows(casement, nameof(Window.Settings.Mullion)));
    }

    [Fact]
    public void ChoosingATypeFillsInItsLightsAndItsCasements()
    {
        var hung = Of(WindowType.SingleHung);
        Assert.Equal((2, 2), (hung.LightsAcross, hung.LightsUp));

        var casement = Of(WindowType.Casement, s => s with { Width = 21 });
        var again = (Window.Settings)Generator.Adjusted(casement, casement with { Width = 21 }, nameof(Window.Settings.Type));
        Assert.Equal(3, again.Casements);
        Assert.Equal(1, again.LightsAcross);
    }

    [Fact]
    public void TrimAndShuttersAreOfferedOnlyWhereThereIsASquareOutlineToFollow()
    {
        Assert.True(Shows(Of(WindowType.Fixed), nameof(Window.Settings.Trim)));
        Assert.True(Shows(Of(WindowType.Casement), nameof(Window.Settings.Shutters)));

        foreach (var shape in new[] { WindowShape.Arched, WindowShape.HalfRound, WindowShape.Round, WindowShape.Bow })
        {
            var s = Of(WindowType.Fixed, w => w with { Shape = shape });
            Assert.False(Shows(s, nameof(Window.Settings.Trim)), $"{shape}");
            Assert.False(Shows(s, nameof(Window.Settings.Shutters)), $"{shape}");
        }
    }

    [Fact]
    public void ShutterOptionsFollowTheStyleChosen()
    {
        var s = Of(WindowType.Fixed, w => w with { Shutters = WindowShutters.Pair });

        Assert.True(Shows(s with { Style = ShutterKind.Louvred }, nameof(Window.Settings.Slats)));
        Assert.False(Shows(s with { Style = ShutterKind.Louvred }, nameof(Window.Settings.Boards)));
        Assert.True(Shows(s with { Style = ShutterKind.Planked }, nameof(Window.Settings.Boards)));
        Assert.True(Shows(s with { Style = ShutterKind.Panelled }, nameof(Window.Settings.ShutterPanels)));
        Assert.False(Shows(s with { Shutters = WindowShutters.None }, nameof(Window.Settings.Style)));
    }

    // --- Sashes ---------------------------------------------------------------------------

    [Fact]
    public void ASingleHungWindowHasTwoSashesOneAboveTheOtherMeetingRailOverRail()
    {
        var s = Of(WindowType.SingleHung);

        var sashes = Window.Sashes(s);

        Assert.Equal(2, sashes.Count);
        var lower = sashes.Single(k => k.Y0 < s.Height / 2f && k.Y1 < s.Height - s.Frame - 0.1f);
        var upper = sashes.Single(k => k != lower);
        Assert.True(upper.Y0 < lower.Y1, "they overlap at the meeting rails");
        Assert.Equal(s.Sash, lower.Y1 - upper.Y0, 3);
        Assert.Equal(lower.X0, upper.X0);
    }

    [Fact]
    public void TheUpperSashIsInFrontByDefaultAndTheLowerWhenAskedForIt()
    {
        var s = Of(WindowType.SingleHung);
        float front = s.Depth - s.SetBack, back = front - s.Step;

        var normal = Window.Sashes(s);
        var upper = normal.Single(k => k.Y1 == normal.Max(m => m.Y1));
        Assert.Equal(front, upper.Level, 4);
        Assert.Equal(back, normal.Single(k => k != upper).Level, 4);

        var reversed = Window.Sashes(s with { UpperFront = false });
        upper = reversed.Single(k => k.Y1 == reversed.Max(m => m.Y1));
        Assert.Equal(back, upper.Level, 4);
        Assert.Equal(front, reversed.Single(k => k != upper).Level, 4);
    }

    [Fact]
    public void ASlidingWindowHasTwoSashesSideBySideMeetingStileOverStile()
    {
        var s = Of(WindowType.Sliding, w => w with { Width = 20, Height = 12 });

        var sashes = Window.Sashes(s);

        Assert.Equal(2, sashes.Count);
        var left = sashes.Single(k => k.X0 == sashes.Min(m => m.X0));
        var right = sashes.Single(k => k != left);
        Assert.Equal(s.Sash, left.X1 - right.X0, 3);
        Assert.NotEqual(left.Level, right.Level);

        var other = Window.Sashes(s with { LeftFront = true });
        Assert.True(other.Single(k => k.X0 == other.Min(m => m.X0)).Level > other.Single(k => k.X0 != other.Min(m => m.X0)).Level);
    }

    [Fact]
    public void ACasementWindowHasAsManyLeavesAsAskedForAllTheSameWidthAndSetBack()
    {
        for (int n = 1; n <= 4; n++)
        {
            var s = Of(WindowType.Casement, w => w with { Width = 32, Casements = n });

            var leaves = Window.Sashes(s);

            Assert.Equal(n, leaves.Count);
            Assert.All(leaves, k => Assert.Equal(s.Depth - s.SetBack, k.Level, 4));
            Assert.All(leaves, k => Assert.Equal(leaves[0].X1 - leaves[0].X0, k.X1 - k.X0, 3));
        }
    }

    [Fact]
    public void TheSashesStandAtTheirLevelsAndTheFrameAtItsDepth()
    {
        var s = Of(WindowType.SingleHung, w => w with { Sill = false });
        float front = s.Depth - s.SetBack, back = front - s.Step;

        var heights = Made(s).Parts.Single(p => p.Role == "frame").Mesh.Positions.Select(p => MathF.Round(p.Z, 3)).ToHashSet();

        Assert.Contains(MathF.Round(s.Depth, 3), heights);
        Assert.Contains(MathF.Round(front, 3), heights);
        Assert.Contains(MathF.Round(back, 3), heights);
    }

    [Fact]
    public void TheMuntinsDivideEverySashIntoTheLightsAskedFor()
    {
        var s = Of(WindowType.SingleHung, w => w with { LightsAcross = 3, LightsUp = 2 });

        var made = Made(s);

        // Two sashes of six lights, and the glass a pane in each of the twelve: a box of twelve
        // triangles apiece, so a muntin left out or one too many would change the count.
        var lights = Window.Sashes(s).SelectMany(k => Window.Lights(s, k)).ToList();
        Assert.Equal(12, lights.Count);
        Assert.Equal(12 * 12, made.Parts.Single(p => p.Role == "glass").Mesh.TriangleCount);

        // No light overlaps another, and each is the same size within its sash.
        for (int i = 0; i < lights.Count; i++)
            for (int j = i + 1; j < lights.Count; j++)
            {
                var (a, b) = (lights[i], lights[j]);
                Assert.False(a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1, $"lights {i} and {j} overlap");
            }
    }

    // --- Printing, facade up --------------------------------------------------------------

    [Fact]
    public void EveryPartLiesOnThePlateWithNothingBelowItOrBuiltOverAir()
    {
        foreach (var type in Enum.GetValues<WindowType>())
            foreach (var trim in Enum.GetValues<WindowTrim>())
                foreach (var shutters in Enum.GetValues<WindowShutters>())
                    foreach (var style in Enum.GetValues<ShutterKind>())
                    {
                        var s = Of(type, w => w with { Trim = trim, Shutters = shutters, Style = style });
                        var made = Made(s);

                        foreach (var part in made.Parts)
                        {
                            var b = part.Mesh.ComputeBounds();
                            Assert.True(MathF.Abs(b.Min.Z) < 1e-4f, $"{type} {trim} {shutters} {style}: {part.Name} starts at {b.Min.Z}");
                            Assert.True(part.Mesh.CheckHealth().IsWatertight, $"{type} {trim} {shutters} {style}: {part.Name}");
                        }
                    }
    }

    [Fact]
    public void LaidFlatTheTrimAndShuttersLieBesideTheWindowAndDoNotOverlapEachOther()
    {
        var s = Of(WindowType.SingleHung, w => w with { Trim = WindowTrim.Lintel, Shutters = WindowShutters.Pair });
        var made = Made(s);

        var window = made.Parts.Single(p => p.Role == "frame").Mesh.ComputeBounds();
        var beside = made.Parts.Where(p => p.Role is "trim" or "shutter-left" or "shutter-right").ToList();

        Assert.Equal(3, beside.Count);
        var boxes = beside.Select(p => p.Mesh.ComputeBounds()).OrderBy(b => b.Min.X).ToList();
        Assert.True(boxes[0].Min.X > window.Max.X, "clear of the window's own sill");
        for (int i = 1; i < boxes.Count; i++) Assert.True(boxes[i].Min.X > boxes[i - 1].Max.X, "clear of each other");
    }

    [Fact]
    public void StandingTheTrimAndShuttersAreOnTheWallInFrontOfTheFrameAndOutsideIt()
    {
        var s = Of(WindowType.Casement, w => w with { Flat = false, Trim = WindowTrim.Plain, Shutters = WindowShutters.Pair });
        var made = Made(s);

        var frame = made.Parts.Single(p => p.Role == "frame").Mesh.ComputeBounds();
        var trim = made.Parts.Single(p => p.Role == "trim").Mesh.ComputeBounds();
        var left = made.Parts.Single(p => p.Role == "shutter-left").Mesh.ComputeBounds();
        var right = made.Parts.Single(p => p.Role == "shutter-right").Mesh.ComputeBounds();

        // The facade faces -Y: the wall's face is the frame's, and they stand out of it.
        Assert.True(trim.Max.Y <= -s.Depth + 1e-3f && trim.Min.Y < -s.Depth);
        Assert.True(left.Max.Y <= -s.Depth + 1e-3f && right.Max.Y <= -s.Depth + 1e-3f);
        Assert.True(left.Max.X < frame.Min.X && right.Min.X > frame.Max.X, "the shutters are beside the frame, past its trim");
        Assert.True(left.Max.X < trim.Min.X && right.Min.X > trim.Max.X);
    }

    [Fact]
    public void ShuttersOnOneSideMakeOnePartAndTheRoleSaysWhichSide()
    {
        var s = Of(WindowType.Fixed, w => w with { Shutters = WindowShutters.Right });

        var roles = Made(s).Parts.Select(p => p.Role).ToList();

        Assert.Contains("shutter-right", roles);
        Assert.DoesNotContain("shutter-left", roles);
        Assert.DoesNotContain("trim", roles);
    }

    [Fact]
    public void ShutterWidthOfNoughtIsHalfTheWindowAndAnythingElseIsThatWide()
    {
        var half = Made(Of(WindowType.Fixed, w => w with { Width = 20, Shutters = WindowShutters.Left }))
            .Parts.Single(p => p.Role == "shutter-left").Mesh.ComputeBounds();
        Assert.Equal(10f, half.Size.X, 2);

        var set = Made(Of(WindowType.Fixed, w => w with { Width = 20, Shutters = WindowShutters.Left, ShutterWidth = 4 }))
            .Parts.Single(p => p.Role == "shutter-left").Mesh.ComputeBounds();
        Assert.Equal(4f, set.Size.X, 2);
    }

    // --- Refusals -------------------------------------------------------------------------

    [Fact]
    public void TooManyCasementsForTheWidthIsRefusedWithAReason()
    {
        var s = Of(WindowType.Casement, w => w with { Width = 8, Casements = 4 });

        Assert.Contains(Generator.Problems(s, Printer.Default), p => p.Contains("casements"));
    }

    [Fact]
    public void SashesTooShallowForTheirGlassAreRefused()
    {
        var s = Of(WindowType.SingleHung, w => w with { Depth = 1.0f, SetBack = 0.4f, Step = 0.4f, GlassThickness = 0.4f });

        Assert.Contains(Generator.Problems(s, Printer.Default), p => p.Contains("shallow"));
    }

    [Fact]
    public void TooManySlatsForTheHeightIsRefused()
    {
        var s = Of(WindowType.Fixed, w => w with { Height = 8, Shutters = WindowShutters.Pair, Style = ShutterKind.Louvred, Slats = 40 });

        Assert.Contains(Generator.Problems(s, Printer.Default), p => p.Contains("slats"));
    }
}
