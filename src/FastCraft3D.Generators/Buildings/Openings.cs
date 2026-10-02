using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Buildings;

/// <summary>What the window and the door share: the glass, outlines, and how their sizes read at a model's scale.</summary>
internal static class Glazing
{
    /// <summary>Pale blue, so the glass reads as glass on the plate.</summary>
    public static readonly Vector3 Colour = new(0.72f, 0.86f, 0.96f);

    /// <summary>The second filament: the clear one, beside whatever the frame is printed in.</summary>
    public const int Filament = 2;

    /// <summary>
    /// The panes as one part, each exactly filling its opening, lying on the plate at the back of
    /// the frame. In the openings rather than under the whole frame, so the frame stands on the
    /// plate as well and nothing prints in the air. The panes never touch, so they go together
    /// as they are.
    /// </summary>
    public static GeneratedPart Panes(IEnumerable<List<Vector2>> openings, float thickness) =>
        new("Glass", Mesh.Combine(openings.Select(o => Shapes.Prism(DrawnIn(o), 0, thickness))), Role: "glass")
        {
            Filament = Filament,
            Colour = Colour
        };

    /// <summary>
    /// How far each pane stands in from the frame it fills. Filling its opening exactly, a pane
    /// shared the frame's corners, and grouped into one object those welded into edges that four
    /// faces meet at - a group that is not a solid, which the health check and Export refuse. A
    /// hundredth of a millimetre is far under anything a printer lays down, and a slicer closes it.
    /// </summary>
    public const float Hair = 0.01f;

    /// <summary>A pane's outline drawn in by about <see cref="Hair"/> all round, toward its middle - panes are convex.</summary>
    private static List<Vector2> DrawnIn(List<Vector2> outline)
    {
        var middle = outline.Aggregate(Vector2.Zero, (sum, p) => sum + p) / outline.Count;
        float least = MathF.Min(outline.Max(p => p.X) - outline.Min(p => p.X), outline.Max(p => p.Y) - outline.Min(p => p.Y));
        float k = 1f - 2f * Hair / MathF.Max(least, 4f * Hair);
        return outline.Select(p => middle + (p - middle) * k).ToList();
    }

    public static List<Vector2> Rect((float X0, float Y0, float X1, float Y1) r) => Shapes.Rect(r.X0, r.Y0, r.X1, r.Y1);

    /// <summary>
    /// The parts stood up as they go in the wall, the face towards -Y - or, to print, left lying
    /// on their backs as they are built. Stood up by default: lying down, a door or a window on
    /// the plate did not read as one, and turning it upright by hand was the first thing anybody did.
    /// </summary>
    public static List<GeneratedPart> Stood(List<GeneratedPart> parts, bool flat)
    {
        if (flat) return parts;
        var up = new Matrix4x4(1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1);
        var stood = parts.Select(p => p with { Mesh = MeshTransform.Transformed(p.Mesh, up) }).ToList();

        // A window's sill hangs below its frame; the whole set is lifted so it stands on the plate.
        float low = stood.Min(p => p.Mesh.ComputeBounds().Min.Z);
        return stood.Select(p => p with { Mesh = Shapes.Moved(p.Mesh, 0, 0, -low) }).ToList();
    }

    public static string Printing(bool flat) => flat
        ? "Printed lying on its back, the face up. Stand it in its opening once printed."
        : "Shown standing, as it goes in the wall. Tick Lay flat to print it on its back, which prints it best.";

    /// <summary>
    /// What it glazes and its glass, put down as an assembly: each a part of its own, the glass on
    /// its clear filament, standing where it goes and picked and moved with the frame by the
    /// assembly's name.
    ///
    /// Grouped into the frame's one object, as they were first, the glass could not be given a
    /// filament of its own without taking the group apart for good; and before that, put down
    /// loose, a window's glass ended up scattered across the plate.
    /// </summary>
    public static Generated Assembled(List<GeneratedPart> parts, List<string> notes) => new(parts, notes) { LaidOut = false };

    /// <param name="flat">Printed lying down, so the glass is the bottom layers.</param>
    public static List<string> Notes(float thickness, Printer printer, bool flat = false)
    {
        int layers = Math.Max(1, (int)MathF.Round(thickness / printer.Layer));
        var notes = new List<string>
        {
            flat
                ? $"The glass is a part of its own, on filament {Filament}, and the first {layers} layers: print it in a "
                  + "clear filament on a printer with two, or print those layers in one and change to the frame's after them."
                : $"The glass is a part of its own, on filament {Filament}: print it in a clear filament."
        };

        if (thickness < 2 * printer.Layer)
            notes.Add($"Under two layers of {printer.Layer:0.##} mm, the glass may not print whole.");

        return notes;
    }

    /// <summary>"At 1:87, 1218 x 1392 mm" - or nothing, for a model drawn full size.</summary>
    public static string? Real(float width, float height, float modelScale, string what) =>
        modelScale > 1.5f ? $"At 1:{modelScale:0}, a {what} {width * modelScale:0} x {height * modelScale:0} mm." : null;

    /// <summary>The part of a convex outline where k.q + c is nought or less.</summary>
    public static List<Vector2> Clip(List<Vector2> outline, Vector2 k, float c)
    {
        var kept = new List<Vector2>();
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            float fp = Vector2.Dot(k, p) + c, fq = Vector2.Dot(k, q) + c;
            if (fp <= 0) kept.Add(p);
            if (fp * fq < 0) kept.Add(p + (q - p) * (fp / (fp - fq)));
        }

        return kept;
    }

    /// <summary>A convex outline cut down to a rectangle.</summary>
    public static List<Vector2> Within(List<Vector2> outline, float x0, float y0, float x1, float y1)
    {
        var o = Clip(outline, new(-1, 0), x0);
        o = Clip(o, new(1, 0), -x1);
        o = Clip(o, new(0, -1), y0);
        return Clip(o, new(0, 1), -y1);
    }

    public static float Area(List<Vector2> outline) => outline.Count < 3 ? 0 : MathF.Abs(Polygon2.SignedArea(outline));

    /// <summary>
    /// A convex outline drawn in by <paramref name="by"/> all round, every side moved in square to
    /// itself: the frame inside a frame. Empty when there is nothing left.
    /// </summary>
    public static List<Vector2> Inset(List<Vector2> outline, float by)
    {
        float turn = Polygon2.SignedArea(outline) >= 0 ? 1f : -1f;
        var kept = outline;

        for (int i = 0; i < outline.Count && kept.Count >= 3; i++)
        {
            var p = outline[i];
            var along = outline[(i + 1) % outline.Count] - p;
            if (along.LengthSquared() < 1e-12f) continue;

            var outward = Vector2.Normalize(new Vector2(along.Y, -along.X)) * turn;
            kept = Clip(kept, outward, by - Vector2.Dot(outward, p));
        }

        return kept.Count >= 3 ? kept : [];
    }

    /// <summary>An arc about <paramref name="centre"/>, from one angle to another, anticlockwise.</summary>
    public static IEnumerable<Vector2> Arc(Vector2 centre, float radius, float from, float to)
    {
        int n = Math.Max(4, (int)MathF.Ceiling(Shapes.Sides(radius) * (to - from) / (2 * MathF.PI)));
        for (int i = 0; i <= n; i++)
        {
            float a = from + (to - from) * i / n;
            yield return centre + radius * new Vector2(MathF.Cos(a), MathF.Sin(a));
        }
    }
}

public enum WindowShape
{
    Rectangular,
    [ShownAs("Arched top")] Arched,
    [ShownAs("Half round")] HalfRound,
    Round,
    [ShownAs("Bow (half octagon)")] Bow
}

/// <summary>
/// A window for a model house: a frame, glazing bars dividing it into panes, a sill, and glass as
/// a thin part of its own in the openings, to print in clear filament. Square, arched at the top,
/// half round or round. Printed lying on its face's back, the glass on the plate; sizes as the
/// model is drawn, with the real ones said beside them.
/// </summary>
public sealed class Window : Generator<Window.Settings>
{
    public override string Id => "building.window";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Window";
    public override string Summary => "A window frame - square, arched, half round or round - with glazing bars, a sill, and glass to print in clear filament.";

    /// <summary>Its height runs up it standing and front to back laid flat to print.</summary>
    public override SizeAxis SizeAxisOf(object settings, GeneratorParameter parameter) =>
        parameter.Name == nameof(Settings.Height) && settings is Settings { Flat: true } ? SizeAxis.Y : parameter.Size;

    public sealed record Settings(
        [Length("Width", 3, 200, Group = "Size", Hint = "Outside the frame, as the model is drawn", Size = SizeAxis.X)] float Width = 14f,
        [Length("Height", 3, 200, Group = "Size", Size = SizeAxis.Z)] float Height = 16f,
        [Length("Frame", 0.4, 10, Group = "Frame", Hint = "How wide the frame is, seen from the front")] float Frame = 0.8f,
        [Length("Depth", 0.4, 20, Group = "Frame", Hint = "How deep the frame is, into the wall")] float Depth = 1.6f,
        [Count("Panes across", 1, 8, Group = "Panes", Hint = "For a half round window, how many it is divided into round the arc")] int Columns = 2,
        [Count("Panes up", 1, 8, Group = "Panes")] int Rows = 2,
        [Length("Glazing bars", 0.2, 5, Group = "Panes", Hint = "How wide the bars between the panes are")] float Bar = 0.4f,
        [Toggle("Sill", Group = "Sill")] bool Sill = true,
        [Length("Sill projection", 0, 10, Group = "Sill", Hint = "How far the sill stands out in front of the frame"), ShowWhen(nameof(Sill), true)] float SillOut = 0.8f,
        [Toggle("Glass", Group = "Glass", Hint = "Off for the frame alone")] bool Glass = true,
        [Length("Glass thickness", 0.1, 2, Group = "Glass", Hint = "Two or three layers"), ShowWhen(nameof(Glass), true)] float GlassThickness = 0.4f,
        [Choice("Shape", Group = "Size")] WindowShape Shape = WindowShape.Rectangular,
        [Toggle("Lay flat to print", Hint = "On its back, the glass on the plate: how it prints best")] bool Flat = false);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Two by two sash", Default),
        ("Casement, one by two", Default with { Width = 9, Columns = 1, Rows = 2 }),
        ("Picture window", Default with { Width = 22, Height = 14, Columns = 1, Rows = 1 }),
        ("Georgian, three by four", Default with { Width = 14, Height = 20, Columns = 3, Rows = 4, Bar = 0.3f }),
        ("Arched, two by three", Default with { Shape = WindowShape.Arched, Width = 10, Height = 20, Columns = 2, Rows = 3 }),
        ("Lunette", Default with { Shape = WindowShape.HalfRound, Width = 16, Columns = 4 }),
        ("Round, four panes", Default with { Shape = WindowShape.Round, Width = 10, Columns = 2, Rows = 2, Sill = false }),
        ("Bow", Default with { Shape = WindowShape.Bow, Width = 24, Height = 16, Columns = 1, Rows = 2 })
    ];

    protected override bool Shows(Settings s, string parameter) => parameter switch
    {
        nameof(Settings.Height) => s.Shape is WindowShape.Rectangular or WindowShape.Arched or WindowShape.Bow,
        nameof(Settings.Rows) => s.Shape != WindowShape.HalfRound,
        nameof(Settings.Sill) or nameof(Settings.SillOut) => s.Shape is not (WindowShape.Round or WindowShape.Bow),
        nameof(Settings.Flat) => s.Shape != WindowShape.Bow,
        _ => true
    };

    /// <summary>Each pane's opening in a rectangular window: X from left to right, Y from the bottom up.</summary>
    internal static List<(float X0, float Y0, float X1, float Y1)> Panes(Settings s)
    {
        float left = -s.Width / 2f + s.Frame, bottom = s.Frame;
        float wide = (s.Width - 2 * s.Frame - (s.Columns - 1) * s.Bar) / s.Columns;
        float tall = (s.Height - 2 * s.Frame - (s.Rows - 1) * s.Bar) / s.Rows;

        var panes = new List<(float, float, float, float)>();
        for (int c = 0; c < s.Columns; c++)
            for (int r = 0; r < s.Rows; r++)
            {
                float x = left + c * (wide + s.Bar), y = bottom + r * (tall + s.Bar);
                panes.Add((x, y, x + wide, y + tall));
            }

        return panes;
    }

    /// <summary>The outside of the frame, and the opening inside it, both convex.</summary>
    private static (List<Vector2> Outside, List<Vector2> Opening) Outlines(Settings s)
    {
        float w = s.Width / 2f, f = s.Frame;
        switch (s.Shape)
        {
            case WindowShape.Arched:
            {
                float spring = MathF.Max(s.Height - w, 0.01f);
                List<Vector2> Arch(float half, float low) =>
                    [new(-half, low), new(half, low), .. Glazing.Arc(new Vector2(0, spring), half, 0, MathF.PI)];
                return (Arch(w, 0), Arch(w - f, f));
            }

            case WindowShape.HalfRound:
                return ([.. Glazing.Arc(Vector2.Zero, w, 0, MathF.PI)],
                        Glazing.Clip([.. Glazing.Arc(Vector2.Zero, w - f, 0, MathF.PI)], new(0, -1), f));

            case WindowShape.Round:
                return (Shapes.Circle(w, new Vector2(0, w)), Shapes.Circle(w - f, new Vector2(0, w), Shapes.Sides(w)));

            default:
                return (Shapes.Rect(-w, 0, w, s.Height), Shapes.Rect(-w + f, f, w - f, s.Height - f));
        }
    }

    /// <summary>Every pane, as the opening cut up by the bars: a grid, or for a half round window, spokes.</summary>
    internal static List<List<Vector2>> PaneOutlines(Settings s)
    {
        if (s.Shape == WindowShape.Rectangular) return Panes(s).Select(Glazing.Rect).ToList();

        var opening = Outlines(s).Opening;
        var panes = new List<List<Vector2>>();

        if (s.Shape == WindowShape.HalfRound)
        {
            for (int i = 0; i < s.Columns; i++)
            {
                float a0 = MathF.PI * i / s.Columns, a1 = MathF.PI * (i + 1) / s.Columns;
                var pane = opening;
                if (i > 0) pane = Glazing.Clip(pane, new(MathF.Sin(a0), -MathF.Cos(a0)), s.Bar / 2f);
                if (i < s.Columns - 1) pane = Glazing.Clip(pane, new(-MathF.Sin(a1), MathF.Cos(a1)), s.Bar / 2f);
                panes.Add(pane);
            }

            return panes;
        }

        float x0 = opening.Min(p => p.X), x1 = opening.Max(p => p.X), y0 = opening.Min(p => p.Y), y1 = opening.Max(p => p.Y);
        float wide = (x1 - x0 - (s.Columns - 1) * s.Bar) / s.Columns, tall = (y1 - y0 - (s.Rows - 1) * s.Bar) / s.Rows;
        for (int c = 0; c < s.Columns; c++)
            for (int r = 0; r < s.Rows; r++)
            {
                float x = x0 + c * (wide + s.Bar), y = y0 + r * (tall + s.Bar);
                panes.Add(Glazing.Within(opening, x, y, x + wide, y + tall));
            }

        return panes;
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Shape == WindowShape.Bow)
        {
            var (face, _) = BowFace(s);
            float wide = (face - 2 * s.Frame - (s.Columns - 1) * s.Bar) / s.Columns;
            float tall = (s.Height - 4 * s.Frame - (s.Rows - 1) * s.Bar) / s.Rows;
            if (wide < 0.5f || tall < 0.5f) yield return "The frame and bars leave panes too small to print. Fewer panes, thinner bars, or a bigger window.";
            if (s.Glass && s.GlassThickness >= s.Depth) yield return "The glass is as thick as the frame is deep.";
            yield break;
        }

        if (s.Shape == WindowShape.Arched && s.Height < s.Width / 2f + s.Frame + 1)
            yield return "Too low for its arch: an arched window is at least half its width tall, and a little.";
        else if (s.Frame * 2 + 1 > (s.Shape == WindowShape.HalfRound ? s.Width / 2f : s.Width))
            yield return "The frame leaves no opening.";
        else if (s.Shape == WindowShape.Rectangular && Panes(s).Any(p => p.X1 - p.X0 < 0.5f || p.Y1 - p.Y0 < 0.5f)
                 || PaneOutlines(s).Any(p => Glazing.Area(p) < 0.25f))
            yield return "The frame and bars leave panes too small to print. Fewer panes, thinner bars, or a bigger window.";

        if (s.Glass && s.GlassThickness >= s.Depth)
            yield return "The glass is as thick as the frame is deep.";
    }

    /// <summary>A bow's three faces are the same width: the front, and the two at forty-five degrees back to the wall.</summary>
    private static (float Face, float Out) BowFace(Settings s)
    {
        float face = s.Width / (1 + MathF.Sqrt(2));
        return (face, face / MathF.Sqrt(2));
    }

    /// <summary>
    /// A bow window: half an octagon standing out from the wall - a front and two sides at
    /// forty-five degrees - each face glazed, with a floor and a top, open at the back where it
    /// meets the wall. Standing, as it goes on the house; it is a box, so it prints that way too.
    /// </summary>
    private static Generated Bow(Settings s, Printer printer)
    {
        var (face, reach) = BowFace(s);
        float w = s.Width / 2f, f = s.Frame, d = s.Depth, h = s.Height;
        Vector2[] corners = [new(-w, 0), new(-face / 2f, -reach), new(face / 2f, -reach), new(w, 0)];

        // Inside: each face's line moved in by the depth, the ends run on past the wall line so the back is open.
        var lines = Enumerable.Range(0, 3).Select(k =>
        {
            var u = Vector2.Normalize(corners[k + 1] - corners[k]);
            return (Point: corners[k] + new Vector2(-u.Y, u.X) * d, Along: u);
        }).ToList();

        static Vector2 Meet((Vector2 Point, Vector2 Along) a, (Vector2 Point, Vector2 Along) b)
        {
            float cross = a.Along.X * b.Along.Y - a.Along.Y * b.Along.X;
            var gap = b.Point - a.Point;
            return a.Point + a.Along * ((gap.X * b.Along.Y - gap.Y * b.Along.X) / cross);
        }

        static Vector2 AtWall((Vector2 Point, Vector2 Along) line) =>
            line.Point + line.Along * ((1f - line.Point.Y) / line.Along.Y);

        List<Vector2> inside = [AtWall(lines[0]), Meet(lines[0], lines[1]), Meet(lines[1], lines[2]), AtWall(lines[2])];
        var cuts = new List<Mesh> { Shapes.Prism(inside, f, h - f) };
        var glass = new List<Mesh>();

        float wide = (face - 2 * f - (s.Columns - 1) * s.Bar) / s.Columns;
        float tall = (h - 4 * f - (s.Rows - 1) * s.Bar) / s.Rows;
        for (int k = 0; k < 3; k++)
        {
            // In each face's own terms: along it, up, and in through the wall.
            var u = Vector2.Normalize(corners[k + 1] - corners[k]);
            var place = new Matrix4x4(u.X, u.Y, 0, 0, 0, 0, 1, 0, -u.Y, u.X, 0, 0, corners[k].X, corners[k].Y, 0, 1);
            for (int c = 0; c < s.Columns; c++)
                for (int r = 0; r < s.Rows; r++)
                {
                    float x = f + c * (wide + s.Bar), y = 2 * f + r * (tall + s.Bar);
                    cuts.Add(MeshTransform.Transformed(Shapes.Box(x, y, -1, x + wide, y + tall, d + 1), place));
                    const float hair = Glazing.Hair;
                    if (s.Glass) glass.Add(MeshTransform.Transformed(Shapes.Box(x + hair, y + hair, d - s.GlassThickness, x + wide - hair, y + tall - hair, d), place));
                }
        }

        var frame = Shapes.Subtract(Shapes.Prism([.. corners], 0, h), cuts);
        var parts = new List<GeneratedPart> { new("Bow window", frame, Role: "frame") };
        var notes = new List<string>();
        if (s.Glass)
        {
            parts.Add(new GeneratedPart("Glass", Mesh.Combine(glass), Role: "glass") { Filament = Glazing.Filament, Colour = Glazing.Colour });
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer));
        }

        notes.Add($"Stands {reach:0.#} mm out from the wall, open at the back: set it against the wall and Merge.");
        return Glazing.Assembled(parts, notes);
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        if (s.Shape == WindowShape.Bow) return Bow(s, printer);

        // The panes cut out of a solid rather than laid as holes in one outline: laid as holes,
        // a grid of them came back open wherever their corners lined up, the triangulation being
        // written for lettering, whose holes never do.
        var panes = PaneOutlines(s);
        var frame = Shapes.Subtract(Shapes.Prism(Outlines(s).Outside, 0, s.Depth), panes.Select(p => Shapes.Prism(p, -1, s.Depth + 1)).ToList());

        if (s.Sill && s.Shape != WindowShape.Round)
        {
            // A little wider than the frame and standing out in front of it, under the bottom rail.
            float over = s.Frame / 2f;
            frame = Shapes.Union(frame, Shapes.Box(-s.Width / 2f - over, -s.Frame, 0, s.Width / 2f + over, 0.01f, s.Depth + s.SillOut));
        }

        var parts = new List<GeneratedPart> { new("Window frame", frame, Role: "frame") };
        var notes = new List<string>();

        if (s.Glass)
        {
            parts.Add(Glazing.Panes(panes, s.GlassThickness));
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer, s.Flat));
        }

        notes.Add(Glazing.Printing(s.Flat));
        return Glazing.Assembled(Glazing.Stood(parts, s.Flat), notes);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        float tall = s.Shape switch { WindowShape.HalfRound => s.Width / 2f, WindowShape.Round => s.Width, _ => s.Height };
        if (Glazing.Real(s.Width, tall, modelScale, "window") is { } real) yield return real;

        if (s.Shape == WindowShape.Bow)
        {
            var (face, reach) = BowFace(s);
            float times = modelScale > 1.5f ? modelScale : 1f;
            yield return $"Three faces {face * times:0} mm wide{(times > 1 ? " real" : "")}, standing {reach * times:0} mm out.";
            yield break;
        }

        if (s.Shape != WindowShape.Rectangular)
        {
            yield return $"{PaneOutlines(s).Count} panes.";
            yield break;
        }

        var pane = Panes(s)[0];
        float scale = modelScale > 1.5f ? modelScale : 1f;
        yield return $"{s.Columns * s.Rows} panes of {(pane.X1 - pane.X0) * scale:0.#} x {(pane.Y1 - pane.Y0) * scale:0.#} mm{(scale > 1 ? " real" : "")}.";
    }
}

public enum DoorType
{
    Single,
    Double,
    French,
    [ShownAs("Dutch (stable)")] Dutch
}

public enum DoorLeaf
{
    Plain,
    Panelled,
    [ShownAs("Glazed at the top")] Glazed
}

public enum DoorShape
{
    Rectangular,
    [ShownAs("Arched top")] Arched
}

public enum DoorGlass
{
    [ShownAs("Panes in the openings")] Panes,
    [ShownAs("One sheet across the back")] Sheet
}

/// <summary>
/// A door for a model house: a frame round the top and sides, square or arched, the leaf in it -
/// or two, for a double or French door - plain or with panels sunk in its face, glass in its top
/// panels if wanted, divided by glazing bars, and split across the middle for a Dutch door. The
/// door and its glass an assembly, printed lying on its back as the window is.
/// </summary>
public sealed class Door : Generator<Door.Settings>
{
    public override string Id => "building.door";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Door";
    public override bool IsBeta => false;
    public override string Summary => "A door in its frame - single, double, French or Dutch, square or arched - plain, panelled or glazed, with glass to print in clear filament.";

    /// <summary>Its height runs up it standing and front to back laid flat to print.</summary>
    public override SizeAxis SizeAxisOf(object settings, GeneratorParameter parameter) =>
        parameter.Name == nameof(Settings.Height) && settings is Settings { Flat: true } ? SizeAxis.Y : parameter.Size;

    public sealed record Settings(
        [Length("Width", 3, 200, Group = "Size", Hint = "Outside the frame, as the model is drawn", Size = SizeAxis.X)] float Width = 11f,
        [Length("Height", 5, 250, Group = "Size", Hint = "From the floor to the top of the frame", Size = SizeAxis.Z)] float Height = 25f,
        [Choice("Shape", Group = "Size")] DoorShape Shape = DoorShape.Rectangular,
        [Length("Arch rise", 0, 100, Group = "Size", Hint = "How far the arch rises above its sides: nought or half the width for a round arch, less for a shallow one"), ShowWhen(nameof(Shape), DoorShape.Arched)] float Rise = 0f,
        [Length("Frame", 0.4, 10, Group = "Frame", Hint = "How wide the frame is, seen from the front")] float Frame = 0.8f,
        [Length("Depth", 0.6, 20, Group = "Frame", Hint = "How deep the frame is, into the wall")] float Depth = 1.6f,
        [Length("Leaf set back", 0, 5, Group = "Frame", Hint = "How far the door itself sits back from the frame's face")] float SetBack = 0.4f,
        [Choice("Leaf", Group = "Leaf")] DoorLeaf Leaf = DoorLeaf.Panelled,
        [Count("Panels", 1, 6, Group = "Leaf", Hint = "One above another"), ShowWhen(nameof(Leaf), DoorLeaf.Panelled, DoorLeaf.Glazed)] int Panels = 4,
        [Count("Glazed panels", 1, 6, Group = "Leaf", Hint = "Counted from the top"), ShowWhen(nameof(Leaf), DoorLeaf.Glazed)] int Glazed = 1,
        [Length("Panel recess", 0.1, 2, Group = "Leaf", Hint = "How deep the panels are sunk into the leaf's face"), ShowWhen(nameof(Leaf), DoorLeaf.Panelled, DoorLeaf.Glazed)] float Recess = 0.3f,
        [Count("Leaves", 1, 2, Group = "Leaf", Hint = "Two for a double or a French door")] int Leaves = 1,
        [Toggle("Split", Group = "Leaf", Hint = "A Dutch door: the top half opens on its own")] bool Split = false,
        [Count("Panes across", 1, 4, Group = "Leaf", Hint = "Glazing bars in each glazed panel"), ShowWhen(nameof(Leaf), DoorLeaf.Glazed)] int PaneColumns = 1,
        [Count("Panes up", 1, 6, Group = "Leaf"), ShowWhen(nameof(Leaf), DoorLeaf.Glazed)] int PaneRows = 1,
        [Choice("Type", Hint = "Fills in the leaves, the split and the glazing; change any of them after")] DoorType Type = DoorType.Single,
        [Toggle("Lay flat to print", Hint = "On its back, the glass on the plate: how it prints best")] bool Flat = false,
        [Count("Sidelights", 0, 2, Group = "Surround", Hint = "Fixed glazed panels beside the door: one on the left, or one each side")] int Sidelights = 0,
        [Length("Sidelight width", 1, 100, Group = "Surround")] float SidelightWidth = 4f,
        [Length("Transom", 0, 100, Group = "Surround", Hint = "A fixed glazed panel over the door, this tall - under an arch, the fanlight. Nought for none.")] float Transom = 0f,
        [Toggle("Glass", Group = "Glass", Hint = "Off for the openings alone: the glazed panels, sidelights and transom left open")] bool Glass = true,
        [Choice("Glass as", Group = "Glass", Hint = "Panes in the openings, flush with the back of the door; or one clear sheet the whole back of the door, the door standing on it")] DoorGlass GlassAs = DoorGlass.Panes,
        [Length("Glass thickness", 0.1, 20, Group = "Glass", Hint = "Two or three layers is usual; as deep as the leaf at the most")] float GlassThickness = 0.4f);

    protected override bool Shows(Settings s, string parameter) => parameter switch
    {
        nameof(Settings.SidelightWidth) => s.Sidelights > 0,
        nameof(Settings.Glass) => Glazes(s),
        nameof(Settings.GlassAs) or nameof(Settings.GlassThickness) => Glazes(s) && s.Glass,
        _ => true
    };

    /// <summary>Whether there is anything to glaze: glazed panels, a sidelight or a transom.</summary>
    private static bool Glazes(Settings s) => s.Leaf == DoorLeaf.Glazed || s.Sidelights > 0 || s.Transom > 0;

    /// <summary>Whether the glass is one sheet across the back rather than a pane in each opening.</summary>
    private static bool Sheet(Settings s) => Glazes(s) && s.Glass && s.GlassAs == DoorGlass.Sheet;

    /// <summary>
    /// Where the door itself is, inside the frame, the sidelights and the transom: its left and
    /// right, and the top of the leaf - its crown, under an arch.
    /// </summary>
    private static (float Left, float Right, float Top) Opening(Settings s)
    {
        float w = s.Width / 2f, f = s.Frame, side = s.Sidelights > 0 ? s.SidelightWidth + f : 0f;
        return (-w + f + side, w - f - (s.Sidelights == 2 ? side : 0f), s.Height - f - (s.Transom > 0 ? s.Transom + f : 0f));
    }

    /// <summary>How far the arch rises above its sides: as asked, or half the width for a round one.</summary>
    private static float RiseOf(Settings s) => s.Rise <= 0f ? s.Width / 2f : MathF.Min(s.Rise, s.Width / 2f);

    /// <summary>The circle the outside of the arch is on: the height of its centre, and its radius.</summary>
    private static (float Centre, float Radius) ArchCircle(Settings s)
    {
        float w = s.Width / 2f, rise = RiseOf(s);
        float radius = (w * w + rise * rise) / (2f * rise);
        return (s.Height - radius, radius);
    }

    /// <summary>The outside of the frame, convex.</summary>
    private static List<Vector2> Outside(Settings s)
    {
        float w = s.Width / 2f;
        if (s.Shape != DoorShape.Arched) return Shapes.Rect(-w, 0, w, s.Height);

        var (centre, radius) = ArchCircle(s);
        float side = MathF.Atan2(s.Height - RiseOf(s) - centre, w);
        return [new(-w, 0), new(w, 0), .. Crowned(centre, radius, side)];
    }

    /// <summary>
    /// The arch from one side over to the other, with a corner at its crown. Drawn in one sweep,
    /// an odd number of sides put a flat across the top and the door came out short of its height.
    /// </summary>
    private static IEnumerable<Vector2> Crowned(float centre, float radius, float side) =>
        Glazing.Arc(new Vector2(0, centre), radius, side, MathF.PI / 2f)
            .Concat(Glazing.Arc(new Vector2(0, centre), radius, MathF.PI / 2f, MathF.PI - side).Skip(1));

    /// <summary>
    /// The inside of the frame, run on below the floor so that whatever is cut out of it opens at
    /// the bottom. Convex, and under an arch concentric with the outside, so the frame is as wide
    /// round the arch as up the sides. Everything inside the frame - the leaves, their panels and
    /// panes, the sidelights, the transom - is a rectangle cut down to this, which is all an arch
    /// asks of any of them.
    /// </summary>
    private static List<Vector2> Inside(Settings s)
    {
        float w = s.Width / 2f - s.Frame;
        if (s.Shape != DoorShape.Arched) return Shapes.Rect(-w, -1f, w, s.Height - s.Frame);

        var (centre, radius) = ArchCircle(s);
        float inner = radius - s.Frame;
        float side = MathF.Atan2(MathF.Sqrt(MathF.Max(inner * inner - w * w, 0f)), w);
        return [new(-w, -1f), new(w, -1f), .. Crowned(centre, inner, side)];
    }

    /// <summary>Where the leaves go: between the sidelights, up to the transom or into the arch.</summary>
    private static List<Vector2> Doorway(Settings s)
    {
        var (left, right, top) = Opening(s);
        return Glazing.Within(Inside(s), left, -1f, right, s.Transom > 0 ? top : float.MaxValue);
    }

    /// <summary>One leaf, from the floor up.</summary>
    private static List<Vector2> LeafOf(Settings s, int leaf)
    {
        var (left, right, _) = Opening(s);
        float each = (right - left) / s.Leaves, from = left + leaf * each;
        return Glazing.Within(Doorway(s), from, 0f, from + each, float.MaxValue);
    }

    /// <summary>Stiles and rails a little wider than the frame, as a real door's are.</summary>
    private static float Stile(Settings s)
    {
        var (left, right, _) = Opening(s);
        return MathF.Max(s.Frame, 0.14f * (right - left) / s.Leaves);
    }

    /// <summary>What each type of door is, filled in when it is chosen.</summary>
    protected override Settings Adjust(Settings before, Settings after, string changed) =>
        changed != nameof(Settings.Type) ? after : after.Type switch
        {
            DoorType.Double => after with { Width = MathF.Max(after.Width, 18), Leaves = 2, Split = false, Leaf = DoorLeaf.Panelled, Panels = 3 },
            DoorType.French => after with { Width = MathF.Max(after.Width, 18), Leaves = 2, Split = false, Leaf = DoorLeaf.Glazed, Panels = 1, Glazed = 1, PaneColumns = 2, PaneRows = 4 },
            // Set deeper in its frame than a plain door: the halves that open stand back from the
            // frame round them, which is most of what tells a stable door from a double one at
            // the size a model's door is.
            DoorType.Dutch => after with
            {
                Leaves = 1, Split = true, Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1, PaneColumns = 2, PaneRows = 3,
                SetBack = MathF.Max(after.SetBack, MathF.Min(after.Depth * 0.5f, 0.8f))
            },
            _ => after with { Leaves = 1, Split = false, Leaf = DoorLeaf.Panelled, Panels = 4 }
        };

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Plain", Default with { Leaf = DoorLeaf.Plain }),
        ("Four panels", Default),
        ("Half glazed", Default with { Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1 }),
        ("Double width, glazed", Default with { Width = 18, Leaf = DoorLeaf.Glazed, Panels = 3, Glazed = 2 }),
        ("French doors", Default with { Type = DoorType.French, Width = 18, Leaves = 2, Leaf = DoorLeaf.Glazed, Panels = 1, Glazed = 1, PaneColumns = 2, PaneRows = 4 }),
        ("Dutch door", Default with { Type = DoorType.Dutch, Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1, Split = true, PaneColumns = 2, PaneRows = 3, SetBack = 0.8f }),
        ("Entrance, sidelights and transom", Default with { Width = 22, Height = 30, Sidelights = 2, SidelightWidth = 3.5f, Transom = 3.5f, Leaf = DoorLeaf.Glazed, Panels = 1, Glazed = 1, PaneColumns = 2, PaneRows = 4 }),
        ("Arched, three panels", Default with { Shape = DoorShape.Arched, Panels = 3 }),
        ("Arched, fanlight over", Default with { Shape = DoorShape.Arched, Width = 16, Height = 30, Transom = 6.4f, Leaf = DoorLeaf.Panelled, Panels = 3 }),
        ("Shallow arch, glazed", Default with { Shape = DoorShape.Arched, Rise = 2.5f, Width = 14, Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1, PaneColumns = 2, PaneRows = 2 })
    ];

    /// <summary>The first leaf's panels, top first, each as the box round it: X from left to right, Y from the floor up.</summary>
    internal static List<(float X0, float Y0, float X1, float Y1)> Panels(Settings s) => PanelOutlines(s, 0).Select(Box).ToList();

    private static (float X0, float Y0, float X1, float Y1) Box(List<Vector2> outline) =>
        (outline.Min(p => p.X), outline.Min(p => p.Y), outline.Max(p => p.X), outline.Max(p => p.Y));

    /// <summary>
    /// A leaf's panels, top first: its face inside the stiles and rails, in bands one above
    /// another with a rail between each. Under an arch the top one follows it round.
    /// </summary>
    private static List<List<Vector2>> PanelOutlines(Settings s, int leaf)
    {
        float stile = Stile(s);
        var face = Glazing.Inset(LeafOf(s, leaf), stile);
        if (face.Count < 3) return [];

        var (x0, y0, x1, y1) = Box(face);
        float tall = (y1 - y0 - (s.Panels - 1) * stile) / s.Panels;
        if (tall <= 0) return [];

        return Enumerable.Range(0, s.Panels)
            .Select(i => Glazing.Within(face, x0, y1 - (i + 1) * tall - i * stile, x1, y1 - i * tall - i * stile))
            .ToList();
    }

    private static float Bar(Settings s) => MathF.Max(0.3f, s.Frame * 0.4f);

    /// <summary>
    /// A glazed panel cut into its panes by glazing bars. Under an arch a pane in the corner can be
    /// cut down to a sliver; one too small to print is left as bar instead.
    /// </summary>
    private static List<List<Vector2>> PanesOf(Settings s, List<Vector2> panel)
    {
        var (x0, y0, x1, y1) = Box(panel);
        float bar = Bar(s);
        float wide = (x1 - x0 - (s.PaneColumns - 1) * bar) / s.PaneColumns;
        float tall = (y1 - y0 - (s.PaneRows - 1) * bar) / s.PaneRows;

        var panes = new List<List<Vector2>>();
        for (int c = 0; c < s.PaneColumns; c++)
            for (int r = 0; r < s.PaneRows; r++)
            {
                float x = x0 + c * (wide + bar), y = y0 + r * (tall + bar);
                var pane = Glazing.Within(panel, x, y, x + wide, y + tall);
                if (Glazing.Area(pane) >= LeastPane) panes.Add(pane);
            }

        return panes;
    }

    /// <summary>A quarter of a square millimeter: smaller, a pane is a pinhole with a hair of glass in it.</summary>
    private const float LeastPane = 0.25f;

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Shape == DoorShape.Arched)
        {
            if (s.Rise > s.Width / 2f + 1e-3f)
                yield return "An arch rises at most half the door's width - that is a round arch.";
            else if (s.Height - RiseOf(s) < 2 * s.Frame + 2)
                yield return "Too low for its arch: the arch would come down to the floor.";
        }

        var (openLeft, openRight, openTop) = Opening(s);
        if (openRight - openLeft < 1.5f * s.Leaves) yield return "The frame and sidelights leave no room for the door.";
        if (openTop < 5f) yield return "The transom leaves no room for the door under it.";
        if (s.SetBack >= s.Depth - 0.3f) yield return "The leaf is set back as far as the frame is deep.";

        float leaf = s.Depth - s.SetBack;
        bool sheet = Sheet(s);

        // A sheet of glass under the whole door takes its thickness off the back of the leaf.
        float under = sheet ? s.GlassThickness + Glazing.Hair : 0f;

        if (s.Leaf != DoorLeaf.Plain)
        {
            var panels = PanelOutlines(s, 0);
            if (panels.Count < s.Panels || panels.Select(Box).Any(p => p.X1 - p.X0 < 0.5f || p.Y1 - p.Y0 < 0.5f))
                yield return "The panels would be under half a millimeter. Fewer of them, or a bigger door.";
            else if (s.Leaf == DoorLeaf.Glazed && s.Glazed <= s.Panels)
            {
                var (x0, y0, x1, y1) = Box(panels[0]);
                if ((x1 - x0 - (s.PaneColumns - 1) * Bar(s)) / s.PaneColumns < 0.5f || (y1 - y0 - (s.PaneRows - 1) * Bar(s)) / s.PaneRows < 0.5f)
                    yield return "The glazing bars leave panes under half a millimeter. Fewer panes, or a bigger door.";
            }

            if (s.Recess >= leaf - under - 0.2f)
                yield return sheet ? "The panels are sunk through to the glass behind them." : "The panels are sunk through the leaf.";
        }

        if (s.Leaf == DoorLeaf.Glazed && s.Glazed > s.Panels) yield return $"Only {s.Panels} panels to glaze.";

        // As thick as the leaf is deep at the most - a pane standing proud of the leaf's face is
        // not glass in a door - and a sheet has to leave the leaf something to stand on it with.
        if (Glazes(s) && s.Glass)
        {
            if (sheet && s.GlassThickness > leaf - 0.2f)
                yield return $"The glass sheet leaves the leaf nothing to stand on: it is {leaf:0.##} mm deep.";
            else if (!sheet && s.GlassThickness > (s.Leaf == DoorLeaf.Glazed ? leaf : s.Depth) + 1e-3f)
                yield return $"The glass is thicker than the {(s.Leaf == DoorLeaf.Glazed ? "leaf" : "frame")} is deep.";
        }
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float w = s.Width / 2f, f = s.Frame;
        var (left, right, top) = Opening(s);
        float leafDepth = s.Depth - s.SetBack;
        var inside = Inside(s);
        var doorway = Doorway(s);

        // One block, cut: the door's opening down to the leaf, set back in it; the sidelights and
        // the transom right through, with the frame round them - so the fixed parts stand flush
        // with the frame and the leaf that opens stands back behind them.
        var cuts = new List<Mesh> { Shapes.Prism(doorway, leafDepth, s.Depth + 1) };
        var lights = new List<List<Vector2>>();

        void Through(List<Vector2> outline, float to)
        {
            if (outline.Count < 3) return;
            cuts.Add(Shapes.Prism(outline, -1, to));
            lights.Add(outline);
        }

        // Without a transom, up into the arch; with one, up to it.
        float besideTop = s.Transom > 0 ? top : float.MaxValue;
        if (s.Sidelights > 0) Through(Glazing.Within(inside, -w + f, f, -w + f + s.SidelightWidth, besideTop), s.Depth + 1);
        if (s.Sidelights == 2) Through(Glazing.Within(inside, w - f - s.SidelightWidth, f, w - f, besideTop), s.Depth + 1);
        if (s.Transom > 0) Through(Glazing.Within(inside, -w + f, top + f, w - f, float.MaxValue), s.Depth + 1);

        for (int l = 0; l < s.Leaves; l++)
        {
            var panels = s.Leaf == DoorLeaf.Plain ? [] : PanelOutlines(s, l);
            int glazed = s.Leaf == DoorLeaf.Glazed ? Math.Min(s.Glazed, panels.Count) : 0;

            foreach (var p in panels.Skip(glazed))
                cuts.Add(Shapes.Prism(p, leafDepth - s.Recess, leafDepth + 1));
            foreach (var pane in panels.Take(glazed).SelectMany(p => PanesOf(s, p)))
                Through(pane, leafDepth + 1);
        }

        // Where two leaves meet, and where a Dutch door's halves do: a gap right through, as a
        // real door has, each part held by the frame. A groove in the face was tried first and
        // could not be seen - it was no deeper than the panels, and ran through them.
        float gap = MathF.Max(0.4f, f * 0.4f), middle = (left + right) / 2f;
        if (s.Leaves == 2)
            cuts.Add(Shapes.Prism(Glazing.Within(doorway, middle - gap / 2f, -1, middle + gap / 2f, float.MaxValue), -1, leafDepth + 1));
        if (s.Split)
        {
            // On the rail between two panels nearest the middle, so it does not cut across a panel.
            float mid = top / 2f;
            var panels = s.Leaf == DoorLeaf.Plain ? [] : PanelOutlines(s, 0).Select(Box).ToList();
            if (panels.Count > 1)
                mid = Enumerable.Range(0, panels.Count - 1).Select(i => (panels[i].Y0 + panels[i + 1].Y1) / 2f).MinBy(y => MathF.Abs(y - mid));
            cuts.Add(Shapes.Prism(Glazing.Within(doorway, left, mid - gap / 2f, right, mid + gap / 2f), -1, leafDepth + 1));
        }

        token.ThrowIfCancellationRequested();

        // On a sheet of glass the door stands a hair clear of it, for the reason a pane stands a
        // hair in from its frame: touching, the two would share a face, and in one file or one
        // group that is not a solid. See Glazing.Hair.
        bool glass = Glazes(s) && s.Glass && lights.Count > 0, sheet = glass && Sheet(s);
        var door = Shapes.Subtract(Shapes.Prism(Outside(s), sheet ? s.GlassThickness + Glazing.Hair : 0f, s.Depth), cuts);

        var parts = new List<GeneratedPart> { new("Door", door, Role: "door") };
        var notes = new List<string>();
        if (glass)
        {
            parts.Add(Glazing.Panes(sheet ? [Outside(s)] : lights, s.GlassThickness));
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer, s.Flat));
        }

        notes.Add(Glazing.Printing(s.Flat));
        return Glazing.Assembled(Glazing.Stood(parts, s.Flat), notes);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        if (Glazing.Real(s.Width, s.Height, modelScale, "door") is { } real) yield return real;

        float scale = modelScale > 1.5f ? modelScale : 1f;
        var (left, right, top) = Opening(s);
        float clear = (right - left) * scale, high = top * scale;
        yield return $"The way through is {clear:0} x {high:0} mm{(scale > 1 ? " real" : "")}.";
        if (scale > 1 && high < 1950) yield return "Lower than a real door's 1950 mm or so: people would stoop.";
    }
}
