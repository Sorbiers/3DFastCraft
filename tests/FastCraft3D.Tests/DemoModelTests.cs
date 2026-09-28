using FastCraft3D.Generators;
using FastCraft3D.Generators.Mechanisms;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using Xunit;
using Xunit.Abstractions;

namespace FastCraft3D.Tests;

/// <summary>
/// Every mechanism made into a model to print and turn by hand: each part a closed solid that
/// stands on the plate as it prints, no two of them sharing any room as they go together - a shaft
/// in its bore, a crank over the gears, a bearing beside a wheel - and the set still turning clear
/// with its base, its posts and its rails in the way.
/// </summary>
[Collection("Sweep")]
public class DemoModelTests(ITestOutputHelper output)
{
    private void Sound(Generator g, object settings)
    {
        var made = g.Make(settings, Printer.Default);
        Assert.True(made.Refusal is null, made.Refusal);
        Assert.Contains(made.Parts, p => p.Role is "base" or "stand");

        var placed = made.Parts.Select(p => (p.Name, Mesh: p.Assembled is { } a ? MeshTransform.Transformed(p.Mesh, a) : p.Mesh)).ToList();
        foreach (var p in made.Parts)
        {
            var health = p.Mesh.CheckHealth();
            Assert.True(health.IsWatertight && !health.IsInsideOut, $"{p.Name}: {health.Describe()}");
            Assert.InRange(p.Mesh.ComputeBounds().Min.Z, -0.01f, 0.01f);
        }

        var clashes = new List<string>();
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++)
            {
                var a = placed[i].Mesh.ComputeBounds();
                var b = placed[j].Mesh.ComputeBounds();
                if (a.Max.X < b.Min.X || b.Max.X < a.Min.X || a.Max.Y < b.Min.Y || b.Max.Y < a.Min.Y || a.Max.Z < b.Min.Z || b.Max.Z < a.Min.Z) continue;
                if (ManifoldCsg.Intersect(placed[i].Mesh, placed[j].Mesh) is { TriangleCount: > 0 } common && Math.Abs(common.ComputeSignedVolume()) > 0.01)
                    clashes.Add($"{placed[i].Name} and {placed[j].Name}: {Math.Abs(common.ComputeSignedVolume()):0.###} mm3");
            }

        foreach (var c in clashes) output.WriteLine(c);
        Assert.Empty(clashes);

        // Shafts print standing on an end.
        foreach (var shaft in made.Parts.Where(p => p.Role == "shaft"))
        {
            var size = shaft.Mesh.ComputeBounds().Size;
            Assert.True(size.Z > size.X, $"{shaft.Name} lies {size.X:0.#} mm long and {size.Z:0.#} tall: it should stand");
        }

        if (made.Motion is not null)
        {
            var film = Films.Shoot(made);
            Assert.True(film.Clear, film.Jam);
        }
    }

    private static readonly Gear Toothed = new();

    public static IEnumerable<object[]> Gears() =>
    [
        ["pair", Toothed.Default with { Teeth = 15, HasPartner = true, PartnerTeeth = 30, Demo = true }],
        ["helical pair", Toothed.Default with { Form = ToothForm.Helical, HasPartner = true, PartnerTeeth = 40, Demo = true }],
        ["single", Toothed.Default with { Demo = true }],
        ["ring", Toothed.Default with { Kind = GearKind.Ring, Teeth = 48, HasPartner = true, PartnerTeeth = 16, Demo = true }],
        ["rack", Toothed.Default with { Kind = GearKind.Rack, Teeth = 30, HasPartner = true, PartnerTeeth = 16, Demo = true }],
        ["bevel", Toothed.Default with { Kind = GearKind.Bevel, HasPartner = true, PartnerTeeth = 20, Demo = true }],
        ["worm", Toothed.Default with { Kind = GearKind.Worm, HasPartner = true, PartnerTeeth = 30, Demo = true }],
        ["ratchet", Toothed.Default with { Kind = GearKind.Ratchet, WithPawl = true, Demo = true }],
        ["frame", Toothed.Default with { Teeth = 24, Partial = true, KeptTeeth = 6, Frame = true, Demo = true }]
    ];

    [Theory]
    [MemberData(nameof(Gears))]
    public void EveryKindOfGearMakesASoundModel(string kind, Gear.Settings settings)
    {
        output.WriteLine(kind);
        Sound(Toothed, settings);
    }

    public static IEnumerable<object[]> Others() =>
    [
        ["gear train", new GearTrain(), new GearTrain().Default with { Demo = true }],
        ["winch train", new GearTrain(), new GearTrain().Default with { Ratio = 50, Stages = 3, Module = 1, Demo = true }],
        ["planetary", new Planetary(), new Planetary().Default with { Demo = true }],
        ["geneva", new Geneva(), new Geneva().Default with { Demo = true }],
        ["six-slot geneva", new Geneva(), new Geneva().Default with { Slots = 6, Distance = 60, Demo = true }],
        ["cam", new Cam(), new Cam().Default with { Demo = true }],
        ["flat cam", new Cam(), new Cam().Default with { Follower = FollowerKind.Flat, Demo = true }],
        ["four-bar", new FourBar(), new FourBar().Default with { Demo = true }],
        ["crank and slider", new CrankSlider(), new CrankSlider().Default with { Demo = true }],
        ["bearing", new Bearing(), new Bearing().Default with { Demo = true }],
        ["motion work", new MotionWork(), new MotionWork().Default with { Demo = true }]
    ];

    [Theory]
    [MemberData(nameof(Others))]
    public void EveryOtherMechanismMakesASoundModel(string kind, Generator generator, object settings)
    {
        output.WriteLine(kind);
        Sound(generator, settings);
    }

    [Fact]
    public void OrganizeOffPutsTheSetDownAsItGoesTogether()
    {
        var made = Toothed.Make(Toothed.Default with { HasPartner = true, Organise = false }, Printer.Default);
        Assert.False(made.LaidOut);
        Assert.True(Toothed.Make(Toothed.Default with { HasPartner = true }, Printer.Default).LaidOut);
    }
}
