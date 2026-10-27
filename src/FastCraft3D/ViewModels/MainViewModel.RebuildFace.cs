using System.Numerics;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;

namespace FastCraft3D.ViewModels;

/// <summary>
/// Rebuild face: the face Surface info has read, laid again from its outline. A boolean leaves a
/// flat face cut into a web of thin triangles running out to points in the middle of it; this
/// takes the points out and fills the outline again, with nothing on the outline moved, so the
/// shape stays exactly as it was.
/// </summary>
public partial class MainViewModel
{
    private ICommand? rebuildFace;

    public ICommand RebuildFaceCommand => rebuildFace ??= RelayCommand.Simple(RebuildFace,
        () => isSurfaceInfoMode && infoFace is not null && infoTarget is { IsLocked: false } target
              && Scene.Objects.Contains(target));

    private void RebuildFace()
    {
        if (infoTarget is not { } target || infoFace is null) return;

        // Found again on the mesh as it is now rather than taken from the read: an undo since
        // then may have put another mesh in, and the face read would name triangles of that one.
        infoMeshes.Remove(target);
        if (SurfaceAt(target, infoPoint, infoNormal) is not { } face)
        {
            Status = "That face is not there any more - read it again";
            return;
        }

        long bytes = (long)target.Mesh.Positions.Count * 12 + (long)target.Mesh.Indices.Count * 4;

        Run(bytes >= SlowFromBytes, "Rebuilding the face...", () =>
        {
            var result = FaceRebuild.Rebuild(target.Mesh, face.Triangles);

            if (result.Refusal is { } why)
            {
                Status = $"Left the face as it was - {why}";
                return;
            }

            if (!result.Changed)
            {
                Status = "Nothing to rebuild - this face is already as lean as its outline allows";
                return;
            }

            Undo.Execute(new ReshapeCommand("Rebuild face", target, target.Mesh, result.Mesh));

            // The highlight and the numbers are of the old triangles; read the face again where it was.
            infoMeshes.Remove(target);
            infoFace = infoHover = SurfaceAt(target, infoPoint, infoNormal);
            RaiseSurfaceInfo();

            Status = result.PointsRemoved > 0
                ? $"Rebuilt the face: {result.TrianglesBefore:N0} triangles became {result.TrianglesAfter:N0}, {result.PointsRemoved:N0} points inside it gone"
                : $"Rebuilt the face: {result.TrianglesBefore:N0} triangles became {result.TrianglesAfter:N0}, laid less thin";
        });
    }

    private ICommand? cloneSurface;
    private float cloneThickness = SurfaceClone.DefaultThicknessMm;
    private float cloneGrow;

    /// <summary>
    /// How far Clone moves the outline out, in millimetres - in, below zero: a frame round an
    /// opening, a gasket. Holes go the other way. Kept for the session.
    /// </summary>
    public float CloneGrow
    {
        get => cloneGrow;
        set
        {
            if (!float.IsFinite(value) || cloneGrow == value) return;
            cloneGrow = value;
            Raise(nameof(CloneGrow));
        }
    }

    /// <summary>How thick Clone makes the face, in millimetres. Kept for the session.</summary>
    public float CloneThickness
    {
        get => cloneThickness;
        set
        {
            // Thinner than a hundredth is not a solid anything can print or a boolean can use.
            if (!float.IsFinite(value) || value < 0.01f || cloneThickness == value) return;
            cloneThickness = value;
            Raise(nameof(CloneThickness));
        }
    }

    public ICommand CloneSurfaceCommand => cloneSurface ??= RelayCommand.Simple(CloneSurface,
        () => isSurfaceInfoMode && infoFace is not null && infoTarget is { } target && Scene.Objects.Contains(target));

    /// <summary>
    /// The face read, added as an object of its own: a thin solid standing on the face, where the
    /// face is. See <see cref="SurfaceClone"/>.
    /// </summary>
    private void CloneSurface()
    {
        if (infoTarget is not { } target || infoFace is null) return;

        // On the plate as the object stands now, not as it stood when the face was read.
        infoMeshes.Remove(target);
        if (SurfaceAt(target, infoPoint, infoNormal) is not { } face)
        {
            Status = "That face is not there any more - read it again";
            return;
        }

        if (SurfaceClone.Extrude(face, cloneThickness, cloneGrow) is not { } mesh)
        {
            Status = cloneGrow != 0
                ? $"Could not clone the face grown by {unit.From(cloneGrow):0.##} {unit.Label} - its outline would vanish or cross itself"
                : "Could not clone the face - its outline does not close round it";
            return;
        }

        var clone = new SceneObject($"{target.Name} surface", mesh)
        {
            Colour = target.Colour,
            Filament = target.Filament,
            Plate = target.Plate
        }.Centred();

        // Adding selects it, and the boxes are told: the clone is the thing to work on next -
        // to move, to resize, to subtract.
        Undo.Execute(new AddObjectsCommand("Clone surface", [clone]));
        RefreshSelection();
        Status = $"Cloned the face as {clone.Name}, {unit.From(cloneThickness):0.##} {unit.Label} thick - it is selected";
    }

    /// <summary>The face's triangle count, and how many points sit inside it rather than on its outline.</summary>
    private static string TrianglesOf(FacePatch face)
    {
        var outline = new HashSet<int>();
        foreach (var (a, b) in face.Boundary)
        {
            outline.Add(a);
            outline.Add(b);
        }

        var inside = new HashSet<int>();
        foreach (int t in face.Triangles)
            for (int k = 0; k < 3; k++)
            {
                int v = face.Mesh.Indices[t + k];
                if (!outline.Contains(v)) inside.Add(v);
            }

        return inside.Count > 0
            ? $"{face.Triangles.Count:N0} ({inside.Count:N0} points inside)"
            : $"{face.Triangles.Count:N0}";
    }
}
