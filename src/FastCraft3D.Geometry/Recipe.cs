using System.Numerics;

namespace FastCraft3D.Geometry;

/// <summary>
/// How a part was made, so it can be made again with different numbers.
///
/// Before this, a generated part became a plain mesh the moment it was added: a box printed with
/// its lid a tenth too tight was made again from nothing, and placed again, and coloured again.
///
/// Plain data, with nothing of the generator in it, so the scene and the project file can carry
/// it without knowing what a generator is. The generator library reads the settings back.
/// </summary>
/// <param name="Generator">The generator's Id, which never changes once shipped.</param>
/// <param name="Version">The generator's Version when this was made.</param>
/// <param name="Settings">Every setting, as JSON, with the numbers actually used.</param>
/// <param name="Role">Which part of a set this is: "box", "lid". Null for a part made alone.</param>
/// <param name="Set">Shared by every part made together, so editing one rebuilds them all.</param>
/// <param name="Origin">
/// Where the generator's own origin is in the object's mesh coordinates: nought as it was made,
/// moved when the object's pivot is. A re-made part is put with its origin there, so a box made
/// wider grows about where it stood rather than about the middle of its old outline.
/// </param>
public sealed record Recipe(
    string Generator,
    int Version,
    string Settings,
    string? Role = null,
    string? Set = null,
    Vector3 Origin = default);
