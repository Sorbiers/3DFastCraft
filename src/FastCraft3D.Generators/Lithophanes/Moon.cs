using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Lithophanes;

public enum MoonSurface
{
    [ShownAs("Craters")] MadeUp,
    [ShownAs("NASA photograph")] Photo
}

/// <summary>
/// A moon lamp: a hollow sphere whose wall carries a moon as thickness - craters, their raised
/// rims and a few dark seas - lit from inside through an opening at the bottom, where it stands
/// on a flat rim. Thicker is darker, as in any lithophane: the seas and the rims stand out dark,
/// the crater floors thin and bright.
///
/// The surface is the real moon's, from the Lunar Reconnaissance Orbiter's map - its seas dark,
/// its highlands and ray craters bright - with the side that faces the Earth to the front. Or it is
/// made up here from a seed, so every seed is a moon of its own. The inside is a plain sphere; all
/// the relief is on the outside, so the wall is never thinner than the wall asked for.
///
/// It can stand on a stand for a bulb socket: a hollow column with a cable notch, a plate at the
/// top the socket goes through, and a ring the moon's opening sits over to keep it central.
/// </summary>
public sealed class Moon : Generator<Moon.Settings>
{
    public override string Id => "lithophane.moon";
    public override int Version => 1;
    public override string Category => "Lithophanes";
    public override string Title => "Moon lamp";
    public override string Summary => "A hollow moon with craters and seas in its wall, to light from inside.";

    public sealed record Settings(
        [Choice("Surface", Hint = "Craters made up from a seed, or the real moon from NASA's photographs")] MoonSurface Surface = MoonSurface.MadeUp,
        [Length("Diameter", 30, 200, Size = SizeAxis.XYZ)] float Diameter = 100f,
        [Wall("Wall", 0.6, 3, Hint = "The thinnest the shell goes, where it is brightest")] float Wall = 0.8f,
        [Length("Relief", 0.4, 5, Hint = "How much thicker the darkest parts are than the brightest")] float Relief = 2f,
        [Count("Craters", 0, 600), ShowWhen(nameof(Surface), MoonSurface.MadeUp)] int Craters = 160,
        [Count("Seas", 0, 12, Hint = "Broad dark patches, as the moon's maria are"), ShowWhen(nameof(Surface), MoonSurface.MadeUp)] int Seas = 5,
        [Count("Seed", 1, 9999, Hint = "Another number, another moon"), ShowWhen(nameof(Surface), MoonSurface.MadeUp)] int Seed = 1,
        [Length("Opening", 10, 150, Hint = "Across the hole at the bottom, for the light to go in")] float Opening = 44f,
        [Length("Detail", 0.3, 3, Hint = "The length of one facet: finer shows smaller craters, and costs triangles")] float Detail = 0.8f,
        [Toggle("Socket stand", Group = "Stand", Hint = "A stand for an E26 or E27 bulb socket: a hollow column with a notch for the cable, the socket through a plate at its top, and a ring the moon's opening sits over")] bool Stand = false,
        [Length("Socket hole", 10, 60, Group = "Stand", Hint = "40 mm takes a socket held by its shade ring, 10.5 mm one on a threaded nipple. Measure yours."), ShowWhen(nameof(Stand), true)] float SocketHole = 40f,
        [Length("Stand height", 15, 150, Group = "Stand", Hint = "Room under the plate for the socket's body and the cable"), ShowWhen(nameof(Stand), true)] float StandHeight = 50f,
        [Toggle("Lay out for printing", Hint = "On: the parts side by side, ready to print. Off: the moon put on its stand, as the preview shows it.")] bool Organise = true);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Moon, 100 mm", Default),
        ("Small moon, 60 mm", Default with { Diameter = 60, Opening = 32, Craters = 110, Relief = 1.6f, Detail = 0.6f }),
        ("Large moon, 150 mm", Default with { Diameter = 150, Opening = 50, Craters = 260, Detail = 1f }),
        ("Moon on an E26 stand, 150 mm", Default with { Diameter = 150, Opening = 70, Detail = 1f, Stand = true }),
        ("Real moon from NASA's map, 100 mm", Default with { Surface = MoonSurface.Photo })
    ];

    protected override IEnumerable<string> Check(Settings s, Printer printer)
    {
        float r = s.Diameter / 2f, inner = r - s.Wall;
        if (s.Opening / 2f >= r - 2) yield return "The opening is as wide as the moon.";

        // The rim stands on a plane that has to cut the inside sphere too, or there is no rim.
        else if (s.Opening / 2f < MathF.Sqrt(r * r - inner * inner) + 1f)
            yield return $"The opening is too small for a wall this thick to end in a flat rim: at least {2 * (MathF.Sqrt(r * r - inner * inner) + 1f):0} mm.";

        if (MathF.PI * s.Diameter / s.Detail > 1400)
            yield return "That is too fine a detail for a moon this size: over two million triangles. A coarser detail.";

        if (s.Stand && Holding(s, printer) is { } why) yield return why;
    }

    /// <summary>The hole in the moon's rim: where the inside sphere meets the plane it stands on.</summary>
    private static float Mouth(Settings s)
    {
        float r = s.Diameter / 2f, inner = r - s.Wall;
        return MathF.Sqrt(MathF.Max(inner * inner - r * r + s.Opening * s.Opening / 4f, 0f));
    }

    /// <summary>
    /// The least opening whose mouth goes over the stand's ring round the socket's hole: the ring
    /// two thick, a millimetre of plate inside it, the printer's clearance outside.
    /// </summary>
    private static float LeastOpening(Settings s, float clearance)
    {
        float r = s.Diameter / 2f, inner = r - s.Wall, mouth = s.SocketHole / 2f + 3f + clearance + 0.05f;
        return MathF.Ceiling(2f * MathF.Sqrt(mouth * mouth + r * r - inner * inner) + 0.5f);
    }

    /// <summary>Turning the stand on opens the moon's mouth enough to go over it, rather than refusing.</summary>
    protected override Settings Adjust(Settings before, Settings after, string changed) =>
        changed is nameof(Settings.Stand) or nameof(Settings.SocketHole) && after.Stand
            ? after with { Opening = MathF.Max(after.Opening, LeastOpening(after, Printer.Default.XyClearance)) }
            : after;

    /// <summary>The stand's ring, inside the moon's mouth, and the plate's hole inside that; or why it cannot be.</summary>
    private static string? Holding(Settings s, Printer printer)
    {
        float ring = Mouth(s) - printer.XyClearance - 0.05f, inside = ring - 2f, hole = s.SocketHole / 2f;
        if (hole > inside - 1f)
            return $"The opening is too small to go over the socket: {LeastOpening(s, printer.XyClearance):0} mm at least, or a smaller socket hole.";

        float outer = s.Opening / 2f + 4f - 3f;
        if (s.StandHeight < 3f + (outer - hole) + 5f)
            return $"The stand is too short for its plate: {3f + (outer - hole) + 5f:0} mm at least.";
        return null;
    }

    /// <summary>
    /// The moon's map, grey, and the levels its darkest and brightest parts come to - taken a
    /// little in from the very ends, so a few black and white pixels do not waste the relief.
    /// </summary>
    private static readonly Lazy<(int Width, int Height, byte[] Grey, float Dark, float Light)> Map = new(() =>
    {
        using var stream = typeof(Moon).Assembly.GetManifestResourceStream("FastCraft3D.Generators.Moon.jpg")
                           ?? throw new Refusal("The moon's map is missing from this copy of the app.");
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var grey = new FormatConvertedBitmap(frame, PixelFormats.Gray8, null, 0);
        int w = grey.PixelWidth, h = grey.PixelHeight;
        var pixels = new byte[w * h];
        grey.CopyPixels(pixels, w, 0);

        var sorted = pixels.OrderBy(p => p).ToArray();
        return (w, h, pixels, sorted[sorted.Length / 50] / 255f, sorted[sorted.Length * 49 / 50] / 255f);
    });

    /// <summary>
    /// How dark the real moon is in each direction, from its map: longitude round from the side
    /// that faces the Earth, which looks along minus Y out of the front of the lamp, east to plus
    /// X as the moon is seen from here; latitude up the Z axis.
    /// </summary>
    internal static float[] Photographed(IReadOnlyList<Vector3> directions)
    {
        var (w, h, grey, dark, light) = Map.Value;
        float At(int x, int y) => grey[Math.Clamp(y, 0, h - 1) * w + ((x % w) + w) % w] / 255f;

        var tone = new float[directions.Count];
        Parallel.For(0, directions.Count, i =>
        {
            var d = directions[i];
            float lon = MathF.Atan2(d.Y, d.X) + MathF.PI / 2f;
            float lat = MathF.Asin(Math.Clamp(d.Z, -1f, 1f));
            float u = (lon / (2f * MathF.PI) + 0.5f) * w - 0.5f, v = (0.5f - lat / MathF.PI) * h - 0.5f;

            int x = (int)MathF.Floor(u), y = (int)MathF.Floor(v);
            float fx = u - x, fy = v - y;
            float top = At(x, y) + (At(x + 1, y) - At(x, y)) * fx;
            float bottom = At(x, y + 1) + (At(x + 1, y + 1) - At(x, y + 1)) * fx;
            float bright = top + (bottom - top) * fy;

            tone[i] = Math.Clamp((light - bright) / MathF.Max(light - dark, 1e-3f), 0f, 1f);
        });

        return tone;
    }

    /// <summary>A number from nought to one, the same every time for the same two.</summary>
    private static float Hash(int a, int b)
    {
        uint h = unchecked((uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u);
        h ^= h >> 13;
        h = unchecked(h * 0x5bd1e995);
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    private static Vector3 Direction(int seed, int k)
    {
        float z = 2f * Hash(seed, 3 * k) - 1f, a = 2f * MathF.PI * Hash(seed, 3 * k + 1);
        float r = MathF.Sqrt(MathF.Max(0, 1 - z * z));
        return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
    }

    /// <summary>
    /// How dark the moon is in a direction, from nought to one: seas raised as broad soft
    /// patches, then each crater - a bowl pressed in, a rim thrown up round it - in turn, the
    /// small ones many and the large ones few, as on the moon.
    /// </summary>
    private static float[] Tone(Settings s, IReadOnlyList<Vector3> directions)
    {
        var seas = Enumerable.Range(0, s.Seas).Select(k => (At: Direction(s.Seed + 7919, k), Size: 0.35f + 0.35f * Hash(s.Seed + 104729, k))).ToArray();
        var craters = Enumerable.Range(0, s.Craters).Select(k =>
        {
            // Sizes spread as a power law: most small, a handful large.
            float u = Hash(s.Seed, 3 * k + 2);
            float size = 0.025f + 0.22f * u * u * u;
            return (At: Direction(s.Seed, k), Size: size, Reach: MathF.Cos(MathF.Min(size * 1.6f, MathF.PI)));
        }).ToArray();

        var tone = new float[directions.Count];
        Parallel.For(0, directions.Count, i =>
        {
            var d = directions[i];
            float t = 0.45f;

            foreach (var sea in seas)
            {
                float angle = MathF.Acos(Math.Clamp(Vector3.Dot(d, sea.At), -1f, 1f));
                t += 0.3f * MathF.Exp(-MathF.Pow(angle / sea.Size, 4));
            }

            foreach (var crater in craters)
            {
                float dot = Vector3.Dot(d, crater.At);
                if (dot < crater.Reach) continue;

                float x = MathF.Acos(Math.Clamp(dot, -1f, 1f)) / crater.Size;
                float bowl = x < 1f ? -0.45f * (1f - x * x) : 0f;
                float rim = 0.3f * MathF.Exp(-MathF.Pow((x - 1f) / 0.22f, 2));
                t += bowl + rim;
            }

            tone[i] = Math.Clamp(t, 0f, 1f);
        });

        return tone;
    }

    protected override Generated Build(Settings s, Printer printer, CancellationToken token)
    {
        float r = s.Diameter / 2f, inner = r - s.Wall;
        float floor = -MathF.Sqrt(r * r - s.Opening * s.Opening / 4f);

        // Rings of latitude from the rim to the top, a point at the pole: the outside's rings from
        // where the rim plane cuts the outer sphere, the inside's from where it cuts the inner one.
        int around = Math.Clamp((int)MathF.Ceiling(2 * MathF.PI * r / s.Detail), 24, 1400);
        float fromOut = MathF.Asin(floor / r), fromIn = MathF.Asin(floor / inner);
        int up = Math.Clamp((int)MathF.Ceiling((MathF.PI / 2f - fromOut) * r / s.Detail), 8, 700);
        int upIn = Math.Max(8, up / 3), aroundIn = Math.Max(24, around / 3);

        Vector3 Unit(float lat, float lon) => new(MathF.Cos(lat) * MathF.Cos(lon), MathF.Cos(lat) * MathF.Sin(lon), MathF.Sin(lat));

        var outer = new List<Vector3>(up * around + 1);
        for (int i = 0; i < up; i++)
            for (int j = 0; j < around; j++)
                outer.Add(Unit(fromOut + (MathF.PI / 2f - fromOut) * i / up, 2 * MathF.PI * j / around));
        outer.Add(Vector3.UnitZ);

        token.ThrowIfCancellationRequested();
        var tone = s.Surface == MoonSurface.Photo ? Photographed(outer) : Tone(s, outer);

        // The relief fades out towards the rim, so the rim is round and flat.
        float fade = MathF.Max(3f, s.Opening * 0.15f) / r;
        var mesh = new Mesh();
        var points = new Vector3[outer.Count];
        for (int k = 0; k < outer.Count; k++)
        {
            float lat = MathF.Asin(Math.Clamp(outer[k].Z, -1f, 1f));
            float keep = Math.Clamp((lat - fromOut) / fade, 0f, 1f);
            // Never below the rim: on a big moon with a small opening the wall by the rim faces
            // nearly straight down, and pushed out it went under the plate.
            var p = outer[k] * (r + s.Relief * tone[k] * keep);
            points[k] = p with { Z = MathF.Max(p.Z, floor) };
        }

        var inside = new List<Vector3>(upIn * aroundIn + 1);
        for (int i = 0; i < upIn; i++)
            for (int j = 0; j < aroundIn; j++)
                inside.Add(Unit(fromIn + (MathF.PI / 2f - fromIn) * i / upIn, 2 * MathF.PI * j / aroundIn) * inner);
        inside.Add(Vector3.UnitZ * inner);

        // Outward on the outside: along the ring then up is anticlockwise seen from outside.
        int O(int i, int j) => i * around + (j % around);
        for (int i = 0; i + 1 < up; i++)
            for (int j = 0; j < around; j++)
            {
                mesh.AddTriangle(points[O(i, j)], points[O(i, j + 1)], points[O(i + 1, j + 1)]);
                mesh.AddTriangle(points[O(i, j)], points[O(i + 1, j + 1)], points[O(i + 1, j)]);
            }
        for (int j = 0; j < around; j++) mesh.AddTriangle(points[O(up - 1, j)], points[O(up - 1, j + 1)], points[^1]);

        // Inward on the inside: the other way round.
        int I(int i, int j) => i * aroundIn + (j % aroundIn);
        for (int i = 0; i + 1 < upIn; i++)
            for (int j = 0; j < aroundIn; j++)
            {
                mesh.AddTriangle(inside[I(i, j)], inside[I(i + 1, j + 1)], inside[I(i, j + 1)]);
                mesh.AddTriangle(inside[I(i, j)], inside[I(i + 1, j)], inside[I(i + 1, j + 1)]);
            }
        for (int j = 0; j < aroundIn; j++) mesh.AddTriangle(inside[I(upIn - 1, j)], inside[^1], inside[I(upIn - 1, j + 1)]);

        // The flat rim between the two, the rings having different counts: walked round both
        // together by angle, a triangle at a time from whichever ring is behind.
        int a = 0, b = 0;
        while (a < around || b < aroundIn)
        {
            float nextA = (a + 1f) / around, nextB = (b + 1f) / aroundIn;
            if (b >= aroundIn || (a < around && nextA <= nextB))
            {
                mesh.AddTriangle(points[O(0, a + 1)], points[O(0, a)], inside[I(0, b)]);
                a++;
            }
            else
            {
                mesh.AddTriangle(points[O(0, a)], inside[I(0, b)], inside[I(0, b + 1)]);
                b++;
            }
        }

        var moon = mesh.Welded();
        if (moon.ComputeSignedVolume() < 0) moon.FlipWinding();
        moon = Shapes.Moved(moon, 0, 0, -floor);

        var notes = new List<string>
        {
            $"{s.Wall:0.##} to {s.Wall + s.Relief:0.##} mm thick; {moon.TriangleCount:N0} triangles.",
            "Print it in white, standing on its rim, with the walls solid: no infill, as many perimeters as the wall is thick."
        };
        if (s.Surface == MoonSurface.Photo)
            notes.Add("The surface is the Lunar Reconnaissance Orbiter's map of the moon, courtesy of NASA's Scientific Visualization Studio. The side that faces the Earth is at the front.");

        if (!s.Stand)
        {
            notes.Add("A tea light or an LED puck goes under the opening.");
            return new Generated([new GeneratedPart("Moon", moon, Role: "moon")], notes) { LaidOut = s.Organise };
        }

        var stand = Stand(s, printer);
        float through = 2f * (Mouth(s) - printer.XyClearance - 2.05f);
        notes.Add($"The socket goes up through the stand's plate, held by its ring; the bulb has to pass up through the stand's ring into the moon - {through:0} mm across at most - so a small LED globe or candle bulb, not a household one.");
        notes.Add("Use an LED bulb: a filament bulb runs hot enough to soften the plastic.");

        var parts = Shapes.InARow(
        [
            ("Moon", "moon", moon, Matrix4x4.CreateTranslation(0, 0, s.StandHeight)),
            ("Stand", "stand", stand, Matrix4x4.Identity)
        ]);
        return new Generated(parts, notes) { LaidOut = s.Organise };
    }

    /// <summary>
    /// The stand: a hollow column, a plate at its top with the socket's hole, and a ring standing on
    /// the plate that the moon's mouth fits over. Under the plate a cone rather than a flat ceiling,
    /// so it prints upright without support; a notch in the foot lets the cable out.
    /// </summary>
    private static Mesh Stand(Settings s, Printer printer)
    {
        float h = s.StandHeight, outer = s.Opening / 2f + 4f, wall = outer - 3f, hole = s.SocketHole / 2f;
        float ring = Mouth(s) - printer.XyClearance - 0.05f, inside = ring - 2f;

        var profile = new List<Vector2>
        {
            new(wall, 0), new(outer, 0), new(outer, h), new(ring, h), new(ring, h + 3f), new(inside, h + 3f), new(inside, h),
            new(hole, h), new(hole, h - 3f), new(wall, h - 3f - (wall - hole))
        };
        var column = Shapes.Turned(profile);
        return Shapes.Subtract(column, Shapes.Box(-5f, -outer - 1f, -1f, 5f, -wall + 1f, MathF.Min(12f, h * 0.4f)));
    }
}
