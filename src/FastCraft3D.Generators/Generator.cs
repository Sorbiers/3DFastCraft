using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators;

/// <summary>
/// What the printer can do, for the settings that depend on it: a wall or a clearance setting
/// starts from these rather than from a number that was right for somebody else's machine.
/// </summary>
/// <param name="Nozzle">Line width, near enough, in millimetres.</param>
/// <param name="Layer">Layer height.</param>
/// <param name="XyClearance">The gap, per side, two printed surfaces need to slide past each other.</param>
public sealed record Printer(float Nozzle = 0.4f, float Layer = 0.2f, float XyClearance = 0.2f)
{
    public const float LeastNozzle = 0.1f, MostNozzle = 2f;
    public const float LeastLayer = 0.02f, MostLayer = 1.2f;
    public const float LeastClearance = 0f, MostClearance = 1f;

    public static Printer Default { get; } = new();

    /// <summary>A printer with each number brought inside what any real one would be set to.</summary>
    public static Printer Sane(float nozzle, float layer, float clearance) => new(
        float.IsFinite(nozzle) ? Math.Clamp(nozzle, LeastNozzle, MostNozzle) : Default.Nozzle,
        float.IsFinite(layer) ? Math.Clamp(layer, LeastLayer, MostLayer) : Default.Layer,
        float.IsFinite(clearance) ? Math.Clamp(clearance, LeastClearance, MostClearance) : Default.XyClearance);
}

/// <summary>One part a generator made.</summary>
/// <param name="Name">What the object is called on the plate.</param>
/// <param name="Mesh">
/// The way up it prints, where it goes as the set prints: every part of a set is in the one
/// frame, so a gear pair comes out in mesh and a lid beside its box, and they are put on the plate
/// as they stand. The set stands on Z = 0; a part may stand on another printed with it - a
/// window's frame on its glass - but nothing goes below the plate.
/// </param>
/// <param name="Assembled">
/// Where the part goes when the set is looked at together rather than printed, or null when that
/// is the same place: a lid is shown on its box and printed beside it.
/// </param>
/// <param name="Anchors">Bores, shafts and the like, in the mesh's own coordinates.</param>
/// <param name="Role">Which part of a set it is, for rebuilding the set later: "box", "lid".</param>
/// <param name="Pivot">
/// The point the part turns about and grows about when it is made again with other numbers, in
/// the mesh's coordinates: a box's is the middle of its bottom, so a wider box stays standing where
/// it stood; a gear's is its shaft at the plate.
/// </param>
/// <param name="Cutter">
/// A solid to take away from another part rather than to print: a threaded hole's cutter. Its
/// origin is its mouth, and it hangs below Z = 0, so a longer one goes deeper into whatever it is
/// cut into rather than standing further out of it. With a part selected, the panel offers to cut
/// it straight in.
/// </param>
public sealed record GeneratedPart(
    string Name,
    Mesh Mesh,
    Matrix4x4? Assembled = null,
    IReadOnlyList<Anchor>? Anchors = null,
    string? Role = null,
    bool Cutter = false,
    Vector3 Pivot = default)
{
    /// <summary>
    /// Which filament prints it, where that is part of the design - a window's glass in a clear
    /// one - or nought to leave it to the app, as every other part.
    /// </summary>
    public int Filament { get; init; }

    /// <summary>The colour it is shown in, where that says what it is - glass pale blue - or null for the app's own.</summary>
    public Vector3? Colour { get; init; }
}

/// <summary>Named settings shipped with a generator, for the sizes people make most: "AA battery", "M3".</summary>
public sealed record Preset(string Name, object Settings);

/// <param name="Parts">What was made - none when it was refused.</param>
/// <param name="Notes">Things worth knowing about what was made.</param>
/// <param name="Refusal">Why nothing was made.</param>
public sealed record Generated(IReadOnlyList<GeneratedPart> Parts, IReadOnlyList<string> Notes, string? Refusal = null)
{
    public static Generated Refused(string why) => new([], [], why);

    /// <summary>How the set moves, for a set that does: what the panel's Turn it plays.</summary>
    public Mechanism? Motion { get; init; }

    public bool IsRefused => Refusal is not null;
}

/// <summary>
/// Something that makes parts from a handful of numbers: a box to a size, a hinge to a
/// clearance, a gear pair to a ratio.
///
/// A generator is one class and nothing else. Its settings record says what the numbers are,
/// and the panel, the preview, inserting and the test sweep are all built from that. Before this,
/// each tool took a XAML panel of its own and another stretch of the main view model, and the
/// gear alone ran to seven hundred lines of plumbing round its geometry.
///
/// The rules:
/// - <b>Build is pure.</b> Settings and printer in, parts out, the same every time. It runs off
///   the UI thread and can be cancelled.
/// - <b>Refuse, don't break.</b> Settings that cannot be made give a refusal saying why. Throwing
///   is a bug, and so is a part that is not watertight.
/// - <b>Settings only grow.</b> A new setting gets a default so an old one still reads. The Id
///   never changes; the Version goes up when the same settings would make a different part.
/// </summary>
public abstract class Generator
{
    /// <summary>What the part is known by for good: "box.open". Never changed once shipped.</summary>
    public abstract string Id { get; }

    /// <summary>Goes up when the same settings would make a different part.</summary>
    public abstract int Version { get; }

    /// <summary>The heading it is listed under: "Boxes", "Hinges".</summary>
    public abstract string Category { get; }

    public abstract string Title { get; }

    /// <summary>A sentence at the top of its panel.</summary>
    public virtual string Summary => "";

    /// <summary>Still being proved out. A generator is until somebody has printed what it makes.</summary>
    public virtual bool IsBeta => true;

    public abstract SettingsShape Shape { get; }

    /// <summary>Settings worth starting from, each with a name. The sweep holds every one to the same rules as the defaults.</summary>
    public virtual IReadOnlyList<Preset> Presets => [];

    public IReadOnlyList<GeneratorParameter> Parameters => Shape.Parameters;

    public object Defaults() => Shape.Defaults();

    /// <summary>
    /// The defaults for this printer: each clearance its gap, and each wall a whole number of
    /// nozzle widths no thinner than the generator asks for.
    /// </summary>
    public object Defaults(Printer printer) =>
        Shape.From(Parameters.Select(p => p.DefaultFor(printer)).ToArray());

    /// <summary>Why these settings cannot be made; empty when they can.</summary>
    public abstract IReadOnlyList<string> Problems(object settings, Printer printer);

    /// <summary>
    /// The settings once one of them has been changed by hand, with anything that follows from it
    /// filled in: choosing M8 brings its pitch and its nut's size with it.
    /// </summary>
    public abstract object Adjusted(object before, object after, string changed);

    /// <summary>
    /// What the settings come to at the scale the model is drawn to: a stair's riser in real
    /// millimetres. Said under the size; depends on nothing but the settings and the scale.
    /// </summary>
    public abstract IReadOnlyList<string> Readouts(object settings, float modelScale);

    /// <summary>
    /// Whether a setting means anything with the others as they are, so its row is shown. Its own
    /// ShowWhen first, then the generator's rule, for what one setting cannot decide alone - a
    /// gear's frame rows belong only to a cut-away gear that has asked for one.
    /// </summary>
    public bool Shows(object settings, GeneratorParameter parameter) =>
        Shape.Shows(settings, parameter) && Matters(settings, parameter.Name);

    private protected abstract bool Matters(object settings, string parameter);

    /// <summary>
    /// The parts for these settings, or a refusal. Numbers outside their range are brought in
    /// first, so a caller never has to.
    /// </summary>
    public Generated Make(object settings, Printer printer, CancellationToken token = default)
    {
        var sane = Shape.Sane(settings);

        var problems = Problems(sane, printer);
        if (problems.Count > 0) return Generated.Refused(problems[0]);

        try
        {
            return BuildFrom(sane, printer, token);
        }
        catch (Refusal refusal)
        {
            return Generated.Refused(refusal.Message);
        }
    }

    private protected abstract Generated BuildFrom(object settings, Printer printer, CancellationToken token);
}

/// <typeparam name="TSettings">
/// A record class whose primary constructor lists the settings, each with a default and one
/// field attribute. Not a record struct: <c>new()</c> on one skips the defaults.
/// </typeparam>
public abstract class Generator<TSettings> : Generator where TSettings : class
{
    // Read once per settings type, lazily, so a mistake in the record is an error with its
    // message rather than a TypeInitializationException.
    private static readonly Lazy<SettingsShape> shape = new(() => SettingsShape.Of(typeof(TSettings)));

    public override SettingsShape Shape => shape.Value;

    public TSettings Default => (TSettings)Shape.Defaults();

    /// <summary>The presets, typed. See <see cref="Generator.Presets"/>.</summary>
    protected virtual IEnumerable<(string Name, TSettings Settings)> Shipped => [];

    public override IReadOnlyList<Preset> Presets => Shipped.Select(p => new Preset(p.Name, p.Settings)).ToList();

    /// <summary>Why these settings cannot be made, one reason per line. The numbers are already in range.</summary>
    protected virtual IEnumerable<string> Check(TSettings settings, Printer printer) => [];

    /// <summary>The parts. Called only with settings in range that <see cref="Check"/> passed.</summary>
    protected abstract Generated Build(TSettings settings, Printer printer, CancellationToken token);

    /// <summary>See <see cref="Generator.Adjusted"/>. Returns <paramref name="after"/> unless something follows.</summary>
    protected virtual TSettings Adjust(TSettings before, TSettings after, string changed) => after;

    /// <summary>See <see cref="Generator.Readouts"/>.</summary>
    protected virtual IEnumerable<string> Describe(TSettings settings, float modelScale) => [];

    /// <summary>See <see cref="Generator.Shows"/>. True unless the generator knows better.</summary>
    protected virtual bool Shows(TSettings settings, string parameter) => true;

    private protected override bool Matters(object settings, string parameter) => Shows((TSettings)settings, parameter);

    public override object Adjusted(object before, object after, string changed) =>
        Adjust((TSettings)before, (TSettings)after, changed);

    public override IReadOnlyList<string> Readouts(object settings, float modelScale) =>
        Describe((TSettings)Shape.Sane(settings), modelScale).ToList();

    public Generated Make(TSettings settings, Printer printer, CancellationToken token = default) =>
        base.Make(settings, printer, token);

    public override IReadOnlyList<string> Problems(object settings, Printer printer) =>
        Check((TSettings)settings, printer).ToList();

    private protected override Generated BuildFrom(object settings, Printer printer, CancellationToken token) =>
        Build((TSettings)settings, printer, token);
}

/// <summary>Every generator in this library, found by looking rather than listed by hand.</summary>
public static class GeneratorRegistry
{
    private static readonly Lazy<IReadOnlyList<Generator>> all = new(() =>
        typeof(Generator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Generator).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Generator)Activator.CreateInstance(t)!)
            .OrderBy(g => g.Category, StringComparer.CurrentCulture)
            .ThenBy(g => g.Title, StringComparer.CurrentCulture)
            .ToList());

    public static IReadOnlyList<Generator> All => all.Value;

    public static Generator? Find(string id) => All.FirstOrDefault(g => g.Id == id);
}
