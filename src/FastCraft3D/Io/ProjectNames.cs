namespace FastCraft3D.Io;

/// <summary>
/// A name for a project nobody has named yet.
///
/// "model_20260924_194412" is not a name, it is a timestamp with a word in front of it, and a
/// folder of them cannot be read at a glance - which is the one job a file name has. Tinkercad
/// worked this out long ago: two words and a noun make something a person can point at across a
/// room, remember a day later, and say out loud on the telephone.
///
/// The date stays on the end, because the name is only there to be told apart from its neighbours
/// and the date is what sorts them. What goes away is having to read twelve digits to do it.
///
/// Deliberately gentle: nothing here can come out rude, boastful or oddly personal, because a
/// generated name ends up on somebody else's screen when the file is shared.
/// </summary>
public static class ProjectNames
{
    private static readonly string[] Manner =
    [
        "amazing", "brave", "bright", "busy", "calm", "cheerful", "chunky", "clever",
        "cosy", "crafty", "curious", "dandy", "eager", "epic", "fancy", "fluffy",
        "gentle", "gleaming", "grand", "happy", "hearty", "jolly", "keen", "kind",
        "lively", "lucky", "mighty", "neat", "nifty", "noble", "plucky", "proper",
        "quiet", "rapid", "shiny", "sleek", "smooth", "snug", "spiffy", "sturdy",
        "super", "swift", "tidy", "trusty", "wobbly", "zesty"
    ];

    private static readonly string[] Creature =
    [
        "albatross", "badger", "beetle", "bison", "bumblebee", "cheetah", "cobra", "crab",
        "dolphin", "falcon", "ferret", "finch", "gecko", "gerbil", "gibbon", "goose",
        "hamster", "heron", "ibex", "jackal", "koala", "lemur", "llama", "lobster",
        "magpie", "marmot", "meerkat", "moose", "narwhal", "newt", "ocelot", "otter",
        "panda", "pangolin", "penguin", "puffin", "quokka", "rhino", "robin", "salmon",
        "seal", "sparrow", "starfish", "stingray", "tapir", "toucan", "trout", "turtle",
        "walrus", "weasel", "wombat", "yak"
    ];

    /// <summary>How many different names there are, for anyone wondering how often two collide.</summary>
    public static int Possibilities => Manner.Length * Creature.Length;

    /// <summary>
    /// Two words joined by a hyphen, in lower case: "wobbly-narwhal".
    ///
    /// Lower case and hyphenated rather than title case and spaced, because this becomes a file
    /// name: a space in one is an argument waiting to be quoted wrong, and a capital is a thing
    /// two machines disagree about.
    /// </summary>
    public static string Invent(Random? random = null)
    {
        var pick = random ?? Random.Shared;

        return $"{Manner[pick.Next(Manner.Length)]}-{Creature[pick.Next(Creature.Length)]}";
    }

    /// <summary>
    /// The whole of a suggested name: the invented one with the day on the end, as
    /// "wobbly-narwhal_20260924".
    ///
    /// The day and not the minute. Two projects made the same afternoon are told apart by their
    /// names, which is what the names are for; the date is there to sort a folder by, and a folder
    /// sorts perfectly well by day.
    /// </summary>
    public static string Suggest(DateTime? on = null, Random? random = null) =>
        $"{Invent(random)}_{(on ?? DateTime.Now):yyyyMMdd}";
}
