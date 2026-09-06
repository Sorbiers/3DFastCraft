using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;

namespace FastCraft3D.ViewModels;

/// <summary>
/// What the engrave panel needs to know: which face was picked, what pattern is set up for it,
/// and whether the settings will actually print.
///
/// Kept apart from the view model because it is the whole of the feature's state and none of it
/// outlives the operation - and because the printing advice is worth testing on its own.
/// </summary>
public sealed class EngraveState
{
    /// <summary>Two 0.2 mm layers. Shallower than this and an FDM print will not show it.</summary>
    public const float FdmMinimumDepth = 0.4f;

    /// <summary>Two passes of a 0.4 mm nozzle. Narrower and the slicer drops the groove.</summary>
    public const float FdmMinimumWidth = 0.8f;

    public FacePatch? Face { get; private set; }

    /// <summary>The selected object baked to world space, which is what gets engraved.</summary>
    public Mesh? WorldMesh { get; private set; }

    public EngraveOptions Options { get; set; } = EngraveOptions.Default;

    public bool HasFace => Face is not null;

    /// <summary>Remembers the picked face and the mesh it came from.</summary>
    public void Pick(Mesh worldMesh, FacePatch face)
    {
        WorldMesh = worldMesh;
        Face = face;
    }

    public void Clear()
    {
        WorldMesh = null;
        Face = null;
    }

    /// <summary>
    /// The grooves the current settings would cut, for drawing on the face. Empty until a face
    /// has been picked.
    /// </summary>
    public GrooveSet Preview() =>
        Face is null ? GrooveSet.Empty : Engraver.Pattern(Face, Options);

    /// <summary>The face's size and how many grooves the current settings would cut into it.</summary>
    public string Describe()
    {
        if (Face is null) return "Click the face you want to engrave.";

        var size = Face.Size;
        int grooves = Engraver.CountGrooves(Face, Options);

        return Options.Raised
            ? $"Face {size.X:0.#} x {size.Y:0.#} mm - about {grooves:N0} pieces standing {Options.Depth:0.##} mm proud"
            : $"Face {size.X:0.#} x {size.Y:0.#} mm - about {grooves:N0} grooves at {Options.Depth:0.##} mm deep";
    }

    /// <summary>
    /// What is likely to go wrong, in the order it matters. Empty when the settings are sound.
    ///
    /// Depth is the setting worth guarding: a groove shallower than a couple of layers simply
    /// does not survive slicing, and one deeper than the wall goes straight through it.
    /// </summary>
    public string Advice()
    {
        if (Face is null || WorldMesh is null) return "";

        var options = Options.Sane();
        float behind = Engraver.MaterialBehind(WorldMesh, Face);

        // Standing proud adds material rather than taking it away, so none of the advice about
        // cutting through the wall applies - and a raised line survives slicing where a groove of
        // the same size is dropped, since the nozzle lays it down rather than having to miss it.
        if (options.Raised)
        {
            return options.Depth < 0.2f
                ? $"{options.Depth:0.##} mm is under one layer, so it will not show on an FDM print."
                : "";
        }

        if (options.Depth >= behind)
            return $"{options.Depth:0.##} mm is deeper than the {behind:0.#} mm of material behind this face - it will cut right through.";

        if (options.Depth > behind * 0.5f)
            return $"{options.Depth:0.##} mm removes over half the {behind:0.#} mm thickness here.";

        if (options.Depth < FdmMinimumDepth)
            return $"{options.Depth:0.##} mm is under two 0.2 mm layers, so an FDM print will lose it. Fine on resin.";

        if (options.GrooveWidth < FdmMinimumWidth)
            return $"{options.GrooveWidth:0.##} mm lines are narrower than two passes of a 0.4 mm nozzle and may not slice.";

        if (Engraver.CountGrooves(Face, options) > GroovePattern.MaximumGrooves / 2)
            return "That is a very fine pattern for a face this size, and it will take a while.";

        return "";
    }

    /// <summary>Picks the face under a click, both arguments in world space.</summary>
    public static FacePatch? FaceAt(Mesh worldMesh, Vector3 point, Vector3 normal) =>
        FacePatch.Find(worldMesh, point, normal);
}
