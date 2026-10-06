using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Buildings;

/// <summary>
/// The windows with sashes - single hung, sliding, casement - and the trim and shutters any square
/// window can have.
///
/// Everything is built as a window lying on its back, as Lay flat prints it: X across, Y up it, Z
/// from the back, which is on the plate, to the facade. Every bar stands up from the plate to its
/// own level and nothing is built over air, so the facade can face up with no support. The sashes
/// are different levels: the one behind stands back of the one in front.
/// </summary>
public sealed partial class Window
{
    /// <summary>A sash's own rectangle, and how high its bars stand from the back.</summary>
    internal readonly record struct SashFrame(float X0, float Y0, float X1, float Y1, float Level);

    /// <summary>A little over where two bars meet, so they join rather than only touch.</summary>
    private const float Over = 2 * Glazing.Hair;

    /// <summary>
    /// A bar with its corners on a thousandth of a millimetre. Two bars that should meet face to
    /// face, the meeting rails of two sashes say, were worked out by different sums and came out a
    /// rounding apart: not the same plane and not quite another, which the union turned into edges
    /// that four faces meet at. Snapped, equal means equal.
    /// </summary>
    private static Mesh Bar(float x0, float y0, float z0, float x1, float y1, float z1) =>
        Shapes.Box(Snap(x0), Snap(y0), Snap(z0), Snap(x1), Snap(y1), Snap(z1));

    private static float Snap(float v) => MathF.Round(v * 1000f) / 1000f;

    /// <summary>The opening in the frame the sashes fill: X from left to right, Y up from the bottom rail.</summary>
    private static (float X0, float Y0, float X1, float Y1) OpeningOf(Settings s) =>
        (-s.Width / 2f + s.Frame, s.Frame, s.Width / 2f - s.Frame, s.Height - s.Frame);

    /// <summary>The sashes of a window that has them, the one in front last.</summary>
    internal static List<SashFrame> Sashes(Settings s)
    {
        var (x0, y0, x1, y1) = OpeningOf(s);
        float front = s.Depth - s.SetBack, back = front - s.Step;
        float a = Snap(s.Sash);
        var sashes = new List<SashFrame>();

        switch (s.Type)
        {
            case WindowType.SingleHung:
            {
                // Each reaches past the middle by a rail's width, so the two meet rail over rail.
                // Both are built from the one coordinate, so the meeting rails are the same to the last digit.
                float meet = Snap(((y0 + y1) + a) / 2f);
                var lower = new SashFrame(x0, y0, x1, meet, s.UpperFront ? back : front);
                var upper = new SashFrame(x0, meet - a, x1, y1, s.UpperFront ? front : back);
                sashes.AddRange(s.UpperFront ? [lower, upper] : [upper, lower]);
                break;
            }

            case WindowType.Sliding:
            {
                float meet = Snap(((x0 + x1) + a) / 2f);
                var left = new SashFrame(x0, y0, meet, y1, s.LeftFront ? front : back);
                var right = new SashFrame(meet - a, y0, x1, y1, s.LeftFront ? back : front);
                sashes.AddRange(s.LeftFront ? [right, left] : [left, right]);
                break;
            }

            default:
            {
                float leaf = ((x1 - x0) - (s.Casements - 1) * s.Mullion) / s.Casements;
                for (int i = 0; i < s.Casements; i++)
                {
                    float x = x0 + i * (leaf + s.Mullion);
                    sashes.Add(new SashFrame(x, y0, x + leaf, y1, front));
                }

                break;
            }
        }

        return sashes;
    }

    /// <summary>The panes of one sash, between its frame and its muntins: X from left to right, Y from the bottom up.</summary>
    internal static IEnumerable<(float X0, float Y0, float X1, float Y1)> Lights(Settings s, SashFrame k)
    {
        float a = Snap(s.Sash);
        float x0 = k.X0 + a, x1 = k.X1 - a, y0 = k.Y0 + a, y1 = k.Y1 - a;
        float wide = (x1 - x0 - (s.LightsAcross - 1) * s.Muntin) / s.LightsAcross;
        float tall = (y1 - y0 - (s.LightsUp - 1) * s.Muntin) / s.LightsUp;

        for (int c = 0; c < s.LightsAcross; c++)
            for (int r = 0; r < s.LightsUp; r++)
            {
                float x = x0 + c * (wide + s.Muntin), y = y0 + r * (tall + s.Muntin);
                yield return (x, y, x + wide, y + tall);
            }
    }

    /// <summary>
    /// One sash: a slab as big as the sash with its lights cut out, standing from
    /// <paramref name="low"/> to its level. The muntins are what is left between the cuts. Built as
    /// bars laid across each other, the same height all over, they came back with an open edge
    /// at every crossing - the reason the fixed window's panes are cut out too.
    ///
    /// Against the frame or a mullion it reaches a little in, so it joins. Against the other sash it
    /// stops a little short of its edge, inside the other's own stile: where the two sashes meet,
    /// no face of one then lies in the same plane as a face of the other.
    /// </summary>
    private static Mesh SashSolid(Settings s, SashFrame k, float low)
    {
        var (ox0, oy0, ox1, oy1) = OpeningOf(s);
        const float Near = 1e-3f;
        bool slid = s.Type == WindowType.Sliding, hung = s.Type == WindowType.SingleHung;

        float x0 = k.X0 + (slid && k.X0 > ox0 + Near ? Over : -Over);
        float x1 = k.X1 + (slid && k.X1 < ox1 - Near ? -Over : Over);
        float y0 = k.Y0 + (hung && k.Y0 > oy0 + Near ? Over : -Over);
        float y1 = k.Y1 + (hung && k.Y1 < oy1 - Near ? -Over : Over);

        var holes = Lights(s, k).Select(l => Bar(l.X0, l.Y0, -1f, l.X1, l.Y1, k.Level + 1f)).ToList();
        return Shapes.Subtract(Bar(x0, y0, low, x1, y1, k.Level), holes);
    }

    private static Generated BuildSashed(Settings s, Printer printer)
    {
        float w = s.Width / 2f;

        // On a sheet of glass the frame stands a hair clear of it, as the fixed window's does.
        bool sheet = s.Glass && s.GlassAs == DoorGlass.Sheet;
        float low = sheet ? s.GlassThickness + Glazing.Hair : 0f;

        var bars = new List<Mesh>
        {
            Bar(-w, 0, low, -w + s.Frame, s.Height, s.Depth),
            Bar(w - s.Frame, 0, low, w, s.Height, s.Depth),
            Bar(-w, 0, low, w, s.Frame, s.Depth),
            Bar(-w, s.Height - s.Frame, low, w, s.Height, s.Depth)
        };

        var sashes = Sashes(s);
        var (ox0, oy0, ox1, oy1) = OpeningOf(s);

        // The bars of the frame between one casement and the next stand as high as the frame.
        if (s.Type == WindowType.Casement)
            for (int i = 1; i < s.Casements; i++)
            {
                float x = sashes[i].X0 - s.Mullion;
                bars.Add(Bar(x, oy0 - Over, low, x + s.Mullion, oy1 + Over, s.Depth));
            }

        foreach (var sash in sashes) bars.Add(SashSolid(s, sash, low));

        if (s.Sill)
        {
            // A little wider than the frame and standing out in front of it, under the bottom rail.
            float over = s.Frame / 2f;
            bars.Add(Bar(-w - over, -s.Frame, 0, w + over, 0.01f, s.Depth + s.SillOut));
        }

        var parts = new List<GeneratedPart> { new("Window frame", Shapes.Union(bars), Role: "frame") };
        var notes = new List<string>();

        if (s.Glass)
        {
            var lights = sashes.SelectMany(k => Lights(s, k)).Select(Glazing.Rect).ToList();
            var whole = Glazing.Rect((-w, s.Sill ? 0.01f + Glazing.Hair : 0f, w, s.Height));

            parts.Add(Glazing.Panes(sheet ? [whole] : lights, s.GlassThickness));
            notes.AddRange(Glazing.Notes(s.GlassThickness, printer, s.Flat));
        }

        parts.AddRange(Surround(s, notes));
        notes.Add(Glazing.Printing(s.Flat));
        return Glazing.Assembled(Glazing.Stood(parts, s.Flat), notes);
    }

    private static IEnumerable<string> CheckSashed(Settings s)
    {
        var (x0, y0, x1, y1) = OpeningOf(s);
        if (x1 - x0 < 1f || y1 - y0 < 1f)
        {
            yield return "The frame leaves no opening.";
            yield break;
        }

        float front = s.Depth - s.SetBack, back = front - s.Step;
        float lowest = s.Type == WindowType.Casement ? front : back;
        if (lowest < 0.3f || s.Glass && lowest < s.GlassThickness + 0.2f)
            yield return "The sashes stand too shallow for the glass: more depth, less set back or step, or thinner glass.";

        if (s.Type == WindowType.Casement && s.Casements > 1 && x1 - x0 - (s.Casements - 1) * s.Mullion < s.Casements * (2 * s.Sash + 1f))
            yield return "Too many casements for the width: fewer, narrower mullions, or a wider window.";

        if (Sashes(s) is { Count: > 0 } sashes
            && sashes.SelectMany(k => Lights(s, k)).Any(p => p.X1 - p.X0 < 0.5f || p.Y1 - p.Y0 < 0.5f))
            yield return "The sashes and muntins leave lights too small to print. Fewer lights, thinner muntins, or a bigger window.";

        if (s.Glass && s.GlassAs == DoorGlass.Sheet && s.GlassThickness > s.Depth - 0.2f)
            yield return $"The glass sheet leaves the frame nothing to stand on: it is {s.Depth:0.##} mm deep.";

        foreach (var problem in CheckSurround(s)) yield return problem;
    }

    private static IEnumerable<string> DescribeSashed(Settings s, float modelScale)
    {
        if (Glazing.Real(s.Width, s.Height, modelScale, "window") is { } real) yield return real;

        var sashes = Sashes(s);
        string what = s.Type switch
        {
            WindowType.SingleHung => "sashes, one above the other",
            WindowType.Sliding => "sashes, side by side",
            _ => s.Casements == 1 ? "casement" : "casements"
        };

        var light = Lights(s, sashes[0]).First();
        float scale = modelScale > 1.5f ? modelScale : 1f;
        yield return $"{sashes.Count} {what}, {sashes.Count * s.LightsAcross * s.LightsUp} lights of "
                     + $"{(light.X1 - light.X0) * scale:0.#} x {(light.Y1 - light.Y0) * scale:0.#} mm{(scale > 1 ? " real" : "")}.";
    }

    // --- Trim and shutters -----------------------------------------------------------------

    /// <summary>Whether the trim and the shutters have a square outline to follow.</summary>
    private static bool Squared(Settings s) => s.Type != WindowType.Fixed || s.Shape == WindowShape.Rectangular;

    /// <summary>How far the shutters are from the frame: its trim, if it has any, and the gap.</summary>
    private static float ShutterReach(Settings s) => (s.Trim != WindowTrim.None ? s.TrimWidth : 0f) + s.ShutterGap;

    private static float ShutterSpan(Settings s) => s.ShutterWidth > 0f ? s.ShutterWidth : s.Width / 2f;

    private static IEnumerable<string> CheckSurround(Settings s)
    {
        if (!Squared(s) || s.Shutters == WindowShutters.None) yield break;

        float width = ShutterSpan(s);
        float rail = MathF.Max(0.5f, MathF.Min(width * 0.16f, 1.6f));
        if (width < 1.5f) yield return "The shutters are too narrow: at least 1.5 mm wide.";
        else if (s.Style == ShutterKind.Louvred && (s.Height - 2 * rail) / s.Slats < 0.5f) yield return "Too many slats for the height: fewer, or taller shutters.";
        else if (s.Style == ShutterKind.Panelled && (s.Height - (s.ShutterPanels + 1) * rail) / s.ShutterPanels < 1f) yield return "Too many panels for the height: fewer.";
        else if (s.Style == ShutterKind.Planked && (width - (s.Boards - 1) * 0.25f) / s.Boards < 0.6f) yield return "Too many boards for the width: fewer, or wider shutters.";
    }

    private static IEnumerable<string> DescribeSurround(Settings s, float modelScale)
    {
        if (!Squared(s) || s.Trim == WindowTrim.None && s.Shutters == WindowShutters.None) yield break;

        float side = s.Shutters == WindowShutters.None ? s.Trim != WindowTrim.None ? s.TrimWidth : 0f : ShutterReach(s) + ShutterSpan(s);
        float left = s.Shutters is WindowShutters.Pair or WindowShutters.Left ? side : s.Trim != WindowTrim.None ? s.TrimWidth : 0f;
        float right = s.Shutters is WindowShutters.Pair or WindowShutters.Right ? side : s.Trim != WindowTrim.None ? s.TrimWidth : 0f;

        float scale = modelScale > 1.5f ? modelScale : 1f;
        yield return $"With its trim and shutters it is {(s.Width + left + right) * scale:0.#} mm wide{(scale > 1 ? " real" : "")}.";
    }

    /// <summary>
    /// The trim and the shutters, each a part of its own, in the pose the window is in: put on the
    /// wall in front of the frame when it stands, and when it is printed flat laid on its back in a
    /// row beside the window - they have nothing under them on the wall to print on, as the window
    /// has the plate.
    /// </summary>
    private static List<GeneratedPart> Surround(Settings s, List<string> notes)
    {
        var made = new List<GeneratedPart>();
        if (!Squared(s)) return made;

        var pieces = new List<(string Name, string Role, Mesh Mesh)>();
        float w = s.Width / 2f;

        if (s.Trim != WindowTrim.None) pieces.Add(("Architrave", "trim", TrimMesh(s)));

        if (s.Shutters != WindowShutters.None)
        {
            float reach = ShutterReach(s), span = ShutterSpan(s);
            if (s.Shutters is WindowShutters.Pair or WindowShutters.Left)
                pieces.Add(("Left shutter", "shutter-left", ShutterMesh(s, -w - reach - span, -w - reach)));
            if (s.Shutters is WindowShutters.Pair or WindowShutters.Right)
                pieces.Add(("Right shutter", "shutter-right", ShutterMesh(s, w + reach, w + reach + span)));
        }

        if (pieces.Count == 0) return made;

        float cursor = w + (s.Sill ? s.Frame / 2f : 0f) + 4f;
        foreach (var (name, role, mesh) in pieces)
        {
            Mesh placed;
            if (s.Flat)
            {
                var b = mesh.ComputeBounds();
                placed = Shapes.Moved(mesh, cursor - b.Min.X, 0, 0);
                cursor += b.Size.X + 3f;
            }
            else
            {
                // Against the wall, which is the face of the frame.
                placed = Shapes.Moved(mesh, 0, 0, s.Depth);
            }

            made.Add(new GeneratedPart(name, placed, Role: role));
        }

        notes.Add(s.Flat
            ? "The trim and the shutters lie beside the window, each on its back, the face up: glue them to the wall round it."
            : "The trim and the shutters are shown on the wall. Tick Lay flat to print them, each on its back.");

        return made;
    }

    /// <summary>
    /// The architrave on its back, as it prints: the trim round the frame's sides and head, and for
    /// a lintel or a pediment the cap on top, a little taller. Standing from the plate to its own
    /// height all over, so it prints with nothing under it.
    /// </summary>
    private static Mesh TrimMesh(Settings s)
    {
        float w = s.Width / 2f, h = s.Height, tw = s.TrimWidth, tp = s.TrimOut;
        float low = 0.01f; // above the sill

        var pieces = new List<Mesh>
        {
            Shapes.Box(-w - tw, low, 0, -w, h + (s.Trim == WindowTrim.Lintel ? 0f : tw), tp),
            Shapes.Box(w, low, 0, w + tw, h + (s.Trim == WindowTrim.Lintel ? 0f : tw), tp)
        };

        switch (s.Trim)
        {
            case WindowTrim.Lintel:
            {
                // Wider than the sides under it, and standing out further.
                float over = tw * 0.6f;
                pieces.Add(Shapes.Box(-w - tw - over, h, 0, w + tw + over, h + tw * 1.4f, tp * 1.5f));
                break;
            }

            case WindowTrim.Pedimented:
            {
                pieces.Add(Shapes.Box(-w - tw, h, 0, w + tw, h + tw, tp));

                float half = w + tw + tw * 0.4f, base_ = h + tw;
                var gable = new List<Vector2> { new(-half, base_), new(half, base_), new(0, base_ + half * 0.45f) };
                pieces.Add(Shapes.Prism(gable, 0, tp * 1.5f));
                break;
            }

            default:
                pieces.Add(Shapes.Box(-w - tw, h, 0, w + tw, h + tw, tp));
                break;
        }

        return Shapes.Union(pieces);
    }

    /// <summary>
    /// One false shutter on its back, as it prints, over X from <paramref name="x0"/> to
    /// <paramref name="x1"/> and the height of the window. A slab with what stands out of it built
    /// up from the slab, so nothing hangs over air: the louvres are wedges whose underside is the
    /// slab, sloping down toward the front as a louvre does.
    /// </summary>
    private static Mesh ShutterMesh(Settings s, float x0, float x1)
    {
        float t = s.ShutterThickness, h = s.Height, width = x1 - x0;
        float slab = MathF.Max(0.3f, t * 0.35f);
        float rail = MathF.Max(0.5f, MathF.Min(width * 0.16f, 1.6f));

        var pieces = new List<Mesh>();

        switch (s.Style)
        {
            case ShutterKind.Planked:
            {
                // Boards with a groove between each, and two battens across them standing higher.
                float groove = 0.25f, ledge = MathF.Min(0.3f, t * 0.3f);
                float board = (width - (s.Boards - 1) * groove) / s.Boards;

                pieces.Add(Shapes.Box(x0, 0, 0, x1, h, slab));
                for (int i = 0; i < s.Boards; i++)
                {
                    float x = x0 + i * (board + groove);
                    pieces.Add(Shapes.Box(x, 0, 0, x + board, h, t - ledge));
                }

                float batten = MathF.Max(0.6f, width * 0.14f);
                pieces.Add(Shapes.Box(x0, h * 0.12f, 0, x1, h * 0.12f + batten, t));
                pieces.Add(Shapes.Box(x0, h * 0.88f - batten, 0, x1, h * 0.88f, t));
                break;
            }

            default:
            {
                pieces.Add(Shapes.Box(x0, 0, 0, x1, h, slab));
                pieces.Add(Shapes.Box(x0, 0, 0, x0 + rail, h, t));
                pieces.Add(Shapes.Box(x1 - rail, 0, 0, x1, h, t));
                pieces.Add(Shapes.Box(x0, 0, 0, x1, rail, t));
                pieces.Add(Shapes.Box(x0, h - rail, 0, x1, h, t));

                float inside = h - 2 * rail;
                if (s.Style == ShutterKind.Panelled)
                {
                    // Panels sunk into the slab, a rail between one and the next.
                    float band = (inside - (s.ShutterPanels - 1) * rail) / s.ShutterPanels;
                    for (int i = 1; i < s.ShutterPanels; i++)
                    {
                        float y = rail + i * band + (i - 1) * rail;
                        pieces.Add(Shapes.Box(x0, y, 0, x1, y + rail, t));
                    }
                }
                else
                {
                    float pitch = inside / s.Slats;
                    for (int i = 0; i < s.Slats; i++)
                    {
                        float y = rail + i * pitch;
                        pieces.Add(Wedge(y, y + pitch * 0.9f, slab, t, x0 + rail - Over, x1 - rail + Over));
                    }
                }

                break;
            }
        }

        return Shapes.Union(pieces);
    }

    /// <summary>
    /// A louvre: a wedge standing on the slab, its top sloping from the slab at <paramref name="yHigh"/>
    /// up to <paramref name="zTop"/> at <paramref name="yLow"/>, so its outer edge is its lower. Drawn
    /// as a triangle in Y and Z and stood along X.
    /// </summary>
    private static Mesh Wedge(float yLow, float yHigh, float zBase, float zTop, float x0, float x1)
    {
        // A little into the slab, so it joins it rather than only touching.
        float sunk = zBase - 0.05f;
        var triangle = new List<Vector2> { new(yLow, sunk), new(yHigh, sunk), new(yLow, zTop) };
        var along = new Matrix4x4(0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 1);

        return MeshTransform.Transformed(Shapes.Prism(triangle, x0, x1), along);
    }
}
