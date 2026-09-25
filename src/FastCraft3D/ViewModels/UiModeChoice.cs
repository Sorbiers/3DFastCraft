namespace FastCraft3D.ViewModels;

/// <summary>
/// How much of the ribbon is shown.
///
/// Three steps rather than two, because "everything" had grown to mean two different audiences:
/// somebody modelling a part wants the sketches, the holes and the threads, and does not want the
/// pivot, the session recorder and the beta tools in the way of them. Only ribbon buttons are ever
/// hidden - never a tool's own settings, and never a key - so nothing a project holds and no habit
/// anybody has depends on which of the three is chosen.
/// </summary>
public enum UiLevel
{
    /// <summary>Only the tools 3D Builder had, for anyone carrying on where it left off.</summary>
    Classic,

    /// <summary>The working set: everything that is settled and in regular use.</summary>
    Advanced,

    /// <summary>And the rest - the specialised, the experimental and the rarely wanted.</summary>
    Extended
}

/// <summary>A choice in the mode switch.</summary>
/// <param name="Level">How much it shows.</param>
/// <param name="Name">As the switch says it.</param>
/// <param name="Colour">The dot beside the name.</param>
/// <param name="Hint">What it shows, for the tooltip.</param>
public sealed record UiModeChoice(UiLevel Level, string Name, string Colour, string Hint);
