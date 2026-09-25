using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Boxes;
using FastCraft3D.Generators.Mechanisms;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Motion;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Sets that move, turned a whole turn by the motion check as the panel's Turn it plays them:
/// each part goes as far as its teeth, its slots or its profile say, and none jams.
/// </summary>
[Collection("Sweep")]
public class MotionFilmTests
{
    private static readonly Gear Toothed = new();

    private static (MotionFilm Film, Generated Made) Turned(Generator g, object settings)
    {
        var made = g.Make(settings, Printer.Default);
        Assert.Null(made.Refusal);
        Assert.NotNull(made.Motion);

        var film = Films.Shoot(made);
        Assert.True(film.Clear, film.Jam);
        return (film, made);
    }

    private static double Degrees(MotionFilm film, int part) => film.Last(part) * 180 / Math.PI;

    [Fact]
    public void AGearTurnsItsMateByTheRatioOfTheirTeeth()
    {
        var (film, _) = Turned(Toothed, Toothed.Default with { HasPartner = true, PartnerTeeth = 40 });

        Assert.Equal(360, Degrees(film, 0), 0);
        Assert.InRange(Degrees(film, 1), -181, -179);
    }

    [Fact]
    public void APinionCarriesItsRackItsOwnPitchCircleAlong()
    {
        var (film, _) = Turned(Toothed, Toothed.Default with { Kind = GearKind.Rack, Teeth = 30, HasPartner = true, PartnerTeeth = 16 });

        double travel = film.Frames.Max(f => f[0]) - film.Frames.Min(f => f[0]);
        Assert.InRange(travel, MathF.PI * 1.5f * 16 - 1, MathF.PI * 1.5f * 16 + 1);
    }

    [Fact]
    public void AGearTrainTurnsItsOutputOnceForEveryRatioTurnsOfItsInput()
    {
        var train = new GearTrain();
        var settings = train.Default;
        var (film, made) = Turned(train, settings);

        double ratio = GearTrain.Teeth(settings).Aggregate(1.0, (r, b) => r * b / settings.Pinion);
        Assert.InRange(Math.Abs(Degrees(film, made.Parts.Count - 1)), 360 / ratio - 1, 360 / ratio + 1);
    }

    [Fact]
    public void WithItsCarrierHeldAPlanetarySetTurnsItsRingBackwardsBySunOverRing()
    {
        var set = new Planetary();
        var settings = set.Default;
        var (film, _) = Turned(set, settings);

        int ring = 1 + settings.Planets;
        Assert.InRange(Degrees(film, ring), -360.0 * settings.Sun / Planetary.Ring(settings) - 1, -360.0 * settings.Sun / Planetary.Ring(settings) + 1);
    }

    [Fact]
    public void AGenevaWheelStepsOneSlotForATurnOfItsDriver()
    {
        var (film, _) = Turned(new Geneva(), new Geneva().Default with { Slots = 5 });
        Assert.InRange(Math.Abs(Degrees(film, 1)), 71, 73);
    }

    [Fact]
    public void AReciprocatingFrameIsCarriedToAndFro()
    {
        var (film, _) = Turned(Toothed, Toothed.Default with { Teeth = 24, Partial = true, KeptTeeth = 6, Frame = true });

        double travel = film.Frames.Max(f => f[1]) - film.Frames.Min(f => f[1]);
        Assert.True(travel > 20, $"the frame moved only {travel:0.#} mm");
    }

    [Fact]
    public void ACamLiftsItsFollowerByTheRiseAndItsSpringBringsItBack()
    {
        var cam = new Cam();
        var (film, _) = Turned(cam, cam.Default with { Rise = 10 });

        Assert.InRange(film.Frames.Max(f => f[1]), 9.5, 10.3);
        Assert.InRange(film.Last(1), -0.1, 0.3);
    }

    [Fact]
    public void ASolidMovedIntoPlaceStillCutsIntoWholeOutlines()
    {
        // The Geneva's wheel, moved beside its driver, once came back as twenty-three fragments,
        // each closing straight across the solid; the drive "jammed" the moment it turned.
        var ring = MeshTransform.Transformed(Shapes.Tube(25, 5, 0, 5), Matrix4x4.CreateTranslation(40.123f, -3.3f, 3.3f));

        Assert.Equal(2, Sections.At(ring, 5.8f).Count);
    }

    [Fact]
    public void ANearlyStraightOutlineIsThinnedToItsCorners()
    {
        var loop = Shapes.RoundedRect(40, 20, 0.001f, steps: 8).ToArray();
        Assert.True(Sections.Thinned(loop, 0.02f).Length <= 8);
    }

    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    private static Button Turn(DependencyObject root) =>
        Descendants(root).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "GeneratorTurn");

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    [Fact]
    public void TurnItIsOfferedWhenInsertingASetThatMovesAndNowhereElse() => RunSta(() =>
    {
        var cam = new Cam();

        var inserting = new GeneratorView(cam, null, GeneratorContext.Default, (_, _) => { }, live: false) { OffersTurning = true };
        inserting.SayMotion(null, false);
        Assert.Equal(Visibility.Visible, Turn(inserting).Visibility);

        Generated? pressed = null;
        inserting.TurnPressed += made => pressed = made;
        Turn(inserting).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.NotNull(pressed?.Motion);

        var editing = new GeneratorView(cam, null, GeneratorContext.Default, (_, _) => { }, "Apply", live: false);
        editing.SayMotion(null, false);
        Assert.NotEqual(Visibility.Visible, Turn(editing).Visibility);

        var box = new GeneratorView(new OpenBox(), null, GeneratorContext.Default, (_, _) => { }, live: false) { OffersTurning = true };
        box.SayMotion(null, false);
        Assert.NotEqual(Visibility.Visible, Turn(box).Visibility);
    });

    [Fact]
    public void AJamFoundByTurningIsSaidInRed() => RunSta(() =>
    {
        var view = new GeneratorView(new Cam(), null, GeneratorContext.Default, (_, _) => { }, live: false);

        view.SayMotion("It jams 40 degrees into a turn: Cam runs into Follower.", true);
        Assert.True(view.ShowsProblem);
        Assert.Contains("It jams 40 degrees", view.SummaryText);
    });
}
