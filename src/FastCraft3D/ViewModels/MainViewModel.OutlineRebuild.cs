using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;

namespace FastCraft3D.ViewModels;

/// <summary>
/// Rebuild, keeping the shape: each object rebuilt from its corners, every flat face filled again
/// from the corners on its outline. See <see cref="OutlineRebuild"/>. Chosen in Rebuild's panel,
/// beside rebuilding from scratch. The objects stay the same objects - only their triangles
/// change - so this is one undo step that swaps meshes.
/// </summary>
public partial class MainViewModel
{
    private void RebuildKeepingShape(IReadOnlyList<SceneObject> objects, float tolerance, float? evenMm = null)
    {
        var targets = objects.Where(o => !o.IsLocked).ToList();
        if (targets.Count == 0) return;

        long bytes = targets.Sum(o => (long)o.Mesh.Positions.Count * 12 + (long)o.Mesh.Indices.Count * 4);

        Run(bytes >= SlowFromBytes, "Rebuilding...", () =>
        {
            var steps = new List<IUndoableCommand>();
            int before = 0, after = 0, left = 0;
            string? refusal = null;

            foreach (var o in targets)
            {
                // The tolerance is on the plate; the mesh is in its own coordinates, stretched by the scale.
                float scale = MathF.Max(MathF.Abs(o.Scale.X), MathF.Max(MathF.Abs(o.Scale.Y), MathF.Abs(o.Scale.Z)));
                scale = MathF.Max(scale, 1e-6f);
                var result = OutlineRebuild.Rebuild(o.Mesh, tolerance / scale);
                refusal ??= result.Refusal;
                var mesh = result.Mesh;

                // Fewest triangles first, then cut evenly: the corners are what the even cut has to
                // keep, and starting from the web would keep that too.
                if (evenMm is { } size)
                {
                    var even = EvenTriangles.Refine(mesh, size / scale);
                    refusal ??= even.Refusal;
                    mesh = even.Mesh;
                }

                if (ReferenceEquals(mesh, o.Mesh)) continue;

                before += o.Mesh.TriangleCount;
                after += mesh.TriangleCount;
                left += result.FacesLeft;
                steps.Add(new ReshapeCommand("Rebuild", o, o.Mesh, mesh));
            }

            if (steps.Count == 0)
            {
                Status = refusal is not null
                    ? $"Left as it was - {refusal}"
                    : "Nothing to rebuild - every point left is a corner";
                return;
            }

            Undo.Execute(steps.Count == 1 ? steps[0] : new CompoundCommand("Rebuild", steps));
            RefreshSelection();

            Status = $"Rebuilt, keeping the shape: {before:N0} triangles became {after:N0}"
                   + (left > 0 ? $" - {left:N0} faces with holes left as they were" : "");
        });
    }
}
