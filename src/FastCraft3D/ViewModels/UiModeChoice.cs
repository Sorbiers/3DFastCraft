namespace FastCraft3D.ViewModels;

/// <summary>A choice in the mode switch: Classic, or Advanced.</summary>
/// <param name="Advanced">Whether it shows every tool.</param>
/// <param name="Name">As the switch says it.</param>
/// <param name="Colour">The dot beside the name.</param>
/// <param name="Hint">What it shows, for the tooltip.</param>
public sealed record UiModeChoice(bool Advanced, string Name, string Colour, string Hint);
