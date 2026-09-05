using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.Io;

/// <summary>
/// Turns a selection of scene objects into what each file format expects.
///
/// Kept separate from the view model so the rules that decide what actually lands in an
/// exported file are reachable without opening a save dialog, and can be tested directly.
/// </summary>
public static class ExportComposer
{
    /// <summary>What the given options will actually write out.</summary>
    public static List<SceneObject> Subjects(Scene scene, ExportOptions options)
    {
        if (!options.SelectedOnly) return scene.Objects.ToList();

        var selection = scene.Selection;
        // Asking for the selection when there is none would silently export an empty file.
        return selection.Count > 0 ? selection.ToList() : scene.Objects.ToList();
    }

    /// <summary>
    /// STL has no concept of separate objects, so everything is baked to world space and
    /// concatenated into one triangle soup.
    ///
    /// Deliberately not welded across objects. Each object is already a closed shell, and
    /// merging their vertices would fuse two solids that merely touch - the shared faces
    /// would become interior walls shared by four triangles, which reads as non-manifold and
    /// is not what either solid meant. Slicers expect overlapping shells and union them.
    /// </summary>
    public static Mesh MergeForStl(IEnumerable<SceneObject> objects, bool dropToPlate = false)
    {
        var meshes = objects.Select(o => o.ToWorldMesh()).ToList();
        var merged = Mesh.Combine(meshes);
        return dropToPlate ? MeshTransform.AlignedToPlate(merged) : merged;
    }

    /// <summary>OBJ keeps objects named and separate, so no merging happens.</summary>
    public static List<ObjObject> ComposeForObj(IEnumerable<SceneObject> objects, bool dropToPlate = false)
    {
        var parts = objects
            .Select(o => (o.Name, Mesh: o.ToWorldMesh(), o.Colour))
            .ToList();

        if (dropToPlate)
        {
            // One shared offset, so the parts keep their positions relative to each other
            // instead of each being dropped onto the plate independently.
            float lift = PlateLift(parts.Select(p => p.Mesh));
            if (lift != 0f)
            {
                var shift = Matrix4x4.CreateTranslation(0, 0, lift);
                parts = parts
                    .Select(p => (p.Name, Mesh: MeshTransform.Transformed(p.Mesh, shift), p.Colour))
                    .ToList();
            }
        }

        return parts.Select(p => new ObjObject(p.Name, p.Mesh, p.Colour)).ToList();
    }

    /// <summary>How far everything must move up (or down) to rest on the build plate.</summary>
    public static float PlateLift(IEnumerable<Mesh> meshes)
    {
        var bounds = Bounds.Empty;
        foreach (var mesh in meshes)
            bounds = bounds.Union(mesh.ComputeBounds());
        return bounds.IsEmpty ? 0f : -bounds.Min.Z;
    }
}
