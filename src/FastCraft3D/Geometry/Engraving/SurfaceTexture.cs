using System.Numerics;

namespace FastCraft3D.Geometry.Engraving;

/// <summary>Which texture to lay over the face.</summary>
public enum TextureKind
{
    /// <summary>Not a texture: whatever was typed or loaded is stamped instead.</summary>
    None,

    /// <summary>
    /// Diamond knurling, as on a knob or a thumbscrew - the pads two crossed families of grooves
    /// leave standing between them.
    /// </summary>
    Knurl,

    /// <summary>Parallel bands: straight knurling, fluting, reeding, a ribbed grip.</summary>
    Ribs,

    /// <summary>Honeycomb pads. A grip that reads as made rather than as roughened.</summary>
    Hex,

    /// <summary>Round pips on a staggered grid, the anti-slip pattern off a step tread.</summary>
    Dots,

    /// <summary>Checker plate: short bars in pairs, each pair leaning the other way.</summary>
    Tread,

    /// <summary>Running-bond masonry: level courses with the joints staggered by half a brick.</summary>
    Brick,

    /// <summary>
    /// Roof tiles: the same stagger, squarer, with a heavier joint under each course where it
    /// laps the one below. That lap is the shadow a tiled roof reads by.
    /// </summary>
    RoofTiles,

    /// <summary>Stack bond - courses and joints both lining up. Wall tiles, floor tiles, ashlar.</summary>
    Tiles,

    /// <summary>Long boards with their end joints staggered, with grain worked into them.</summary>
    Planks,

    /// <summary>Lap siding: strips the whole way across, each sloping out and stepping back.</summary>
    Siding
}

/// <param name="Kind">Which texture.</param>
/// <param name="PitchMm">Middle to middle of one cell and the next, across the face.</param>
/// <param name="LineMm">
/// The groove left between one pad and the next. It is the gap, not the pad: a texture is judged
/// by how deep and how wide its grooves are, and the pad is whatever that leaves.
/// </param>
/// <param name="AngleDegrees">
/// For knurling, how far the grooves lean from the axis. Forty-five gives square diamonds; less
/// makes them tall, more makes them broad.
/// </param>
/// <param name="Across">
/// Whether ribs or boards run the other way. Ribs run up the face by default, which is the way
/// that grips a knob; boards lie along it. Courses of brick and tile are level by definition and
/// take no notice of this.
/// </param>

/// <param name="Aspect">
/// How many times longer each piece is than it is deep, for the patterns made of pieces. Zero
/// takes whatever the pattern normally is - three for brick, one for a wall tile, eight for a
/// floorboard. Laying a roof as brick is how the first one came out with 39 x 13 cm tiles.
/// </param>
/// <param name="SlopeDegrees">
/// How far each piece of a laid texture is tilted - a roof tile, a strip of siding. Four degrees
/// is what reads as a lapped roof; nought lays the pieces flat. It means nothing to the textures
/// cut from flat outlines.
/// </param>
/// <param name="Slope">Which way that tilt runs: up the course, or along the piece.</param>
public readonly record struct TextureOptions(
    TextureKind Kind = TextureKind.Knurl,
    float PitchMm = 2f,
    float LineMm = 0.6f,
    float AngleDegrees = 45f,
    bool Across = false,
    float Aspect = 0f,
    float SlopeDegrees = 4f,
    TileSlope Slope = TileSlope.Roll)
{
    /// <summary>
    /// Two nozzle widths. A pad narrower than this is not a pad, it is a smear, and the printer
    /// will lay one line down the middle of it or nothing at all.
    /// </summary>
    public const float LeastPadMm = 0.8f;

    /// <summary>A groove narrower than one nozzle closes up as it prints.</summary>
    public const float LeastLineMm = 0.4f;

    /// <summary>
    /// Settings to start from. Not <c>new TextureOptions()</c>, which is the zero value and skips
    /// the primary constructor's defaults - the trap this codebase has met before.
    /// </summary>
    public static TextureOptions Default => new(TextureKind.Knurl, 2f, 0.6f, 45f);

    public bool IsOn => Kind != TextureKind.None;

    /// <summary>Patterns made of pieces with joints between them, rather than of lines on a surface.</summary>
    public bool IsMasonry => Kind is TextureKind.Brick or TextureKind.Tiles;

    /// <summary>
    /// Patterns with a shape to them rather than an outline: a lap, a slope, a grain. They are
    /// built as a height over the face by <see cref="ReliefField"/> instead of being cut from
    /// flat pads, which is the only way any of those three can be said at all.
    /// </summary>
    public bool IsProfiled =>
        Kind is TextureKind.RoofTiles or TextureKind.Planks or TextureKind.Siding;

    /// <summary>
    /// The ones built as slabs rather than sampled as a field.
    ///
    /// A tile and a strip of siding are rectangles with a plate on them, laid at an angle, and
    /// nothing about either of them wants a grid. Boarding still does, because its grain genuinely
    /// is a height that changes everywhere.
    /// </summary>
    public bool IsLaid => Kind is TextureKind.RoofTiles or TextureKind.Siding;

    /// <summary>
    /// Whether running it the other way means anything. Vertical boarding is real; a roof laid in
    /// vertical columns is not a roof.
    /// </summary>
    public bool Turns => Kind is TextureKind.Ribs;

    /// <summary>The proportions to use, with zero meaning whatever the pattern normally is.</summary>
    public float Courses => Aspect <= 0 ? Usual(Kind) : Math.Clamp(Aspect, LeastAspect, MostAspect);

    /// <summary>Below this a course is deeper than it is long, which is not a course.</summary>
    public const float LeastAspect = 0.2f;

    /// <summary>And beyond this it is a stripe, which is its own pattern.</summary>
    public const float MostAspect = 20f;

    private static float Usual(TextureKind kind) => kind switch
    {
        TextureKind.RoofTiles => 1.5f,
        TextureKind.Tiles => 1f,
        TextureKind.Planks => 8f,
        _ => 3f
    };

    public TextureOptions Sane()
    {
        float line = Math.Max(LineMm, LeastLineMm);

        return this with
        {
            PitchMm = Math.Max(PitchMm, line + LeastPadMm),
            LineMm = line,
            AngleDegrees = Math.Clamp(AngleDegrees, 15f, 75f),
            SlopeDegrees = Math.Clamp(SlopeDegrees, 0f, TileSolid.MostSlopeDegrees)
        };
    }
}

/// <summary>
/// Knurling, ribs, honeycomb and the rest, as outlines over a rectangle.
///
/// Nothing here knows what it will be laid on. It produces the same <see cref="TextShape"/>s that
/// lettering and an imported drawing produce, so everything downstream - the placement, the wrap
/// round a barrel, cut or raised, the bevel, keeping it as its own part - works on a knurl exactly
/// as it works on a word, and none of it had to be written again.
///
/// A wall is laid as its bricks, not as its mortar, and the two are the same relief seen from
/// either side: raise the bricks and the mortar is the face, cut them and the mortar stands proud.
/// Raising them is the one that looks like a wall, so the bonds arrive raised. Cutting the mortar
/// itself was tried and cannot be done here at all - it is one connected region, so it has to go
/// over either as rectangles sharing a wall at every junction, which stops the tool being a solid,
/// or as one outline with a hole per brick, which the ear clipper cannot triangulate past a
/// hundred or so holes. Both came back with the middle taken out of the wall.
///
/// Two rules shape the rest of the file. The pads never overlap, because the retiling that makes a
/// pattern watertight without a boolean refuses crossing outlines, and falling back to the boolean
/// for a thousand little diamonds is the difference between a second and a minute. And nothing is
/// clipped: a pad that will not fit whole inside the rectangle is left out instead, which is what
/// the retiling wants anyway - it needs a margin of untouched face to hold the original outline
/// together.
///
/// That is also why a knurl is built as the pads rather than as the grooves. Two crossed families
/// of grooves are the obvious way to describe it and they cross by definition; the pads they leave
/// standing are the same shape seen the other way round, and they never touch.
/// </summary>
public static class SurfaceTexture
{
    /// <summary>
    /// The profile a shaped texture stands for, or null when this one is cut from flat outlines.
    /// </summary>
    /// <param name="nozzleMm">
    /// How fine the result may be. The grain on a board is held to this, so a preview can ask for
    /// a coarse one and get a field a fraction of the size to draw while the numbers are moving.
    /// </param>
    /// <summary>
    /// The courses a slab-built texture comes to on a face, or null when this one is a sampled
    /// field instead.
    /// </summary>
    /// <param name="thickMm">
    /// How thick a tile is. It is what the panel's Depth asks for, and the whole relief is this
    /// plus what the roll adds - <see cref="TileSolid.ReliefOf"/> has that figure.
    /// </param>
    public static TileCourses? CoursesOf(TextureOptions options, float thickMm)
    {
        var o = options.Sane();
        if (!o.IsLaid) return null;

        float course = MathF.Max(o.PitchMm / MathF.Max(o.Courses, 0.2f), TextureOptions.LeastPadMm);

        // Siding is one strip the whole way across, so its course is the pitch itself rather than
        // the pitch divided by how long a piece is - there are no pieces.
        return o.Kind == TextureKind.Siding
            ? new TileCourses(o.PitchMm, o.PitchMm, thickMm, o.LineMm, o.SlopeDegrees, o.Slope,
                              Stagger: false, Ends: false)
            : new TileCourses(o.PitchMm, course, thickMm, o.LineMm, o.SlopeDegrees, o.Slope,
                              Stagger: true, Ends: true);
    }

    public static IRelief? ProfileOf(TextureOptions options, float depthMm, float nozzleMm = 0.4f)
    {
        var o = options.Sane();
        float depth = MathF.Max(depthMm, 0.1f);
        float course = MathF.Max(o.PitchMm / MathF.Max(o.Courses, 0.2f), TextureOptions.LeastPadMm);

        return o.Kind switch
        {
            // Tiles and siding are not here: they are slabs, built by TileSolid.
            TextureKind.Planks =>
                new SurfaceProfiles.Boarding(course, depth, o.LineMm, nozzleMm, o.PitchMm),
            _ => null
        };
    }

    /// <summary>
    /// Past this the face is finer than the printer can resolve and the retiling costs more than
    /// the result is worth. It is always a sign the pitch is wrong for the face.
    /// </summary>
    public const int MostPads = 20_000;

    /// <summary>How many corners a dot is drawn with. Eight reads as round at these sizes.</summary>
    private const int DotCorners = 8;

    /// <summary>
    /// The texture over a rectangle <paramref name="acrossMm"/> by <paramref name="upMm"/>,
    /// centred on the origin, in the face's own millimetres.
    /// </summary>
    /// <param name="seamless">
    /// Whether the rectangle meets itself - it is the way round a barrel rather than a flat face.
    /// The pitch is then nudged so a whole number of cells goes round, or there is a seam down one
    /// side where a wide cell meets a narrow one.
    /// </param>
    public static List<TextShape> Over(float acrossMm, float upMm, TextureOptions options, bool seamless)
    {
        var o = options.Sane();
        if (!o.IsOn || o.IsProfiled || acrossMm <= 0.01f || upMm <= 0.01f) return [];

        return o.Kind switch
        {
            TextureKind.Knurl => Knurl(acrossMm, upMm, o, seamless),
            TextureKind.Ribs => Ribs(acrossMm, upMm, o, seamless),
            TextureKind.Hex => Hex(acrossMm, upMm, o, seamless),
            TextureKind.Dots => Dots(acrossMm, upMm, o, seamless),
            TextureKind.Tread => Tread(acrossMm, upMm, o, seamless),

            // The bonds differ in two numbers and nothing else: whether alternate courses shift
            // by half a piece, and how much heavier the joint under a course is than the joints
            // within it. A tile laps the course below and that lap throws a shadow the whole way
            // along, which is the only thing separating a roof from a wall at this size.
            TextureKind.Brick => Masonry(acrossMm, upMm, o, seamless, true, 1f),
            TextureKind.Tiles => Masonry(acrossMm, upMm, o, seamless, false, 1f),

            _ => []
        };
    }

    /// <summary>
    /// How many cells fit across, and the pitch that makes them fit exactly.
    ///
    /// Left alone on a flat face - a face is not obliged to be a whole number of cells wide, and
    /// forcing it would move a pitch somebody typed. Round a barrel the two ends are the same
    /// place, so the pitch has to divide the way round or the last cell is a different size from
    /// all the others, in the one spot where that shows.
    /// </summary>
    private static (int Count, float Pitch) Fit(float acrossMm, float pitch, bool seamless, bool wantEven)
    {
        int count = Math.Max(1, (int)MathF.Round(acrossMm / pitch));

        // A staggered lattice has a two-column repeat, so an odd count puts two unstaggered
        // columns side by side where the ends meet.
        if (seamless && wantEven && count % 2 == 1) count++;

        return seamless ? (count, acrossMm / count) : (count, pitch);
    }

    /// <summary>The diamonds two crossed families of grooves leave standing.</summary>
    private static List<TextShape> Knurl(float acrossMm, float upMm, TextureOptions o, bool seamless)
    {
        var (_, pitch) = Fit(acrossMm, o.PitchMm, seamless, wantEven: false);

        // The grooves lean this far off the axis, so a diamond one pitch wide stands this tall.
        // Forty-five degrees gives the square diamond of an ordinary knurling wheel.
        float lean = MathF.Tan(o.AngleDegrees * MathF.PI / 180f);
        float tall = Math.Max(pitch / MathF.Max(lean, 0.01f), TextureOptions.LeastPadMm + o.LineMm);

        float halfWide = (pitch - o.LineMm) / 2f;
        float halfTall = (tall - o.LineMm) / 2f;
        if (halfWide <= 0 || halfTall <= 0) return [];

        return Lay(pitch, tall, halfWide, halfTall, o.LineMm, acrossMm, upMm, stagger: false,
            (cx, cy, _, _) =>
            [
                new Vector2(cx - halfWide, cy),
                new Vector2(cx, cy - halfTall),
                new Vector2(cx + halfWide, cy),
                new Vector2(cx, cy + halfTall)
            ]);
    }

    /// <summary>Plain parallel bands, running up the face or across it.</summary>
    private static List<TextShape> Ribs(float acrossMm, float upMm, TextureOptions o, bool seamless)
    {
        // Ribs across the face do not meet themselves even when the face is wrapped: the seam runs
        // the other way. Only bands running up it have two ends that have to agree.
        float span = o.Across ? upMm : acrossMm;
        var (count, pitch) = Fit(span, o.PitchMm, seamless && !o.Across, wantEven: false);

        float half = (pitch - o.LineMm) / 2f;
        if (half <= 0) return [];

        var made = new List<TextShape>();
        float length = (o.Across ? acrossMm : upMm) / 2f;

        for (int i = 0; i < count; i++)
        {
            float centre = -span / 2f + (i + 0.5f) * pitch;
            if (centre + half > span / 2f + 1e-4f || centre - half < -span / 2f - 1e-4f) continue;

            made.Add(new TextShape(o.Across
                ?
                [
                    new Vector2(-length, centre - half), new Vector2(length, centre - half),
                    new Vector2(length, centre + half), new Vector2(-length, centre + half)
                ]
                :
                [
                    new Vector2(centre - half, -length), new Vector2(centre + half, -length),
                    new Vector2(centre + half, length), new Vector2(centre - half, length)
                ], []));
        }

        return made;
    }

    /// <summary>
    /// Honeycomb: flat-topped cells in columns, every other column dropped by half a cell.
    ///
    /// Columns rather than rows, because the stagger then runs up the face and the seam runs down
    /// it, and the two never have to be reconciled. Laid the other way round, a barrel needs the row
    /// offset to come back to itself as well as the pitch.
    ///
    /// Flat-topped is what the column stagger asks for, and getting that wrong is how this went in:
    /// pointy-topped cells staggered by column interlock a quarter of their height into each other,
    /// so at a fine groove the cells ran into their own neighbours and the whole field was refused.
    /// It held together at a coarse groove, which is what the tests used.
    /// </summary>
    private static List<TextShape> Hex(float acrossMm, float upMm, TextureOptions o, bool seamless)
    {
        // Columns sit closer together than the cells are wide, since they interlock: a hexagon is
        // two flats across for every root-three it stands tall.
        var (_, wide) = Fit(acrossMm, o.PitchMm * MathF.Sqrt(3f) / 2f, seamless, wantEven: true);

        // Flat to flat across a cell, which is how far apart neighbours are on every side of it.
        float flat = wide * 2f / MathF.Sqrt(3f);
        float radius = MathF.Max(flat - o.LineMm, TextureOptions.LeastPadMm) / MathF.Sqrt(3f);
        if (radius <= 0) return [];

        float halfTall = radius * MathF.Sqrt(3f) / 2f;

        return Lay(wide, flat, radius, halfTall, o.LineMm, acrossMm, upMm, stagger: true,
            (cx, cy, _, _) =>
            [
                new Vector2(cx - radius, cy),
                new Vector2(cx - radius / 2f, cy + halfTall),
                new Vector2(cx + radius / 2f, cy + halfTall),
                new Vector2(cx + radius, cy),
                new Vector2(cx + radius / 2f, cy - halfTall),
                new Vector2(cx - radius / 2f, cy - halfTall)
            ]);
    }

    /// <summary>
    /// Round pips, on the same interlocking lattice as the honeycomb and for the same reason: it is
    /// the arrangement that puts every pip the same distance from all six of its neighbours, so one
    /// groove width holds everywhere.
    /// </summary>
    private static List<TextShape> Dots(float acrossMm, float upMm, TextureOptions o, bool seamless)
    {
        var (_, wide) = Fit(acrossMm, o.PitchMm * MathF.Sqrt(3f) / 2f, seamless, wantEven: true);

        float apart = wide * 2f / MathF.Sqrt(3f);
        float radius = (apart - o.LineMm) / 2f;
        if (radius <= 0) return [];

        return Lay(wide, apart, radius, radius, o.LineMm, acrossMm, upMm, stagger: true,
            (cx, cy, _, _) =>
            {
                var loop = new List<Vector2>(DotCorners);
                for (int i = 0; i < DotCorners; i++)
                {
                    float a = MathF.Tau * i / DotCorners;
                    loop.Add(new Vector2(cx + radius * MathF.Cos(a), cy + radius * MathF.Sin(a)));
                }
                return loop;
            });
    }

    /// <summary>
    /// Checker plate: a short leaning bar to a cell, each one leaning against its neighbours. It
    /// is the one texture nobody mistakes for roughening.
    /// </summary>
    private static List<TextShape> Tread(float acrossMm, float upMm, TextureOptions o, bool seamless)
    {
        var (_, pitch) = Fit(acrossMm, o.PitchMm, seamless, wantEven: true);

        float tall = pitch;
        float half = MathF.Max(TextureOptions.LeastPadMm, pitch * 0.2f) / 2f;
        float reach = (pitch - o.LineMm) / 2f;
        float rise = MathF.Max(tall / 2f - half - o.LineMm / 2f, 0f);
        if (reach <= 0) return [];

        return Lay(pitch, tall, reach, rise + half, o.LineMm, acrossMm, upMm, stagger: false,
            (cx, cy, column, row) =>
            {
                // Every other bar leans the other way, which is what makes a plate read as checker
                // rather than as a field of dashes all pointing one way.
                float lean = (column + row) % 2 == 0 ? rise : -rise;

                return
                [
                    new Vector2(cx - reach, cy - lean - half),
                    new Vector2(cx + reach, cy + lean - half),
                    new Vector2(cx + reach, cy + lean + half),
                    new Vector2(cx - reach, cy - lean + half)
                ];
            });
    }

    /// <summary>
    /// Courses of pieces with joints between them: brick, roof tile, wall tile, floorboard.
    ///
    /// Built as the pieces rather than as the joints, like everything else here, which is also why
    /// a bond that puts a whole brick over a joint below can be described at all: two families of
    /// grooves would cross at every perpend.
    ///
    /// The one piece of real work is the brick that runs off the end of a wrapped course. A
    /// staggered course starts half a piece along, so where the two ends of a barrel meet there is
    /// always a piece straddling the join. Dropping it leaves a notch in every other course, down
    /// the one seam anybody will look at, so it is emitted as the two halves it really is - which
    /// is what a bricklayer does with it, and they close up into one brick when the face is wrapped.
    /// </summary>
    private static List<TextShape> Masonry(
        float acrossMm, float upMm, TextureOptions o, bool seamless, bool stagger, float bed)
    {
        // Boards may stand on end. Courses of brick and tile are level by definition.
        bool upright = o.Across && o.Turns;

        float aspect = o.Courses;
        float joint = o.LineMm;
        float leastDeep = joint * bed + TextureOptions.LeastPadMm;

        float length, depth;
        int wrapCount;

        if (!upright)
        {
            (wrapCount, length) = Fit(acrossMm, o.PitchMm, seamless, wantEven: false);
            depth = MathF.Max(length / aspect, leastDeep);
        }
        else
        {
            // Standing on end, it is the courses that repeat round the wrap, so it is the depth of
            // a course that has to divide it rather than the length of a board.
            (wrapCount, depth) = Fit(acrossMm, MathF.Max(o.PitchMm / aspect, leastDeep), seamless, false);
            length = MathF.Max(depth * aspect, joint + TextureOptions.LeastPadMm);
        }

        float pitchX = upright ? depth : length;
        float pitchY = upright ? length : depth;
        float halfX = (pitchX - (upright ? joint * bed : joint)) / 2f;
        float halfY = (pitchY - (upright ? joint : joint * bed)) / 2f;
        if (halfX <= 0 || halfY <= 0) return [];

        int rows = Math.Max(1, (int)MathF.Ceiling(upMm / pitchY) + 1);
        float halfAcross = acrossMm / 2f;
        float halfUp = upMm / 2f;

        // Every piece stops half a joint short of the edge of the field, the cut ones included.
        float margin = joint / 2f;

        var made = new List<TextShape>();

        // From one row before the first, exactly as the columns do. A staggered column is lifted
        // by half a piece, so without it the bottom of every other column is bare - which is what
        // boarding on a face only a course or two tall came back as.
        for (int row = -1; row < rows && made.Count < MostPads; row++)
        {
            float cy = -halfUp + (row + 0.5f) * pitchY;
            float alongX = !upright && stagger && row % 2 == 1 ? pitchX / 2f : 0f;


            // One column either side of the run, so a staggered course is closed off with a half
            // piece at each end instead of a gap the size of one.
            for (int column = -1; column <= wrapCount && made.Count < MostPads; column++)
            {
                // Wrapped, the column before the first and the last column are the same piece -
                // the one on the join - reached from either side. Laying both put two identical
                // half bricks on top of each other at each end, and four coincident walls is how
                // a sound boolean comes back with three dozen non-manifold edges.
                if (seamless && column < 0) continue;

                float cx = -halfAcross + (column + 0.5f) * pitchX + alongX;
                float low = cx - halfX, high = cx + halfX;

                if (upright)
                {
                    // Nothing straddles the seam this way round: the pieces repeat across it whole.
                    float lift = stagger && ((column % 2) + 2) % 2 == 1 ? pitchY / 2f : 0f;
                    Keep(made, low, high, cy + lift - halfY, cy + lift + halfY, halfAcross, halfUp, margin);
                    continue;
                }

                if (seamless && low < halfAcross && high > halfAcross)
                {
                    // The half that is here and the half that comes round the other side, each
                    // stopping half a joint short of the join. Butted right up to it they meet
                    // wall to wall once the face is wrapped, and coincident walls are the one
                    // thing the boolean cannot be asked to union. Half a joint either side reads
                    // as the stop end of a wall, which is what it is.
                    Keep(made, low, halfAcross - margin, cy - halfY, cy + halfY, halfAcross, halfUp, margin);
                    Keep(made, -halfAcross + margin, -halfAcross + (high - halfAcross),
                         cy - halfY, cy + halfY, halfAcross, halfUp, margin);
                    continue;
                }

                // On a flat face a piece is simply cut off at the end of the run, which is what a
                // wall does at a corner - half a joint short of it, like every other piece, so that
                // the course reads as ending in a half brick rather than as running out.
                Keep(made, low, high, cy - halfY, cy + halfY, halfAcross, halfUp, margin);
            }
        }

        return made;
    }

    /// <summary>
    /// Adds a rectangular piece, cut off at the edges of the field and dropped only when what is
    /// left of it is too small to print.
    ///
    /// Cut off rather than thrown away, on both axes, because that is what a wall does at its
    /// edges - and because throwing away leaves holes. A staggered course lifts every other piece
    /// by half its length, so on a face only a few courses tall the lifted ones ran past the top
    /// and vanished, and the wall came back with bald patches at the top of one column and the
    /// bottom of the next. Cut short they are half boards, which is what a joiner does with them.
    ///
    /// The size is measured with a thousandth of slack, and that is not politeness. A board
    /// standing on end comes out exactly the minimum wide, because the minimum is what set the
    /// depth of its course in the first place - and a width worked out as one subtraction of two
    /// large numbers loses its last bit as the pieces get further from the middle of the face.
    /// Measured exactly, boarding came back as a band a dozen pieces wide down the middle.
    /// </summary>
    private static void Keep(
        List<TextShape> made, float low, float high, float bottom, float top,
        float halfAcross, float halfUp, float marginMm)
    {
        const float Hair = 1e-3f;

        float edgeX = halfAcross - marginMm;
        float edgeY = halfUp - marginMm;

        low = MathF.Max(low, -edgeX);
        high = MathF.Min(high, edgeX);
        bottom = MathF.Max(bottom, -edgeY);
        top = MathF.Min(top, edgeY);

        if (high - low < TextureOptions.LeastPadMm - Hair) return;
        if (top - bottom < TextureOptions.LeastPadMm - Hair) return;

        made.Add(new TextShape(
        [
            new Vector2(low, bottom), new Vector2(high, bottom),
            new Vector2(high, top), new Vector2(low, top)
        ], []));
    }

    /// <summary>
    /// Lays one shape out over the rectangle and throws away anything that will not fit whole.
    ///
    /// Placed by the edge of a cell rather than by its centre, and that is the whole of it. Half a
    /// pitch in from the edge is the same thing for a rectangle sitting in its own cell, and it is
    /// wrong for anything that interlocks: a honeycomb cell is wider than its columns are apart, so
    /// the outermost one hung over the edge and was thrown away, and at a coarse pitch a third of
    /// the face came back bare.
    ///
    /// The count follows from the same figures, so it cannot disagree with the placement, and what
    /// is left over after a whole number of cells is shared between the two ends so the field sits
    /// in the middle of the face rather than against one edge of it.
    /// </summary>
    private static List<TextShape> Lay(
        float pitch, float step, float halfWide, float halfTall, float lineMm,
        float acrossMm, float upMm, bool stagger,
        Func<float, float, int, int, List<Vector2>> cell)
    {
        var made = new List<TextShape>();
        if (pitch <= 0 || step <= 0) return made;

        float insetX = halfWide + lineMm / 2f;
        float insetY = halfTall + lineMm / 2f;

        int columns = 1 + (int)MathF.Floor((acrossMm - 2f * insetX) / pitch + 1e-4f);
        int rows = 1 + (int)MathF.Floor((upMm - 2f * insetY) / step + 1e-4f);
        if (columns <= 0 || rows <= 0) return made;

        float slackX = (acrossMm - 2f * insetX - (columns - 1) * pitch) / 2f;
        float slackY = (upMm - 2f * insetY - (rows - 1) * step) / 2f;

        for (int column = 0; column < columns && made.Count < MostPads; column++)
        {
            float cx = -acrossMm / 2f + insetX + slackX + column * pitch;
            float lift = stagger && column % 2 == 1 ? step / 2f : 0f;

            for (int row = 0; row < rows && made.Count < MostPads; row++)
            {
                float cy = -upMm / 2f + insetY + slackY + row * step + lift;

                var loop = cell(cx, cy, column, row);
                if (Fits(loop, acrossMm, upMm)) made.Add(new TextShape(loop, []));
            }
        }

        return made;
    }

    /// <summary>
    /// Whether a pad is wholly inside the rectangle, with a hair of slack for the arithmetic that
    /// put it there.
    /// </summary>
    private static bool Fits(List<Vector2> loop, float acrossMm, float upMm)
    {
        float halfAcross = acrossMm / 2f + 1e-4f;
        float halfUp = upMm / 2f + 1e-4f;

        foreach (var point in loop)
            if (MathF.Abs(point.X) > halfAcross || MathF.Abs(point.Y) > halfUp) return false;

        return true;
    }
}
