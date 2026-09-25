using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Buildings;

/// <summary>What the window and the door share: the glass, and how their sizes read at a model's scale.</summary>
internal static class Glazing
{
    /// <summary>Pale blue, so the glass reads as glass on the plate.</summary>
    public static readonly Vector3 Colour = new(0.72f, 0.86f, 0.96f);

    /// <summary>The second filament: the clear one, beside whatever the frame is printed in.</summary>
    public const int Filament = 2;

    /// <summary>
    /// The panes as one part, each exactly filling its opening, lying on the plate at the back of
    /// the frame. In the openings rather than under the whole frame, so the frame stands on the
    /// plate as well and nothing prints in the air.
    /// </summary>
    public static GeneratedPart Panes(IEnumerable<(float X0, float Y0, float X1, float Y1)> openings, float thickness) =>
        new("Glass", Shapes.Union(openings.Select(o => Shapes.Box(o.X0, o.Y0, 0, o.X1, o.Y1, thickness)).ToList()), Role: "glass")
        {
            Filament = Filament,
            Colour = Colour
        };

    public static List<string> Notes(float thickness, Printer printer)
    {
        var notes = new List<string>
        {
            $"The glass is its own part on filament {Filament}: print it in a clear filament, "
            + $"or pause after its {Math.Max(1, (int)MathF.Round(thickness / printer.Layer))} layers to change to one."
        };

        if (thickness < 2 * printer.Layer)
            notes.Add($"Under two layers of {printer.Layer:0.##} mm, the glass may not print whole.");

        return notes;
    }

    /// <summary>"At 1:87, 1218 x 1392 mm" - or nothing, for a model drawn full size.</summary>
    public static string? Real(float width, float height, float modelScale, string what) =>
        modelScale > 1.5f ? $"At 1:{modelScale:0}, a {what} {width * modelScale:0} x {height * modelScale:0} mm." : null;
}

/// <summary>
/// A window for a model house: a frame, glazing bars dividing it into panes, a sill, and glass as
/// a thin part of its own in the openings, to print in clear filament. Printed lying on its face's
/// back, the glass on the plate; sizes as the model is drawn, with the real ones said beside them.
/// </summary>
public sealed class Window : Generator<Window.Settings>
{
    public override string Id => "building.window";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Window";
    public override string Summary => "A window frame with glazing bars and a sill, and glass to print in clear filament.";

    public sealed record Settings(
        [Length("Width", 3, 200, Group = "Size", Hint = "Outside the frame, as the model is drawn")] float Width = 14f,
        [Length("Height", 3, 200, Group = "Size")] float Height = 16f,
        [Length("Frame", 0.4, 10, Group = "Frame", Hint = "How wide the frame is, seen from the front")] float Frame = 0.8f,
        [Length("Depth", 0.4, 20, Group = "Frame", Hint = "How deep the frame is, into the wall")] float Depth = 1.6f,
        [Count("Panes across", 1, 8, Group = "Panes")] int Columns = 2,
        [Count("Panes up", 1, 8, Group = "Panes")] int Rows = 2,
        [Length("Glazing bars", 0.2, 5, Group = "Panes", Hint = "How wide the bars between the panes are")] float Bar = 0.4f,
        [Toggle("Sill", Group = "Sill")] bool Sill = true,
        [Length("Sill projection", 0, 10, Group = "Sill", Hint = "How far the sill stands out in front of the frame"), ShowWhen(nameof(Sill), true)] float SillOut = 0.8f,
        [Toggle("Glass", Group = "Glass", Hint = "Off for the frame alone")] bool Glass = true,
        [Length("Glass thickness", 0.1, 2, Group = "Glass", Hint = "Two or three layers"), ShowWhen(nameof(Glass), true)] float GlassThickness = 0.4f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Two by two sash", Default),
        ("Casement, one by two", Default with { Width = 9, Columns = 1, Rows = 2 }),
        ("Picture window", Default with { Width = 22, Height = 14, Columns = 1, Rows = 1 }),
        ("Georgian, three by four", Default with { Width = 14, Height = 20, Columns = 3, Rows = 4, Bar = 0.3f })
    ];

    /// <summary>Each pane's opening: X from left to right, Y from the bottom up.</summary>
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

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        var pane = Panes(s)[0];
        if (pane.X1 - pane.X0 < 0.5f || pane.Y1 - pane.Y0 < 0.5f)
            yield return "The frame and bars leave panes under half a millimetre. Fewer panes, thinner bars, or a bigger window.";

        if (s.Glass && s.GlassThickness >= s.Depth)
            yield return "The glass is as thick as the frame is deep.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        // The panes cut out of a block rather than laid as holes in one outline: laid as holes,
        // a grid of them came back open wherever their corners lined up, the triangulation being
        // written for lettering, whose holes never do.
        var panes = Panes(s);
        var frame = Shapes.Subtract(Shapes.Box(-s.Width / 2f, 0, 0, s.Width / 2f, s.Height, s.Depth),
            panes.Select(p => Shapes.Box(p.X0, p.Y0, -1, p.X1, p.Y1, s.Depth + 1)).ToList());

        if (s.Sill)
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
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer));
        }

        notes.Add("Printed lying on its back, the face up. Stand it in its opening once printed.");
        return new Generated(parts, notes);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        if (Glazing.Real(s.Width, s.Height, modelScale, "window") is { } real) yield return real;

        var pane = Panes(s)[0];
        float scale = modelScale > 1.5f ? modelScale : 1f;
        yield return $"{s.Columns * s.Rows} panes of {(pane.X1 - pane.X0) * scale:0.#} x {(pane.Y1 - pane.Y0) * scale:0.#} mm{(scale > 1 ? " real" : "")}.";
    }
}

public enum DoorLeaf
{
    Plain,
    Panelled,
    [ShownAs("Glazed at the top")] Glazed
}

/// <summary>
/// A door for a model house: a frame round the top and sides, the leaf in it, plain or with
/// panels sunk in its face, and glass in its top panels if wanted. One part with the glass apart,
/// printed lying on its back as the window is.
/// </summary>
public sealed class Door : Generator<Door.Settings>
{
    public override string Id => "building.door";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Door";
    public override string Summary => "A door in its frame, plain, panelled or glazed, with glass to print in clear filament.";

    public sealed record Settings(
        [Length("Width", 3, 200, Group = "Size", Hint = "Outside the frame, as the model is drawn")] float Width = 11f,
        [Length("Height", 5, 250, Group = "Size", Hint = "From the floor to the top of the frame")] float Height = 25f,
        [Length("Frame", 0.4, 10, Group = "Frame", Hint = "How wide the frame is, seen from the front")] float Frame = 0.8f,
        [Length("Depth", 0.6, 20, Group = "Frame", Hint = "How deep the frame is, into the wall")] float Depth = 1.6f,
        [Length("Leaf set back", 0, 5, Group = "Frame", Hint = "How far the door itself sits back from the frame's face")] float SetBack = 0.4f,
        [Choice("Leaf", Group = "Leaf")] DoorLeaf Leaf = DoorLeaf.Panelled,
        [Count("Panels", 1, 6, Group = "Leaf", Hint = "One above another"), ShowWhen(nameof(Leaf), DoorLeaf.Panelled, DoorLeaf.Glazed)] int Panels = 4,
        [Count("Glazed panels", 1, 6, Group = "Leaf", Hint = "Counted from the top"), ShowWhen(nameof(Leaf), DoorLeaf.Glazed)] int Glazed = 1,
        [Length("Panel recess", 0.1, 2, Group = "Leaf", Hint = "How deep the panels are sunk into the leaf's face"), ShowWhen(nameof(Leaf), DoorLeaf.Panelled, DoorLeaf.Glazed)] float Recess = 0.3f,
        [Length("Glass thickness", 0.1, 2, Group = "Leaf", Hint = "Two or three layers"), ShowWhen(nameof(Leaf), DoorLeaf.Glazed)] float GlassThickness = 0.4f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Plain", Default with { Leaf = DoorLeaf.Plain }),
        ("Four panels", Default),
        ("Half glazed", Default with { Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1 }),
        ("Double width, glazed", Default with { Width = 18, Leaf = DoorLeaf.Glazed, Panels = 3, Glazed = 2 })
    ];

    /// <summary>The leaf's panels, top first: X from left to right, Y from the floor up.</summary>
    internal static List<(float X0, float Y0, float X1, float Y1)> Panels(Settings s)
    {
        float leafLeft = -s.Width / 2f + s.Frame, leafRight = s.Width / 2f - s.Frame, leafTop = s.Height - s.Frame;
        float leafWidth = leafRight - leafLeft;

        // Stiles and rails a little wider than the frame, as a real door's are.
        float stile = MathF.Max(s.Frame, 0.14f * leafWidth);
        float tall = (leafTop - (s.Panels + 1) * stile) / s.Panels;

        return Enumerable.Range(0, s.Panels)
            .Select(i => (leafLeft + stile, leafTop - stile - (i + 1) * tall - i * stile, leafRight - stile, leafTop - stile - i * tall - i * stile))
            .ToList();
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (s.Width - 2 * s.Frame < 1.5f) yield return "The frame leaves no room for the door.";
        if (s.SetBack >= s.Depth - 0.3f) yield return "The leaf is set back as far as the frame is deep.";

        if (s.Leaf != DoorLeaf.Plain)
        {
            var panel = Panels(s)[0];
            if (panel.X1 - panel.X0 < 0.5f || panel.Y1 - panel.Y0 < 0.5f)
                yield return "The panels would be under half a millimetre. Fewer of them, or a bigger door.";
            if (s.Recess >= s.Depth - s.SetBack - 0.2f) yield return "The panels are sunk through the leaf.";
        }

        if (s.Leaf == DoorLeaf.Glazed && s.Glazed > s.Panels)
            yield return $"Only {s.Panels} panels to glaze.";

        if (s.Leaf == DoorLeaf.Glazed && s.GlassThickness >= s.Depth - s.SetBack)
            yield return "The glass is as thick as the leaf.";
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float w = s.Width / 2f, f = s.Frame, h = s.Height;

        // The frame round the top and both sides, open at the floor.
        var frame = Shapes.Prism(
        [
            new(-w, 0), new(-w + f, 0), new(-w + f, h - f), new(w - f, h - f), new(w - f, 0), new(w, 0), new(w, h), new(-w, h)
        ], 0, s.Depth);

        float leafDepth = s.Depth - s.SetBack;
        var leaf = Shapes.Box(-w + f, 0, 0, w - f, h - f, leafDepth);

        var panels = s.Leaf == DoorLeaf.Plain ? [] : Panels(s);
        var glazed = s.Leaf == DoorLeaf.Glazed ? panels.Take(s.Glazed).ToList() : [];

        var cuts = new List<Mesh>();
        foreach (var p in panels.Skip(glazed.Count))
            cuts.Add(Shapes.Box(p.X0, p.Y0, leafDepth - s.Recess, p.X1, p.Y1, leafDepth + 1));
        foreach (var p in glazed)
            cuts.Add(Shapes.Box(p.X0, p.Y0, -1, p.X1, p.Y1, leafDepth + 1));

        token.ThrowIfCancellationRequested();
        var door = Shapes.Subtract(Shapes.Union(frame, leaf), cuts);

        var parts = new List<GeneratedPart> { new("Door", door, Role: "door") };
        var notes = new List<string>();
        if (glazed.Count > 0)
        {
            parts.Add(Glazing.Panes(glazed, s.GlassThickness));
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer));
        }

        notes.Add("Printed lying on its back, the face up. Stand it in its opening once printed.");
        return new Generated(parts, notes);
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        if (Glazing.Real(s.Width, s.Height, modelScale, "door") is { } real) yield return real;

        float scale = modelScale > 1.5f ? modelScale : 1f;
        float clear = (s.Width - 2 * s.Frame) * scale, high = (s.Height - s.Frame) * scale;
        yield return $"The way through is {clear:0} x {high:0} mm{(scale > 1 ? " real" : "")}.";
        if (scale > 1 && high < 1950) yield return "Lower than a real door's 1950 mm or so: people would stoop.";
    }
}
