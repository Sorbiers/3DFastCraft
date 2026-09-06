using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

public enum PatternKind
{
    /// <summary>Running-bond masonry: level courses with the joints staggered by half a brick.</summary>
    Brick,

    /// <summary>Flowing grain that opens around knots, cut as curves rather than lines.</summary>
    Wood,

    /// <summary>Evenly spaced parallel grooves - lap siding, panelling, ribs.</summary>
    Stripes
}

/// <summary>Which way the pattern runs across the face.</summary>
public enum PatternDirection
{
    Horizontal,
    Vertical
}

/// <param name="Kind">Which pattern.</param>
/// <param name="Size">Brick length, grain spacing or stripe spacing, in millimetres.</param>
/// <param name="GrooveWidth">How wide the cut lines are, in millimetres.</param>
/// <param name="Depth">How deep they are cut, in millimetres.</param>
/// <param name="Direction">Which way the courses, grain or stripes run.</param>
/// <param name="OffsetU">Slides the pattern across the face, for lining a corner up.</param>
/// <param name="OffsetV">Slides the pattern along the face.</param>
public readonly record struct EngraveOptions(
    PatternKind Kind = PatternKind.Brick,
    float Size = 20f,
    float GrooveWidth = 1.2f,
    float Depth = 0.6f,
    PatternDirection Direction = PatternDirection.Horizontal,
    float OffsetU = 0f,
    float OffsetV = 0f,
    bool Raised = false)
{
    /// <summary>Below this the pattern is finer than the cutter can meaningfully resolve.</summary>
    public const float MinimumSize = 0.5f;

    /// <summary>
    /// Settings to start from.
    ///
    /// Not <c>new EngraveOptions()</c>: this is a record struct, so the parameterless form is the
    /// zero value and skips the primary constructor's defaults altogether. The panel would open
    /// showing nothing but zeros, and Sane() would quietly turn them into the finest pattern it
    /// allows rather than a sensible brick.
    /// </summary>
    public static EngraveOptions Default => new(PatternKind.Brick, 20f, 1.2f, 0.6f);

    public EngraveOptions Sane() => this with
    {
        Size = Math.Max(Size, MinimumSize),
        GrooveWidth = Math.Clamp(GrooveWidth, 0.05f, Math.Max(Size, MinimumSize) * 0.9f),
        Depth = Math.Max(Depth, 0.01f)
    };
}

/// <summary>
/// Turns a pattern into the grooves that get cut out of a face.
///
/// Brick and stripes come out as rectangles, which may overlap freely - a brick's perpend joints
/// deliberately run into the courses above and below, because a joint that merely met the course
/// would leave two grooves touching at a coplanar face. <see cref="GrooveSolid"/> resolves those
/// overlaps into a single region. Wood comes out as curved ribbons instead, which must not
/// overlap; <see cref="WoodGrain"/> guarantees that itself.
/// </summary>
public static class GroovePattern
{
    /// <summary>
    /// Past this the cutter costs more than the result is worth, and it is always a sign that
    /// the pattern size is wrong for the face rather than something anyone intended.
    /// </summary>
    public const int MaximumGrooves = 20000;

    private const float BrickAspect = 3f; // a brick is about three times as long as it is tall

    /// <summary>
    /// The grooves covering <paramref name="area"/>, which should already be a little larger than
    /// the face so lines run off the edge instead of stopping exactly on it.
    /// </summary>
    public static GrooveSet Build(EngraveOptions options, Rect2 area)
    {
        options = options.Sane();
        if (area.IsEmpty) return GrooveSet.Empty;

        // Every pattern is authored running left to right; a vertical one is the same pattern on
        // a transposed face, turned back at the end.
        bool turned = options.Direction == PatternDirection.Vertical;
        var canvas = turned ? Transpose(area) : area;
        var anchor = Anchor(canvas, options, turned);

        if (options.Kind == PatternKind.Wood)
        {
            var grain = WoodGrain.Build(options, canvas, anchor);
            return GrooveSet.Of(turned ? grain.Select(Transpose).ToList() : grain);
        }

        var grooves = options.Kind == PatternKind.Brick
            ? Brick(options, canvas, anchor)
            : Stripes(options, canvas, anchor);

        return GrooveSet.Of(turned ? grooves.Select(Transpose).ToList() : grooves);
    }

    /// <summary>
    /// How many separate cuts the pattern makes, without making them. The panel shows this,
    /// because pattern size against face size is the only thing that governs the cost.
    /// </summary>
    public static int Count(EngraveOptions options, Rect2 area)
    {
        options = options.Sane();
        if (area.IsEmpty) return 0;

        bool turned = options.Direction == PatternDirection.Vertical;
        var canvas = turned ? Transpose(area) : area;

        return options.Kind switch
        {
            PatternKind.Brick => Lines(canvas.Height, options.Size / BrickAspect + options.GrooveWidth)
                * (1 + Lines(canvas.Width, options.Size + options.GrooveWidth)),
            PatternKind.Wood => WoodGrain.Count(options, canvas),
            _ => Lines(canvas.Height, options.Size)
        };
    }

    /// <summary>
    /// Where the pattern starts. Offsetting it is how two walls are made to meet at a corner:
    /// each face measures from its own bottom-left, and which corner that is depends on which way
    /// the face points, so the two rarely line up by themselves.
    /// </summary>
    private static Vector2 Anchor(Rect2 canvas, EngraveOptions options, bool turned) =>
        new(canvas.MinU + (turned ? options.OffsetV : options.OffsetU),
            canvas.MinV + (turned ? options.OffsetU : options.OffsetV));

    private static int Lines(float span, float pitch) =>
        pitch <= 0 ? 0 : (int)(span / pitch) + 2;

    /// <summary>The index of the first line at or below the start of the area.</summary>
    private static int FirstIndex(float anchor, float from, float pitch) =>
        (int)MathF.Floor((from - anchor) / pitch);

    /// <summary>
    /// The same pattern in the positive: the bricks and boards themselves rather than the joints
    /// between them.
    ///
    /// Not the complement worked out afterwards - generated directly, because the complement of a
    /// connected web of joints is a great many separate pieces and finding them is more work than
    /// laying them out in the first place. The pieces come out disjoint by construction, which is
    /// the whole point of raising a pattern rather than cutting it.
    /// </summary>
    public static GrooveSet Raised(EngraveOptions options, Rect2 area)
    {
        options = options.Sane();
        if (area.IsEmpty) return GrooveSet.Empty;

        bool turned = options.Direction == PatternDirection.Vertical;
        var canvas = turned ? Transpose(area) : area;
        var anchor = Anchor(canvas, options, turned);

        // Grain is the exception, and it is the pattern that says what "raised" means. Brick and
        // siding are made of pieces with joints between them, so raising them means the pieces:
        // raising the joints would give a lattice, not a wall. Grain is not made of pieces - it
        // is the lines - so raising it means the lines, standing proud like the hard grain of a
        // weathered board rather than sunk into it.
        if (options.Kind == PatternKind.Wood)
        {
            var grain = WoodGrain.Build(options, canvas, anchor)
                .SelectMany(line => Held(line, canvas))
                .ToList();

            return GrooveSet.Of(turned ? grain.Select(Transpose).ToList() : grain);
        }

        var pieces = options.Kind == PatternKind.Brick
            ? BrickFaces(options, canvas, anchor)
            : BoardFaces(options, canvas, anchor);

        return GrooveSet.Of(turned ? pieces.Select(Transpose).ToList() : pieces);
    }

    /// <summary>
    /// The stretches of a grain line that stay inside the area, its own width taken into account.
    ///
    /// A cut line is allowed to run off the face - the part hanging over meets no material and
    /// removes nothing. A raised one is real, and a raised line hanging over the edge would be a
    /// ledge standing in mid air, so it is trimmed back to where the face can hold it. A line
    /// that leaves and returns comes back as two, which is what the grain looks like anyway.
    /// </summary>
    private static IEnumerable<Polyline2> Held(Polyline2 line, Rect2 area)
    {
        float margin = line.Width * 0.5f;
        var room = new Rect2(
            area.MinU + margin, area.MinV + margin, area.MaxU - margin, area.MaxV - margin);

        if (room.IsEmpty) yield break;

        var run = new List<Vector2>();
        bool whole = true;

        foreach (var point in line.Points)
        {
            if (room.Contains(point.X, point.Y))
            {
                run.Add(point);
                continue;
            }

            whole = false;
            if (run.Count >= 2) yield return new Polyline2(run, line.Width);
            run = [];
        }

        // A ring that never left stays a ring; one that did comes back as an open stretch.
        if (run.Count >= 2) yield return new Polyline2(run, line.Width, line.Closed && whole);
    }

    /// <summary>The bricks: what is left of each course between one perpend and the next.</summary>
    private static List<Rect2> BrickFaces(EngraveOptions options, Rect2 area, Vector2 anchor)
    {
        float height = options.Size / BrickAspect;
        float groove = options.GrooveWidth;
        float pitchV = height + groove;
        float pitchU = options.Size + groove;

        var faces = new List<Rect2>();

        for (int course = FirstIndex(anchor.Y, area.MinV, pitchV); ; course++)
        {
            float v = anchor.Y + course * pitchV;
            if (v > area.MaxV) break;

            float low = v + groove, high = v + pitchV;
            float offset = ((course % 2) + 2) % 2 == 0 ? 0 : pitchU * 0.5f;

            for (int brick = FirstIndex(anchor.X + offset, area.MinU, pitchU); ; brick++)
            {
                float u = anchor.X + offset + brick * pitchU;
                if (u > area.MaxU) break;

                faces.Add(new Rect2(u + groove, low, u + pitchU, high));
                if (faces.Count > MaximumGrooves) return faces;
            }
        }

        return faces;
    }

    /// <summary>The boards: the band between one groove and the next.</summary>
    private static List<Rect2> BoardFaces(EngraveOptions options, Rect2 area, Vector2 anchor)
    {
        var faces = new List<Rect2>();

        for (int stripe = FirstIndex(anchor.Y, area.MinV, options.Size); ; stripe++)
        {
            float v = anchor.Y + stripe * options.Size;
            if (v > area.MaxV) break;

            faces.Add(new Rect2(
                area.MinU, v + options.GrooveWidth, area.MaxU, v + options.Size));

            if (faces.Count > MaximumGrooves) break;
        }

        return faces;
    }

    private static List<Rect2> Brick(EngraveOptions options, Rect2 area, Vector2 anchor)
    {
        float height = options.Size / BrickAspect;
        float groove = options.GrooveWidth;
        float pitchV = height + groove;
        float pitchU = options.Size + groove;

        var grooves = new List<Rect2>();

        for (int course = FirstIndex(anchor.Y, area.MinV, pitchV); ; course++)
        {
            float v = anchor.Y + course * pitchV;
            if (v > area.MaxV) break;

            // The bed joint, running the full width of the face.
            grooves.Add(new Rect2(area.MinU, v, area.MaxU, v + groove));

            // The perpend joints for the course above it, offset by half a brick on alternate
            // courses - that stagger is what makes it read as masonry rather than as tiles.
            float offset = ((course % 2) + 2) % 2 == 0 ? 0 : pitchU * 0.5f;
            float top = v + pitchV + groove; // deliberately into the bed joint above

            for (int brick = FirstIndex(anchor.X + offset, area.MinU, pitchU); ; brick++)
            {
                float u = anchor.X + offset + brick * pitchU;
                if (u > area.MaxU) break;

                grooves.Add(new Rect2(u, v, u + groove, top));
                if (grooves.Count > MaximumGrooves) return grooves;
            }
        }

        return grooves;
    }

    private static List<Rect2> Stripes(EngraveOptions options, Rect2 area, Vector2 anchor)
    {
        var grooves = new List<Rect2>();

        for (int stripe = FirstIndex(anchor.Y, area.MinV, options.Size); ; stripe++)
        {
            float v = anchor.Y + stripe * options.Size;
            if (v > area.MaxV) break;

            grooves.Add(new Rect2(area.MinU, v, area.MaxU, v + options.GrooveWidth));
            if (grooves.Count > MaximumGrooves) break;
        }

        return grooves;
    }

    private static Rect2 Transpose(Rect2 r) => new(r.MinV, r.MinU, r.MaxV, r.MaxU);

    private static Polyline2 Transpose(Polyline2 line) => line with
    {
        Points = line.Points.Select(p => new Vector2(p.Y, p.X)).ToList()
    };
}
