using System.Numerics;
using FastCraft3D.Generators.Lithophanes;
using Xunit;

namespace FastCraft3D.Tests;

public class MoonMapTests
{
    /// <summary>A direction out of the lamp for a place on the moon: the near side to the front, east to plus X.</summary>
    private static Vector3 At(float latitude, float longitude)
    {
        float lat = latitude * MathF.PI / 180f, lon = (longitude - 90f) * MathF.PI / 180f;
        return new Vector3(MathF.Cos(lat) * MathF.Cos(lon), MathF.Cos(lat) * MathF.Sin(lon), MathF.Sin(lat));
    }

    [Fact]
    public void TheSeasOnTheNearSideComeOutThickAndTheHighlandsThin()
    {
        var tone = Moon.Photographed(
        [
            At(33, -16),   // Mare Imbrium
            At(17, 59),    // Mare Crisium
            At(8, 31),     // Mare Tranquillitatis
            At(-40, 10),   // the southern highlands, round Tycho
            -Vector3.UnitY, // the middle of the near side, looking straight out of the front
            At(-20, -15),  // Mare Nubium
            At(-20, 15),   // highlands, where Nubium would be mirrored east for west
            At(-33, -16)   // highlands, where Imbrium would be mirrored north for south
        ]);

        Assert.True(tone[0] > tone[3] + 0.3f, $"Imbrium {tone[0]:0.##}, highlands {tone[3]:0.##}");
        Assert.True(tone[1] > tone[3] + 0.3f, $"Crisium {tone[1]:0.##}, highlands {tone[3]:0.##}");
        Assert.True(tone[2] > tone[3] + 0.3f, $"Tranquillitatis {tone[2]:0.##}, highlands {tone[3]:0.##}");

        // The right way round: neither mirrored east for west nor upside down.
        Assert.True(tone[5] > tone[6] + 0.2f, $"Nubium {tone[5]:0.##}, its mirror {tone[6]:0.##}");
        Assert.True(tone[0] > tone[7] + 0.2f, $"Imbrium {tone[0]:0.##}, its mirror {tone[7]:0.##}");
    }
}
