using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Boxes;

public enum EnclosureLid
{
    [ShownAs("Lift-off")] LiftOff,
    [ShownAs("Screwed down")] Screwed
}

public enum Board
{
    [ShownAs("Raspberry Pi 4 or 5")] RaspberryPi,
    [ShownAs("Raspberry Pi Zero")] RaspberryPiZero,
    [ShownAs("Arduino Uno")] ArduinoUno,
    Custom
}

/// <summary>
/// A box for a circuit board: standoffs where the board's mounting holes are, room round it, and
/// a lift-off lid. The boards' outlines and holes are from their makers' drawings.
/// </summary>
public sealed class Enclosure : Generator<Enclosure.Settings>
{
    public override string Id => "box.enclosure";
    public override int Version => 1;
    public override string Category => "Boxes";
    public override string Title => "Electronics enclosure";
    public override string Summary => "A box for a board, with standoffs at its mounting holes and a lid. Cut-outs for its sockets are not made yet.";

    /// <param name="Holes">From the board's lower left corner.</param>
    /// <param name="Screw">The screw's size: 2.5 for M2.5.</param>
    public sealed record BoardSpec(float Width, float Depth, Vector2[] Holes, float Screw);

    public static BoardSpec Spec(Board board, Settings s) => board switch
    {
        Board.RaspberryPi => new(85f, 56f, [new(3.5f, 3.5f), new(61.5f, 3.5f), new(3.5f, 52.5f), new(61.5f, 52.5f)], 2.5f),
        Board.RaspberryPiZero => new(65f, 30f, [new(3.5f, 3.5f), new(61.5f, 3.5f), new(3.5f, 26.5f), new(61.5f, 26.5f)], 2.5f),
        Board.ArduinoUno => new(68.6f, 53.3f, [new(13.97f, 2.54f), new(15.24f, 50.8f), new(66.04f, 7.62f), new(66.04f, 35.56f)], 3f),
        _ => new(s.BoardWidth, s.BoardDepth,
            [new(s.HoleInset, s.HoleInset), new(s.BoardWidth - s.HoleInset, s.HoleInset),
             new(s.HoleInset, s.BoardDepth - s.HoleInset), new(s.BoardWidth - s.HoleInset, s.BoardDepth - s.HoleInset)], 3f)
    };

    public sealed record Settings(
        [Choice("Board")] Board Board = Board.RaspberryPi,
        [Length("Board width", 10, 180), ShowWhen(nameof(Board), Board.Custom)] float BoardWidth = 60f,
        [Length("Board depth", 10, 180), ShowWhen(nameof(Board), Board.Custom)] float BoardDepth = 40f,
        [Length("Hole inset", 2, 20, Hint = "From each edge to the middle of the corner holes"), ShowWhen(nameof(Board), Board.Custom)] float HoleInset = 3.5f,
        [Length("Room round it", 1, 30, Group = "Box")] float Gap = 3f,
        [Length("Inside height", 10, 100, Group = "Box", Hint = "From the floor to the underside of the lid")] float InsideHeight = 30f,
        [Wall("Wall", 1.2, 5, Group = "Box")] float Wall = 2f,
        [Length("Floor", 1, 5, Group = "Box")] float Floor = 2f,
        [Length("Standoffs", 2, 20, Group = "Box", Hint = "How high the board stands off the floor")] float Standoff = 5f,
        [Length("Corner radius", 0, 15, Group = "Box")] float Radius = 3f,
        [Clearance("Lid fit", 0.05, 1, Group = "Box")] float Fit = 0.2f,
        [Choice("Lid", Group = "Lid")] EnclosureLid Lid = EnclosureLid.LiftOff,
        [Length("Lid screws", 2, 5, Group = "Lid", Hint = "M3 for 3: through the lid into a post in each corner"), ShowWhen(nameof(Lid), EnclosureLid.Screwed)] float LidScrew = 3f);

    protected override bool Shows(Settings s, string parameter) => parameter != nameof(Settings.Fit) || s.Lid == EnclosureLid.LiftOff;

    /// <summary>A corner post's radius: round the screw's pilot, with a wall a screw can bite into.</summary>
    private static float PostRadius(Settings s) => s.LidScrew / 2f + 1.6f;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.InsideHeight < s.Standoff + 1.6f + 4 + 2)
            yield return "Not enough height inside for the standoffs, the board and the lid's lip.";

        var spec = Spec(s.Board, s);
        if (s.Board == Board.Custom && 2 * s.HoleInset > MathF.Min(s.BoardWidth, s.BoardDepth) - 4)
            yield return "The corner holes run into each other.";
        if (spec.Width + 2 * s.Gap + 2 * s.Wall > 200 || spec.Depth + 2 * s.Gap + 2 * s.Wall > 200)
            yield return "That box is bigger than the plate.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var board = Spec(s.Board, s);
        // Screwed down, the box is a post wider each way, so the corner posts stand clear of the board.
        float posts = s.Lid == EnclosureLid.Screwed ? 2 * PostRadius(s) : 0f;
        float width = board.Width + 2 * (s.Gap + s.Wall) + posts;
        float depth = board.Depth + 2 * (s.Gap + s.Wall) + posts;
        float height = s.Floor + s.InsideHeight;

        var pieces = new List<Mesh> { OpenBox.Tray(width, depth, height, s.Wall, s.Floor, s.Radius) };

        // A pilot hole a screw cuts its own thread into.
        float pilot = board.Screw * 0.85f;
        float post = board.Screw + 3f;
        var corner = new Vector2(-board.Width / 2f, -board.Depth / 2f);
        foreach (var hole in board.Holes)
            pieces.Add(Shapes.Tube(post / 2f, pilot / 2f, s.Floor - 0.5f, s.Floor + s.Standoff, corner + hole));

        var corners = new List<Vector2>();
        if (s.Lid == EnclosureLid.Screwed)
        {
            // In each inside corner, running into both walls, up to the rim the lid sits on.
            float r = PostRadius(s), cx = width / 2f - s.Wall - r + 0.5f, cy = depth / 2f - s.Wall - r + 0.5f;
            corners.AddRange([new(-cx, -cy), new(cx, -cy), new(-cx, cy), new(cx, cy)]);
            foreach (var c in corners)
                pieces.Add(Shapes.Tube(r, s.LidScrew * 0.85f / 2f, s.Floor - 0.5f, height, c));
        }

        token.ThrowIfCancellationRequested();
        var box = Shapes.Union(pieces);

        if (s.Lid == EnclosureLid.Screwed)
        {
            // A flat lid the box's own outline, a clearance hole over each post.
            var plate = Shapes.Prism(Shapes.RoundedRect(width, depth, s.Radius), 0, s.Floor);
            var flat = Shapes.Subtract(plate, corners.Select(c => Shapes.Cylinder(s.LidScrew / 2f + 0.2f, -1, s.Floor + 1, c)).ToList());
            var screwed = Shapes.InARow([("Enclosure", "box", box, Matrix4x4.Identity), ("Lid", "lid", flat, Matrix4x4.CreateTranslation(0, 0, height))]);
            return new Generated(screwed,
            [
                $"M{board.Screw:0.#} screws into {pilot:0.#} mm pilot holes for the board; M{s.LidScrew:0.#} screws through the lid into the corner posts.",
                "Cut the sockets' openings with Subtract for now."
            ]);
        }

        const float lip = 4f;
        var lid = LiddedBox.LiftOffLid(width, depth, s.Radius, s.Wall, s.Fit, s.Floor, lip);
        var together = Matrix4x4.CreateRotationX(MathF.PI) * Matrix4x4.CreateTranslation(0, 0, height + s.Floor);

        var parts = Shapes.InARow([("Enclosure", "box", box, Matrix4x4.Identity), ("Lid", "lid", lid, together)]);
        return new Generated(parts, [$"M{board.Screw:0.#} screws into {pilot:0.#} mm pilot holes. Cut the sockets' openings with Subtract for now."]);
    }
}
