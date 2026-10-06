using System.Numerics;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// How narrow a texture's Groove may be. A nozzle width for the patterns made of pads, since a gap
/// narrower than that closes up as it prints; nothing for siding, rubble, coursed stone and bark,
/// whose pieces can meet - and have to build closed when they do.
/// </summary>
public class GrooveTests
{
    private static readonly TextureKind[] MayHaveNone =
        [TextureKind.Siding, TextureKind.Rubble, TextureKind.CoursedStone, TextureKind.Bark];

    public static TheoryData<TextureKind> EveryKind
    {
        get
        {
            var data = new TheoryData<TextureKind>();
            foreach (var kind in Enum.GetValues<TextureKind>().Where(k => k != TextureKind.None)) data.Add(kind);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void OnlySidingRubbleCoursedStoneAndBarkMayHaveNoGroove(TextureKind kind)
    {
        float least = MayHaveNone.Contains(kind) ? 0f : TextureOptions.LeastLineMm;

        Assert.Equal(least, TextureOptions.LeastLineOf(kind));

        var o = (TextureOptions.Default with { Kind = kind, PitchMm = 8f, LineMm = 0f }).Sane();
        Assert.Equal(least, o.LineMm);
        Assert.Equal(8f, o.PitchMm);
    }

    [Fact]
    public void ANegativeGrooveIsNoGrooveAndTheLeastIsKeptAsTyped()
    {
        var siding = (TextureOptions.Default with { Kind = TextureKind.Siding, LineMm = -2f }).Sane();
        Assert.Equal(0f, siding.LineMm);

        var rubble = (TextureOptions.Default with { Kind = TextureKind.Rubble, LineMm = 0.1f }).Sane();
        Assert.Equal(0.1f, rubble.LineMm);

        var knurl = (TextureOptions.Default with { Kind = TextureKind.Knurl, LineMm = 0.1f }).Sane();
        Assert.Equal(TextureOptions.LeastLineMm, knurl.LineMm);
    }

    private static void WithModel(Action<MainViewModel> body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(new MainViewModel()); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void ChoosingSidingStartsItWithNoGrooveAndLeavingItRaisesTheGrooveToTheLeast() => WithModel(model =>
    {
        model.EmbossTexture = TextureKind.Knurl;
        model.EmbossTextureLine = 0.6f;

        model.EmbossTexture = TextureKind.Siding;
        Assert.Equal(0f, model.EmbossTextureLine);

        model.EmbossTexture = TextureKind.Hex;
        Assert.Equal(TextureOptions.LeastLineMm, model.EmbossTextureLine);

        // The others keep what was typed.
        model.EmbossTextureLine = 1.2f;
        model.EmbossTexture = TextureKind.Rubble;
        Assert.Equal(1.2f, model.EmbossTextureLine);
    });

    [Fact]
    public void ATypedGrooveBelowTheLeastIsRaisedInTheBoxAndNoughtIsKeptWhereItIsAllowed() => WithModel(model =>
    {
        var told = new List<string>();
        model.PropertyChanged += (_, e) => told.Add(e.PropertyName ?? "");

        model.EmbossTexture = TextureKind.Dots;
        model.EmbossTextureLine = 0.1f;

        Assert.Equal(TextureOptions.LeastLineMm, model.EmbossTextureLine);

        // Typed again, and the same after the correction: the box is still told, or it would show 0.1.
        told.Clear();
        model.EmbossTextureLine = 0.2f;
        Assert.Contains(nameof(MainViewModel.EmbossTextureLine), told);

        model.EmbossTexture = TextureKind.CoursedStone;
        model.EmbossTextureLine = 0f;
        Assert.Equal(0f, model.EmbossTextureLine);
    });

    private static PlanarSurface Face(float side)
    {
        var box = MeshTransform.Transformed(
            Primitives.Box(side, side, side), Matrix4x4.CreateTranslation(0, 0, side / 2f));

        return new PlanarSurface(FacePatch.Find(box, new Vector3(0, -side / 2f, side / 2f), -Vector3.UnitY)!);
    }

    /// <summary>
    /// Stones and bark plates that meet edge to edge are still a closed field - the joints are the
    /// shoulders of the stones - and still read as stones: some of the face is low.
    /// </summary>
    [Theory]
    [InlineData(TextureKind.Rubble)]
    [InlineData(TextureKind.CoursedStone)]
    [InlineData(TextureKind.Bark)]
    public void AShapedFieldWithNoGrooveIsClosedAndStillHasItsJoints(TextureKind kind)
    {
        var failures = new List<string>();

        foreach (float pitch in new[] { 3f, 8f, 15f })
            foreach (float depth in new[] { 0.4f, 1f, 2.5f })
                foreach (float aspect in new[] { 0f, 1f, 3f })
                {
                    var o = new TextureOptions(kind, pitch, 0f, 45f, false, aspect);
                    var relief = SurfaceTexture.ProfileOf(o, depth)!;
                    var cost = ReliefField.Cost(relief, 40f, 30f);
                    if (!cost.CanBuild) { failures.Add($"{kind} {pitch}/{depth}/{aspect}: refused - {cost.Refusal}"); continue; }

                    var built = ReliefField.Build(Face(44f), relief, 40f, 30f);
                    if (!built.CheckHealth().IsWatertight)
                        failures.Add($"{kind} pitch {pitch} depth {depth} aspect {aspect}: {built.CheckHealth().Describe()}");
                }

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(2.5f, 0.6f)]
    [InlineData(5f, 1f)]
    [InlineData(12f, 2f)]
    public void SidingWithNoGrooveIsBuiltClosedAndJoinsAFace(float pitch, float thick)
    {
        var options = new TextureOptions(TextureKind.Siding, pitch, 0f);
        var courses = SurfaceTexture.CoursesOf(options, thick)!.Value;

        // Never nought between slabs: they are closed solids of their own and must not share a wall.
        Assert.True(courses.JointMm >= TileSolid.LeastJointMm);

        var slabs = TileSolid.Build(Face(64f), courses, 60f, 60f);
        Assert.True(slabs.TriangleCount > 0);
        Assert.True(slabs.CheckHealth().IsWatertight, slabs.CheckHealth().Describe());

        var box = MeshTransform.Transformed(Primitives.Box(64, 64, 64), Matrix4x4.CreateTranslation(0, 0, 32));
        var joined = ManifoldCsg.Union(box, slabs);

        Assert.NotNull(joined);
        Assert.True(joined!.CheckHealth().IsWatertight, joined.CheckHealth().Describe());
    }
}
