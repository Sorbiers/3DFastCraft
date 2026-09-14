using System.IO;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Io;

/// <param name="Width">Across the whole drawing, in millimetres; the height follows from its proportions.</param>
/// <param name="Thickness">How thick the solid is, standing up off the plate.</param>
/// <param name="Separate">One object per filled shape rather than one for the whole drawing.</param>
public readonly record struct SvgImportOptions(float Width, float Thickness, bool Separate)
{
    /// <summary>A size to start from: a drawing has no size of its own worth trusting.</summary>
    public static SvgImportOptions Default => new(50f, 2f, false);
}

/// <summary>
/// An SVG drawing brought in as a solid: its filled shapes, holes and all, made as thick as asked
/// and lying flat on the plate.
///
/// The outlines come from the same reader Emboss stamps drawings with, so a drawing that
/// embosses imports, and the same things are left out: strokes, text not turned to paths, images.
///
/// Sized by width rather than by the file's own units. Drawing programs write width and height in
/// px, pt, mm or nothing at all, and a px means whatever the program that wrote it thought it
/// meant; a width typed in millimetres means one thing.
/// </summary>
public static class SvgImport
{
    public const float MinimumSize = 0.5f;
    public const float MinimumThickness = 0.1f;

    /// <summary>How wide the drawing is for each millimetre it is tall. Zero for a drawing with nothing filled in it.</summary>
    public static float Aspect(IReadOnlyList<TextShape> shapesOneTall)
    {
        if (shapesOneTall.Count == 0) return 0f;

        float min = float.MaxValue, max = float.MinValue;
        foreach (var shape in shapesOneTall)
            foreach (var p in shape.Outline)
            {
                min = MathF.Min(min, p.X);
                max = MathF.Max(max, p.X);
            }

        return MathF.Max(0f, max - min);
    }

    /// <summary>The drawing's shapes, one millimetre tall, for working out its proportions.</summary>
    public static List<TextShape> Outlines(string file) => SvgOutlines.Read(file, 1f);

    /// <summary>
    /// The solids for a drawing at this size: one per shape, or one for the lot. Shapes that
    /// overlap are unioned when they go into one object, since two solids laid through each other
    /// are not one solid a slicer can read.
    /// </summary>
    public static List<Mesh> Build(string svgText, SvgImportOptions options, CancellationToken token = default)
    {
        var oneTall = SvgOutlines.Parse(svgText, 1f);
        float aspect = Aspect(oneTall);
        if (aspect <= 0f) return [];

        float height = MathF.Max(options.Width, MinimumSize) / aspect;
        var shapes = SvgOutlines.Parse(svgText, height);
        float thickness = MathF.Max(options.Thickness, MinimumThickness);

        var solids = shapes
            .Select(shape => TextSolid.Extrude([shape], 0f, thickness))
            .Where(mesh => mesh.TriangleCount > 0)
            .ToList();

        if (options.Separate || solids.Count <= 1) return solids;

        token.ThrowIfCancellationRequested();

        // Gathered into groups that touch, so shapes standing apart are simply put together and
        // only the ones that overlap pay for a boolean.
        Mesh? whole = null;
        foreach (var solid in solids)
        {
            token.ThrowIfCancellationRequested();

            if (whole is null)
            {
                whole = solid;
                continue;
            }

            var a = whole.ComputeBounds();
            var b = solid.ComputeBounds();
            bool touches = a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y;

            whole = touches ? LocalCsg.Union(whole, solid, token) : Mesh.Combine([whole, solid]);
        }

        return whole is null ? [] : [whole];
    }

    public static List<Mesh> BuildFile(string file, SvgImportOptions options, CancellationToken token = default) =>
        Build(File.ReadAllText(file), options, token);
}
