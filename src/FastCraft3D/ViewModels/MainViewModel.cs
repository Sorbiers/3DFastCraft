using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using FastCraft3D.View;
using Microsoft.Win32;

namespace FastCraft3D.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly Vector3[] Palette =
    [
        new(0.30f, 0.55f, 0.85f), new(0.85f, 0.45f, 0.30f), new(0.40f, 0.72f, 0.45f),
        new(0.75f, 0.40f, 0.70f), new(0.90f, 0.72f, 0.25f), new(0.35f, 0.70f, 0.75f)
    ];

    private int colourCursor;
    private string status = "Ready";
    private bool isBusy;
    private string? projectPath;
    private SceneObject? selected;
    private Axis splitAxis = Axis.Z;
    private float splitOffset;
    private SplitKeep splitKeep = SplitKeep.Both;
    private bool isSplitMode;
    private Vector3 splitNormal = Vector3.UnitZ;
    private readonly List<SceneObject> clipboard = new();
    private GizmoMode gizmoMode = GizmoMode.Move;
    private bool uniformScale = true;
    private bool snapRotation = true;
    private bool stickySelection = true;
    private bool isSelectionMenuOpen = true;

    public MainViewModel()
    {
        Scene = new Scene();
        Undo = new UndoStack(Scene);
        Undo.Changed += () => Raise(nameof(UndoLabel));
        Scene.Objects.CollectionChanged += (_, _) => RefreshSelection();

        InsertCommand = new RelayCommand(p => Insert(p));
        DeleteCommand = RelayCommand.Simple(Delete, () => Scene.Selection.Count > 0);
        DuplicateCommand = RelayCommand.Simple(Duplicate, () => Scene.Selection.Count > 0);
        MirrorCommand = new RelayCommand(p => Mirror(p), _ => Scene.Selection.Count > 0);
        AlignToPlateCommand = RelayCommand.Simple(AlignToPlate, () => Scene.Selection.Count > 0);
        SelectAllCommand = RelayCommand.Simple(SelectAll, () => Scene.Objects.Count > 0);
        InvertSelectionCommand = RelayCommand.Simple(InvertSelection, () => Scene.Objects.Count > 0);
        GroupCommand = RelayCommand.Simple(Group, () => Scene.Selection.Count > 1);
        UngroupCommand = RelayCommand.Simple(Ungroup, () => Scene.Selection.Count > 0);
        DeselectAllCommand = RelayCommand.Simple(
            () => { Scene.ClearSelection(); RefreshSelection(); },
            () => Scene.Selection.Count > 0);

        BooleanCommand = new AsyncRelayCommand(p => RunBoolean(p), _ => Scene.Selection.Count >= 2);
        BeginSplitCommand = RelayCommand.Simple(BeginSplit, () => Scene.Selection.Count == 1);
        ApplySplitCommand = AsyncRelayCommand.Simple(ApplySplit, () => IsSplitMode);
        CancelSplitCommand = RelayCommand.Simple(() => IsSplitMode = false);

        UndoCommand = RelayCommand.Simple(() => { Undo.Undo(); RefreshSelection(); }, () => Undo.CanUndo);
        RedoCommand = RelayCommand.Simple(() => { Undo.Redo(); RefreshSelection(); }, () => Undo.CanRedo);

        NewCommand = RelayCommand.Simple(NewScene);
        OpenCommand = RelayCommand.Simple(OpenProject);
        SaveCommand = RelayCommand.Simple(() => SaveProject(saveAs: false));
        SaveAsCommand = RelayCommand.Simple(() => SaveProject(saveAs: true));
        SetMoveModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Move);
        SetRotateModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Rotate);
        SetScaleModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Scale);
        RoundCommand = RelayCommand.Simple(RoundSelection, () => Scene.Selection.Any(o => o.CanRound));
        CopyCommand = RelayCommand.Simple(Copy, () => Scene.Selection.Count > 0);
        PasteCommand = RelayCommand.Simple(Paste, () => clipboard.Count > 0);
        ImportCommand = RelayCommand.Simple(Import);
        ExportCommand = RelayCommand.Simple(Export, () => Scene.Objects.Count > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Scene Scene { get; }
    public UndoStack Undo { get; }

    public System.Windows.Input.ICommand InsertCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand DuplicateCommand { get; }
    public System.Windows.Input.ICommand MirrorCommand { get; }
    public System.Windows.Input.ICommand AlignToPlateCommand { get; }
    public System.Windows.Input.ICommand SelectAllCommand { get; }
    public System.Windows.Input.ICommand InvertSelectionCommand { get; }
    public System.Windows.Input.ICommand GroupCommand { get; }
    public System.Windows.Input.ICommand UngroupCommand { get; }
    public System.Windows.Input.ICommand DeselectAllCommand { get; }
    public System.Windows.Input.ICommand BooleanCommand { get; }
    public System.Windows.Input.ICommand BeginSplitCommand { get; }
    public System.Windows.Input.ICommand ApplySplitCommand { get; }
    public System.Windows.Input.ICommand CancelSplitCommand { get; }
    public System.Windows.Input.ICommand UndoCommand { get; }
    public System.Windows.Input.ICommand RedoCommand { get; }
    public System.Windows.Input.ICommand NewCommand { get; }
    public System.Windows.Input.ICommand OpenCommand { get; }
    public System.Windows.Input.ICommand SaveCommand { get; }
    public System.Windows.Input.ICommand SaveAsCommand { get; }
    public System.Windows.Input.ICommand SetMoveModeCommand { get; }
    public System.Windows.Input.ICommand SetRotateModeCommand { get; }
    public System.Windows.Input.ICommand SetScaleModeCommand { get; }
    public System.Windows.Input.ICommand RoundCommand { get; }
    public System.Windows.Input.ICommand CopyCommand { get; }
    public System.Windows.Input.ICommand PasteCommand { get; }
    public System.Windows.Input.ICommand ImportCommand { get; }
    public System.Windows.Input.ICommand ExportCommand { get; }

    /// <summary>Raised when geometry changed enough that the camera should reframe.</summary>
    public event Action? ZoomExtentsRequested;

    /// <summary>Raised after the model's selection changes, so the object list can mirror it.</summary>
    public event Action? SelectionChanged;

    public SceneObject? Selected
    {
        get => selected;
        private set
        {
            selected = value;
            Raise(nameof(Selected));
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionSummary));
        }
    }

    /// <summary>One object selected - the numeric fields in the manipulator bar need exactly one.</summary>
    public bool HasSelection => selected is not null;

    /// <summary>Anything selected. The handles work on a group even when the fields cannot.</summary>
    public bool HasAnySelection => Scene.Selection.Count > 0;

    public string Status
    {
        get => status;
        set => Set(ref status, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => Set(ref isBusy, value);
    }

    public string UndoLabel => Undo.NextUndoLabel is { } label ? $"Undo {label}" : "Undo";

    public string SelectionSummary
    {
        get
        {
            var selection = Scene.Selection;
            if (selection.Count == 0) return $"{Scene.Objects.Count} object(s) on the plate";
            if (selection.Count > 1) return $"{selection.Count} objects selected";

            var o = selection[0];
            var health = o.ToWorldMesh().CheckHealth();
            var bounds = o.WorldBounds;
            return $"{o.Name} - {bounds} - {health.TriangleCount:N0} triangles - " +
                   $"{health.VolumeCm3:0.##} cm3 - {health.Describe()}";
        }
    }

    // --- Selection options -------------------------------------------------------------

    /// <summary>
    /// On - the default, as in 3D Builder - a click adds or removes just the object clicked and
    /// leaves the rest of the selection alone. Off, a click selects only what was clicked and
    /// clicking empty space clears the selection.
    /// </summary>
    public bool StickySelection
    {
        get => stickySelection;
        set
        {
            Set(ref stickySelection, value);
            Status = value
                ? "Sticky selection on - clicking an object adds or removes just that object"
                : "Sticky selection off - clicking an object selects only that object";
        }
    }

    /// <summary>Whether the selection menu on the right of the viewport is expanded.</summary>
    public bool IsSelectionMenuOpen
    {
        get => isSelectionMenuOpen;
        set => Set(ref isSelectionMenuOpen, value);
    }

    // --- Manipulator state -----------------------------------------------------------

    /// <summary>
    /// Which handles the viewport shows. Exposed as three booleans as well, because the mode
    /// buttons are toggles and WPF has no built-in enum-to-bool binding.
    /// </summary>
    public GizmoMode GizmoMode
    {
        get => gizmoMode;
        set
        {
            if (gizmoMode == value) return;
            gizmoMode = value;
            Raise(nameof(GizmoMode));
            Raise(nameof(IsMoveMode));
            Raise(nameof(IsRotateMode));
            Raise(nameof(IsScaleMode));
            Status = value switch
            {
                GizmoMode.Move => "Move: drag an arrow to slide along that axis",
                GizmoMode.Rotate => "Rotate: drag a ring to turn around that axis",
                _ => "Resize: drag an arrow to change that dimension"
            };
        }
    }

    public bool IsMoveMode
    {
        get => gizmoMode == GizmoMode.Move;
        set { if (value) GizmoMode = GizmoMode.Move; }
    }

    public bool IsRotateMode
    {
        get => gizmoMode == GizmoMode.Rotate;
        set { if (value) GizmoMode = GizmoMode.Rotate; }
    }

    public bool IsScaleMode
    {
        get => gizmoMode == GizmoMode.Scale;
        set { if (value) GizmoMode = GizmoMode.Scale; }
    }

    /// <summary>Resize all three axes together, keeping the shape proportional.</summary>
    public bool UniformScale
    {
        get => uniformScale;
        set => Set(ref uniformScale, value);
    }

    /// <summary>Snap rotation to 15 degree steps.</summary>
    public bool SnapRotation
    {
        get => snapRotation;
        set => Set(ref snapRotation, value);
    }

    // --- Split plane state -----------------------------------------------------------

    public bool IsSplitMode
    {
        get => isSplitMode;
        set
        {
            Set(ref isSplitMode, value);
            Raise(nameof(SplitPlaneVisible));
        }
    }

    public bool SplitPlaneVisible => isSplitMode;

    public Axis SplitAxis
    {
        get => splitAxis;
        set
        {
            Set(ref splitAxis, value);
            SplitNormal = PlaneSplit.NormalFor(value);
            ResetSplitOffset();
        }
    }

    /// <summary>
    /// Which way the split plane faces. The axis buttons set it, and the tilt rings turn it, so
    /// the plane is not limited to the three axis-aligned orientations.
    /// </summary>
    public Vector3 SplitNormal
    {
        get => splitNormal;
        set
        {
            splitNormal = Vector3.Normalize(value);
            Raise(nameof(SplitNormal));
            Raise(nameof(KeepFrontLabel));
            Raise(nameof(KeepBackLabel));
        }
    }

    /// <summary>
    /// Names the two halves after the direction the plane actually faces, so the buttons read
    /// as "Top" and "Bottom" for a horizontal plane rather than an abstract front and back.
    /// </summary>
    public string KeepFrontLabel => DominantAxisLabels().Positive;

    public string KeepBackLabel => DominantAxisLabels().Negative;

    private (string Positive, string Negative) DominantAxisLabels()
    {
        float x = MathF.Abs(splitNormal.X), y = MathF.Abs(splitNormal.Y), z = MathF.Abs(splitNormal.Z);

        if (z >= x && z >= y) return splitNormal.Z >= 0 ? ("Top", "Bottom") : ("Bottom", "Top");
        if (y >= x) return splitNormal.Y >= 0 ? ("Back", "Front") : ("Front", "Back");
        return splitNormal.X >= 0 ? ("Right", "Left") : ("Left", "Right");
    }

    public float SplitOffset
    {
        get => splitOffset;
        set => Set(ref splitOffset, value);
    }

    public float SplitMinimum { get; private set; } = -100;
    public float SplitMaximum { get; private set; } = 100;

    public SplitKeep SplitKeep
    {
        get => splitKeep;
        set => Set(ref splitKeep, value);
    }

    // --- Operations ------------------------------------------------------------------

    private void Insert(object? parameter)
    {
        if (parameter is not PrimitiveKind kind)
        {
            if (parameter is string text && Enum.TryParse(text, out PrimitiveKind parsed)) kind = parsed;
            else return;
        }

        var mesh = Primitives.Create(kind);
        var o = new SceneObject(Scene.UniqueName(kind.ToString()), mesh)
        {
            Colour = Palette[colourCursor++ % Palette.Length],
            Origin = kind
        };

        // Primitives are centred on their own origin, so lift by half the height to sit on the plate.
        o.Position = new Vector3(0, 0, mesh.ComputeBounds().Size.Z / 2f);

        Undo.Execute(new AddObjectsCommand($"Insert {kind}", [o]));
        RefreshSelection();
        Status = $"Inserted {kind}";
        // Deliberately no reframing here: a primitive always lands at the origin, already in
        // view, and moving the camera on every insert makes the scene hard to build up.
    }

    private void Delete()
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return;
        Undo.Execute(new DeleteObjectsCommand(selection));
        RefreshSelection();
        Status = "Deleted";
    }

    private void Duplicate()
    {
        var copies = Scene.Selection.Select(o =>
        {
            var copy = o.Clone();
            copy.Name = Scene.UniqueName(o.Name);
            // Offset so the copy is visible rather than hidden inside the original.
            copy.Position += new Vector3(o.WorldBounds.Size.X + 5f, 0, 0);
            return copy;
        }).ToList();

        if (copies.Count == 0) return;
        Undo.Execute(new AddObjectsCommand("Duplicate", copies));
        RefreshSelection();
        Status = "Duplicated";
    }

    private void Mirror(object? parameter)
    {
        if (!TryParseAxis(parameter, out var axis)) return;

        var selection = Scene.Selection;
        var before = selection.Select(TransformState.Capture).ToList();

        // Negating the scale is what mirrors; MeshTransform flips the winding to match, so the
        // result still exports with outward-facing normals.
        foreach (var o in selection)
        {
            var s = o.Scale;
            o.Scale = axis switch
            {
                Axis.X => s with { X = -s.X },
                Axis.Y => s with { Y = -s.Y },
                _ => s with { Z = -s.Z }
            };
        }

        if (TransformCommand.CreateIfChanged($"Mirror {axis}", selection, before) is { } command)
            Undo.Execute(command);

        RefreshSelection();
        Status = $"Mirrored on {axis}";
    }

    private void AlignToPlate()
    {
        var selection = Scene.Selection;
        var before = selection.Select(TransformState.Capture).ToList();

        foreach (var o in selection)
        {
            float bottom = o.WorldBounds.Min.Z;
            o.Position = o.Position with { Z = o.Position.Z - bottom };
        }

        if (TransformCommand.CreateIfChanged("Align to plate", selection, before) is { } command)
            Undo.Execute(command);

        RefreshSelection();
        Status = "Aligned to the build plate";
    }

    private void SelectAll()
    {
        foreach (var o in Scene.Objects) o.IsSelected = true;
        RefreshSelection();
    }

    /// <summary>
    /// Rebuilds the selected primitives with rounded edges.
    ///
    /// Rounding regenerates the shape from its parameters at its current size rather than
    /// filleting the existing mesh, so the object's scale is reset in the process - otherwise a
    /// box stretched 2x in X would come back with elliptical corners.
    /// </summary>
    private void RoundSelection()
    {
        var roundable = Scene.Selection.Where(o => o.CanRound).ToList();
        if (roundable.Count == 0) return;

        int skipped = Scene.Selection.Count - roundable.Count;

        var dialog = new RoundDialog(roundable) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } radius) return;

        var edges = dialog.Edges;
        var produced = roundable.Select(o =>
        {
            var size = new Vector3(o.SizeX, o.SizeY, o.SizeZ);
            var mesh = RoundedPrimitives.Create(o.Origin!.Value, size, radius, edges);

            // Scale is deliberately left at its default: the mesh is already the right size.
            return new SceneObject(o.Name, mesh)
            {
                Position = o.Position,
                Rotation = o.Rotation,
                Colour = o.Colour,
                Origin = o.Origin
            };
        }).ToList();

        Undo.Execute(new ReplaceObjectsCommand("Round edges", roundable, produced));
        RefreshSelection();

        string which = edges == RoundEdges.All ? "all edges" : edges.ToString().ToLowerInvariant();
        Status = skipped == 0
            ? $"Rounded {which} of {produced.Count} object(s) to {radius:0.##} mm"
            : $"Rounded {produced.Count} object(s); {skipped} skipped - only a cube or cylinder can be rounded";
    }

    /// <summary>
    /// Copies the selection into an in-app clipboard. Clones are taken now rather than at paste
    /// time, so editing or deleting the originals afterwards does not change what gets pasted.
    /// </summary>
    private void Copy()
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return;

        clipboard.Clear();
        foreach (var o in selection) clipboard.Add(o.Clone());

        Status = selection.Count == 1 ? "Copied 1 object" : $"Copied {selection.Count} objects";
    }

    private void Paste()
    {
        if (clipboard.Count == 0) return;

        // Offset so a paste is visible rather than hidden exactly inside its original, and
        // cloned again so pasting twice does not hand out the same instance.
        var pasted = clipboard.Select(source =>
        {
            var copy = source.Clone();
            copy.Name = Scene.UniqueName(source.Name);
            copy.Position += new Vector3(source.WorldBounds.Size.X + 5f, 0, 0);
            return copy;
        }).ToList();

        Undo.Execute(new AddObjectsCommand("Paste", pasted));
        RefreshSelection();
        Status = pasted.Count == 1 ? "Pasted 1 object" : $"Pasted {pasted.Count} objects";
    }

    private void InvertSelection()
    {
        foreach (var o in Scene.Objects) o.IsSelected = !o.IsSelected;
        RefreshSelection();
        Status = $"{Scene.Selection.Count} object(s) selected";
    }

    /// <summary>
    /// Combines the selection into one object.
    ///
    /// The meshes are concatenated, not fused: each part keeps its own shell, so Ungroup can
    /// tell them apart again afterwards. Use Merge on the Object tab for a true boolean union.
    /// </summary>
    private void Group()
    {
        var selection = Scene.Selection;
        if (selection.Count < 2) return;

        var combined = Mesh.Combine(selection.Select(o => o.ToWorldMesh()));
        var grouped = new SceneObject(Scene.UniqueName("Group"), combined)
        {
            Colour = selection[0].Colour
        };

        Undo.Execute(new ReplaceObjectsCommand("Group", selection, [grouped]));
        RefreshSelection();
        Status = $"Grouped {selection.Count} objects";
    }

    /// <summary>
    /// Splits each selected object into its separate pieces.
    ///
    /// Pieces are found geometrically, by which triangles are connected, so this also breaks up
    /// an imported STL that holds several loose parts. Parts that actually touch are one piece
    /// and stay together - grouping is not recorded anywhere, it is read back off the mesh.
    /// </summary>
    private void Ungroup()
    {
        var consumed = new List<SceneObject>();
        var produced = new List<SceneObject>();

        foreach (var o in Scene.Selection)
        {
            var parts = MeshComponents.Split(o.Mesh);
            if (parts.Count < 2) continue;

            consumed.Add(o);
            for (int i = 0; i < parts.Count; i++)
            {
                produced.Add(new SceneObject(Scene.UniqueName($"{o.Name} part {i + 1}"), parts[i])
                {
                    // Parts come out in the object's own space, so its transform still applies.
                    Position = o.Position,
                    Rotation = o.Rotation,
                    Scale = o.Scale,
                    Colour = o.Colour
                });
            }
        }

        if (consumed.Count == 0)
        {
            Status = "Nothing to ungroup - the selection has no separate pieces";
            return;
        }

        Undo.Execute(new ReplaceObjectsCommand("Ungroup", consumed, produced));
        RefreshSelection();
        Status = $"Ungrouped into {produced.Count} objects";
    }

    private async Task RunBoolean(object? parameter)
    {
        if (parameter is not BooleanOp op && !(parameter is string s && Enum.TryParse(s, out op))) return;

        var selection = Scene.Selection;
        if (selection.Count < 2) return;

        IsBusy = true;
        Status = $"{op}...";
        try
        {
            // Everything is baked to world space first: CSG has no concept of per-object transforms.
            var meshes = selection.Select(o => o.ToWorldMesh()).ToList();

            var result = await Task.Run(() =>
            {
                var accumulator = meshes[0];
                for (int i = 1; i < meshes.Count; i++)
                    accumulator = CsgSolid.Apply(accumulator, meshes[i], op);
                return accumulator;
            });

            if (result.TriangleCount == 0)
            {
                Status = $"{op} removed everything - nothing left to keep";
                MessageBox.Show(
                    "That operation left no geometry behind.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var combined = new SceneObject(Scene.UniqueName(op.ToString()), result)
            {
                Colour = selection[0].Colour
            };

            Undo.Execute(new ReplaceObjectsCommand(op.ToString(), selection, [combined]));
            RefreshSelection();

            var health = result.CheckHealth();
            Status = $"{op}: {health.TriangleCount:N0} triangles, {health.Describe()}";
        }
        catch (Exception ex)
        {
            Status = $"{op} failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BeginSplit()
    {
        if (Scene.Selection.Count != 1) return;
        IsSplitMode = true;
        ResetSplitOffset();
        Status = "Drag the arrows to slide the split plane, the rings to tilt it";
    }

    private void ResetSplitOffset()
    {
        var selection = Scene.Selection;
        if (selection.Count != 1) return;

        var (min, max) = PlaneSplit.OffsetRange(selection[0].WorldBounds, splitNormal);
        SplitMinimum = min;
        SplitMaximum = max;
        SplitOffset = (min + max) / 2f;
        Raise(nameof(SplitMinimum));
        Raise(nameof(SplitMaximum));
    }

    private async Task ApplySplit()
    {
        var selection = Scene.Selection;
        if (selection.Count != 1) return;

        var source = selection[0];
        var world = source.ToWorldMesh();
        var normal = splitNormal;
        float offset = splitOffset;
        var keep = splitKeep;

        IsBusy = true;
        Status = "Splitting...";
        try
        {
            var (front, back) = await Task.Run(() => PlaneSplit.Split(world, normal, offset, keep));

            var produced = new List<SceneObject>();
            if (front is not null)
                produced.Add(new SceneObject(Scene.UniqueName($"{source.Name} {KeepFrontLabel}"), front) { Colour = source.Colour });
            if (back is not null)
                produced.Add(new SceneObject(Scene.UniqueName($"{source.Name} {KeepBackLabel}"), back) { Colour = source.Colour });

            if (produced.Count == 0)
            {
                Status = "The split plane missed the object";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Split", [source], produced));
            IsSplitMode = false;
            RefreshSelection();
            Status = produced.Count == 2 ? "Split into two halves" : "Split applied";
        }
        catch (Exception ex)
        {
            Status = $"Split failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Files -----------------------------------------------------------------------

    private void NewScene()
    {
        Scene.Objects.Clear();
        Undo.Clear();
        projectPath = null;
        RefreshSelection();
        Status = "New scene";
    }

    private void OpenProject()
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"3DFastCraft project (*{SceneSerializer.Extension})|*{SceneSerializer.Extension}",
            Title = "Open project"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var loaded = SceneSerializer.Load(dialog.FileName);
            Scene.Objects.Clear();
            foreach (var o in loaded) Scene.Objects.Add(o);
            Undo.Clear();
            projectPath = dialog.FileName;
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();
            Status = $"Opened {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveProject(bool saveAs)
    {
        string? target = projectPath;
        if (saveAs || target is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = $"3DFastCraft project (*{SceneSerializer.Extension})|*{SceneSerializer.Extension}",
                DefaultExt = SceneSerializer.Extension,
                FileName = "scene" + SceneSerializer.Extension
            };
            if (dialog.ShowDialog() != true) return;
            target = dialog.FileName;
        }

        try
        {
            SceneSerializer.Save(target, Scene);
            projectPath = target;
            Status = $"Saved {Path.GetFileName(target)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "3D models (*.stl;*.obj)|*.stl;*.obj|STL (*.stl)|*.stl|Wavefront OBJ (*.obj)|*.obj",
            Title = "Import model"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var imported = new List<SceneObject>();
            if (Path.GetExtension(dialog.FileName).Equals(".obj", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (name, mesh) in ObjReader.Read(dialog.FileName))
                    imported.Add(new SceneObject(Scene.UniqueName(name), mesh)
                    {
                        Colour = Palette[colourCursor++ % Palette.Length]
                    });
            }
            else
            {
                var mesh = StlReader.Read(dialog.FileName);
                imported.Add(new SceneObject(
                    Scene.UniqueName(Path.GetFileNameWithoutExtension(dialog.FileName)), mesh)
                {
                    Colour = Palette[colourCursor++ % Palette.Length]
                });
            }

            if (imported.Count == 0)
            {
                Status = "Nothing to import - the file contained no triangles";
                return;
            }

            Undo.Execute(new AddObjectsCommand("Import", imported));
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();

            int triangles = imported.Sum(o => o.Mesh.TriangleCount);
            Status = triangles > 200_000
                ? $"Imported {triangles:N0} triangles - boolean operations on a mesh this dense will be slow"
                : $"Imported {triangles:N0} triangles";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Export()
    {
        if (Scene.Objects.Count == 0) return;

        // Scope is asked for rather than inferred. Exporting only what happened to be selected
        // is how half a model reaches a slicer without anyone noticing, so the whole plate is
        // the default and narrowing to the selection is a deliberate choice.
        var options = new ExportDialog(Scene) { Owner = Application.Current?.MainWindow };
        if (options.ShowDialog() != true || options.Result is not { } chosen) return;

        var subjects = ExportComposer.Subjects(Scene, chosen);
        if (subjects.Count == 0) return;

        var dialog = new SaveFileDialog
        {
            Filter = chosen.Filter,
            DefaultExt = chosen.Extension,
            FileName = "model" + chosen.Extension,
            Title = chosen.SelectedOnly
                ? $"Export {subjects.Count} selected object(s)"
                : $"Export all {subjects.Count} object(s)"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (chosen.IsObj)
            {
                ObjWriter.Write(dialog.FileName, ExportComposer.ComposeForObj(subjects, chosen.DropToPlate));
                Status = $"Exported {subjects.Count} object(s) to {Path.GetFileName(dialog.FileName)}";
            }
            else
            {
                var merged = ExportComposer.MergeForStl(subjects, chosen.DropToPlate);
                StlWriter.Write(dialog.FileName, merged, binary: chosen.Format == ExportFormat.BinaryStl);
                Status = $"Exported {merged.TriangleCount:N0} triangles to {Path.GetFileName(dialog.FileName)}";
                WarnIfNotPrintable(merged);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Warns rather than blocks. A mesh with a few stray edges often still slices fine, so the
    /// decision belongs to the user - but silently writing an unprintable file does not.
    /// </summary>
    private static void WarnIfNotPrintable(Mesh mesh)
    {
        var health = mesh.CheckHealth();
        if (health.IsWatertight && !health.IsInsideOut) return;

        MessageBox.Show(
            $"The exported mesh may not print cleanly:\n\n{health.Describe()}\n\n" +
            "The file has been written. Most slicers can repair minor problems, " +
            "but you may want to check it before printing.",
            "Export finished with warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // --- Plumbing --------------------------------------------------------------------

    /// <summary>Called after anything touches the selection, including viewport clicks.</summary>
    public void RefreshSelection()
    {
        var selection = Scene.Selection;
        Selected = selection.Count == 1 ? selection[0] : null;
        SelectionChanged?.Invoke();
        Raise(nameof(HasAnySelection));
        Raise(nameof(SelectionSummary));
        Raise(nameof(UndoLabel));
        if (IsSplitMode && selection.Count == 1) ResetSplitOffset();
    }

    private static bool TryParseAxis(object? parameter, out Axis axis)
    {
        switch (parameter)
        {
            case Axis a:
                axis = a;
                return true;
            case string s when Enum.TryParse(s, out Axis parsed):
                axis = parsed;
                return true;
            default:
                axis = Axis.X;
                return false;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(property);
    }

    private void Raise(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
