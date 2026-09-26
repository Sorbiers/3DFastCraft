using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.Generators.Buildings;

public enum RoofShape
{
    Gable,
    Hip,
    [ShownAs("Half hip")] HalfHip,
    [ShownAs("Lean-to")] LeanTo,
    Gambrel,
    Mansard,
    Flat
}

public enum RoofCovering
{
    Smooth,
    Tiles,
    Slates,
    Shingles,
    [ShownAs("Corrugated sheet")] Corrugated,
    [ShownAs("Standing seam")] StandingSeam
}

/// <summary>
/// A roof to sit on a model's walls: gable, hip, half hip, lean-to, gambrel, mansard or flat,
/// hollow or solid, its slopes tiled, slated, shingled or sheeted in relief.
///
/// Every sloped roof here is the solid under a handful of planes - two for a gable, four for a
/// hip, two to a side for a gambrel - cut from a block the size of the eaves. Built that way
/// rather than face by face, because everything else comes from the same planes: where each one
/// is the roof, and so where its tiles go; where two meet, and so where the ridge and hips run;
/// and the hollow inside, which is the same planes each let down by the shell's thickness.
/// </summary>
public sealed class Roof : Generator<Roof.Settings>
{
    public override string Id => "building.roof";
    public override int Version => 1;
    public override string Category => "Buildings";
    public override string Title => "Roof";
    public override string Summary => "A roof to sit on the walls - gable, hip, gambrel, mansard and more - tiled, slated or sheeted in relief.";

    /// <summary>More tiles than this and a roof takes seconds to cut; nobody sees them singly at that size.</summary>
    private const int MostTiles = 6000;

    /// <summary>The same for ribs of sheet, each a few points in one long outline.</summary>
    private const int MostRibs = 600;

    public sealed record Settings(
        [Choice("Shape")] RoofShape Shape = RoofShape.Gable,
        [Length("Across", 5, 400, Group = "Walls", Hint = "Outside the walls it sits on, the way the slopes fall")] float Width = 60f,
        [Length("Along", 5, 400, Group = "Walls", Hint = "Outside the walls, along the ridge")] float Length = 80f,
        [Angle("Pitch", 5, 75, Group = "Slopes")] float Pitch = 40f,
        [Angle("Lower pitch", 30, 85, Group = "Slopes", Hint = "The steep slope from the eaves")] float LowerPitch = 65f,
        [Angle("Upper pitch", 5, 45, Group = "Slopes", Hint = "The shallow slope to the ridge")] float UpperPitch = 25f,
        [Number("Lower slope", 10, 90, UnitText = "% of the way in", Group = "Slopes", Hint = "How far in from the eaves the steep slope runs before it breaks")] float Break = 35f,
        [Number("Hipped", 10, 90, UnitText = "% of the height", Group = "Slopes", Hint = "How far down from the ridge the ends are hipped")] float Hipped = 35f,
        [Length("Eaves overhang", 0, 30, Group = "Edges", Hint = "How far the roof stands out past the walls under its slopes")] float Eaves = 3f,
        [Length("Gable overhang", 0, 30, Group = "Edges", Hint = "How far it stands out past the gable walls")] float Verge = 2f,
        [Length("Edge thickness", 0.4, 20, Group = "Edges", Hint = "How thick the roof is at the eaves; the slab, for a flat roof")] float Fascia = 1.5f,
        [Toggle("Parapet", Group = "Edges", Hint = "A low wall round a flat roof")] bool Parapet = true,
        [Length("Parapet height", 0.4, 20, Group = "Edges")] float ParapetHeight = 2f,
        [Length("Parapet width", 0.4, 10, Group = "Edges")] float ParapetWidth = 1.2f,
        [Toggle("Hollow", Group = "Hollow", Hint = "A shell open underneath, where it sits on the walls, rather than a solid block")] bool Hollow = true,
        [Wall("Shell", 0.8, 10, Group = "Hollow")] float Shell = 1.6f,
        [Choice("Covering", Group = "Covering")] RoofCovering Covering = RoofCovering.Tiles,
        [Length("Course", 0.5, 10, Group = "Covering", Hint = "How much of each row of tiles shows, up the slope")] float Course = 2.2f,
        [Length("Tile width", 0.5, 20, Group = "Covering", Hint = "Across the slope; for sheet, the spacing of its ribs")] float TileWidth = 2.6f,
        [Length("Relief", 0.1, 3, Group = "Covering", Hint = "How far the tiles or ribs stand off the slopes")] float Relief = 0.4f,
        [Toggle("Ridge tiles", Group = "Covering", Hint = "A rounded capping along the ridge and the hips")] bool Ridge = true);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Gable, tiled", Default),
        ("Hip, slated", Default with { Shape = RoofShape.Hip, Covering = RoofCovering.Slates, Course = 1.6f, TileWidth = 2.2f, Relief = 0.25f }),
        ("Half hip, tiled", Default with { Shape = RoofShape.HalfHip, Pitch = 45 }),
        ("Barn, gambrel shingles", Default with { Shape = RoofShape.Gambrel, Covering = RoofCovering.Shingles, Course = 1.3f, TileWidth = 1.8f, Relief = 0.3f }),
        ("Mansard, slated", Default with { Shape = RoofShape.Mansard, Covering = RoofCovering.Slates, Course = 1.6f, TileWidth = 2.2f, Relief = 0.25f }),
        ("Lean-to, corrugated", Default with { Shape = RoofShape.LeanTo, Width = 30, Length = 50, Pitch = 15, Covering = RoofCovering.Corrugated, TileWidth = 1.5f, Ridge = false }),
        ("Shed, standing seam", Default with { Shape = RoofShape.Gable, Width = 40, Length = 50, Pitch = 30, Covering = RoofCovering.StandingSeam, TileWidth = 4f, Relief = 0.5f, Ridge = false }),
        ("Flat, with a parapet", Default with { Shape = RoofShape.Flat, Fascia = 3f, Hollow = false })
    ];

    private static bool Sloped(Settings s) => s.Shape != RoofShape.Flat;

    /// <summary>
    /// A covering's own proportions, brought in when it is chosen: a slate is not a tile of the
    /// same size laid flatter. With one set of numbers for all three, tiles, slates and shingles
    /// came out the same roof.
    /// </summary>
    internal static (float Course, float Width, float Relief) Proportions(RoofCovering covering) => covering switch
    {
        RoofCovering.Slates => (1.6f, 2.2f, 0.25f),
        RoofCovering.Shingles => (1.3f, 1.8f, 0.3f),
        RoofCovering.Corrugated => (2.2f, 1.5f, 0.4f),
        RoofCovering.StandingSeam => (2.2f, 4f, 0.5f),
        _ => (2.2f, 2.6f, 0.4f)
    };

    protected override Settings Adjust(Settings before, Settings after, string changed)
    {
        if (changed != nameof(Settings.Covering)) return after;
        var (course, width, relief) = Proportions(after.Covering);
        return after with { Course = course, TileWidth = width, Relief = relief };
    }

    private static bool Tiled(Settings s) => s.Covering is RoofCovering.Tiles or RoofCovering.Slates or RoofCovering.Shingles;

    private static bool TwoPitch(Settings s) => s.Shape is RoofShape.Gambrel or RoofShape.Mansard;

    protected override bool Shows(Settings s, string parameter) => parameter switch
    {
        nameof(Settings.Pitch) => Sloped(s) && !TwoPitch(s),
        nameof(Settings.LowerPitch) or nameof(Settings.UpperPitch) or nameof(Settings.Break) => TwoPitch(s),
        nameof(Settings.Hipped) => s.Shape == RoofShape.HalfHip,
        nameof(Settings.Verge) => s.Shape is RoofShape.Gable or RoofShape.HalfHip or RoofShape.LeanTo or RoofShape.Gambrel,
        nameof(Settings.Parapet) => !Sloped(s),
        nameof(Settings.ParapetHeight) or nameof(Settings.ParapetWidth) => !Sloped(s) && s.Parapet,
        nameof(Settings.Shell) => s.Hollow,
        nameof(Settings.Covering) => Sloped(s),
        nameof(Settings.Course) => Sloped(s) && Tiled(s),
        nameof(Settings.TileWidth) or nameof(Settings.Relief) => Sloped(s) && s.Covering != RoofCovering.Smooth,
        nameof(Settings.Ridge) => Sloped(s) && s.Shape != RoofShape.LeanTo,
        _ => true
    };

    /// <summary>One plane of a roof, rising inward from a level eave line at a pitch.</summary>
    /// <param name="Eave">A point on the eave line, in plan.</param>
    /// <param name="Up">The way up the slope, in plan: square to the eave, inward.</param>
    /// <param name="Z">How high the eave line is.</param>
    /// <param name="Pitch">In radians.</param>
    private readonly record struct Slope(Vector2 Eave, Vector2 Up, float Z, float Pitch)
    {
        public float Rise => MathF.Tan(Pitch);

        public Vector2 Along => new(Up.Y, -Up.X);

        public float At(Vector2 q) => Z + Vector2.Dot(q - Eave, Up) * Rise;

        /// <summary>From the slope's own coordinates - along the eave, up the slope, out of it - to the roof's.</summary>
        public Matrix4x4 Frame
        {
            get
            {
                float c = MathF.Cos(Pitch), s = MathF.Sin(Pitch);
                return new Matrix4x4(
                    Up.Y, -Up.X, 0, 0,
                    Up.X * c, Up.Y * c, s, 0,
                    -Up.X * s, -Up.Y * s, c, 0,
                    Eave.X, Eave.Y, Z, 1);
            }
        }
    }

    /// <summary>Half the eaves across and along: the walls and the overhangs.</summary>
    private static (float A, float B) Eaves(Settings s)
    {
        bool gabled = s.Shape is RoofShape.Gable or RoofShape.HalfHip or RoofShape.LeanTo or RoofShape.Gambrel;
        return (s.Width / 2f + s.Eaves, s.Length / 2f + (gabled ? s.Verge : s.Eaves));
    }

    private static float Radians(float degrees) => degrees * MathF.PI / 180f;

    private static List<Slope> Slopes(Settings s)
    {
        var (a, b) = Eaves(s);
        float f = s.Fascia, p = Radians(s.Pitch);

        var left = (Eave: new Vector2(-a, 0), Up: Vector2.UnitX, Half: a);
        var right = (Eave: new Vector2(a, 0), Up: -Vector2.UnitX, Half: a);
        var front = (Eave: new Vector2(0, -b), Up: Vector2.UnitY, Half: b);
        var back = (Eave: new Vector2(0, b), Up: -Vector2.UnitY, Half: b);

        // A steep slope from the eaves, breaking a way in to a shallow one.
        IEnumerable<Slope> Broken((Vector2 Eave, Vector2 Up, float Half) side, float run)
        {
            float low = Radians(s.LowerPitch);
            yield return new Slope(side.Eave, side.Up, f, low);
            yield return new Slope(side.Eave + side.Up * run, side.Up, f + run * MathF.Tan(low), Radians(s.UpperPitch));
        }

        switch (s.Shape)
        {
            case RoofShape.Gable:
                return [new(left.Eave, left.Up, f, p), new(right.Eave, right.Up, f, p)];

            case RoofShape.LeanTo:
                return [new(right.Eave, right.Up, f, p)];

            case RoofShape.Hip:
                return [new(left.Eave, left.Up, f, p), new(right.Eave, right.Up, f, p), new(front.Eave, front.Up, f, p), new(back.Eave, back.Up, f, p)];

            case RoofShape.HalfHip:
            {
                // The ends rise from partway up the gable, at the same pitch as the sides.
                float ridge = f + a * MathF.Tan(p);
                float from = ridge - (ridge - f) * s.Hipped / 100f;
                return [new(left.Eave, left.Up, f, p), new(right.Eave, right.Up, f, p), new(front.Eave, front.Up, from, p), new(back.Eave, back.Up, from, p)];
            }

            case RoofShape.Gambrel:
                return [.. Broken(left, a * s.Break / 100f), .. Broken(right, a * s.Break / 100f)];

            case RoofShape.Mansard:
            {
                float run = MathF.Min(a, b) * s.Break / 100f;
                return [.. Broken(left, run), .. Broken(right, run), .. Broken(front, run), .. Broken(back, run)];
            }

            default:
                return [];
        }
    }

    /// <summary>
    /// Where slope <paramref name="i"/> is the roof: the eaves' rectangle, less wherever another
    /// slope is lower. Held <paramref name="hip"/> back from a slope facing another way,
    /// <paramref name="kink"/> from one facing the same way, and reaching <paramref name="beyond"/>
    /// past the eaves.
    /// </summary>
    private static List<Vector2> Region(List<Slope> slopes, int i, float a, float b, float hip = 0, float kink = 0, float beyond = 0)
    {
        var region = Shapes.Rect(-a - beyond, -b - beyond, a + beyond, b + beyond);
        var mine = slopes[i];

        for (int j = 0; j < slopes.Count && region.Count >= 3; j++)
        {
            if (j == i) continue;
            var other = slopes[j];

            // Where mine is no higher than the other: both are linear in plan, so it is a half-plane.
            var k = mine.Up * mine.Rise - other.Up * other.Rise;
            float c = mine.Z - Vector2.Dot(mine.Eave, mine.Up) * mine.Rise - (other.Z - Vector2.Dot(other.Eave, other.Up) * other.Rise);
            float back = Vector2.Dot(mine.Up, other.Up) > 0.99f ? kink : hip;
            region = Clip(region, k, c + back * k.Length());
        }

        return region.Count >= 3 && MathF.Abs(Polygon2.SignedArea(region)) > 0.01f ? region : [];
    }

    /// <summary>The part of a convex outline where k.q + c is nought or less.</summary>
    private static List<Vector2> Clip(List<Vector2> outline, Vector2 k, float c)
    {
        var kept = new List<Vector2>();
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            float fp = Vector2.Dot(k, p) + c, fq = Vector2.Dot(k, q) + c;

            if (fp <= 1e-5f) Add(p);
            if ((fp < -1e-5f && fq > 1e-5f) || (fp > 1e-5f && fq < -1e-5f)) Add(p + (q - p) * (fp / (fp - fq)));
        }

        if (kept.Count > 1 && Vector2.DistanceSquared(kept[0], kept[^1]) < 1e-8f) kept.RemoveAt(kept.Count - 1);
        return kept;

        void Add(Vector2 point)
        {
            if (kept.Count == 0 || Vector2.DistanceSquared(kept[^1], point) > 1e-8f) kept.Add(point);
        }
    }

    /// <summary>How high the roof stands above the walls, capping aside.</summary>
    internal static float Top(Settings s)
    {
        if (!Sloped(s)) return s.Fascia + (s.Parapet ? s.ParapetHeight : 0);

        var (a, b) = Eaves(s);
        var slopes = Slopes(s);
        return slopes.Select((slope, i) => Region(slopes, i, a, b).Select(slope.At).DefaultIfEmpty(slope.Z).Max()).Max();
    }

    /// <summary>A block cut down to lie under every slope, or nothing if no part of it does.</summary>
    private static Mesh? Under(IReadOnlyList<Slope> slopes, float x0, float y0, float x1, float y1, float low, float high)
    {
        Mesh? solid = Shapes.Box(x0, y0, low, x1, y1, high);
        float reach = (x1 - x0) + (y1 - y0) + (high - low) + 10f;

        foreach (var slope in slopes)
        {
            var below = MeshTransform.Transformed(Shapes.Box(-reach, -reach, -reach, reach, reach, 0), slope.Frame);
            solid = ManifoldCsg.Intersect(solid, below);
            if (solid is not { TriangleCount: > 0 }) return null;
        }

        return solid;
    }

    /// <summary>What the covering asks for: tiles over the roof, or ribs across it.</summary>
    private static float Pieces(Settings s)
    {
        var (a, b) = Eaves(s);
        if (Tiled(s))
        {
            float steepest = Radians(TwoPitch(s) ? s.LowerPitch : s.Pitch);
            return 4 * a * b / MathF.Cos(steepest) / (s.Course * s.TileWidth);
        }

        return 2 * MathF.Max(a, b) / s.TileWidth;
    }

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        if (TwoPitch(s) && s.UpperPitch > s.LowerPitch - 5)
            yield return "The upper slope has to be shallower than the lower one, by five degrees or more.";

        if (s.Hollow && 2 * s.Shell + 2 > MathF.Min(s.Width, s.Length))
            yield return "Too small to hollow with a shell that thick.";

        if (!Sloped(s) && s.Parapet)
        {
            var (a, b) = Eaves(s);
            if (2 * s.ParapetWidth + 2 > 2 * MathF.Min(a, b)) yield return "The parapet is wider than the roof leaves room for.";
        }

        if (Sloped(s) && s.Covering != RoofCovering.Smooth)
        {
            if (Tiled(s) && s.Relief > 0.8f * s.Course)
                yield return "The relief is deeper than the courses are long.";

            float pieces = Pieces(s);
            if (Tiled(s) && pieces > MostTiles)
                yield return $"That is about {pieces:0} tiles, more than {MostTiles}. Bigger tiles, or a smaller roof.";
            if (!Tiled(s) && pieces > MostRibs)
                yield return $"That is about {pieces:0} ribs across, more than {MostRibs}. Wider spacing, or a smaller roof.";
        }
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        var notes = new List<string>();
        var (a, b) = Eaves(s);
        float top = Top(s);

        Mesh roof;
        if (!Sloped(s))
        {
            roof = Shapes.Box(-a, -b, 0, a, b, top);
            if (s.Parapet)
                roof = Shapes.Subtract(roof, Shapes.Box(-a + s.ParapetWidth, -b + s.ParapetWidth, s.Fascia, a - s.ParapetWidth, b - s.ParapetWidth, top + 1));

            if (s.Hollow)
            {
                if (s.Fascia - s.Shell >= 0.4f)
                {
                    float ix = s.Width / 2f - s.Shell, iy = s.Length / 2f - s.Shell;
                    roof = Shapes.Subtract(roof, Shapes.Box(-ix, -iy, -1, ix, iy, s.Fascia - s.Shell));
                    notes.Add("Hollow underneath: the flat ceiling inside is a bridge, and may want supports.");
                }
                else
                {
                    notes.Add("The slab is too thin to hollow, so it is solid.");
                }
            }

            notes.Add($"Sits on walls {s.Width:0.#} x {s.Length:0.#} mm. Printed as it sits.");
            return new Generated([new GeneratedPart("Roof", roof, Role: "roof")], notes);
        }

        var slopes = Slopes(s);
        roof = Under(slopes, -a, -b, a, b, 0, top + 1) ?? throw new Refusal("The slopes leave no roof.");
        token.ThrowIfCancellationRequested();

        if (s.Hollow)
        {
            // Each slope let down square to itself by the shell: the same planes, so the shell is
            // even all over, a steep slope's as thick as a shallow one's. Open underneath inside
            // the walls, so it sits on them.
            var inside = slopes.Select(slope => slope with { Z = slope.Z - s.Shell / MathF.Cos(slope.Pitch) }).ToList();
            float ix = s.Width / 2f - s.Shell, iy = s.Length / 2f - s.Shell;

            if (Under(inside, -ix, -iy, ix, iy, -1, top) is { } cavity)
            {
                roof = Shapes.Subtract(roof, cavity);
                float shallowest = slopes.Min(slope => slope.Pitch) * 180f / MathF.PI;
                if (shallowest < 45)
                    notes.Add($"Inside, the roof slopes at {shallowest:0} degrees, shallower than 45: print it with supports, or solid.");
            }
            else
            {
                notes.Add("Too low to hollow, so it is solid.");
            }
        }

        token.ThrowIfCancellationRequested();
        if (s.Covering != RoofCovering.Smooth)
        {
            var laid = Covering(s, slopes, a, b, printer, token);
            if (laid.Count > 0) roof = Shapes.Union([roof, .. laid]);
        }

        token.ThrowIfCancellationRequested();
        if (s.Ridge && s.Shape != RoofShape.LeanTo && Caps(s, slopes, a, b, top) is { } caps)
            roof = Shapes.Union(roof, caps);

        notes.Add($"Sits on walls {s.Width:0.#} x {s.Length:0.#} mm. Printed as it sits, the eaves on the plate.");
        return new Generated([new GeneratedPart("Roof", roof, Role: "roof")], notes);
    }

    /// <summary>
    /// The covering, laid on each slope as the Emboss tool lays roof tiles: separate slabs, each
    /// a closed box sunk a hair into the slope and tilted so its tail stands proud of the course
    /// below, with a joint between every two so no two touch. Sheet is laid the same way, as ribs
    /// running down the slope.
    ///
    /// Cut into the slopes instead, it did not come out closed. Two slopes' courses met on the hip
    /// in edges shared by both cuts, and a capping laid over courses cut to the hip left dozens of
    /// slivers finer than a weld; reaching one slope's cut into the next left a few still. Laid,
    /// each slope keeps to its own face and stops short of its hips and ridge by the capping's
    /// width, so the capping meets only smooth roof and the pieces meet nothing but their slope.
    /// </summary>
    private static List<Mesh> Covering(Settings s, List<Slope> slopes, float a, float b, Printer printer, CancellationToken token)
    {
        float hip = s.Ridge && s.Shape != RoofShape.LeanTo ? Capping(s) + 0.1f : 0.3f;
        float joint = MathF.Max(printer.Nozzle, TextureOptions.LeastLineMm);
        var laid = new List<Mesh>();

        for (int i = 0; i < slopes.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var region = Region(slopes, i, a, b, hip: hip, kink: 0.3f);
            if (region.Count == 0) continue;

            var pieces = s.Covering switch
            {
                RoofCovering.Corrugated or RoofCovering.StandingSeam => Ribs(s, slopes[i], region, printer),
                _ => Tiles(s, slopes[i], region, joint)
            };

            if (pieces is { TriangleCount: > 0 }) laid.Add(pieces);
        }

        return laid;
    }

    /// <summary>The slope's region as a face of its own, for the Emboss tool's tiling to fill.</summary>
    private static Mesh? Tiles(Settings s, Slope slope, List<Vector2> region, float joint)
    {
        var patch = new Mesh();
        var corners = region.Select(q => new Vector3(q, slope.At(q))).ToList();
        for (int k = 1; k + 1 < corners.Count; k++) patch.AddTriangle(corners[0], corners[k], corners[k + 1]);

        // Wound to face up the slope and out of it, which is the side the tiles stand on.
        var normal = new Vector3(-slope.Up * MathF.Sin(slope.Pitch), MathF.Cos(slope.Pitch));
        if (Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), normal) < 0) patch.FlipWinding();

        // Welded, so the triangles share their corners: a face is grown across shared edges, and
        // laid as separate triangles it was the first of them alone - the tiles filled half of
        // every slope, up to the diagonal.
        if (FacePatch.FromTriangle(patch.Welded(), 0) is not { } face) return null;

        // Each laid as it is on a roof. A tile laps the course below and stands off it at its tail;
        // a slate is thin and lies nearly flat; a shingle is split, and leans along its length,
        // so a course of them reads as a row of pieces rather than as a lap.
        var (tilt, lean, thick) = s.Covering switch
        {
            RoofCovering.Slates => (1.5f, TileSlope.Roll, s.Relief),
            RoofCovering.Shingles => (5f, TileSlope.Pitch, s.Relief),
            _ => (4f, TileSlope.Roll, s.Relief)
        };

        // The face's own V runs whichever way it runs; a tile's tail has to be at the bottom of
        // its course, down the slope, so the tilt is turned to suit.
        if (lean == TileSlope.Roll && face.V.Z < 0) tilt = -tilt;

        var courses = new TileCourses(s.TileWidth, s.Course, thick, MathF.Min(joint, s.TileWidth / 3f), tilt, lean);
        var surface = new PlanarSurface(face);
        var size = face.Size;
        return TileSolid.Build(surface, courses, size.X, size.Y, room: TileRoom.Of(surface));
    }

    /// <summary>
    /// Ribs down the slope, each a closed piece sunk a hair into it and cut off where the slope
    /// ends: rounded and close together for corrugated sheet, square and far apart for standing seam.
    /// </summary>
    private static Mesh Ribs(Settings s, Slope slope, List<Vector2> region, Printer printer)
    {
        float w = s.TileWidth, d = s.Relief, cos = MathF.Cos(slope.Pitch);
        bool seam = s.Covering == RoofCovering.StandingSeam;

        // The region in the slope's own terms: along the eave, and up the slope measured on it.
        var flat = region.Select(q => new Vector2(Vector2.Dot(q - slope.Eave, slope.Along), Vector2.Dot(q - slope.Eave, slope.Up) / cos)).ToList();
        float u0 = flat.Min(p => p.X), u1 = flat.Max(p => p.X);

        float half = seam ? MathF.Min(MathF.Max(printer.Nozzle * 1.2f, 0.5f), w / 3f) / 2f : w * 0.35f;
        var ribs = new List<Mesh>();

        for (int j = (int)MathF.Ceiling((u0 + half) / w); j * w <= u1 - half; j++)
        {
            float u = j * w;

            // Both sides of the rib have slope under them the whole of its length.
            if (Span(flat, u - half) is not { } left || Span(flat, u + half) is not { } right) continue;
            float v0 = MathF.Max(left.Low, right.Low) + 0.2f, v1 = MathF.Min(left.High, right.High) - 0.2f;
            if (v1 - v0 < 1f) continue;

            ribs.Add(seam
                ? Shapes.Box(u - half, v0, -TileSolid.SinkMm, u + half, v1, d)
                : Shapes.RodAlongY(half, v0, v1, u, d - half, 12));
        }

        return MeshTransform.Transformed(Mesh.Combine(ribs), slope.Frame);
    }

    /// <summary>Where a line across the slope at <paramref name="u"/> is inside a convex outline, up the slope.</summary>
    private static (float Low, float High)? Span(List<Vector2> outline, float u)
    {
        float low = float.MaxValue, high = float.MinValue;
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 p = outline[i], q = outline[(i + 1) % outline.Count];
            if ((p.X - u) * (q.X - u) > 0 || MathF.Abs(q.X - p.X) < 1e-6f) continue;

            float v = p.Y + (q.Y - p.Y) * (u - p.X) / (q.X - p.X);
            low = MathF.Min(low, v);
            high = MathF.Max(high, v);
        }

        return high > low ? (low, high) : null;
    }

    /// <summary>
    /// A rounded capping along every line where two slopes facing different ways meet - the
    /// ridge and the hips - but not where a gambrel's slopes break, which face the same way.
    /// </summary>
    private static float Capping(Settings s) => MathF.Max(0.5f, 1.5f * s.Relief);

    private static Mesh? Caps(Settings s, List<Slope> slopes, float a, float b, float top)
    {
        float r = Capping(s);
        var regions = Enumerable.Range(0, slopes.Count).Select(i => Region(slopes, i, a, b)).ToList();
        var rods = new List<Mesh>();

        bool OnEdge(Vector2 p, Vector2 q)
        {
            const float e = 1e-3f;
            return (MathF.Abs(p.X + a) < e && MathF.Abs(q.X + a) < e) || (MathF.Abs(p.X - a) < e && MathF.Abs(q.X - a) < e)
                || (MathF.Abs(p.Y + b) < e && MathF.Abs(q.Y + b) < e) || (MathF.Abs(p.Y - b) < e && MathF.Abs(q.Y - b) < e);
        }

        for (int i = 0; i < slopes.Count; i++)
        {
            var region = regions[i];
            for (int n = 0; n < region.Count; n++)
            {
                Vector2 p = region[n], q = region[(n + 1) % region.Count];
                if (Vector2.Distance(p, q) < 0.05f || OnEdge(p, q)) continue;

                for (int j = i + 1; j < slopes.Count; j++)
                {
                    if (Vector2.Dot(slopes[i].Up, slopes[j].Up) > 0.99f) continue;

                    float hp = slopes[i].At(p), hq = slopes[i].At(q);
                    if (MathF.Abs(hp - slopes[j].At(p)) > 1e-3f * (1 + hp) || MathF.Abs(hq - slopes[j].At(q)) > 1e-3f * (1 + hq)) continue;

                    rods.Add(Rod(new Vector3(p, hp), new Vector3(q, hq), r));
                    break;
                }
            }
        }

        if (rods.Count == 0) return null;
        // Trimmed a whisker inside the eaves: flush with a gable face, the capping and the face met in the same plane.
        const float inside = 0.02f;
        return ManifoldCsg.Intersect(Shapes.Union(rods), Shapes.Box(-a + inside, -b + inside, 0, a - inside, b - inside, top + r + 1)) is { TriangleCount: > 0 } caps ? caps : null;
    }

    /// <summary>A rod from one point to another, run on past each by its radius so rods meeting at an angle close up.</summary>
    private static Mesh Rod(Vector3 from, Vector3 to, float radius)
    {
        var along = Vector3.Normalize(to - from);
        float length = Vector3.Distance(from, to) + 2 * radius;
        return MeshTransform.Transformed(Shapes.Cylinder(radius, 0, length, sides: 12),
            MeshTransform.RotationBetween(Vector3.UnitZ, along) * Matrix4x4.CreateTranslation(from - along * radius));
    }

    protected override IEnumerable<string> Describe(Settings s, float modelScale)
    {
        float scale = modelScale > 1.5f ? modelScale : 1f;
        float top = Top(s);

        yield return scale > 1
            ? $"{(Sloped(s) ? "The ridge stands" : "It stands")} {top:0.#} mm above the walls: {top * scale / 1000f:0.0#} m at 1:{scale:0}."
            : $"{(Sloped(s) ? "The ridge stands" : "It stands")} {top:0.#} mm above the walls.";

        if (scale > 1 && Sloped(s) && Tiled(s))
            yield return $"Each {(s.Covering == RoofCovering.Tiles ? "tile" : s.Covering == RoofCovering.Slates ? "slate" : "shingle")} shows {s.TileWidth * scale:0} x {s.Course * scale:0} mm.";
    }
}
