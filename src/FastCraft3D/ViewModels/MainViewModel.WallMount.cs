using System.Numerics;
using System.Windows.Input;
using FastCraft3D.Generators.Fasteners;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;

namespace FastCraft3D.ViewModels;

/// <summary>
/// Keyhole: keyholes cut into a face of the selected part, so it hangs on screws or on printed
/// wall studs. Worked as Emboss is - the face clicked, the keyholes placed on it with the same
/// handles, Apply to cut - but its own tool, since it also puts parts on the plate: the studs and
/// a template for drilling the wall.
/// </summary>
public partial class MainViewModel
{
    private bool isWallMountMode;
    private KeyholeOptions keyholes = new();
    private FacePatch? mountFace;
    private SurfacePlacement mountPlacement = SurfacePlacement.Middle;
    private ICommand? beginWallMount, applyWallMount, cancelWallMount;

    public ICommand BeginWallMountCommand => beginWallMount ??= Track(RelayCommand.Simple(BeginWallMount, () => Scene.Selection.Count == 1));
    public ICommand ApplyWallMountCommand => applyWallMount ??= AsyncRelayCommand.Simple(ApplyWallMount);
    public ICommand CancelWallMountCommand => cancelWallMount ??= RelayCommand.Simple(() => IsWallMountMode = false);

    /// <summary>Raised when the face, the keyholes or where they sit changes, for the preview to follow.</summary>
    public event Action? WallMountChanged;

    public bool IsWallMountMode
    {
        get => isWallMountMode;
        set
        {
            if (isWallMountMode == value) return;
            Set(ref isWallMountMode, value);
            if (!value)
            {
                mountFace = null;
                mountPlacement = SurfacePlacement.Middle;
            }

            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
            Raise(nameof(ShowManipulatorBar));
            Raise(nameof(HasWallMountFace));
            Raise(nameof(WallMountSummary));
            WallMountChanged?.Invoke();
            PlacementChanged?.Invoke();
        }
    }

    public bool HasWallMountFace => mountFace is not null;

    public FacePatch? WallMountFace => mountFace;

    private void BeginWallMount()
    {
        if (Scene.Selection.Count != 1) return;

        IsEmbossMode = false;
        IsEngraveMode = false;
        IsSplitMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        IsWallMountMode = true;
        Status = "Click the face of the part that goes against the wall";
    }

    /// <summary>A click on the part while the tool is out: the face the keyholes go into.</summary>
    public bool PickWallMountFace(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isWallMountMode) return false;

        if (EngraveState.FaceAt(target.ToWorldMesh(), worldPoint, worldNormal) is not { } face)
        {
            Status = "That is not a flat face - pick one of the flat sides";
            return false;
        }

        mountFace = face;
        mountPlacement = SurfacePlacement.Middle;
        Raise(nameof(HasWallMountFace));
        KeyholesChanged();
        return true;
    }

    public IPlacementSurface? WallMountSurface() => mountFace is null ? null : new PlanarSurface(mountFace);

    public SurfacePlacement WallMountPlacement
    {
        get => mountPlacement;
        set
        {
            if (mountPlacement == value) return;
            mountPlacement = value;
            WallMountChanged?.Invoke();
        }
    }

    /// <summary>
    /// Keyhole outlines as they lie on the face, placed. Up the face is where the slots run: the
    /// face's own up is whichever way it was worked out, so on an upright face it is turned to
    /// point up the wall.
    /// </summary>
    private IReadOnlyList<TextShape> Laid(List<TextShape> shapes)
    {
        if (mountFace is { } face && face.V.Z < -1e-3f)
            shapes = shapes.Select(s => new TextShape(s.Outline.Select(p => new Vector2(-p.X, -p.Y)).ToList(), [])).ToList();
        return mountPlacement.Apply(shapes);
    }

    private float MountClearance => CurrentPrinter.XyClearance;

    public Vector2 WallMountExtent => SurfacePlacement.Extent(Keyholes.Mouth(keyholes, MountClearance));

    /// <summary>A thin slab of the keyholes on the face, so where they go can be seen before they are cut.</summary>
    public Mesh? WallMountPreview()
    {
        if (WallMountSurface() is not { } surface || Keyholes.Problem(keyholes) is not null) return null;
        float clear = surface.ClearanceMm + 0.06f;
        return TextSolid.Build(Laid(Keyholes.Mouth(keyholes, MountClearance)), surface, clear, clear + 0.03f);
    }

    public string WallMountSummary =>
        Keyholes.Problem(keyholes)
        ?? (mountFace is null
            ? "Click the face of the part that goes against the wall."
            : $"{keyholes.Count} keyhole{(keyholes.Count == 1 ? "" : "s")} for {(keyholes.Studs ? "printed wall studs" : $"{keyholes.Head:0.#} mm screw heads")}, "
              + $"cut {keyholes.Depth:0.#} mm deep: the part has to be thicker than that. Drag the handles to place them; the slots run up from the round holes.");

    private void KeyholesChanged()
    {
        Raise(nameof(WallMountSummary));
        Raise(nameof(KeyholeStuds));
        Raise(nameof(ScrewKeyholes));
        WallMountChanged?.Invoke();
        PlacementChanged?.Invoke();
    }

    public int KeyholeCount { get => keyholes.Count; set { keyholes = keyholes with { Count = Math.Clamp(value, 1, 4) }; KeyholesChanged(); } }
    public float KeyholeApart { get => keyholes.Apart; set { keyholes = keyholes with { Apart = Math.Clamp(value, 5f, 500f) }; KeyholesChanged(); } }
    public float KeyholeHead { get => keyholes.Head; set { keyholes = keyholes with { Head = Math.Clamp(value, 3f, 20f) }; KeyholesChanged(); } }
    public float KeyholeShank { get => keyholes.Shank; set { keyholes = keyholes with { Shank = Math.Clamp(value, 1.5f, 10f) }; KeyholesChanged(); } }
    public float KeyholeSlot { get => keyholes.Slot; set { keyholes = keyholes with { Slot = Math.Clamp(value, 2f, 50f) }; KeyholesChanged(); } }
    public float KeyholeLip { get => keyholes.Lip; set { keyholes = keyholes with { Lip = Math.Clamp(value, 0.8f, 10f) }; KeyholesChanged(); } }
    public float KeyholeHeadRoom { get => keyholes.HeadRoom; set { keyholes = keyholes with { HeadRoom = Math.Clamp(value, 1.5f, 10f) }; KeyholesChanged(); } }
    public bool KeyholeStuds { get => keyholes.Studs; set { keyholes = keyholes with { Studs = value }; KeyholesChanged(); } }
    public float KeyholeWallScrew { get => keyholes.WallScrew; set { keyholes = keyholes with { WallScrew = Math.Clamp(value, 2f, 6f) }; KeyholesChanged(); } }
    /// <summary>Hung on screw heads rather than studs, for the screw rows to show.</summary>
    public bool ScrewKeyholes => !keyholes.Studs;

    public bool KeyholeTemplate { get => keyholes.Template; set { keyholes = keyholes with { Template = value }; KeyholesChanged(); } }

    /// <summary>
    /// Cuts the keyholes, and puts the studs and the template on the plate beside the part, as one
    /// undo step. The cut is the mouth from the face down to the full depth and the wider slot
    /// under the lip, taken out together.
    /// </summary>
    private async Task ApplyWallMount()
    {
        if (Scene.Selection.Count != 1 || WallMountSurface() is not { } surface)
        {
            Status = "Select the part, then click the face that goes against the wall";
            return;
        }

        if (Keyholes.Problem(keyholes) is { } problem)
        {
            Status = problem;
            return;
        }

        if (IsBusy) return;

        var source = Scene.Selection[0];
        var options = keyholes;
        var mouth = Laid(Keyholes.Mouth(options, MountClearance));
        var under = Laid(Keyholes.Undercut(options, MountClearance));

        var token = StartWork("Cutting keyholes");
        try
        {
            var world = source.ToWorldMesh();
            var cut = await Task.Run(() =>
            {
                var open = TextSolid.Build(mouth, surface, -options.Depth, surface.ClearanceMm + 0.3f);
                var behind = TextSolid.Build(under, surface, -options.Depth, -options.Lip);
                return ManifoldCsg.SubtractAll(world, [open, behind], token);
            });

            if (cut is null || cut.TriangleCount == 0 || !cut.CheckHealth().IsWatertight)
            {
                Status = "The keyholes would not cut cleanly - nothing was changed";
                return;
            }

            var added = new List<SceneObject>();
            if (options.Studs)
            {
                var stud = Keyholes.Stud(options);
                for (int i = 0; i < options.Count; i++)
                {
                    added.Add(new SceneObject(Scene.UniqueName("Wall stud"), stud) { Colour = NextAutomaticColour() }.Centred());
                }
            }

            if (options.Template)
                added.Add(new SceneObject(Scene.UniqueName("Drilling template"), Keyholes.Template(options)) { Colour = NextAutomaticColour() }.Centred());

            if (added.Count > 0)
            {
                // In a row, a gap between each - the studs were first put down on the template -
                // then the row beside what is on the plate, clear of it.
                float cursor = 0;
                foreach (var o in added)
                {
                    var b = o.WorldBounds;
                    o.Position += new Vector3(cursor - b.Min.X, -b.Center.Y, 0);
                    cursor += b.Size.X + 5f;
                }

                var taken = Scene.Objects.Where(o => !o.IsHidden).Select(o => o.WorldBounds).ToList();
                BedPlacement.Fit(added, 1f);
                var reach = BedPlacement.Reach(added);
                var free = BedPlacement.Clear(new Vector2(reach.Size.X, reach.Size.Y), taken, PlateWidth, PlateDepth);
                foreach (var o in added) o.Position += new Vector3(free.X - reach.Center.X, free.Y - reach.Center.Y, 0);
            }

            var result = new SceneObject(source.Name, cut) { Colour = source.Colour }.Centred();
            Undo.Execute(new ReplaceObjectsCommand("Cut keyholes", [source], [result, .. added]));
            IsWallMountMode = false;
            RefreshSelection();
            Status = $"Cut {options.Count} keyhole{(options.Count == 1 ? "" : "s")} into {source.Name}"
                     + (added.Count > 0 ? $", and added {string.Join(" and ", added.Select(o => o.Name))}" : "");
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"The keyholes could not be cut: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }
}
