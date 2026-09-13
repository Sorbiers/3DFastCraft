namespace FastCraft3D.Io;

public enum ExportFormat
{
    BinaryStl,
    AsciiStl,
    Obj,
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
public readonly record struct ExportOptions(ExportFormat Format, bool SelectedOnly, bool DropToPlate)
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
