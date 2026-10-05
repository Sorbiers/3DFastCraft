namespace FastCraft3D.ViewModels;

/// <summary>
/// How much of the ribbon is shown.
///
/// Only ribbon buttons are ever hidden - never a tool's own settings, and never a key - so
/// nothing a project holds and no habit anybody has depends on which is chosen. There was a third
/// level, Extended, for the specialised and experimental tools; it was one more thing to choose
/// between for the sake of a dozen buttons, and they are in Advanced now.
/// </summary>
public enum UiLevel
{
    /// <summary>Only the tools 3D Builder had, for anyone carrying on where it left off.</summary>
    Classic,

    /// <summary>Every tool there is.</summary>
    Advanced
}

/// <summary>A choice in the mode switch.</summary>
/// <param name="Level">How much it shows.</param>
/// <param name="Name">As the switch says it.</param>
/// <param name="Colour">The dot beside the name.</param>
/// <param name="Hint">What it shows, for the tooltip.</param>
public sealed record UiModeChoice(UiLevel Level, string Name, string Colour, string Hint);
