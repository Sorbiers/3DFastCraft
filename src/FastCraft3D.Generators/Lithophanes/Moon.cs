using System.Numerics;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators.Lithophanes;

/// <summary>
/// A moon lamp: a hollow sphere whose wall carries a moon as thickness - craters, their raised
/// rims and a few dark seas - lit from inside through an opening at the bottom, where it stands
/// on a flat rim. Thicker is darker, as in any lithophane: the seas and the rims stand out dark,
/// the crater floors thin and bright.
///
/// No picture is asked for: the surface is made up here, from a seed, so every seed is a moon of
/// its own and the same seed is always the same moon. The inside is a plain sphere; all the relief
/// is on the outside, so the wall is never thinner than the wall asked for.
/// </summary>
public sealed class Moon : Generator<Moon.Settings>
{
    public override string Id => "lithophane.moon";
    public override int Version => 1;
    public override string Category => "Lithophanes";
    public override string Title => "Moon lamp";
    public override string Summary => "A hollow moon with craters and seas in its wall, to light from inside.";

    public sealed record Settings(
        [Length("Diameter", 30, 200)] float Diameter = 100f,
        [Wall("Wall", 0.6, 3, Hint = "The thinnest the shell goes, where it is brightest")] float Wall = 0.8f,
        [Length("Relief", 0.4, 5, Hint = "How much thicker the darkest parts are than the brightest")] float Relief = 2f,
        [Count("Craters", 0, 600)] int Craters = 160,
        [Count("Seas", 0, 12, Hint = "Broad dark patches, as the moon's maria are")] int Seas = 5,
        [Count("Seed", 1, 9999, Hint = "Another number, another moon")] int Seed = 1,
        [Length("Opening", 10, 150, Hint = "Across the hole at the bottom, for the light to go in")] float Opening = 44f,
        [Length("Detail", 0.3, 3, Hint = "The length of one facet: finer shows smaller craters, and costs triangles")] float Detail = 0.8f);

    protected override IEnumerable<(string Name, Settings Settings)> Shipped =>
    [
        ("Moon, 100 mm", Default),
        ("Small moon, 60 mm", Default with { Diameter = 60, Opening = 32, Craters = 110, Relief = 1.6f, Detail = 0.6f }),
        ("Large moon, 150 mm", Default with { Diameter = 150, Opening = 50, Craters = 260, Detail = 1f })
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
        var tone = Tone(s, outer);

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

        return new Generated([new GeneratedPart("Moon", moon, Role: "moon")],
        [
            $"{s.Wall:0.##} to {s.Wall + s.Relief:0.##} mm thick; {moon.TriangleCount:N0} triangles.",
            "Print it in white, standing on its rim, with the walls solid: no infill, as many perimeters as the wall is thick. A tea light or an LED puck goes under the opening."
        ]);
    }
}
