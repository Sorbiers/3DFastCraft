using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Fasteners;

/// <summary>The ISO metric coarse sizes, and Custom for anything else. The names are <see cref="Threads.Metric"/>'s.</summary>
public enum ThreadSize { M3, M4, M5, M6, M8, M10, M12, M16, M20, Custom }

/// <summary>
/// A threaded rod, a bolt, a nut, or the cutter for a threaded hole, built by
/// <see cref="Threads"/>.
///
/// A metric size brings its pitch and its nut's width and height with it, so the usual case is two
/// choices; Custom opens the diameter and pitch for anything else. The cutter is made hanging from
/// its mouth, so with a part selected it can be cut straight into that part's top.
/// </summary>
public sealed class ScrewThread : Generator<ScrewThread.Settings>
{
    public override string Id => "fastener.thread";
    public override int Version => 1;
    public override string Category => "Fasteners";
    public override string Title => "Thread";
    public override string Summary => "A threaded rod, a bolt, a nut, or a cutter to Subtract for a threaded hole.";
    public override bool IsBeta => false;

    public sealed record Settings(
        [Choice("Part")] ThreadKind Kind = ThreadKind.Rod,
        [Choice("Size", Hint = "ISO metric coarse. Custom opens the diameter and pitch.")] ThreadSize Size = ThreadSize.M8,
        [Length("Diameter", ThreadOptions.MinimumDiameter, ThreadOptions.MaximumDiameter, Hint = "The nominal major diameter: 8 for M8"),
         ShowWhen(nameof(Size), ThreadSize.Custom)] float Diameter = 8f,
        [Length("Pitch", ThreadOptions.MinimumPitch, 10, Hint = "From one crest to the next"),
         ShowWhen(nameof(Size), ThreadSize.Custom)] float Pitch = 1.25f,
        [Length("Length", ThreadOptions.MinimumLength, ThreadOptions.MaximumLength, Hint = "Along the thread; under the head, for a bolt"),
         ShowWhen(nameof(Kind), ThreadKind.Rod, ThreadKind.Bolt, ThreadKind.HoleCutter)] float Length = 20f,
        [Length("Clearance", 0, ThreadOptions.MaximumClearance,
            Hint = "The gap on the diameter between a rod and a nut of the same size. Half comes off the rod and half goes on the nut, so either also fits a bought one.")]
        float Clearance = 0.2f,
        [Choice("Body", Hint = "The nut's body, or the bolt's head"), ShowWhen(nameof(Kind), ThreadKind.Nut, ThreadKind.Bolt)] NutBody Body = NutBody.Hexagon,
        [Length("Across flats", 1, 300, Hint = "The spanner size of a hexagon; the outside of a round one"),
         ShowWhen(nameof(Kind), ThreadKind.Nut, ThreadKind.Bolt)] float AcrossFlats = 13f,
        [Length("Nut height", 0.5, 300), ShowWhen(nameof(Kind), ThreadKind.Nut)] float NutHeight = 6.8f,
        [Length("Head height", 0.5, 300), ShowWhen(nameof(Kind), ThreadKind.Bolt)] float HeadHeight = 5.3f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
        Threads.Metric.SelectMany(m => new[]
        {
            ($"{m.Name} nut", Adjust(Default, Default with { Kind = ThreadKind.Nut, Size = SizeOf(m) }, nameof(Settings.Size))),
            ($"{m.Name} bolt", Adjust(Default, Default with { Kind = ThreadKind.Bolt, Size = SizeOf(m), Length = m.Diameter * 2.5f }, nameof(Settings.Size)))
        });

    protected override Settings Adjust(Settings before, Settings after, string changed)
    {
        if (changed != nameof(Settings.Size) || Metric(after.Size) is not { } m) return after;

        return after with
        {
            Diameter = m.Diameter,
            Pitch = m.Pitch,
            AcrossFlats = m.AcrossFlats,
            NutHeight = m.NutHeight,
            HeadHeight = m.HeadHeight
        };
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var asked = Options(s);
        var sane = asked.Sane();

        token.ThrowIfCancellationRequested();
        var mesh = Threads.Build(sane);

        // Built directly rather than cut, so it should always close; if some number finds a way it
        // does not, say so rather than hand back a part that will not slice.
        if (mesh.TriangleCount == 0 || !mesh.CheckHealth().IsWatertight)
            return Generated.Refused("These numbers do not make a closed solid. Try a slightly different length.");

        // Stood on the plate, or for a cutter hung from its mouth: either way its origin is on the
        // thread's axis, at the end a longer one grows away from.
        var bounds = mesh.ComputeBounds();
        bool cutter = sane.Kind == ThreadKind.HoleCutter;
        mesh = MeshTransform.Transformed(mesh, Matrix4x4.CreateTranslation(0, 0, cutter ? -bounds.Max.Z : -bounds.Min.Z));

        return new Generated([new GeneratedPart(sane.Name, mesh, Role: "thread", Cutter: cutter)], Notes(asked, sane));
    }

    private static List<string> Notes(ThreadOptions asked, ThreadOptions sane)
    {
        var notes = new List<string>
        {
            sane.Kind switch
            {
                ThreadKind.Rod => $"{sane.RodOutside:0.##} mm over the crests, {sane.RodCore:0.##} mm at the core.",
                ThreadKind.Bolt => $"{sane.RodOutside:0.##} mm over the crests, {sane.RodCore:0.##} mm at the core, under a "
                                   + $"{sane.AcrossFlats:0.##} mm head {sane.HeadHeight:0.##} mm tall. It stands on its head, as it prints best.",
                ThreadKind.Nut => $"{sane.NutBore:0.##} mm through the crests of the thread, "
                                  + $"{sane.AcrossFlats / 2f - sane.Diameter / 2f - sane.Clearance / 4f:0.##} mm of wall at the thinnest.",
                _ => $"Cuts a hole {sane.NutBore:0.##} mm through the crests. Stand its top a little past the surface of the "
                     + "part and Subtract it, or select the part first and Cut it straight in."
            }
        };

        if (MathF.Abs(asked.Diameter - sane.Diameter) > 1e-3f)
            notes.Add($"The diameter is held to {sane.Diameter:0.##} mm.");
        if (MathF.Abs(asked.Pitch - sane.Pitch) > 1e-3f)
            notes.Add(asked.Pitch > sane.Pitch
                ? $"The pitch is held to {sane.Pitch:0.##} mm, the coarsest a {sane.Diameter:0.##} mm thread takes."
                : $"The pitch is held to {sane.Pitch:0.##} mm.");
        if (sane.Kind is ThreadKind.Nut or ThreadKind.Bolt && asked.AcrossFlats < sane.AcrossFlats - 1e-3f)
            notes.Add($"Widened to {sane.AcrossFlats:0.##} mm to leave a {ThreadOptions.MinimumWall:0.#} mm wall round the thread.");

        if (!sane.Engages)
            notes.Add("With this much clearance a rod's crests pass inside a nut's, and it would slide straight through.");
        if (sane.Pitch < 1f)
            notes.Add("A pitch under 1 mm is finer than most filament printers draw cleanly. Print it slowly "
                      + "with fine layers, or on a resin printer; M6 and up print far more reliably.");

        return notes;
    }

    /// <summary>The thread these settings ask for, before it is held to what can be made.</summary>
    internal static ThreadOptions Options(Settings s)
    {
        var m = Metric(s.Size);
        return new ThreadOptions(
            s.Kind, m?.Diameter ?? s.Diameter, m?.Pitch ?? s.Pitch, s.Length, s.Clearance,
            s.Body, s.AcrossFlats, s.NutHeight) { HeadHeight = s.HeadHeight };
    }

    private static MetricSize? Metric(ThreadSize size) =>
        size == ThreadSize.Custom ? null : Threads.Metric.First(m => m.Name == size.ToString());

    private static ThreadSize SizeOf(MetricSize m) => Enum.Parse<ThreadSize>(m.Name);
}
