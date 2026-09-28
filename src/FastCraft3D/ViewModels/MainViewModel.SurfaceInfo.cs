using System.Numerics;
using System.Windows.Input;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.ViewModels;

/// <summary>One line of what Surface info read off a face.</summary>
public sealed record SurfaceFact(string Label, string Value);

/// <summary>
/// Surface info: where a face is and how big - its middle, which way it faces, its size and area -
/// read off by pointing at it. The face under the pointer lights up; a click reads it. Nothing is
/// changed, so the tool stays out until Cancel and every click reads another face, on any object.
/// </summary>
public partial class MainViewModel
{
    private bool isSurfaceInfoMode;
    private FacePatch? infoHover, infoFace;
    private string? infoObject;
    private ICommand? beginSurfaceInfo, cancelSurfaceInfo;

    /// <summary>
    /// Each object's mesh in the world, made once for the tool's time out. Hovering asks for it on
    /// every move of the pointer, and nothing can move while the tool is out.
    /// </summary>
    private readonly Dictionary<SceneObject, Mesh> infoMeshes = [];

    public ICommand BeginSurfaceInfoCommand => beginSurfaceInfo ??= Track(RelayCommand.Simple(BeginSurfaceInfo, () => Scene.Objects.Count > 0));
    public ICommand CancelSurfaceInfoCommand => cancelSurfaceInfo ??= RelayCommand.Simple(() => IsSurfaceInfoMode = false);

    /// <summary>Raised when the face hovered or read changes, for the viewport to light it.</summary>
    public event Action? SurfaceInfoChanged;

    public bool IsSurfaceInfoMode
    {
        get => isSurfaceInfoMode;
        set
        {
            if (isSurfaceInfoMode == value) return;
            Set(ref isSurfaceInfoMode, value);
            if (!value)
            {
                infoHover = infoFace = null;
                infoObject = null;
                infoMeshes.Clear();
                RaiseSurfaceInfo();
            }

            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
            Raise(nameof(ShowManipulatorBar));
        }
    }

    /// <summary>The face to light: the one under the pointer, or else the one last read.</summary>
    public FacePatch? SurfaceInfoFace => infoHover ?? infoFace;

    /// <summary>Whether the lit face is the one read, which is drawn in the picked colour.</summary>
    public bool SurfaceInfoShowsPicked => infoFace is not null && (infoHover is null || ReferenceEquals(infoHover, infoFace));

    public bool HasSurfaceInfo => infoFace is not null;

    /// <summary>The middle of the face read, where the numbers are measured from.</summary>
    public IReadOnlyList<Vector3> SurfaceInfoAnchors => infoFace is { } f ? [f.ToLocal((f.Min + f.Max) * 0.5f)] : [];

    private void RaiseSurfaceInfo()
    {
        Raise(nameof(HasSurfaceInfo));
        Raise(nameof(SurfaceFacts));
        SurfaceInfoChanged?.Invoke();
    }

    private void BeginSurfaceInfo()
    {
        IsSplitMode = false;
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsPivotMode = false;
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        IsWallMountMode = false;

        infoHover = infoFace = null;
        infoObject = null;
        infoMeshes.Clear();
        IsSurfaceInfoMode = true;
        RaiseSurfaceInfo();
        Status = "Point at a face on any object to see it, click to read it; Cancel when done";
    }

    private FacePatch? SurfaceAt(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!infoMeshes.TryGetValue(target, out var world))
            infoMeshes[target] = world = target.ToWorldMesh();
        return FacePatch.Find(world, worldPoint, worldNormal);
    }

    public void HoverSurfaceInfo(SceneObject? target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isSurfaceInfoMode) return;

        var face = target is null ? null : SurfaceAt(target, worldPoint, worldNormal);

        // The same face as before is left as it is: finding it again gives a new object for it,
        // which would redraw the highlight on every move of the pointer across it.
        if (face is null && infoHover is null) return;
        if (face is not null && infoHover is not null && SameSurface(face, infoHover)) return;

        infoHover = face is not null && infoFace is not null && SameSurface(face, infoFace) ? infoFace : face;
        SurfaceInfoChanged?.Invoke();
    }

    private static bool SameSurface(FacePatch a, FacePatch b) =>
        ReferenceEquals(a.Mesh, b.Mesh) && a.Triangles.Count == b.Triangles.Count
        && a.Triangles.Count > 0 && a.Triangles[0] == b.Triangles[0];

    public bool PickSurfaceInfo(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isSurfaceInfoMode) return false;

        if (SurfaceAt(target, worldPoint, worldNormal) is not { } face)
        {
            Status = "Nothing to read there - point at a face";
            return true;
        }

        infoFace = infoHover = face;
        infoObject = target.Name;
        RaiseSurfaceInfo();
        Status = $"Read a face on {target.Name}";
        return true;
    }

    /// <summary>What the face read is, line by line, in the unit the boxes are in.</summary>
    public IReadOnlyList<SurfaceFact> SurfaceFacts
    {
        get
        {
            if (infoFace is not { } face) return [];

            string L(float mm) => unit.From(mm).ToString("0.##");
            var middle = face.ToLocal((face.Min + face.Max) * 0.5f);
            var n = face.Normal;

            var low = new Vector3(float.MaxValue);
            var high = new Vector3(float.MinValue);
            foreach (int t in face.Triangles)
                for (int k = 0; k < 3; k++)
                {
                    var p = face.Mesh.Positions[face.Mesh.Indices[t + k]];
                    low = Vector3.Min(low, p);
                    high = Vector3.Max(high, p);
                }

            float tilt = MathF.Acos(Math.Clamp(n.Z, -1f, 1f)) * 180f / MathF.PI;
            string facing = tilt < 0.5f ? "up, level"
                : tilt > 179.5f ? "down, level"
                : MathF.Abs(tilt - 90f) < 0.5f ? $"sideways, upright - {Heading(n)}"
                : tilt < 90f ? $"up, {90f - tilt:0.#}° off upright - {Heading(n)}"
                : $"down, {tilt - 90f:0.#}° off upright - {Heading(n)}";

            float area = unit.From(unit.From(face.Area));
            var facts = new List<SurfaceFact>
            {
                new("Object", infoObject ?? ""),
                new("Middle", $"X {L(middle.X)}   Y {L(middle.Y)}   Z {L(middle.Z)} {unit.Label}"),
                new("Faces", facing),
                new("Normal", $"{n.X:0.###}, {n.Y:0.###}, {n.Z:0.###}"),
                new("Size", $"{L(face.Size.X)} × {L(face.Size.Y)} {unit.Label}"),
                new("Area", $"{area:0.##} {unit.Label}²"),
                new("From", $"X {L(low.X)}   Y {L(low.Y)}   Z {L(low.Z)}"),
                new("To", $"X {L(high.X)}   Y {L(high.Y)}   Z {L(high.Z)}")
            };

            if (modelScale > 1.001f)
                facts.Add(new("Real size", $"{ToReal(face.Size.X):0.###} × {ToReal(face.Size.Y):0.###} {RealUnit}"));

            return facts;
        }
    }

    /// <summary>Which way a face looks across the plate, as the views name the sides.</summary>
    private static string Heading(Vector3 n)
    {
        float angle = MathF.Atan2(n.Y, n.X) * 180f / MathF.PI;
        string side = angle switch
        {
            >= -22.5f and < 22.5f => "to the right (+X)",
            >= 22.5f and < 67.5f => "to the back right",
            >= 67.5f and < 112.5f => "to the back (+Y)",
            >= 112.5f and < 157.5f => "to the back left",
            >= -67.5f and < -22.5f => "to the front right",
            >= -112.5f and < -67.5f => "to the front (-Y)",
            >= -157.5f and < -112.5f => "to the front left",
            _ => "to the left (-X)"
        };
        return side;
    }
}
