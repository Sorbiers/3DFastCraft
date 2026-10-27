namespace FastCraft3D.Io;

public enum ExportFormat
{
    BinaryStl,
    AsciiStl,
    Obj,

    // 3MF only a plate per file. The project is a 3MF of everything, so Save gives a slicer that
    // with its parts, names, colors and filaments, and a second way to write the same file only
    // asked which one to use. What Save cannot do is a plate apiece: Bambu Studio keeps plates
    // but reads them only from its own projects, and takes any other 3MF onto its first plate.
    ThreeMf
}

/// <param name="SelectedOnly">
/// Export just the selection instead of the whole plate. Off by default: quietly exporting
/// only what happened to be selected is how half a model reaches a slicer unnoticed.
/// </param>
/// <param name="DropToPlate">
/// Shift the geometry so its lowest point rests on Z = 0. The whole export moves together, so
/// parts keep their positions relative to each other.
/// </param>
/// <param name="ByPlate">A file for each build plate in use, named with its number.</param>
public readonly record struct ExportOptions(ExportFormat Format, bool SelectedOnly, bool DropToPlate, bool ByPlate = false)
{
    public bool IsObj => Format == ExportFormat.Obj;

    public string Extension => Format switch
    {
        ExportFormat.Obj => ".obj",
        ExportFormat.ThreeMf => ".3mf",
        _ => ".stl"
    };

    public string Filter => Format switch
    {
        ExportFormat.Obj => "Wavefront OBJ (*.obj)|*.obj",
        ExportFormat.ThreeMf => "3D Manufacturing Format (*.3mf)|*.3mf",
        ExportFormat.AsciiStl => "ASCII STL (*.stl)|*.stl",
        _ => "Binary STL (*.stl)|*.stl"
    };
}
