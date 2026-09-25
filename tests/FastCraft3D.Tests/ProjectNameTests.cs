using System.IO;
using FastCraft3D.Io;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The name a project carries before anybody has named it.
///
/// A file name has one job - to be told apart from its neighbours at a glance - and a timestamp
/// does it badly. What matters here is only that the result is safe to be a file name and that
/// there are enough of them for two to be an unlucky coincidence rather than a daily event.
/// </summary>
public class ProjectNameTests
{
    [Fact]
    public void ANameIsTwoWordsAndTheDay()
    {
        var made = ProjectNames.Suggest(new DateTime(2026, 9, 24), new Random(1));

        Assert.EndsWith("_20260924", made);

        string words = made[..made.IndexOf('_')];
        Assert.Contains('-', words);
        Assert.Equal(2, words.Split('-').Length);
        Assert.All(words.Split('-'), part => Assert.NotEmpty(part));
    }

    /// <summary>
    /// It becomes a file name, so it has to be one. A space wants quoting, a capital is something
    /// two machines disagree about, and the rest of these are simply not allowed.
    /// </summary>
    [Fact]
    public void ANameIsSafeToPutOnADisk()
    {
        var awkward = Path.GetInvalidFileNameChars().Append(' ').ToArray();
        var random = new Random(7);

        for (int i = 0; i < 400; i++)
        {
            string made = ProjectNames.Suggest(DateTime.Now, random);

            Assert.Equal(-1, made.IndexOfAny(awkward));
            Assert.Equal(made.ToLowerInvariant(), made);
        }
    }

    /// <summary>
    /// Enough of them that two on one desk is bad luck. Two thousand is the same odds as a room of
    /// sixty sharing a birthday, which is the level nobody complains about.
    /// </summary>
    [Fact]
    public void ThereAreEnoughNamesToGoRound()
    {
        Assert.True(ProjectNames.Possibilities > 2000, $"only {ProjectNames.Possibilities} names");

        var random = new Random(3);
        var seen = new HashSet<string>();

        for (int i = 0; i < 300; i++) seen.Add(ProjectNames.Invent(random));

        // Not a claim about randomness, just that it is not handing out the same one every time.
        Assert.True(seen.Count > 250, $"{seen.Count} different names out of 300");
    }
}
