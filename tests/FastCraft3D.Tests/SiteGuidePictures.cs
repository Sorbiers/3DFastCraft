using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Generators;
using FastCraft3D.Generators.Buildings;
using FastCraft3D.Geometry;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Makes the pictures for the website's Windows and Doors pages: each type, trim and shutter as the
/// generators build them, on a bit of wall, drawn by the same small software renderer the panels use.
///
/// Does nothing unless FASTCRAFT_SITE_IMG names the folder to write to (the site's web\img), which keeps
/// it out of an ordinary test run. To remake them: set it, and run the tests of this class.
/// </summary>
public class SiteGuidePictures
{
    private static string? Folder => Environment.GetEnvironmentVariable("FASTCRAFT_SITE_IMG") is { Length: > 0 } folder
        ? folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar
        : null;

    private const int Side = 560;

    private static readonly Vector3 Background = new(0.075f, 0.085f, 0.105f);
    private static readonly Vector3 Wall = new(0.60f, 0.54f, 0.47f);
    private static readonly Vector3 Paint = new(0.95f, 0.93f, 0.87f);
    private static readonly Vector3 Glass = new(0.52f, 0.74f, 0.92f);
    private static readonly Vector3 Green = new(0.22f, 0.40f, 0.31f);
    private static readonly Vector3 Red = new(0.52f, 0.15f, 0.13f);
    private static readonly Vector3 Blue = new(0.20f, 0.30f, 0.47f);

    private static void Save(byte[] rgb, int width, int height, string path)
    {
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, rgb, width * 3);
        picture.Freeze();

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static Bounds BoundsOf(IEnumerable<MeshShot.Piece> pieces)
    {
        var b = Bounds.Empty;
        foreach (var piece in pieces) b = b.Union(piece.Mesh.ComputeBounds());
        return b;
    }

    /// <summary>The pieces seen from the front and a little to one side and above, filling the picture.</summary>
    private static byte[] Shot(IReadOnlyList<MeshShot.Piece> pieces, int size = Side)
    {
        var b = BoundsOf(pieces);
        var target = b.Center;
        float distance = b.Diagonal / 2f * 1.1f / MathF.Sin(14f * MathF.PI / 180f);
        var eye = target + new Vector3(0.38f, -0.90f, 0.32f) * distance;

        return MeshShot.Render(pieces, eye, target, 28f, size, Background, supersample: 3);
    }

    /// <summary>Pictures side by side in one wide one.</summary>
    private static (byte[] Rgb, int Width) Row(IReadOnlyList<byte[]> shots, int size)
    {
        var all = new byte[shots.Count * size * size * 3];
        int width = shots.Count * size;

        for (int s = 0; s < shots.Count; s++)
            for (int y = 0; y < size; y++)
                Array.Copy(shots[s], y * size * 3, all, (y * width + s * size) * 3, size * 3);

        return (all, width);
    }

    /// <summary>A wall as thick as the frame is deep, with a square opening for it, round everything shown.</summary>
    private static MeshShot.Piece WallAround(Bounds shown, float depth, float openX0, float openX1, float openZ0, float openZ1)
    {
        float margin = 4f;
        var wall = Shapes.Box(shown.Min.X - margin, -depth, shown.Min.Z - 1.5f, shown.Max.X + margin, 0, shown.Max.Z + margin);
        var opening = Shapes.Box(openX0, -depth - 1, openZ0, openX1, 1, openZ1);

        return new MeshShot.Piece(Shapes.Subtract(wall, opening), Wall);
    }

    // --- Windows ---------------------------------------------------------------------------

    private static List<MeshShot.Piece> WindowScene(Window.Settings s, bool onWall, Vector3? shutters = null)
    {
        var made = new Window().Make(s with { Flat = false }, Printer.Default);
        Assert.False(made.IsRefused, made.Refusal);

        var pieces = made.Parts.Select(p => new MeshShot.Piece(p.Mesh, p.Role switch
        {
            "glass" => Glass,
            "shutter-left" or "shutter-right" => shutters ?? Green,
            _ => Paint
        })).ToList();

        if (onWall)
        {
            var frame = made.Parts.Single(p => p.Role == "frame").Mesh.ComputeBounds();
            float bottom = frame.Max.Z - s.Height;
            pieces.Insert(0, WallAround(BoundsOf(pieces), s.Depth, -s.Width / 2f, s.Width / 2f, bottom, frame.Max.Z));
        }

        return pieces;
    }

    [Fact]
    public void WindowPictures()
    {
        if (Folder is not { } folder) return;

        var d = new Window().Default;
        var hung = d with { Type = WindowType.SingleHung, Width = 12, Height = 20, LightsAcross = 2, LightsUp = 3 };

        var cards = new (string File, Window.Settings S, bool Wall, Vector3? Shutters)[]
        {
            ("win-fixed", d with { Width = 14, Height = 16 }, true, null),
            ("win-fixed-arched", d with { Shape = WindowShape.Arched, Width = 10, Height = 20, Columns = 2, Rows = 3 }, false, null),
            ("win-hung", d with { Type = WindowType.SingleHung, Width = 14, Height = 22, LightsAcross = 3, LightsUp = 3, Muntin = 0.25f }, true, null),
            ("win-sliding", d with { Type = WindowType.Sliding, Width = 22, Height = 14, LightsAcross = 2, LightsUp = 2 }, true, null),
            ("win-casement", d with { Type = WindowType.Casement, Width = 14, Height = 18, Casements = 2, LightsAcross = 1, LightsUp = 3 }, true, null),
            ("win-casement-three", d with { Type = WindowType.Casement, Width = 24, Height = 16, Casements = 3, LightsAcross = 1, LightsUp = 2 }, true, null),
            ("win-trim-plain", hung with { Trim = WindowTrim.Plain }, true, null),
            ("win-trim-lintel", hung with { Trim = WindowTrim.Lintel }, true, null),
            ("win-trim-pediment", hung with { Trim = WindowTrim.Pedimented }, true, null),
            ("win-shutters-louvred", hung with { Shutters = WindowShutters.Pair, Style = ShutterKind.Louvred }, true, Green),
            ("win-shutters-panelled", hung with { Shutters = WindowShutters.Pair, Style = ShutterKind.Panelled }, true, Blue),
            ("win-shutters-planked", hung with { Shutters = WindowShutters.Pair, Style = ShutterKind.Planked }, true, Red)
        };

        foreach (var (file, s, wall, shutters) in cards)
            Save(Shot(WindowScene(s, wall, shutters)), Side, Side, folder + file + ".jpg");

        // Three together, for the top of the page and for a link's preview.
        var hero = new[]
        {
            Shot(WindowScene(hung with { Trim = WindowTrim.Lintel, Shutters = WindowShutters.Pair, Style = ShutterKind.Louvred }, true), 500),
            Shot(WindowScene(d with { Type = WindowType.Sliding, Width = 22, Height = 14, Trim = WindowTrim.Plain }, true), 500),
            Shot(WindowScene(d with
            {
                Type = WindowType.Casement, Width = 14, Height = 20, Casements = 2, LightsAcross = 1, LightsUp = 3,
                Trim = WindowTrim.Pedimented, Shutters = WindowShutters.Pair, Style = ShutterKind.Panelled
            }, true, Blue), 500)
        };
        var (rgb, width) = Row(hero, 500);
        Save(rgb, width, 500, folder + "windows-types.jpg");

        // As it prints: lying on its back, the facade up, the glass on the plate, the trim and
        // the shutters beside it, each on its back.
        var flat = new Window().Make(
            hung with { Trim = WindowTrim.Lintel, Shutters = WindowShutters.Pair, Style = ShutterKind.Louvred, Flat = true }, Printer.Default);
        Assert.False(flat.IsRefused, flat.Refusal);

        var plate = flat.Parts.Select(p => new MeshShot.Piece(p.Mesh, p.Role switch
        {
            "glass" => Glass,
            "shutter-left" or "shutter-right" => Green,
            _ => Paint
        })).ToList();
        plate.Insert(0, new MeshShot.Piece(Shapes.Box(-22, -4, -0.6f, 56, 26, 0), new Vector3(0.20f, 0.22f, 0.25f)));

        var b = BoundsOf(plate);
        var target = b.Center;
        float distance = b.Diagonal / 2f * 1.05f / MathF.Sin(14f * MathF.PI / 180f);
        var eye = target + new Vector3(0f, -0.20f, 1f) * distance;
        const int Big = 1000;
        var full = MeshShot.Render(plate, eye, target, 28f, Big, Background, supersample: 2);

        // The middle band, which is where it all is.
        const int Band = 520, Top = (Big - Band) / 2;
        var cropped = new byte[Big * Band * 3];
        Array.Copy(full, Top * Big * 3, cropped, 0, cropped.Length);
        Save(cropped, Big, Band, folder + "windows-print.jpg");
    }

    // --- Doors -----------------------------------------------------------------------------

    private static List<MeshShot.Piece> DoorScene(Door.Settings s, Vector3 paint, bool onWall)
    {
        var made = new Door().Make(s with { Flat = false }, Printer.Default);
        Assert.False(made.IsRefused, made.Refusal);

        var pieces = made.Parts.Select(p => new MeshShot.Piece(p.Mesh, p.Role == "glass" ? Glass : paint)).ToList();

        if (onWall)
        {
            var door = BoundsOf(pieces);
            pieces.Insert(0, WallAround(door, s.Depth, door.Min.X, door.Max.X, door.Min.Z, door.Max.Z));
        }

        return pieces;
    }

    [Fact]
    public void DoorPictures()
    {
        if (Folder is not { } folder) return;

        var d = new Door().Default;
        var french = d with { Type = DoorType.French, Width = 18, Leaves = 2, Leaf = DoorLeaf.Glazed, Panels = 1, Glazed = 1, PaneColumns = 2, PaneRows = 4 };
        var dutch = d with { Type = DoorType.Dutch, Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1, Split = true, PaneColumns = 2, PaneRows = 3, SetBack = 0.8f };

        var cards = new (string File, Door.Settings S, Vector3 Paint, bool Wall)[]
        {
            ("door-single", d, Red, true),
            ("door-glazed", d with { Leaf = DoorLeaf.Glazed, Panels = 2, Glazed = 1, PaneColumns = 2, PaneRows = 2 }, Blue, true),
            ("door-double", d with { Type = DoorType.Double, Width = 18, Leaves = 2, Panels = 3 }, Green, true),
            ("door-french", french, Paint, true),
            ("door-dutch", dutch, Blue, true),
            ("door-arched", d with { Shape = DoorShape.Arched, Width = 16, Height = 30, Transom = 6.4f, Panels = 3 }, Red, false),
            ("door-entrance", d with
            {
                Width = 22, Height = 30, Sidelights = 2, SidelightWidth = 3.5f, Transom = 3.5f,
                Leaf = DoorLeaf.Glazed, Panels = 1, Glazed = 1, PaneColumns = 2, PaneRows = 4
            }, Paint, true)
        };

        foreach (var (file, s, paint, wall) in cards)
            Save(Shot(DoorScene(s, paint, wall)), Side, Side, folder + file + ".jpg");

        var hero = new[]
        {
            Shot(DoorScene(d, Red, true), 500),
            Shot(DoorScene(french, Paint, true), 500),
            Shot(DoorScene(dutch, Blue, true), 500)
        };
        var (rgb, width) = Row(hero, 500);
        Save(rgb, width, 500, folder + "doors-types.jpg");
    }

    // --- Fill up and Pivot -----------------------------------------------------------------

    private static MeshShot.Piece[] Cup(float at, bool filled, Mesh? fill)
    {
        // Cut away at the front, so what is inside can be seen.
        var cutaway = Shapes.Box(at, -40, -1, at + 30, 0, 40);

        var outer = MeshTransform.Transformed(Primitives.Prism(14f, 30f, 72), Matrix4x4.CreateTranslation(at, 0, 15));
        var inner = MeshTransform.Transformed(Primitives.Prism(11f, 30f, 72), Matrix4x4.CreateTranslation(at, 0, 18));
        var cup = Shapes.Subtract(Shapes.Subtract(outer, inner), cutaway);

        var pieces = new List<MeshShot.Piece> { new(cup, Paint) };
        if (filled && fill is not null)
            pieces.Add(new(Shapes.Subtract(MeshTransform.Transformed(fill, Matrix4x4.CreateTranslation(at, 0, 0)), cutaway), Glass));

        return [.. pieces];
    }

    [Fact]
    public void FillUpPicture()
    {
        if (Folder is not { } folder) return;

        var outer = Primitives.Prism(14f, 30f, 72);
        var inner = Primitives.Prism(11f, 30f, 72);
        var world = Shapes.Subtract(
            MeshTransform.Transformed(outer, Matrix4x4.CreateTranslation(0, 0, 15)),
            MeshTransform.Transformed(inner, Matrix4x4.CreateTranslation(0, 0, 18)));

        var analysis = CavityFill.Analyse(world, 160);
        var made = analysis.Apply(world, 21f, apart: true);
        Assert.NotNull(made);

        var empty = Cup(0, filled: false, null);
        var full = Cup(0, filled: true, made!.Value.Fill);

        // The same cup twice: as it is, and filled to 21 mm as a part of its own.
        var cupBounds = BoundsOf([.. Shifted(empty, -19f), .. Shifted(full, 19f)]);
        var target = cupBounds.Center;
        float distance = cupBounds.Diagonal / 2f * 1.0f / MathF.Sin(14f * MathF.PI / 180f);
        var eye = target + new Vector3(0.30f, -0.90f, 0.42f) * distance;

        const int Wide = 1000, Band = 560;
        var picture = MeshShot.Render([.. Shifted(empty, -19f), .. Shifted(full, 19f)], eye, target, 28f, Wide, Background, supersample: 2);
        var cropped = new byte[Wide * Band * 3];
        Array.Copy(picture, ((Wide - Band) / 2) * Wide * 3, cropped, 0, cropped.Length);
        Save(cropped, Wide, Band, folder + "fill-up.jpg");
    }

    private static IEnumerable<MeshShot.Piece> Shifted(IEnumerable<MeshShot.Piece> pieces, float x) =>
        pieces.Select(p => new MeshShot.Piece(MeshTransform.Transformed(p.Mesh, Matrix4x4.CreateTranslation(x, 0, 0)), p.Colour));

    [Fact]
    public void PivotPicture()
    {
        if (Folder is not { } folder) return;

        // The same lid, seen from above, turned 40 degrees: about its own middle, and about a corner.
        var lid = MeshTransform.Transformed(Primitives.Box(44f, 26f, 4f), Matrix4x4.CreateTranslation(0, 0, 2));
        var peg = MeshTransform.Transformed(Primitives.Prism(1.5f, 9f, 24), Matrix4x4.CreateTranslation(0, 0, 4.5f));

        MeshShot.Piece[] Scene(float pivotX, float pivotY)
        {
            var about = Matrix4x4.CreateTranslation(-pivotX, -pivotY, 0) * Matrix4x4.CreateRotationZ(40f * MathF.PI / 180f)
                        * Matrix4x4.CreateTranslation(pivotX, pivotY, 1.0f);

            return
            [
                new(lid, new Vector3(0.42f, 0.45f, 0.49f)),
                new(MeshTransform.Transformed(lid, about), new Vector3(0.93f, 0.62f, 0.20f)),
                new(MeshTransform.Transformed(peg, Matrix4x4.CreateTranslation(pivotX, pivotY, 0)), new Vector3(0.95f, 0.18f, 0.16f))
            ];
        }

        byte[] FromAbove(MeshShot.Piece[] pieces)
        {
            var b = BoundsOf(pieces);
            var target = b.Center;
            float distance = 40f * 1.08f / MathF.Sin(14f * MathF.PI / 180f);
            var eye = target + new Vector3(0.0f, -0.18f, 1f) * distance;
            return MeshShot.Render(pieces, eye, target, 28f, 560, Background, supersample: 3);
        }

        var shots = new[] { FromAbove(Scene(0, 0)), FromAbove(Scene(-22f, -13f)) };
        var (rgb, width) = Row(shots, 560);
        Save(rgb, width, 560, folder + "pivot-compare.jpg");
    }
}
