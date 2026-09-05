using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using FastCraft3D.View;
using Microsoft.Win32;

namespace FastCraft3D.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private int colourCursor;
    private string status = "Ready";
    private bool isBusy;
    private string? projectPath;
    private bool isDirty;
    private SceneObject? selected;
    private Axis splitAxis = Axis.Z;
    private float splitOffset;
    private SplitKeep splitKeep = SplitKeep.Both;
    private bool isSplitMode;
    private bool isEngraveMode;
    private bool? damaged;
    private bool scaleOneSide;
    private bool showWireframe;
    private bool showXray;
    private bool showPlate = true;
    private float plateSize = Scene.PlateSize;
    private readonly EngraveState engrave = new();
    private Vector3 splitNormal = Vector3.UnitZ;
    private readonly List<SceneObject> clipboard = new();
    private GizmoMode gizmoMode = GizmoMode.Move;
    private bool uniformScale = true;
    private bool snapRotation = true;
    private double snapStep;
    private readonly RecentFiles recent = new();
    private bool stickySelection = true;
    private bool isSelectionMenuOpen = true;

    public MainViewModel()
    {
        Scene = new Scene();
        Undo = new UndoStack(Scene);
        Undo.Changed += () =>
        {
            Raise(nameof(UndoLabel));
            IsDirty = true;
        };
        Scene.Objects.CollectionChanged += (_, _) => RefreshSelection();

        InsertCommand = new RelayCommand(p => Insert(p));
        DeleteCommand = RelayCommand.Simple(Delete, () => Scene.Selection.Count > 0);
        DuplicateCommand = new RelayCommand(p => Duplicate(offset: !Equals(p, "InPlace")),
            _ => Scene.Selection.Count > 0);
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
        BeginSplitCommand = RelayCommand.Simple(BeginSplit, () => Scene.Selection.Count > 0);
        ApplySplitCommand = AsyncRelayCommand.Simple(ApplySplit, () => IsSplitMode);
        CancelSplitCommand = RelayCommand.Simple(() => IsSplitMode = false);

        RepairCommand = AsyncRelayCommand.Simple(RepairObjects, () => Scene.Objects.Count > 0);
        SmoothCommand = AsyncRelayCommand.Simple(SmoothSelection, () => Scene.Selection.Count > 0);
        BeginEngraveCommand = RelayCommand.Simple(BeginEngrave, () => Scene.Selection.Count == 1);
        ApplyEngraveCommand = AsyncRelayCommand.Simple(ApplyEngrave, () => isEngraveMode && engrave.HasFace);
        CancelEngraveCommand = RelayCommand.Simple(() => IsEngraveMode = false);

        UndoCommand = RelayCommand.Simple(() => { Undo.Undo(); RefreshSelection(); }, () => Undo.CanUndo);
        RedoCommand = RelayCommand.Simple(() => { Undo.Redo(); RefreshSelection(); }, () => Undo.CanRedo);

        OpenRecentCommand = new RelayCommand(OpenRecent);
        recent.Changed += () => Raise(nameof(RecentFiles));
        SaveVersionCommand = RelayCommand.Simple(SaveVersion, () => Scene.Objects.Count > 0);
        VersionsCommand = RelayCommand.Simple(ShowVersions, () => projectPath is not null);
        NewCommand = RelayCommand.Simple(NewScene);
        OpenCommand = RelayCommand.Simple(OpenProject);
        SaveCommand = RelayCommand.Simple(() => SaveProject(saveAs: false));
        SaveAsCommand = RelayCommand.Simple(() => SaveProject(saveAs: true));
        SetMoveModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Move);
        SetRotateModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Rotate);
        SetScaleModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Scale);
        AlignCommand = new RelayCommand(Align, _ => Scene.Selection.Count > 1);
        RoundCommand = RelayCommand.Simple(RoundSelection, () => Scene.Selection.Any(o => o.CanRound));
        CopyCommand = RelayCommand.Simple(Copy, () => Scene.Selection.Count > 0);
        PasteCommand = RelayCommand.Simple(Paste, () => clipboard.Count > 0);
        ImportCommand = RelayCommand.Simple(Import);
        ExportCommand = RelayCommand.Simple(Export, () => Scene.Objects.Count > 0);
        SetColourCommand = new RelayCommand(SetColour, _ => Scene.Selection.Count > 0);
        PickColourCommand = RelayCommand.Simple(PickColour, () => Scene.Selection.Count > 0);
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
    public System.Windows.Input.ICommand RepairCommand { get; }
    public System.Windows.Input.ICommand SmoothCommand { get; }
    public System.Windows.Input.ICommand BeginEngraveCommand { get; }
    public System.Windows.Input.ICommand ApplyEngraveCommand { get; }
    public System.Windows.Input.ICommand CancelEngraveCommand { get; }
    public System.Windows.Input.ICommand UndoCommand { get; }
    public System.Windows.Input.ICommand RedoCommand { get; }
    public System.Windows.Input.ICommand OpenRecentCommand { get; }
    public System.Windows.Input.ICommand SaveVersionCommand { get; }
    public System.Windows.Input.ICommand VersionsCommand { get; }
    public System.Windows.Input.ICommand NewCommand { get; }
    public System.Windows.Input.ICommand OpenCommand { get; }
    public System.Windows.Input.ICommand SaveCommand { get; }
    public System.Windows.Input.ICommand SaveAsCommand { get; }
    public System.Windows.Input.ICommand SetMoveModeCommand { get; }
    public System.Windows.Input.ICommand SetRotateModeCommand { get; }
    public System.Windows.Input.ICommand SetScaleModeCommand { get; }
    public System.Windows.Input.ICommand AlignCommand { get; }
    public System.Windows.Input.ICommand RoundCommand { get; }
    public System.Windows.Input.ICommand CopyCommand { get; }
    public System.Windows.Input.ICommand PasteCommand { get; }
    public System.Windows.Input.ICommand ImportCommand { get; }
    public System.Windows.Input.ICommand ExportCommand { get; }
    public System.Windows.Input.ICommand SetColourCommand { get; }
    public System.Windows.Input.ICommand PickColourCommand { get; }

    /// <summary>The swatch grid shown in the properties panel.</summary>
    public IReadOnlyList<Swatch> Swatches => Palette.Swatches;

    /// <summary>Recently opened projects, most recent first, for the File tab.</summary>
    public IReadOnlyList<RecentEntry> RecentFiles => recent.Paths
        .Select(p => new RecentEntry(Path.GetFileNameWithoutExtension(p), p))
        .ToList();

    /// <param name="Name">What to show.</param>
    /// <param name="Path">The full path, shown as a tooltip and used to open it.</param>
    public readonly record struct RecentEntry(string Name, string Path);

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

    /// <summary>Whether there are changes that have not been written to the project file.</summary>
    public bool IsDirty
    {
        get => isDirty;
        private set
        {
            Set(ref isDirty, value);
            Raise(nameof(WindowTitle));
        }
    }

    /// <summary>
    /// The file being edited, marked with an asterisk while it has unsaved changes - the usual
    /// convention, and the quickest way to tell which of several open models is which.
    /// </summary>
    public string WindowTitle
    {
        get
        {
            string name = projectPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(projectPath);
            return $"{name}{(isDirty ? " *" : string.Empty)} - 3DFastCraft";
        }
    }

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

    /// <summary>
    /// Grow only the way the handle is dragged, holding the opposite face where it is.
    ///
    /// The default resizes about the centre, which keeps a part where it sits but moves both
    /// faces. Holding one still is what you want when the part has to keep meeting the one next
    /// to it. Shortcut: O.
    /// </summary>
    public bool ScaleOneSide
    {
        get => scaleOneSide;
        set => Set(ref scaleOneSide, value);
    }

    /// <summary>
    /// Millimetres a drag snaps to, or zero for free movement. Offered as a choice rather than
    /// always on: a grid is what makes parts meet exactly, and a nuisance when they should not.
    /// </summary>
    public double SnapStep
    {
        get => snapStep;
        set
        {
            Set(ref snapStep, value);
            Raise(nameof(SnapOff));
            Raise(nameof(SnapOne));
            Raise(nameof(SnapFive));
            Status = value <= 0 ? "Snapping off" : $"Snapping to {value:0.##} mm";
        }
    }

    public bool SnapOff { get => snapStep <= 0; set { if (value) SnapStep = 0; } }
    public bool SnapOne { get => Math.Abs(snapStep - 1) < 1e-6; set { if (value) SnapStep = 1; } }
    public bool SnapFive { get => Math.Abs(snapStep - 5) < 1e-6; set { if (value) SnapStep = 5; } }

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

    // --- Engraving -----------------------------------------------------------------

    /// <summary>Raised when the picked face changes, so the viewport can highlight it.</summary>
    public event Action? EngraveFaceChanged;

    /// <summary>
    /// While this is on, clicking the object picks a face to engrave rather than changing the
    /// selection. It is its own mode for the same reason splitting is: the click means something
    /// different, and there is nowhere else to say so.
    /// </summary>
    public bool IsEngraveMode
    {
        get => isEngraveMode;
        set
        {
            if (isEngraveMode == value) return;

            Set(ref isEngraveMode, value);
            if (!value) engrave.Clear();

            Raise(nameof(HasEngraveFace));
            RaiseEngraveText();
            EngraveFaceChanged?.Invoke();
        }
    }

    public bool HasEngraveFace => engrave.HasFace;

    /// <summary>
    /// Whether anything on the plate would fail to print, which is what raises the repair
    /// banner. Worked out once per edit and remembered: checking a mesh means walking every
    /// edge of it, which is not something to do on every redraw.
    /// </summary>
    // --- What the viewport shows ---------------------------------------------------

    /// <summary>Raised when a view setting changes, for the parts of the scene the renderer owns.</summary>
    public event Action? ViewChanged;

    /// <summary>Triangle edges drawn over every object.</summary>
    public bool ShowWireframe
    {
        get => showWireframe;
        set { Set(ref showWireframe, value); ViewChanged?.Invoke(); }
    }

    /// <summary>Unselected objects go see-through, so a part inside another can be worked on.</summary>
    public bool ShowXray
    {
        get => showXray;
        set { Set(ref showXray, value); ViewChanged?.Invoke(); }
    }

    public bool ShowPlate
    {
        get => showPlate;
        set { Set(ref showPlate, value); ViewChanged?.Invoke(); }
    }

    /// <summary>
    /// The printer's bed, in millimetres. It is a guide rather than a limit - nothing stops an
    /// object being placed off it - so any printer's size can be dialled in.
    /// </summary>
    public float PlateSize
    {
        get => plateSize;
        set
        {
            float wanted = Math.Clamp(value, 20f, 2000f);
            if (Math.Abs(wanted - plateSize) < 0.01f) return;

            Set(ref plateSize, wanted);
            ViewChanged?.Invoke();
        }
    }

    /// <summary>Common bed sizes, so the usual ones are one click rather than a typed number.</summary>
    public IReadOnlyList<float> PlateSizes { get; } = [120f, 180f, 200f, 220f, 250f, 300f, 350f, 400f];

    public bool HasDamagedObjects => damaged ??= Scene.Objects.Any(o => !o.Mesh.CheckHealth().IsWatertight);

    /// <summary>The picked face, in world space, or null. Read by the renderer.</summary>
    public FacePatch? EngraveFace => engrave.Face;

    /// <summary>What the pattern would cut, drawn on the face while the settings are chosen.</summary>
    public GrooveSet EngravePreview => engrave.Preview();

    public IReadOnlyList<PatternKind> EngravePatterns { get; } = Enum.GetValues<PatternKind>();
    public IReadOnlyList<PatternDirection> EngraveDirections { get; } = Enum.GetValues<PatternDirection>();

    public PatternKind EngravePattern
    {
        get => engrave.Options.Kind;
        set => SetEngrave(engrave.Options with { Kind = value });
    }

    public float EngraveSize
    {
        get => engrave.Options.Size;
        set => SetEngrave(engrave.Options with { Size = value });
    }

    public float EngraveGrooveWidth
    {
        get => engrave.Options.GrooveWidth;
        set => SetEngrave(engrave.Options with { GrooveWidth = value });
    }

    public float EngraveDepth
    {
        get => engrave.Options.Depth;
        set => SetEngrave(engrave.Options with { Depth = value });
    }

    public PatternDirection EngraveDirection
    {
        get => engrave.Options.Direction;
        set => SetEngrave(engrave.Options with { Direction = value });
    }

    /// <summary>
    /// Slides the pattern across the face. This is how two walls are made to meet: each face
    /// measures from its own bottom-left corner, and which corner that is depends on which way
    /// the face points, so adjacent walls almost never line up on their own.
    /// </summary>
    public float EngraveOffsetU
    {
        get => engrave.Options.OffsetU;
        set => SetEngrave(engrave.Options with { OffsetU = value });
    }

    public float EngraveOffsetV
    {
        get => engrave.Options.OffsetV;
        set => SetEngrave(engrave.Options with { OffsetV = value });
    }

    public string EngraveSummary => engrave.Describe();
    public string EngraveAdvice => engrave.Advice();
    public bool HasEngraveAdvice => EngraveAdvice.Length > 0;

    /// <summary>Only Stripes and Wood have a direction; brick courses are always level.</summary>
    public bool EngraveDirectionApplies => engrave.Options.Kind != PatternKind.Brick;

    private void SetEngrave(EngraveOptions options)
    {
        engrave.Options = options;

        Raise(nameof(EngravePattern));
        Raise(nameof(EngraveSize));
        Raise(nameof(EngraveGrooveWidth));
        Raise(nameof(EngraveDepth));
        Raise(nameof(EngraveDirection));
        Raise(nameof(EngraveDirectionApplies));
        Raise(nameof(EngraveOffsetU));
        Raise(nameof(EngraveOffsetV));
        RaiseEngraveText();

        // The preview is drawn from these settings, so it has to be redrawn with them.
        EngraveFaceChanged?.Invoke();
    }

    private void RaiseEngraveText()
    {
        Raise(nameof(EngraveSummary));
        Raise(nameof(EngraveAdvice));
        Raise(nameof(HasEngraveAdvice));
    }

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
            Colour = NextAutomaticColour(),
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

    /// <summary>
    /// Copies the selection, either set aside or left exactly where the original is.
    ///
    /// In place is the one you want before a boolean or a mirror, where the copy has to start
    /// from the same spot; offset is the one you want when laying parts out, where a copy hidden
    /// inside its original just looks like nothing happened.
    /// </summary>
    private void Duplicate(bool offset)
    {
        var copies = Scene.Selection.Select(o =>
        {
            var copy = o.Clone();
            copy.Name = Scene.UniqueName(o.Name);
            if (offset) copy.Position += new Vector3(o.WorldBounds.Size.X + 5f, 0, 0);
            return copy;
        }).ToList();

        if (copies.Count == 0) return;

        Undo.Execute(new AddObjectsCommand(offset ? "Duplicate" : "Duplicate in place", copies));
        RefreshSelection();
        Status = offset ? "Duplicated" : "Duplicated in place";
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
    /// Lines the selection up along an axis. The parameter is "axis:mode", for example "X:Centre".
    /// </summary>
    private void Align(object? parameter)
    {
        if (parameter is not string text) return;
        var parts = text.Split(':');
        if (parts.Length != 2) return;
        if (!Enum.TryParse<Axis>(parts[0], out var axis)) return;
        if (!Enum.TryParse<AlignMode>(parts[1], out var mode)) return;

        var selection = Scene.Selection;
        if (selection.Count < 2) return;

        var before = selection.Select(TransformState.Capture).ToList();
        var offsets = AlignTools.Offsets(selection, axis, mode);

        for (int i = 0; i < selection.Count; i++)
            selection[i].Position += offsets[i];

        if (TransformCommand.CreateIfChanged($"Align {axis} {mode}", selection, before) is { } command)
        {
            Undo.Execute(command);
            Status = mode == AlignMode.Distribute
                ? $"Spread {selection.Count} objects evenly along {axis}"
                : $"Aligned {selection.Count} objects to {mode} on {axis}";
        }
        else
        {
            Status = "Already aligned";
        }

        RefreshSelection();
    }

    /// <summary>
    /// Paints the whole selection, from a palette swatch or a hex string.
    ///
    /// Everything selected takes the colour, not just the primary object: picking a swatch with
    /// five things selected and watching one of them change would be a surprise.
    /// </summary>
    private void SetColour(object? parameter)
    {
        if (TryReadColour(parameter, out var colour)) ApplyColour(colour);
    }

    private void PickColour()
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return;

        // Seeded from the first selected object, so a custom colour starts as a tweak of what
        // is already there rather than from an arbitrary point on the wheel.
        var dialog = new ColourDialog(selection[0].Colour) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } picked) return;

        ApplyColour(picked);
    }

    private void ApplyColour(Vector3 colour)
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        string hex = Palette.ToHex(colour);
        string label = selection.Count == 1 ? "Colour" : $"Colour {selection.Count} objects";

        if (ColourCommand.CreateIfChanged(label, selection, colour) is not { } command)
        {
            Status = $"Already {hex}";
            return;
        }

        Undo.Execute(command);
        RefreshSelection();
        Status = selection.Count == 1
            ? $"Painted {selection[0].Name} {hex}"
            : $"Painted {selection.Count} objects {hex}";
    }

    /// <summary>Swatch buttons pass the vector; anything authored in XAML passes hex.</summary>
    private static bool TryReadColour(object? parameter, out Vector3 colour)
    {
        switch (parameter)
        {
            case Vector3 v:
                colour = v;
                return true;
            case Swatch swatch:
                colour = swatch.Colour;
                return true;
            case string text:
                return Palette.TryFromHex(text, out colour);
            default:
                colour = default;
                return false;
        }
    }

    private Vector3 NextAutomaticColour() => Palette.Cycle[colourCursor++ % Palette.Cycle.Length];

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

    /// <summary>
    /// Rounds the facets off the selection.
    ///
    /// Applied a fixed amount at a time and meant to be repeated: it is far easier to judge how
    /// much smoothing a shape wants by watching it than by choosing a number beforehand, and
    /// each press is its own undo step.
    /// </summary>
    private async Task SmoothSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        IsBusy = true;
        Status = "Smoothing...";
        try
        {
            var meshes = selection.Select(o => o.Mesh).ToList();
            var smoothed = await Task.Run(() => meshes.Select(m => MeshSmoothing.Smooth(m)).ToList());

            var produced = new List<SceneObject>();
            for (int i = 0; i < selection.Count; i++)
            {
                produced.Add(new SceneObject(selection[i].Name, smoothed[i])
                {
                    Position = selection[i].Position,
                    Rotation = selection[i].Rotation,
                    Scale = selection[i].Scale,
                    Colour = selection[i].Colour
                    // Origin is deliberately dropped: the shape is no longer the primitive it
                    // was, so it can no longer be rebuilt with rounded edges.
                });
            }

            Undo.Execute(new ReplaceObjectsCommand("Smooth", selection, produced));
            RefreshSelection();

            // Smoothing can only work with the vertices it is given, so on a shape with few of
            // them almost nothing happens - which is worth saying rather than leaving someone
            // pressing the button.
            int vertices = smoothed.Sum(m => m.VertexCount);
            Status = vertices < 200
                ? $"Smoothed - though with only {vertices} vertices there is little to smooth"
                : $"Smoothed {produced.Count} object(s)";
        }
        catch (Exception ex)
        {
            Status = $"Smooth failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Repairs whatever is broken - the selection if there is one, otherwise everything.
    ///
    /// Objects the pass cannot help are left exactly as they were and counted, rather than being
    /// quietly replaced by something no better.
    /// </summary>
    private async Task RepairObjects()
    {
        var targets = (Scene.Selection.Count > 0 ? Scene.Selection.ToList() : Scene.Objects.ToList())
            .Where(o => !o.Mesh.CheckHealth().IsWatertight)
            .ToList();

        if (targets.Count == 0)
        {
            Status = "Nothing needs repairing - everything on the plate is watertight";
            return;
        }

        IsBusy = true;
        Status = $"Repairing {targets.Count} object(s)...";
        try
        {
            var healed = await Task.Run(() => targets.Select(o => MeshHealer.Heal(o.Mesh)).ToList());

            var replaced = new List<SceneObject>();
            var produced = new List<SceneObject>();
            int fixedUp = 0, beyond = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                if (!healed[i].Improved) { beyond++; continue; }

                replaced.Add(targets[i]);
                produced.Add(new SceneObject(targets[i].Name, healed[i].Mesh)
                {
                    Position = targets[i].Position,
                    Rotation = targets[i].Rotation,
                    Scale = targets[i].Scale,
                    Colour = targets[i].Colour,
                    Origin = targets[i].Origin
                });

                if (healed[i].After.IsWatertight) fixedUp++;
            }

            if (replaced.Count == 0)
            {
                Status = $"{targets.Count} object(s) are damaged in a way this cannot mend - they are unchanged";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Repair", replaced, produced));
            RefreshSelection();

            Status = beyond == 0
                ? $"Repaired {fixedUp} of {replaced.Count} object(s)"
                : $"Repaired {fixedUp} object(s); {beyond} beyond mending and left alone";
        }
        catch (Exception ex)
        {
            Status = $"Repair failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BeginEngrave()
    {
        if (Scene.Selection.Count != 1) return;

        IsSplitMode = false;
        IsEngraveMode = true;
        Status = "Click the face you want to engrave";
    }

    /// <summary>
    /// Picks the face under a click. Both arguments are in world space, and so is the mesh the
    /// face is found on: the object is baked before engraving, exactly as it is for a boolean,
    /// so that a shape stretched twice as wide does not come back with bricks stretched with it.
    /// </summary>
    public bool PickEngraveFace(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isEngraveMode) return false;

        var world = target.ToWorldMesh();
        var face = EngraveState.FaceAt(world, worldPoint, worldNormal);

        if (face is null)
        {
            Status = "That is not a flat face - pick one of the flat sides";
            return false;
        }

        engrave.Pick(world, face);

        Raise(nameof(HasEngraveFace));
        RaiseEngraveText();
        EngraveFaceChanged?.Invoke();

        Status = EngraveSummary;
        return true;
    }

    private async Task ApplyEngrave()
    {
        if (Scene.Selection.Count != 1) return;
        if (engrave.Face is not { } face || engrave.WorldMesh is not { } world) return;

        var source = Scene.Selection[0];
        var options = engrave.Options;

        IsBusy = true;
        Status = $"Engraving {options.Kind}...";
        try
        {
            var result = await Task.Run(() => Engraver.Engrave(world, face, options));

            if (result.Grooves == 0 || result.Mesh.TriangleCount == 0)
            {
                Status = "That pattern did not reach the face - try a smaller pattern size";
                return;
            }

            // Nothing is applied unless it would print. Leaving the object alone and saying so
            // is far better than handing back a model that looks right and slices wrong; it is
            // usually a face that is one facet of a curved surface, where a flat pattern was
            // never going to sit properly anyway.
            if (!result.IsPrintable)
            {
                Status = $"Engraving that face came out unprintable - {result.Health.Describe()}. Nothing was changed.";
                MessageBox.Show(
                    "The pattern could not be cut into that face cleanly, so the object has been "
                    + "left as it was.\n\n"
                    + $"The result would have had {result.Health.Describe().ToLowerInvariant()}.\n\n"
                    + "This happens on faces that are one facet of a curved surface, such as the "
                    + "side of a cylinder or a cone. A flat face - the side of a box, a gable "
                    + "end - will cut cleanly. A coarser pattern or a shallower depth may also "
                    + "get through.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // The mesh is already in world space, so the new object carries no transform of its
            // own. Same as a boolean, and for the same reason.
            var engraved = new SceneObject(Scene.UniqueName($"{source.Name} {options.Kind}"), result.Mesh)
            {
                Colour = source.Colour
            };

            Undo.Execute(new ReplaceObjectsCommand($"Engrave {options.Kind}", [source], [engraved]));
            IsEngraveMode = false;
            RefreshSelection();

            var health = result.Mesh.CheckHealth();
            Status = $"Engraved {result.Grooves:N0} grooves - {health.TriangleCount:N0} triangles, {health.Describe()}";
        }
        catch (Exception ex)
        {
            Status = $"Engrave failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BeginSplit()
    {
        if (Scene.Selection.Count == 0) return;

        IsEngraveMode = false; // both modes claim the click, so only one can be on
        IsSplitMode = true;
        ResetSplitOffset();
        Status = "Drag the arrows to slide the split plane, the rings to tilt it";
    }

    private void ResetSplitOffset()
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return;

        // Everything selected, so the plane and its handles span the whole group rather than
        // whichever object happened to be first.
        var together = Bounds.Empty;
        foreach (var o in selection) together = together.Union(o.WorldBounds);

        var (min, max) = PlaneSplit.OffsetRange(together, splitNormal);
        SplitMinimum = min;
        SplitMaximum = max;
        SplitOffset = (min + max) / 2f;
        Raise(nameof(SplitMinimum));
        Raise(nameof(SplitMaximum));
    }

    /// <summary>
    /// Puts one plane through everything selected.
    ///
    /// The plane belongs to the scene rather than to an object, so several parts are cut in the
    /// same stroke and in one undo step - which is how you slice an assembly in half, and what
    /// the old app did. Objects the plane misses are left alone rather than being dropped;
    /// missing one of five is not a reason to refuse the other four.
    /// </summary>
    private async Task ApplySplit()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var meshes = selection.Select(o => o.ToWorldMesh()).ToList();
        var normal = splitNormal;
        float offset = splitOffset;
        var keep = splitKeep;

        IsBusy = true;
        Status = selection.Count == 1 ? "Splitting..." : $"Splitting {selection.Count} objects...";
        try
        {
            var halves = await Task.Run(
                () => meshes.Select(mesh => PlaneSplit.Split(mesh, normal, offset, keep)).ToList());

            var consumed = new List<SceneObject>();
            var produced = new List<SceneObject>();
            int missed = 0;

            for (int i = 0; i < selection.Count; i++)
            {
                var source = selection[i];
                var (front, back) = halves[i];

                if (front is null && back is null)
                {
                    missed++;
                    continue;
                }

                consumed.Add(source);
                if (front is not null)
                    produced.Add(new SceneObject(Scene.UniqueName($"{source.Name} {KeepFrontLabel}"), front) { Colour = source.Colour });
                if (back is not null)
                    produced.Add(new SceneObject(Scene.UniqueName($"{source.Name} {KeepBackLabel}"), back) { Colour = source.Colour });
            }

            if (consumed.Count == 0)
            {
                Status = selection.Count == 1
                    ? "The split plane missed the object"
                    : "The split plane missed every selected object";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Split", consumed, produced));
            IsSplitMode = false;
            RefreshSelection();

            Status = missed == 0
                ? $"Split {consumed.Count} object(s) into {produced.Count} piece(s)"
                : $"Split {consumed.Count} object(s) into {produced.Count} piece(s); the plane missed {missed}";
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

    /// <summary>
    /// Offers to save before something replaces the scene. Returns false to call the whole thing
    /// off - including a window close, which is why this exists: quietly discarding an unsaved
    /// model on exit is the one mistake the app cannot let the user make.
    /// </summary>
    public bool ConfirmDiscardChanges()
    {
        if (!IsDirty || Scene.Objects.Count == 0) return true;

        string name = projectPath is null ? "this model" : Path.GetFileName(projectPath);
        var answer = MessageBox.Show(
            $"Save changes to {name} before continuing?",
            "3DFastCraft", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        switch (answer)
        {
            case MessageBoxResult.Yes:
                SaveProject(saveAs: false);
                // Still dirty means the save dialog was cancelled or the write failed, so the
                // original request must not go ahead either.
                return !IsDirty;

            case MessageBoxResult.No:
                return true;

            default:
                return false;
        }
    }

    private void NewScene()
    {
        if (!ConfirmDiscardChanges()) return;

        Scene.Objects.Clear();
        Undo.Clear();
        projectPath = null;
        IsDirty = false;
        RefreshSelection();
        Status = "New scene";
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            Filter = $"3DFastCraft project (*{SceneSerializer.Extension})|*{SceneSerializer.Extension}",
            Title = "Open project"
        };
        if (dialog.ShowDialog() != true) return;

        LoadProject(dialog.FileName);
    }

    private void LoadProject(string path)
    {
        try
        {
            var loaded = SceneSerializer.Load(path);
            Scene.Objects.Clear();
            foreach (var o in loaded) Scene.Objects.Add(o);
            Undo.Clear();
            projectPath = path;
            IsDirty = false;
            recent.Add(path);
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();
            Status = $"Opened {Path.GetFileName(path)}";
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
            IsDirty = false;
            recent.Add(target);
            Status = $"Saved {Path.GetFileName(target)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Keeps a labelled snapshot inside the project file, then saves.
    ///
    /// This is the answer to wanting to go back after closing the file. Undo already keeps every
    /// step, but only for as long as the app is open, and its steps are far too fine-grained to
    /// be worth persisting - a named version is both smaller and easier to find your way back to.
    /// </summary>
    private void SaveVersion()
    {
        if (Scene.Objects.Count == 0) return;

        // A version has to live in a file, so an unsaved scene needs a home first.
        if (projectPath is null)
        {
            SaveProject(saveAs: true);
            if (projectPath is null) return;
        }

        var prompt = new VersionNameDialog { Owner = Application.Current?.MainWindow };
        if (prompt.ShowDialog() != true) return;

        try
        {
            SceneSerializer.SaveVersion(projectPath, Scene, prompt.VersionLabel);
            IsDirty = false;
            Status = $"Kept version \"{prompt.VersionLabel}\" in {Path.GetFileName(projectPath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save the version", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenRecent(object? parameter)
    {
        if (parameter is not string path) return;

        if (!File.Exists(path))
        {
            MessageBox.Show(
                $"{path}" + Environment.NewLine + Environment.NewLine +
                "That file is no longer there, so it has been removed from the list.",
                "Cannot open", MessageBoxButton.OK, MessageBoxImage.Information);
            recent.Remove(path);
            return;
        }

        if (!ConfirmDiscardChanges()) return;
        LoadProject(path);
    }

    private void ShowVersions()
    {
        if (projectPath is null) return;

        var dialog = new VersionsDialog(projectPath) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.RestoreIndex is not { } index) return;

        try
        {
            var restored = SceneSerializer.LoadVersion(projectPath, index);

            // Routed through undo, so restoring a version is as reversible as anything else.
            Undo.Execute(new ReplaceObjectsCommand("Restore version", Scene.Objects.ToList(), restored));
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();
            Status = $"Restored a version with {restored.Count} object(s) - Ctrl+Z to go back";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not restore that version", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        Colour = NextAutomaticColour()
                    });
            }
            else
            {
                var mesh = StlReader.Read(dialog.FileName);
                imported.Add(new SceneObject(
                    Scene.UniqueName(Path.GetFileNameWithoutExtension(dialog.FileName)), mesh)
                {
                    Colour = NextAutomaticColour()
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
        damaged = null;
        Raise(nameof(HasDamagedObjects));

        var selection = Scene.Selection;
        Selected = selection.Count == 1 ? selection[0] : null;
        SelectionChanged?.Invoke();
        Raise(nameof(HasAnySelection));
        Raise(nameof(SelectionSummary));
        Raise(nameof(UndoLabel));
        if (IsSplitMode && selection.Count > 0) ResetSplitOffset();
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
