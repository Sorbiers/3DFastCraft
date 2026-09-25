using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Organisers;

/// <summary>
/// A hook for pegboard on the 25.4 mm (one inch) grid with quarter-inch holes: an L-shaped peg
/// that hooks behind the board, a straight one below it to stop it turning, and an arm out front
/// with its tip turned up. Printed lying on its side, so the arm's layers run along it.
/// </summary>
public sealed class PegboardHook : Generator<PegboardHook.Settings>
{
    public const float Pitch = 25.4f;
    /// <summary>
    /// A hair over 6 mm, in a 6.35 mm hole. At exactly 6 the flattening cut met a 6 mm thick
    /// hook's round peg on a tangent, and the solid came back with edges belonging to three faces.
    /// </summary>
    public const float Peg = 6.2f;

    public override string Id => "organiser.pegboard-hook";
    public override int Version => 1;
    public override string Category => "Organisers";
    public override string Title => "Pegboard hook";
    public override string Summary => "A hook for one-inch pegboard with quarter-inch holes.";

    public sealed record Settings(
        [Length("Reach", 10, 150, Hint = "How far the arm comes out from the board")] float Reach = 50f,
        [Length("Tip", 0, 40, Hint = "How far the arm's end turns up")] float Tip = 10f,
        [Length("Thickness", 5, 10, Hint = "How wide the hook is, as it lies printed; the pegs are no wider than a hole takes")] float Thickness = 6f,
        [Length("Board", 2, 8, Hint = "The pegboard's thickness: 3.2 or 6.4 for the usual kinds")] float Board = 5f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Short hook", Default with { Reach = 30, Tip = 8 }),
        ("Long hook", Default with { Reach = 120, Tip = 15 }),
        ("Shelf bracket", Default with { Reach = 80, Tip = 0, Thickness = 10 })
    ];

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        // In the plane: X out from the board, Y up it; stood up along Z by the thickness.
        const float back = 4f, arm = 5f;
        float low = Peg / 2f + 1f, high = low + Pitch;
        float t = s.Thickness;

        var pieces = new List<Mesh>
        {
            Shapes.Box(0, 0, 0, back, high + Peg / 2f + 1f, t),
            Shapes.Box(0, 0, 0, s.Reach, arm, t),
            Shapes.Box(s.Reach - arm, 0, 0, s.Reach, arm + s.Tip, t),

            // The top peg through the board, then down behind it.
            Pegs(-s.Board - 1f, 0.5f, high),
            Shapes.Box(-s.Board - 1f - Peg, high - 8f, 0, -s.Board - 0.5f, high + Peg / 4f, t),

            // The bottom one straight in, to stop the hook swinging.
            Pegs(-s.Board, 0.5f, low)
        };

        token.ThrowIfCancellationRequested();
        return new Generated([new GeneratedPart("Pegboard hook", Shapes.Union(pieces), Role: "hook")], []);

        // A round peg as a hole takes it, flattened to the thickness so the hook lies flat.
        Mesh Pegs(float from, float to, float y) =>
            Shapes.Intersect(Shapes.RodAlongX(Peg / 2f, from, to, y, t / 2f), Shapes.Box(from - 1, y - Peg, 0, to + 1, y + Peg, t));
    }
}
