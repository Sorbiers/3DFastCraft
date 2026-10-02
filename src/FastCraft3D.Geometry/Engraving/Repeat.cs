namespace FastCraft3D.Geometry.Engraving;

/// <summary>
/// Randomness that comes out the same every time, and repeats exactly round a barrel.
///
/// A stone wall or a grain is irregular by nature, and wrapped round a barrel its two ends are the
/// same place. Anything random has to agree with itself there, or the seam shows as a line of
/// stones cut in half against stones that do not match them. So every lattice the textures are
/// drawn from - the stones, the noise, the knots - is fitted to a whole number of repeats round
/// the way, and every index into it is taken modulo that number. On a flat face there is nothing
/// to meet and the count is nought, which leaves the lattice running on for ever.
/// </summary>
public static class Repeat
{
    /// <summary>
    /// How many of something about <paramref name="sizeMm"/> long go round, and how long each one
    /// then is. Not round, the size is left as it is and the count is nought.
    /// </summary>
    /// <param name="even">
    /// For a lattice whose alternate columns are set over by half a cell: an odd count puts two
    /// columns that are not set over side by side where the ends meet.
    /// </param>
    public static (int Count, float SizeMm) Fit(float aroundMm, float sizeMm, bool even = false)
    {
        if (aroundMm <= 0f || sizeMm <= 0f) return (0, sizeMm);

        int count = Math.Max(1, (int)MathF.Round(aroundMm / sizeMm));
        if (even) count = Math.Max(2, count + (count & 1));

        return (count, aroundMm / count);
    }

    /// <summary>An index on a lattice that repeats every <paramref name="count"/>; nought never repeats.</summary>
    public static int Index(int i, int count) => count > 0 ? ((i % count) + count) % count : i;

    /// <summary>A number from nought to one for a lattice point, the same every time it is asked.</summary>
    public static float Random(int x, int y, int salt)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)salt * 0xCB1AB31Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;

        return (h >> 8) / 16777216f;
    }

    /// <summary>A number from minus one to one.</summary>
    public static float Signed(int x, int y, int salt) => 2f * Random(x, y, salt) - 1f;

    /// <summary>
    /// Value noise from nought to one, one lattice cell per unit, repeating every
    /// <paramref name="periodX"/> cells across and <paramref name="periodY"/> up; nought for
    /// either means it does not repeat that way.
    /// </summary>
    public static float Noise(float x, float y, int periodX, int periodY, int salt)
    {
        float fx = MathF.Floor(x), fy = MathF.Floor(y);
        int ix = (int)fx, iy = (int)fy;

        // Smoothstep, so the value has no corners where one cell meets the next.
        float sx = x - fx, sy = y - fy;
        sx = sx * sx * (3f - 2f * sx);
        sy = sy * sy * (3f - 2f * sy);

        int x0 = Index(ix, periodX), x1 = Index(ix + 1, periodX);
        int y0 = Index(iy, periodY), y1 = Index(iy + 1, periodY);

        float a = Random(x0, y0, salt), b = Random(x1, y0, salt);
        float c = Random(x0, y1, salt), d = Random(x1, y1, salt);

        float low = a + (b - a) * sx;
        float high = c + (d - c) * sx;
        return low + (high - low) * sy;
    }

    /// <summary>
    /// Noise of a given grain in millimetres, its cell nudged so a whole number of them goes round.
    /// Built once per texture and read many times, so the fitting is not done per sample.
    /// </summary>
    public readonly struct Pattern
    {
        private readonly float acrossMm, upMm;
        private readonly int periodX, periodY, salt;

        /// <param name="aroundX">The way round across, or nought.</param>
        /// <param name="aroundY">The way round up, or nought.</param>
        public Pattern(float acrossMm, float upMm, float aroundX, float aroundY, int salt)
        {
            (periodX, this.acrossMm) = Fit(aroundX, acrossMm);
            (periodY, this.upMm) = Fit(aroundY, upMm);
            this.salt = salt;
        }

        /// <summary>From nought to one.</summary>
        public float At(float x, float y) => Noise(x / acrossMm, y / upMm, periodX, periodY, salt);

        /// <summary>From minus one to one.</summary>
        public float Signed(float x, float y) => 2f * At(x, y) - 1f;

        /// <summary>
        /// Two octaves, the second twice as fine and half as strong, from minus one to one. The
        /// finer one repeats twice as often, so it still meets itself.
        /// </summary>
        public float Rough(float x, float y) =>
            (2f * Noise(x / acrossMm, y / upMm, periodX, periodY, salt) - 1f) * (2f / 3f)
          + (2f * Noise(2f * x / acrossMm + 0.37f, 2f * y / upMm + 0.71f,
                        2 * periodX, 2 * periodY, salt + 1) - 1f) * (1f / 3f);
    }
}
