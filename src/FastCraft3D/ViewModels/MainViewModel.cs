using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Geometry.Moulding;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using FastCraft3D.Text;
using FastCraft3D.View;
using Microsoft.Win32;

namespace FastCraft3D.ViewModels;

/// <summary>
/// A pattern and the name shown for it. The enum member cannot carry the name: "RoofTiles" is
/// not a phrase, and the picker showed the identifier as written.
/// </summary>
public readonly record struct PatternChoice(PatternKind Kind, string Label)
{
    /// <summary>
    /// What automation and a screen reader read out. DisplayMemberPath governs what is drawn,
    /// not what the item is called: without this the picker's rows were named
    /// "PatternChoice { Kind = RoofTiles, Label = Roof tiles }", because WPF takes an item's
    /// name from the bound object itself.
    /// </summary>
    public override string ToString() => Label;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private int colourCursor;
    private string status = "Ready";
    private bool isBusy;
    private bool isStopping;
    private bool historyTrimmed;
    private string busyTitle = "";
    private string busyElapsed = "";
    private string busyStage = "";
    private double busyFraction;
    private bool busyIndeterminate = true;
    private WorkProgress reported;
    private CancellationTokenSource? work;
    private DispatcherTimer? workClock;
    private readonly Sink sink;
    private DateTime workStarted;
    private string? projectPath;
    private bool isDirty;
    private SceneObject? selected;
    private Axis splitAxis = Axis.Z;
    private float splitOffset;
    private SplitKeep splitKeep = SplitKeep.Both;
    private Render.SplitOffcut splitOffcut = Render.SplitOffcut.Faded;
    private GizmoMode splitGizmoMode = GizmoMode.Move;
    private bool splitFillsCut = true;
    private bool isSplitMode;
    private bool isEngraveMode;
    private bool isMeasureMode;
    private bool isSubtractMode;
    private bool subtractKeepsCutter;
    private float subtractTolerance;
    private bool isEmbossMode;
    private bool isLayMode;
    private FacePatch? embossFace;
    private Mesh? embossMesh;
    private string embossText = "TEXT";
    private string svgFile = "";
    private string embossFont = "Arial";
    private float embossHeight = 10f;
    private float embossDepth = 0.8f;
    private bool embossBold = true;
    private bool embossRaised;
    private float embossBevel;
    private TextProjection embossProjection = TextProjection.Planar;
    private SurfacePlacement embossPlacement = SurfacePlacement.Middle;
    private List<TextShape>? letteringCache;
    private Vector3 embossPick;
    private Bounds embossBounds = Bounds.Empty;
    private bool measureSnap = true;
    private Vector3? measureFrom;
    private Vector3? measureTo;
    private bool? damaged;
    private bool scaleOneSide;
    private bool stopOnContact;
    private bool showWireframe;
    private bool showXray;
    private bool showPlate = true;
    private float plateSize = Scene.PlateSize;
    private float modelScale = 1f;
    private readonly EngraveState engrave = new();
    private Vector3 splitNormal = Vector3.UnitZ;
    private readonly List<SceneObject> clipboard = new();
    private GizmoMode gizmoMode = GizmoMode.Move;
    private bool uniformScale = true;
    private MeasureUnit unit = MeasureUnit.Default;
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
        InsertStairCommand = RelayCommand.Simple(InsertStair);
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
        BeginSubtractCommand = RelayCommand.Simple(BeginSubtract, () => Scene.Selection.Count >= 2);
        ApplySubtractCommand = new AsyncRelayCommand(_ => ApplySubtract(), _ => Scene.Selection.Count >= 2);
        CancelSubtractCommand = RelayCommand.Simple(() => IsSubtractMode = false);
        BeginSplitCommand = RelayCommand.Simple(BeginSplit, () => Scene.Selection.Count > 0);
        AlignToAxesCommand = RelayCommand.Simple(AlignToAxes, AnythingTurned);
        ApplySplitCommand = AsyncRelayCommand.Simple(ApplySplit, () => IsSplitMode);
        CancelSplitCommand = RelayCommand.Simple(() => IsSplitMode = false);

        RepairCommand = AsyncRelayCommand.Simple(RepairObjects, () => Scene.Objects.Count > 0);
        SmoothCommand = RelayCommand.Simple(SmoothSelection, () => Scene.Selection.Count > 0);
        RebuildCommand = AsyncRelayCommand.Simple(RebuildObjects, () => Scene.Objects.Count > 0);
        SimplifyCommand = AsyncRelayCommand.Simple(SimplifySelection, () => Scene.Selection.Count > 0);
        HollowCommand = AsyncRelayCommand.Simple(HollowSelection, () => Scene.Selection.Count > 0);
        BeginEmbossCommand = RelayCommand.Simple(BeginEmboss, () => Scene.Selection.Count == 1);
        BeginLayCommand = RelayCommand.Simple(BeginLay, () => Scene.Selection.Count == 1);
        ApplyEmbossCommand = AsyncRelayCommand.Simple(ApplyEmboss, () => isEmbossMode && embossFace is not null);
        CancelEmbossCommand = RelayCommand.Simple(() => IsEmbossMode = false);
        LoadDrawingCommand = RelayCommand.Simple(LoadDrawing);
        ClearDrawingCommand = RelayCommand.Simple(
            () => { svgFile = ""; RefreshDrawing(); }, () => svgFile.Length > 0);
        RepeatCommand = RelayCommand.Simple(RepeatSelection, () => Scene.Selection.Count > 0);
        AbortCommand = RelayCommand.Simple(AbortWork, () => CanAbort);
        Undo.Trimmed += () => HistoryTrimmed = true;
        sink = new Sink(value => reported = value);
        MouldCommand = new AsyncRelayCommand(_ => MakeMould(), _ => Scene.Selection.Count == 1);
        AlignToSelectionCommand = RelayCommand.Simple(
            AlignToSelection, () => Scene.Selection.Count == 2);
        FitCheckCommand = RelayCommand.Simple(FitCheck, () => Scene.Selection.Count == 2);
        BeginMeasureCommand = RelayCommand.Simple(BeginMeasure, () => Scene.Objects.Count > 0);
        CancelMeasureCommand = RelayCommand.Simple(() => IsMeasureMode = false);
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
    public System.Windows.Input.ICommand InsertStairCommand { get; }
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
    public System.Windows.Input.ICommand BeginSubtractCommand { get; }
    public System.Windows.Input.ICommand ApplySubtractCommand { get; }
    public System.Windows.Input.ICommand CancelSubtractCommand { get; }
    public System.Windows.Input.ICommand BeginSplitCommand { get; }
    public System.Windows.Input.ICommand AlignToAxesCommand { get; }
    public System.Windows.Input.ICommand ApplySplitCommand { get; }
    public System.Windows.Input.ICommand CancelSplitCommand { get; }
    public System.Windows.Input.ICommand RepairCommand { get; }
    public System.Windows.Input.ICommand SmoothCommand { get; }
    public System.Windows.Input.ICommand RebuildCommand { get; }
    public System.Windows.Input.ICommand SimplifyCommand { get; }
    public System.Windows.Input.ICommand HollowCommand { get; }
    public System.Windows.Input.ICommand RepeatCommand { get; }
    public System.Windows.Input.ICommand AbortCommand { get; }
    public System.Windows.Input.ICommand MouldCommand { get; }
    public System.Windows.Input.ICommand AlignToSelectionCommand { get; }
    public System.Windows.Input.ICommand FitCheckCommand { get; }
    public System.Windows.Input.ICommand BeginEmbossCommand { get; }
    public System.Windows.Input.ICommand BeginLayCommand { get; }
    public System.Windows.Input.ICommand ApplyEmbossCommand { get; }
    public System.Windows.Input.ICommand CancelEmbossCommand { get; }
    public System.Windows.Input.ICommand LoadDrawingCommand { get; }
    public System.Windows.Input.ICommand ClearDrawingCommand { get; }
    public System.Windows.Input.ICommand BeginMeasureCommand { get; }
    public System.Windows.Input.ICommand CancelMeasureCommand { get; }
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
            // The real-unit fields are the millimetre ones divided by the scale, so they have to
            // hear about every move and resize - including one made by dragging a handle, which
            // never passes through this class at all. The object is the only thing that knows.
            if (selected is not null) selected.PropertyChanged -= OnSelectedTransformed;
            selected = value;
            if (selected is not null) selected.PropertyChanged += OnSelectedTransformed;

            Raise(nameof(Selected));
            Raise(nameof(HasSelection));
            Raise(nameof(SelectionSummary));
            RaiseReal();
        }
    }

    /// <summary>
    /// Mirrors the selected object's own notifications onto the boxes.
    ///
    /// Only on the transform itself. The object announces a dozen derived properties for one
    /// change - every position, size and rotation component - and answering each of them meant
    /// the whole bar, the status line and the readings at scale were rebuilt twelve times over.
    /// </summary>
    private void OnSelectedTransformed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SceneObject.Transform) or nameof(SceneObject.Mesh))
            RaiseTransformFields();
    }

    /// <summary>One object selected - the numeric fields in the manipulator bar need exactly one.</summary>
    public bool HasSelection => selected is not null;

    /// <summary>Anything selected. The handles work on a group even when the fields cannot.</summary>
    public bool HasAnySelection => Scene.Selection.Count > 0;

    public bool HasOneSelected => Scene.Selection.Count == 1;

    public bool HasManySelected => Scene.Selection.Count > 1;

    /// <summary>
    /// The middle of everything selected, and where to carry it to.
    ///
    /// One object shows its own position; several have no single position to show, and leaving
    /// the boxes blank - which is what happened before - loses the readout exactly when a group
    /// is hardest to place by eye. The middle of the lot is the honest answer, and typing into it
    /// moves the whole group by the difference, so the parts keep their arrangement.
    ///
    /// Measured on the bounding box rather than by averaging the objects' own positions, because
    /// an object's position is wherever its origin happens to sit and a big part would otherwise
    /// drag the middle towards itself.
    /// </summary>
    public float GroupX
    {
        get => unit.From(GroupCentre().X);
        set => MoveGroupTo(Axis.X, unit.To(value));
    }

    public float GroupY
    {
        get => unit.From(GroupCentre().Y);
        set => MoveGroupTo(Axis.Y, unit.To(value));
    }

    public float GroupZ
    {
        get => unit.From(GroupCentre().Z);
        set => MoveGroupTo(Axis.Z, unit.To(value));
    }

    /// <summary>
    /// The turn every selected object shares, or nothing when they disagree.
    ///
    /// Typing one sets all of them to it. Dragging the rings adds the same amount to each, which
    /// is the same idea: a group has no rotation of its own, so the honest thing to show is what
    /// they have in common.
    /// </summary>
    public float GroupRoll
    {
        get => Shared(o => o.RotationX);
        set => TurnGroup(Axis.X, value);
    }

    public float GroupPitch
    {
        get => Shared(o => o.RotationY);
        set => TurnGroup(Axis.Y, value);
    }

    public float GroupYaw
    {
        get => Shared(o => o.RotationZ);
        set => TurnGroup(Axis.Z, value);
    }

    /// <summary>
    /// How far the whole selection reaches, and what to stretch it to.
    ///
    /// Typing a size here moves the parts as well as scaling them, about the middle of the lot,
    /// so the box really does come out the size asked for. Dragging a handle grows each part
    /// where it stands and leaves the gaps between them alone - which is what you want from a
    /// drag, and not what you want from a number that has to be met exactly.
    /// </summary>
    public float GroupSizeX
    {
        get => unit.From(GroupExtent().X);
        set => ResizeGroup(Axis.X, unit.To(value));
    }

    public float GroupSizeY
    {
        get => unit.From(GroupExtent().Y);
        set => ResizeGroup(Axis.Y, unit.To(value));
    }

    public float GroupSizeZ
    {
        get => unit.From(GroupExtent().Z);
        set => ResizeGroup(Axis.Z, unit.To(value));
    }

    private float Shared(Func<SceneObject, float> of)
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return 0f;

        float first = of(selection[0]);
        foreach (var o in selection)
            if (MathF.Abs(of(o) - first) > 0.05f) return 0f;

        return first;
    }

    /// <summary>
    /// Puts the three angles back to zero without moving the object.
    ///
    /// The turn is folded into the geometry instead: every vertex is moved to where it was
    /// already being drawn, and the transform is left with nothing to do. The object does not
    /// budge - what changes is that its own axes are now the world's, so the resize arrows and
    /// the box round it line up with the plate again instead of leaning with the object.
    ///
    /// Zeroing the angles on their own would not do: that swings the object back to how it was
    /// built, which throws away the very turn that was wanted. Nor would leaving the turn where
    /// it is, because then the arrows go on leaning.
    ///
    /// The scale goes in with it. The two cannot be separated on anything stretched unevenly -
    /// scaling then turning is not the same as turning then scaling - so both are baked and the
    /// size boxes go on reading the same millimetres as before.
    /// </summary>
    private void AlignToAxes()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var aligned = new List<SceneObject>(selection.Count);

        foreach (var o in selection)
        {
            var baked = MeshTransform.Transformed(
                o.Mesh, Matrix4x4.CreateScale(o.Scale) * MeshTransform.Rotation(o.Rotation));

            aligned.Add(new SceneObject(o.Name, baked)
            {
                Colour = o.Colour,
                Position = o.Position,
                Origin = o.Origin
            });
        }

        Undo.Execute(new ReplaceObjectsCommand("Align to axes", selection, aligned));
        RefreshSelection();

        Status = selection.Count == 1
            ? $"{selection[0].Name} aligned with the world axes"
            : $"{selection.Count} objects aligned with the world axes";
    }

    /// <summary>Whether anything selected is turned. Nothing to align if none of it is.</summary>
    private bool AnythingTurned()
    {
        foreach (var o in Scene.Selection)
            if (o.Rotation != Vector3.Zero) return true;

        return false;
    }

    private void TurnGroup(Axis axis, float degrees)
    {
        if (!float.IsFinite(degrees) || Scene.Selection.Count == 0) return;

        float wanted = GizmoMath.NormaliseDegrees(degrees);

        foreach (var o in Scene.Selection)
        {
            var was = o.Rotation;
            o.Rotation = axis switch
            {
                Axis.X => was with { X = wanted },
                Axis.Y => was with { Y = wanted },
                _ => was with { Z = wanted }
            };
        }

        RaiseGroup();
    }

    private void ResizeGroup(Axis axis, float millimetres)
    {
        if (!float.IsFinite(millimetres) || millimetres < 0.01f) return;
        if (Scene.Selection.Count == 0) return;

        float now = Along(GroupExtent(), axis);
        if (now < 1e-3f) return;

        float ratio = millimetres / now;
        if (MathF.Abs(ratio - 1f) < 1e-4f) return;

        float anchor = Along(GroupCentre(), axis);

        foreach (var o in Scene.Selection)
        {
            var scale = o.Scale;
            o.Scale = UniformScale
                ? scale * ratio
                : axis switch
                {
                    Axis.X => scale with { X = scale.X * ratio },
                    Axis.Y => scale with { Y = scale.Y * ratio },
                    _ => scale with { Z = scale.Z * ratio }
                };

            var at = o.Position;
            o.Position = axis switch
            {
                Axis.X => at with { X = GizmoMath.ScaledAbout(anchor, at.X, ratio) },
                Axis.Y => at with { Y = GizmoMath.ScaledAbout(anchor, at.Y, ratio) },
                _ => at with { Z = GizmoMath.ScaledAbout(anchor, at.Z, ratio) }
            };
        }

        RaiseGroup();
    }

    private static float Along(Vector3 v, Axis axis) =>
        axis switch { Axis.X => v.X, Axis.Y => v.Y, _ => v.Z };

    private Vector3 GroupExtent()
    {
        var bounds = Bounds.Empty;
        foreach (var o in Scene.Selection) bounds = bounds.Union(o.WorldBounds);

        return bounds.IsEmpty ? Vector3.Zero : bounds.Size;
    }

    private void RaiseGroup()
    {
        Raise(nameof(GroupX));
        Raise(nameof(GroupY));
        Raise(nameof(GroupZ));
        Raise(nameof(GroupRoll));
        Raise(nameof(GroupPitch));
        Raise(nameof(GroupYaw));
        Raise(nameof(GroupSizeX));
        Raise(nameof(GroupSizeY));
        Raise(nameof(GroupSizeZ));

        // The real-unit fields are derived from all of the above, so they have to be told too -
        // without this they showed what the selection measured when it was picked, and went on
        // showing it while the parts moved under the pointer.
        RaiseReal();
        IsDirty = true;
    }

    private Vector3 GroupCentre()
    {
        var bounds = Bounds.Empty;
        foreach (var o in Scene.Selection) bounds = bounds.Union(o.WorldBounds);

        return bounds.IsEmpty ? Vector3.Zero : bounds.Center;
    }

    private void MoveGroupTo(Axis axis, float where)
    {
        if (!float.IsFinite(where) || Scene.Selection.Count == 0) return;

        var centre = GroupCentre();
        float travel = where - axis switch { Axis.X => centre.X, Axis.Y => centre.Y, _ => centre.Z };
        if (MathF.Abs(travel) < 1e-4f) return;

        var offset = axis switch
        {
            Axis.X => new Vector3(travel, 0, 0),
            Axis.Y => new Vector3(0, travel, 0),
            _ => new Vector3(0, 0, travel)
        };

        foreach (var o in Scene.Selection) o.Position += offset;

        RaiseGroup();
    }

    /// <summary>
    /// The floating move/rotate/resize strip. Out of the way while a tool is running, along with
    /// the handles it drives - leaving it up would offer a resize that the tool's own handles
    /// are sitting on top of.
    /// </summary>
    public bool ShowManipulatorBar => HasAnySelection && !IsToolRunning;

    public string Status
    {
        get => status;
        set => Set(ref status, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            Set(ref isBusy, value);
            Raise(nameof(CanAbort));
        }
    }

    /// <summary>What is running, named on the panel that covers the window while it does.</summary>
    public string BusyTitle
    {
        get => busyTitle;
        private set => Set(ref busyTitle, value);
    }

    /// <summary>
    /// How long it has been going.
    ///
    /// Shown because the honest answer to "is it stuck?" is a number that keeps moving. A bar
    /// that sweeps back and forth says only that the window is still being painted.
    /// </summary>
    public string BusyElapsed
    {
        get => busyElapsed;
        private set => Set(ref busyElapsed, value);
    }

    /// <summary>
    /// What the running operation is doing now - "Sampling the model", "Keying piece 2 of 4".
    ///
    /// This is the half that answers "has it stopped?" for the work that cannot count itself. A
    /// boolean recurses over a tree whose size is not known until it has been built, so there is no
    /// honest fraction to show for one; there is always an honest answer to what it is up to.
    /// </summary>
    public string BusyStage
    {
        get => busyStage;
        private set => Set(ref busyStage, value);
    }

    /// <summary>How far through, from nought to one, when the work can say.</summary>
    public double BusyFraction
    {
        get => busyFraction;
        private set => Set(ref busyFraction, value);
    }

    /// <summary>Whether the bar sweeps rather than fills, because there is no fraction to show.</summary>
    public bool BusyIndeterminate
    {
        get => busyIndeterminate;
        private set => Set(ref busyIndeterminate, value);
    }

    /// <summary>
    /// The history has had to let go of its oldest steps, so undo can no longer get all the way
    /// back - and the only thing that still can is a saved version.
    ///
    /// Shown until one is saved rather than flashed in the status bar, because it is not news
    /// about the last operation; it is a standing fact about what can still be recovered.
    /// </summary>
    public bool HistoryTrimmed
    {
        get => historyTrimmed;
        private set => Set(ref historyTrimmed, value);
    }

    /// <summary>Abort has been pressed and the work has not reached a checkpoint yet.</summary>
    public bool IsStopping
    {
        get => isStopping;
        private set
        {
            Set(ref isStopping, value);
            Raise(nameof(CanAbort));
            Raise(nameof(BusyHint));
        }
    }

    public bool CanAbort => isBusy && !isStopping;

    /// <summary>
    /// Why aborting is safe, and - once pressed - why it has not happened yet.
    ///
    /// Worth saying outright: every one of these operations builds its result on a background
    /// thread and only reaches the scene once that returns, so there is never a half-applied
    /// model to recover from.
    /// </summary>
    public string BusyHint => isStopping
        ? "Stopping - waiting for the current step to reach a point it can leave off at."
        : "Aborting changes nothing: the result is only applied once the work has finished.";

    public string UndoLabel => Undo.NextUndoLabel is { } label ? $"Undo {label}" : "Undo";

    /// <summary>
    /// Names for one operation that makes several objects at once.
    ///
    /// <see cref="Scene.UniqueName"/> reads the plate, and these operations ask for every name
    /// before any of the objects joins it. Passing the method straight in gave every copy the
    /// same answer - a spiral stair of twelve treads all called "Tread 2", a paste of three
    /// walls all called "Wall 2". This counts what it has already given out.
    /// </summary>
    private Func<string, string> Namer()
    {
        List<string> given = [];

        return baseName =>
        {
            string name = Scene.UniqueName(baseName, given);
            given.Add(name);
            return name;
        };
    }

    /// <summary>
    /// Marks the start of a long operation: covers the window, starts the clock, and hands back
    /// the token the work has to watch.
    /// </summary>
    /// <summary>
    /// Where a running operation reports to.
    ///
    /// Handed to the work that can count itself and ignored by the rest. Nothing is marshalled: the
    /// worker writes the last report into a field and the clock below publishes it, because a grid
    /// slice is not worth a hop onto the UI thread and there are hundreds of thousands of them.
    /// </summary>
    private IProgress<WorkProgress> Progress => sink;

    private sealed class Sink(Action<WorkProgress> keep) : IProgress<WorkProgress>
    {
        public void Report(WorkProgress value) => keep(value);
    }

    private CancellationToken StartWork(string title)
    {
        work?.Dispose();
        work = new CancellationTokenSource();

        BusyTitle = title;
        BusyStage = "";
        BusyElapsed = "";
        BusyFraction = 0;
        BusyIndeterminate = true;
        reported = WorkProgress.Doing("");
        IsStopping = false;
        IsBusy = true;
        Status = $"{title}...";

        workStarted = DateTime.UtcNow;
        workClock ??= CreateClock();
        workClock.Start();

        return work.Token;
    }

    private void EndWork()
    {
        workClock?.Stop();

        BusyStage = "";
        BusyFraction = 0;
        BusyIndeterminate = true;

        IsBusy = false;
        IsStopping = false;
        BusyElapsed = "";

        // Only ever disposed here, which is reached in a finally after the work has been awaited,
        // so nothing is still reading the token by this point.
        work?.Dispose();
        work = null;
    }

    /// <summary>
    /// Asks the running operation to stop.
    ///
    /// Cooperative, so it takes as long as the work takes to reach its next look at the token -
    /// under a tenth of a second inside a boolean, up to one grid slice during a rebuild. The
    /// panel says it is stopping rather than simply vanishing, because a button that appears to
    /// do nothing for half a second gets pressed again.
    /// </summary>
    private void AbortWork()
    {
        if (work is null || isStopping) return;

        IsStopping = true;
        Status = $"Stopping {busyTitle}...";
        work.Cancel();
    }

    private DispatcherTimer CreateClock()
    {
        // A quarter of a second: often enough that the number never looks frozen, rare enough
        // that it is not competing with the work for the dispatcher.
        var clock = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        clock.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - workStarted;

            // Nothing at first. Most operations are over well inside a second, and a counter
            // that flashes up "0 s" and disappears is worse than no counter at all.
            BusyElapsed = elapsed.TotalSeconds < 0.7 ? "" : Elapsed(elapsed);

            // Read once. The worker writes a fraction and a stage as one value without a lock, so
            // a torn read would pair one with the other's neighbour - a quarter of a second of a
            // slightly wrong caption, and cheaper than making every report take a lock.
            var now = reported;

            BusyStage = now.Stage ?? BusyStage;
            BusyIndeterminate = !now.Measured;

            if (now.Measured) BusyFraction = Math.Clamp(now.Done, 0d, 1d);
        };

        return clock;
    }

    private static string Elapsed(TimeSpan elapsed) => elapsed.TotalSeconds < 60
        ? $"{elapsed.TotalSeconds:0} s"
        : $"{(int)elapsed.TotalMinutes} m {elapsed.Seconds:00} s";

    /// <summary>
    /// Whether a failure was really the user pressing Abort.
    ///
    /// Parallel.For is given the token so that cancelling surfaces as a plain
    /// OperationCanceledException, but a body that throws one per worker can still arrive
    /// wrapped, and a wrapped abort must not be reported as a crash.
    /// </summary>
    private static bool WasAborted(Exception ex) =>
        ex is OperationCanceledException
        || (ex is AggregateException all && all.InnerExceptions.Count > 0
            && all.InnerExceptions.All(inner => inner is OperationCanceledException));

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
            return $"{name}{(isDirty ? " *" : string.Empty)} - 3DFastCraft {Version}";
        }
    }

    /// <summary>
    /// The version, from the assembly rather than a constant here, so there is one place to
    /// change it and no way for the two to disagree. Three parts, matching what the release is
    /// called - the fourth is always zero and would be nothing but noise.
    /// </summary>
    public static string Version
    {
        get
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
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

            // The object's own kept answer. Building the world mesh and checking it here cost a
            // second at eight hundred thousand triangles and four at three million, every time
            // this was raised - and one nudge raises it a dozen times.
            var health = o.Health;

            // Measured here rather than by Bounds, which reports in millimetres and should: the
            // unit is the view's business and the geometry has no idea one has been chosen.
            var size = o.WorldBounds.Size;
            string measured = o.WorldBounds.IsEmpty
                ? "empty"
                : $"{unit.From(size.X):0.####} x {unit.From(size.Y):0.####} x "
                  + $"{unit.From(size.Z):0.####} {unit.Label}";

            return $"{o.Name} - {measured} - {health.TriangleCount:N0} triangles - " +
                   $"{o.VolumeCm3:0.##} cm3 - {health.Describe()}";
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

    /// <summary>
    /// The unit the position and size boxes are read and typed in.
    ///
    /// Display only. Millimetres are what is stored and what is written to an STL, which carries
    /// no unit of its own and is read as millimetres by every slicer - so working in inches
    /// changes the boxes and not one number in the exported file.
    /// </summary>
    public MeasureUnit Unit
    {
        get => unit;
        set
        {
            if (unit.Label == value.Label) return;

            Set(ref unit, value);
            Raise(nameof(UnitLabel));
            RaiseTransformFields();
        }
    }

    public IReadOnlyList<MeasureUnit> Units => MeasureUnit.All;

    /// <summary>What the boxes are labelled with - "mm", "in", and so on.</summary>
    public string UnitLabel => unit.Label;

    /// <summary>What one press of an arrow key changes a box by, in the current unit.</summary>
    public float UnitStep => unit.Step;

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
    /// Stop a dragged object where it meets another rather than letting it pass through.
    ///
    /// Sliding a part up against its neighbour until it stops is how things get assembled. Off by
    /// default, because parts often need to overlap on their way to a boolean. Shortcut: C.
    /// </summary>
    public bool StopOnContact
    {
        get => stopOnContact;
        set => Set(ref stopOnContact, value);
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
            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
        }
    }

    public bool SplitPlaneVisible => isSplitMode;

    /// <summary>
    /// Whether a tool has taken the object over.
    ///
    /// Splitting, engraving and lettering all put their own handles on the very same object and
    /// all mean something different by a click on it. The move and resize handles, and the tool
    /// strip that goes with them, stand down while one of them is running rather than sitting
    /// underneath and leaving it to the pointer to decide which was meant.
    /// </summary>
    public bool IsToolRunning => isSplitMode || isEngraveMode || isEmbossMode || isLayMode;

    /// <summary>
    /// While this is on, clicking a face tips the object over onto it. One click does the whole
    /// job, so unlike the other face tools there is no panel and no second step.
    /// </summary>
    public bool IsLayMode
    {
        get => isLayMode;
        set
        {
            if (isLayMode == value) return;

            Set(ref isLayMode, value);
            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
        }
    }

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

            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
            Raise(nameof(HasEngraveFace));
            RaiseEngraveText();
            EngraveFaceChanged?.Invoke();
        }
    }

    public bool HasEngraveFace => engrave.HasFace;

    // --- Lettering -----------------------------------------------------------------

    /// <summary>
    /// While this is on, clicking picks the face to letter. Its own mode, like engraving, and
    /// they take it in turns rather than sharing.
    /// </summary>
    public bool IsEmbossMode
    {
        get => isEmbossMode;
        set
        {
            if (isEmbossMode == value) return;

            Set(ref isEmbossMode, value);
            if (!value)
            {
                embossFace = null;
                embossMesh = null;
                embossPlacement = SurfacePlacement.Middle;
                letteringCache = null;
            }

            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
            Raise(nameof(HasEmbossFace));
            Raise(nameof(EmbossSummary));
            EngraveFaceChanged?.Invoke();
        }
    }

    public bool HasEmbossFace => embossFace is not null;

    /// <summary>The face being lettered, for the viewport to highlight.</summary>
    public FacePatch? EmbossFace => embossFace;

    public string EmbossText
    {
        get => embossText;
        set { Set(ref embossText, value ?? ""); RefreshLettering(); }
    }

    public string EmbossFont
    {
        get => embossFont;
        set { Set(ref embossFont, string.IsNullOrWhiteSpace(value) ? "Arial" : value); RefreshLettering(); }
    }

    /// <summary>Common faces, so the usual ones need no typing.</summary>
    public IReadOnlyList<string> EmbossFonts { get; } =
        ["Arial", "Segoe UI", "Calibri", "Consolas", "Georgia", "Impact", "Times New Roman", "Verdana"];

    public float EmbossHeight
    {
        get => embossHeight;
        set { Set(ref embossHeight, Math.Clamp(value, 1f, 500f)); RefreshLettering(); }
    }

    /// <summary>How deep it is cut, or how far it stands proud.</summary>
    public float EmbossDepth
    {
        get => embossDepth;
        set { Set(ref embossDepth, Math.Clamp(value, 0.05f, 50f)); RefreshEmboss(); }
    }

    public bool EmbossBold
    {
        get => embossBold;
        set { Set(ref embossBold, value); RefreshLettering(); }
    }

    /// <summary>Raised off the face rather than cut into it.</summary>
    public bool EmbossRaised
    {
        get => embossRaised;
        set { Set(ref embossRaised, value); RefreshEmboss(); }
    }

    /// <summary>
    /// How far the far end of the lettering is drawn in, sloping its walls.
    ///
    /// Worth having for printing rather than for looks: raised lettering with upright walls
    /// leaves a sharp step the first layer has to bridge, and cut lettering with upright walls
    /// traps the nozzle in a slot the width of one line.
    /// </summary>
    public float EmbossBevel
    {
        get => embossBevel;
        set { Set(ref embossBevel, Math.Clamp(value, 0f, 10f)); RefreshEmboss(); }
    }

    public TextProjection EmbossProjection
    {
        get => embossProjection;
        set
        {
            if (embossProjection == value) return;

            Set(ref embossProjection, value);
            Raise(nameof(IsEmbossWrapped));
            RefreshEmboss();
        }
    }

    public IReadOnlyList<TextProjection> EmbossProjections { get; } =
        [TextProjection.Planar, TextProjection.Cylindrical, TextProjection.Spherical];

    /// <summary>Whether the lettering is being wrapped rather than laid flat.</summary>
    public bool IsEmbossWrapped => embossProjection != TextProjection.Planar;

    /// <summary>Where the lettering sits, and which way up. Driven by the handles or the fields.</summary>
    public SurfacePlacement EmbossPlacement
    {
        get => embossPlacement;
        set
        {
            if (embossPlacement == value) return;

            embossPlacement = value;
            Raise(nameof(EmbossAcross));
            Raise(nameof(EmbossUp));
            Raise(nameof(EmbossAngle));
            RefreshEmboss();
        }
    }

    public float EmbossAcross
    {
        get => embossPlacement.OffsetMm.X;
        set => EmbossPlacement = embossPlacement with
        {
            OffsetMm = new Vector2(Rounded(value), embossPlacement.OffsetMm.Y)
        };
    }

    public float EmbossUp
    {
        get => embossPlacement.OffsetMm.Y;
        set => EmbossPlacement = embossPlacement with
        {
            OffsetMm = new Vector2(embossPlacement.OffsetMm.X, Rounded(value))
        };
    }

    public float EmbossAngle
    {
        get => embossPlacement.AngleDegrees;
        set => EmbossPlacement = embossPlacement with { AngleDegrees = Rounded(value) };
    }

    private static float Rounded(float value) => float.IsFinite(value) ? MathF.Round(value, 3) : 0f;

    /// <summary>Half the size of the lettering, for the handles to be drawn round.</summary>
    public Vector2 EmbossExtent => SurfacePlacement.Extent(Lettering());

    /// <summary>
    /// The shape the lettering is laid onto.
    ///
    /// The curved ones are taken from where the click landed rather than from the object's
    /// bounding box alone: a barrel picked on its side gives a radius that passes exactly
    /// through the point clicked, so the lettering starts where it was asked for instead of
    /// somewhere near it.
    /// </summary>
    public IPlacementSurface? EmbossSurface()
    {
        if (embossFace is not { } face) return null;

        switch (embossProjection)
        {
            case TextProjection.Cylindrical:
            {
                var axis = new Vector2(embossBounds.Center.X, embossBounds.Center.Y);
                var outward = new Vector2(embossPick.X, embossPick.Y) - axis;

                float radius = outward.Length();
                if (radius < 0.05f) return new PlanarSurface(face);

                return new CylinderSurface(
                    new Vector3(axis.X, axis.Y, embossPick.Z), radius,
                    MathF.Atan2(outward.Y, outward.X));
            }

            case TextProjection.Spherical:
            {
                var outward = embossPick - embossBounds.Center;

                float radius = outward.Length();
                if (radius < 0.05f) return new PlanarSurface(face);

                return new SphereSurface(
                    embossBounds.Center, radius,
                    MathF.Atan2(outward.Y, outward.X),
                    MathF.Asin(Math.Clamp(outward.Z / radius, -1f, 1f)));
            }

            default:
                return new PlanarSurface(face);
        }
    }

    public string EmbossSummary
    {
        get
        {
            if (embossFace is null) return "Click the face to letter.";

            var shapes = EmbossShapes();
            if (shapes.Count == 0) return "Nothing to letter - type something.";

            string what = embossRaised
                ? $"raised {embossDepth:0.##} mm"
                : $"cut {embossDepth:0.##} mm deep";

            string wrapped = embossProjection == TextProjection.Planar
                ? ""
                : $", {embossProjection.ToString().ToLowerInvariant()}";

            string bevel = embossBevel > 0 ? $", {embossBevel:0.##} mm bevel" : "";

            return $"{shapes.Count} shape(s), {embossHeight:0.#} mm tall, {what}{wrapped}{bevel}";
        }
    }

    private void BeginLay()
    {
        if (Scene.Selection.Count != 1) return;

        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = true;

        Status = "Click the face you want it to stand on";
    }

    /// <summary>
    /// Tips the object over so the face under the click ends up flat on the plate.
    ///
    /// The face's outward direction is turned to point straight down and the object is dropped
    /// onto the bed. Which face was clicked is all that is needed - not a flat patch of them, as
    /// engraving wants - so this works on a facet of anything round as well, laying it on the
    /// tangent there.
    ///
    /// One click and it is done: the mode ends itself, because there is nothing else to say.
    /// </summary>
    public bool LayOnFace(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isLayMode) return false;
        if (worldNormal.LengthSquared() < 1e-12f) return false;

        var facing = Vector3.Normalize(worldNormal);

        // The hit reports the triangle's own direction, which may be the inward one. It is the
        // outward face that has to end up against the bed.
        if (Vector3.Dot(facing, worldPoint - target.WorldBounds.Center) < 0) facing = -facing;

        var before = new[] { TransformState.Capture(target) };

        var turn = MeshTransform.TurnFromTo(facing, -Vector3.UnitZ);
        target.Rotation = MeshTransform.EulerFrom(MeshTransform.Rotation(target.Rotation) * turn);

        // Tipping it over will have left it through the bed or above it.
        target.Position = target.Position with { Z = target.Position.Z - target.WorldBounds.Min.Z };

        if (TransformCommand.CreateIfChanged("Lay on face", [target], before) is { } command)
            Undo.Execute(command);

        IsLayMode = false;
        RefreshSelection();
        Status = $"{target.Name} laid on the picked face";

        return true;
    }

    private void BeginEmboss()
    {
        if (Scene.Selection.Count != 1) return;

        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsEmbossMode = true;

        Status = "Click the face you want to letter";
    }

    /// <summary>Picks the face to letter. Both arguments are in world space, as for engraving.</summary>
    public bool PickEmbossFace(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isEmbossMode) return false;

        var world = target.ToWorldMesh();
        var face = EngraveState.FaceAt(world, worldPoint, worldNormal);

        if (face is null)
        {
            Status = "That is not a flat face - pick one of the flat sides";
            return false;
        }

        // Clicking the same face again is how the wrapping is re-anchored, and it would be
        // maddening if it also threw away a placement that had just been dragged into position.
        if (!SameFace(embossFace, face)) embossPlacement = SurfacePlacement.Middle;

        embossMesh = world;
        embossFace = face;
        embossPick = worldPoint;
        embossBounds = world.ComputeBounds();
        letteringCache = null;

        Raise(nameof(HasEmbossFace));
        Raise(nameof(EmbossAcross));
        Raise(nameof(EmbossUp));
        Raise(nameof(EmbossAngle));
        RefreshEmboss();
        Status = EmbossSummary;
        return true;
    }

    /// <summary>Whether two picks landed on the same flat face - same direction, same plane.</summary>
    private static bool SameFace(FacePatch? a, FacePatch? b)
    {
        if (a is null || b is null) return false;
        if (Vector3.Dot(a.Normal, b.Normal) < 0.999f) return false;

        return MathF.Abs(Vector3.Dot(a.Normal, a.Origin) - Vector3.Dot(b.Normal, b.Origin)) < 0.01f;
    }

    /// <summary>
    /// The outlines as the font gives them: middle at the origin, nothing placed yet.
    ///
    /// Kept from one call to the next. Dragging the handles asks for the lettering on every
    /// mouse move, and asking the font system to lay a word out again each time - only to move
    /// the result a millimetre - is the one thing here expensive enough to be felt.
    /// </summary>
    private List<TextShape> Lettering()
    {
        if (embossFace is null) return [];
        if (letteringCache is not null) return letteringCache;

        if (svgFile.Length > 0)
        {
            try
            {
                return letteringCache = SvgOutlines.Read(svgFile, embossHeight);
            }
            catch (Exception ex)
            {
                // A file that has gone missing or will not parse drops the tool back to text
                // rather than leaving it stuck on something it cannot read.
                Status = $"Could not read {SvgName}: {ex.Message}";
                svgFile = "";
                RefreshDrawing();
            }
        }

        return letteringCache = GlyphOutlines
            .Build(embossText, embossFont, embossHeight, embossBold)
            .Select(g => new TextShape(g.Outline, g.Holes))
            .ToList();
    }

    /// <summary>
    /// Stamps a shape from an SVG drawing instead of typed letters.
    ///
    /// Everything downstream sees the same thing either way - an outline and the loops inside it
    /// - so the placement handles, the size, the bevel, the wrapping and the choice of cut or
    /// raised all work on a logo exactly as they do on a word.
    /// </summary>
    private void LoadDrawing()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Drawing (*.svg)|*.svg|All files (*.*)|*.*",
            Title = "Stamp a drawing"
        };
        if (dialog.ShowDialog() != true) return;

        svgFile = dialog.FileName;
        RefreshDrawing();

        if (svgFile.Length > 0 && Lettering().Count == 0)
            Status = $"{SvgName} has no filled shape in it - lines on their own have no area to stamp.";
    }

    private void RefreshDrawing()
    {
        Raise(nameof(HasDrawing));
        Raise(nameof(UsesText));
        Raise(nameof(SvgName));
        Raise(nameof(DrawingShapes));
        RefreshLettering();
    }

    /// <summary>Whether the lettering is coming from a drawing rather than from the text box.</summary>
    public bool HasDrawing => svgFile.Length > 0;

    /// <summary>
    /// The loaded drawing's outlines, for the panel to show. The very ones that will be stamped,
    /// so what is on screen is what will be on the object - which is the only way to see that a
    /// drawing came in with its holes intact before committing to it.
    /// </summary>
    public IReadOnlyList<TextShape> DrawingShapes => HasDrawing ? Lettering() : [];

    public bool UsesText => svgFile.Length == 0;

    public string SvgName => Path.GetFileName(svgFile);

    /// <summary>What is being stamped, for the messages that have to name it.</summary>
    private string Stamped() => svgFile.Length > 0 ? SvgName : $"\"{embossText}\"";

    /// <summary>Throws the laid-out lettering away, for whatever would change how it reads.</summary>
    private void RefreshLettering()
    {
        letteringCache = null;
        RefreshEmboss();
    }

    /// <summary>The outlines where they have been put, ready to lay on the surface.</summary>
    private IReadOnlyList<TextShape> EmbossShapes() => embossPlacement.Apply(Lettering());

    /// <summary>A thin slab of the lettering, laid on the shape so the placement can be seen.</summary>
    public Mesh? EmbossPreview()
    {
        if (EmbossSurface() is not { } surface) return null;

        var shapes = EmbossShapes();

        // Standing proud whichever way it will go: a preview sunk into the object would be
        // hidden by the very face it is being placed on.
        float clear = surface.ClearanceMm + 0.06f;

        return shapes.Count == 0 ? null : TextSolid.Build(shapes, surface, clear, clear + 0.03f);
    }

    /// <summary>
    /// What to try next when the lettering would not go on. Different for wrapped lettering,
    /// because the advice that fits a flat face - a bigger size, a shallower cut - is not what
    /// is going wrong round a barrel.
    /// </summary>
    private string WayRound() => embossProjection == TextProjection.Planar
        ? "A flatter face, a larger size or a shallower depth will usually get through."
        : "Wrapping is hardest on letters with an enclosed middle - O, B, A, D. Lettering "
          + "without them usually goes on; so does Rebuild on the Tools tab afterwards, which "
          + "remakes the whole shape from scratch.";

    private void RefreshEmboss()
    {
        Raise(nameof(EmbossSummary));
        Raise(nameof(EmbossExtent));

        if (!isEmbossMode) return;

        EngraveFaceChanged?.Invoke();
        PlacementChanged?.Invoke();
    }

    /// <summary>
    /// Raised when the placement handles need re-drawing: what they are placing has moved, or
    /// changed size, or is now on a different face. Both tools that use them raise it.
    /// </summary>
    public event Action? PlacementChanged;

    private async Task ApplyEmboss()
    {
        if (Scene.Selection.Count != 1) return;
        if (embossMesh is not { } world || EmbossSurface() is not { } surface) return;

        var shapes = EmbossShapes();
        if (shapes.Count == 0)
        {
            Status = svgFile.Length > 0
                ? $"{SvgName} has nothing in it to stamp"
                : "Nothing to letter - type something first";
            return;
        }

        var source = Scene.Selection[0];
        bool raised = embossRaised;
        float amount = embossDepth;

        // A bevel that would meet in the middle before it reached the far end leaves nothing
        // there to cap, so it is held to the depth it has room to slope over.
        float bevel = Math.Min(embossBevel, amount * 0.9f);

        if (IsBusy) return;

        var token = StartWork(raised ? "Raising lettering" : "Cutting lettering");
        try
        {
            var result = await Task.Run(
                () => TextCutter.Apply(world, shapes, surface, raised, amount, bevel, token));

            if (result is null || result.TriangleCount == 0)
            {
                Status = "The lettering produced no geometry";
                return;
            }

            if (!result.CheckHealth().IsWatertight)
            {
                Status = $"Lettering that face came out unprintable - {result.CheckHealth().Describe()}. Nothing was changed.";
                MessageBox.Show(
                    "The lettering could not be applied cleanly, so the object has been left as "
                    + "it was." + Environment.NewLine + Environment.NewLine + WayRound(),
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var lettered = new SceneObject(Scene.UniqueName($"{source.Name} text"), result)
            {
                Colour = source.Colour
            }.Centred();

            Undo.Execute(new ReplaceObjectsCommand(raised ? "Raise text" : "Cut text", [source], [lettered]));
            IsEmbossMode = false;
            RefreshSelection();

            Status = $"{(raised ? "Raised" : "Cut")} {Stamped()} - {result.TriangleCount:N0} triangles";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Lettering failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndWork();
        }
    }

    // --- Measuring -----------------------------------------------------------------

    /// <summary>Raised when the tape moves, so the viewport can redraw it.</summary>
    public event Action? MeasureChanged;

    /// <summary>
    /// While this is on, clicking takes a measurement rather than selecting. Its own mode for
    /// the same reason splitting and engraving are: the click means something else.
    /// </summary>
    public bool IsMeasureMode
    {
        get => isMeasureMode;
        set
        {
            if (isMeasureMode == value) return;

            Set(ref isMeasureMode, value);
            if (!value) { measureFrom = null; measureTo = null; }

            RaiseMeasure();
        }
    }

    /// <summary>Pull each click onto the nearest corner or edge midpoint.</summary>
    public bool MeasureSnap
    {
        get => measureSnap;
        set => Set(ref measureSnap, value);
    }

    public Vector3? MeasureFrom => measureFrom;
    public Vector3? MeasureTo => measureTo;

    public bool HasMeasurement => measureFrom is not null && measureTo is not null;

    /// <summary>The reading, including the axis-by-axis gaps that a single number hides.</summary>
    public string MeasureSummary
    {
        get
        {
            if (measureFrom is not { } a) return "Click the first point.";
            if (measureTo is not { } b) return "Click the second point.";

            var gap = Vector3.Abs(b - a);
            return $"{Vector3.Distance(a, b):0.##} mm    "
                 + $"X {gap.X:0.##}    Y {gap.Y:0.##}    Z {gap.Z:0.##} mm";
        }
    }

    private void BeginSubtract()
    {
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsLayMode = false;
        IsMeasureMode = false;
        IsSplitMode = false;

        IsSubtractMode = true;
        Raise(nameof(SubtractSummary));
        Status = SubtractSummary;
    }

    private async Task ApplySubtract()
    {
        await RunBoolean(BooleanOp.Subtract);
        IsSubtractMode = false;
    }

    private void BeginMeasure()
    {
        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsLayMode = false;
        measureFrom = null;
        measureTo = null;
        IsMeasureMode = true;

        Status = "Click two points to measure between them";
    }

    /// <summary>
    /// Takes one end of a measurement. The third click starts a fresh one, so measuring several
    /// things in a row needs no button between them.
    /// </summary>
    public void TakeMeasurePoint(Vector3 world)
    {
        if (!isMeasureMode) return;

        if (measureFrom is null || measureTo is not null)
        {
            measureFrom = world;
            measureTo = null;
        }
        else
        {
            measureTo = world;
        }

        RaiseMeasure();
        Status = MeasureSummary;
    }

    /// <summary>
    /// Moves one end of the tape that is already down. Dragging is how a measurement gets taken
    /// twice - the first click is rarely on the exact corner meant, and re-clicking used to
    /// start a whole new measurement rather than adjust the one on screen.
    /// </summary>
    public void MoveMeasurePoint(bool second, Vector3 world)
    {
        if (!isMeasureMode) return;

        if (second) measureTo = world;
        else measureFrom = world;

        RaiseMeasure();
        Status = MeasureSummary;
    }

    private void RaiseMeasure()
    {
        Raise(nameof(MeasureFrom));
        Raise(nameof(MeasureTo));
        Raise(nameof(HasMeasurement));
        Raise(nameof(MeasureSummary));
        MeasureChanged?.Invoke();
    }

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

    /// <summary>
    /// What the model is drawn to: 87 means 1:87, and 1 means the part is the size it says.
    ///
    /// Nothing about the geometry changes - millimetres stay millimetres. What it buys is being
    /// told what a dimension means. Building a house at 1:87 meant dividing by 87 by hand for
    /// every wall, every riser and every door, and a number worked out on paper is a number that
    /// can be wrong without anything noticing.
    /// </summary>
    public float ModelScale
    {
        get => modelScale;
        set
        {
            float wanted = Math.Clamp(value, 1f, 5000f);
            if (Math.Abs(wanted - modelScale) < 0.001f) return;

            Set(ref modelScale, wanted);
            Raise(nameof(ScaleLabel));
            Raise(nameof(ModelScaleText));
            Raise(nameof(RealUnit));
            RaiseReal();
            RefreshSelection();
        }
    }

    /// <summary>
    /// The same size and position in real units, as fields rather than a read-out.
    ///
    /// Typing 9.57 into the metres box makes the part 110 mm at 1:87, and typing 110 into the
    /// millimetre box makes the metres box read 9.57. Both write the same underlying number -
    /// the model is always stored in millimetres, because that is what an STL carries and what
    /// a slicer reads. Nothing here is a second copy of the geometry.
    /// </summary>
    public bool ScaleApplies => modelScale > 1.001f && Scene.Selection.Count > 0;

    /// <summary>
    /// Millimetres on the plate to what they stand for at this scale, in metres or in feet
    /// depending on which system the boxes are in.
    /// </summary>
    private float ToReal(float millimetres) => millimetres * modelScale / unit.Real.Millimetres;

    private float FromReal(float real) => real * unit.Real.Millimetres / modelScale;

    /// <summary>
    /// Where the selection sits and how big it is, in millimetres.
    ///
    /// The Group boxes read in the display unit now, so the readings at scale have to come off
    /// these instead - taking the display value would have converted it twice and put a part in
    /// the wrong place the moment anyone left millimetres.
    /// </summary>
    private float GroupAt(Axis axis) => Along(GroupCentre(), axis);

    private float GroupSpan(Axis axis) => Along(GroupExtent(), axis);

    public float RealX
    {
        get => ToReal(HasOneSelected ? Selected!.PositionX : GroupAt(Axis.X));
        set
        {
            if (HasOneSelected) Selected!.PositionX = FromReal(value);
            else MoveGroupTo(Axis.X, FromReal(value));
            RaiseReal();
        }
    }

    public float RealY
    {
        get => ToReal(HasOneSelected ? Selected!.PositionY : GroupAt(Axis.Y));
        set
        {
            if (HasOneSelected) Selected!.PositionY = FromReal(value);
            else MoveGroupTo(Axis.Y, FromReal(value));
            RaiseReal();
        }
    }

    public float RealZ
    {
        get => ToReal(HasOneSelected ? Selected!.PositionZ : GroupAt(Axis.Z));
        set
        {
            if (HasOneSelected) Selected!.PositionZ = FromReal(value);
            else MoveGroupTo(Axis.Z, FromReal(value));
            RaiseReal();
        }
    }

    /// <summary>
    /// Where the selected object sits, in whatever unit is chosen.
    ///
    /// These were bound straight to the object, which knows only millimetres. They come through
    /// here now for the same reason the sizes do: the unit is the view's business, not the
    /// model's, and the model never learns about it.
    /// </summary>
    public float ObjectPositionX
    {
        get => unit.From(Selected?.PositionX ?? 0f);
        set { if (Selected is { } o) { o.PositionX = unit.To(value); RaiseTransformFields(); } }
    }

    public float ObjectPositionY
    {
        get => unit.From(Selected?.PositionY ?? 0f);
        set { if (Selected is { } o) { o.PositionY = unit.To(value); RaiseTransformFields(); } }
    }

    public float ObjectPositionZ
    {
        get => unit.From(Selected?.PositionZ ?? 0f);
        set { if (Selected is { } o) { o.PositionZ = unit.To(value); RaiseTransformFields(); } }
    }

    /// <summary>
    /// The selected object's own width, depth and height, honouring the proportions lock.
    ///
    /// The boxes used to bind straight to the object, whose SizeX only ever sets X - so with the
    /// lock on, typing a width changed the width and nothing else, and the shape came out
    /// stretched. Dragging a handle went through the view model and did obey the lock, which is
    /// what made the fault so easy to miss: the same toggle worked one way of asking and not the
    /// other.
    ///
    /// The group path already did this properly, but it cannot simply be reused. For one object
    /// the boxes are its *own* extents, so they do not change when it is turned; the group boxes
    /// are the bounding box of the selection in world space, which does.
    /// </summary>
    public float ObjectSizeX
    {
        get => unit.From(Selected?.SizeX ?? 0f);
        set => ResizeSelected(Axis.X, unit.To(value));
    }

    public float ObjectSizeY
    {
        get => unit.From(Selected?.SizeY ?? 0f);
        set => ResizeSelected(Axis.Y, unit.To(value));
    }

    public float ObjectSizeZ
    {
        get => unit.From(Selected?.SizeZ ?? 0f);
        set => ResizeSelected(Axis.Z, unit.To(value));
    }

    /// <summary>
    /// Sets one of the selected object's own dimensions, taking the other two with it when the
    /// proportions are locked.
    /// </summary>
    private void ResizeSelected(Axis axis, float millimetres)
    {
        if (Selected is not { } o) return;
        if (!float.IsFinite(millimetres) || millimetres < 0.01f) return;

        float now = axis switch { Axis.X => o.SizeX, Axis.Y => o.SizeY, _ => o.SizeZ };

        if (!UniformScale || now < 1e-4f)
        {
            switch (axis)
            {
                case Axis.X: o.SizeX = millimetres; break;
                case Axis.Y: o.SizeY = millimetres; break;
                default: o.SizeZ = millimetres; break;
            }

            RaiseReal();
            return;
        }

        // Each axis read and written on its own, because they are independent: scaling one does
        // not move the others, so the same ratio applied three times is the whole of it.
        float ratio = millimetres / now;

        o.SizeX *= ratio;
        o.SizeY *= ratio;
        o.SizeZ *= ratio;

        RaiseReal();
    }

    public float RealW
    {
        get => ToReal(HasOneSelected ? Selected!.SizeX : GroupSpan(Axis.X));
        set
        {
            if (HasOneSelected) ResizeSelected(Axis.X, FromReal(value));
            else ResizeGroup(Axis.X, FromReal(value));
            RaiseReal();
        }
    }

    public float RealD
    {
        get => ToReal(HasOneSelected ? Selected!.SizeY : GroupSpan(Axis.Y));
        set
        {
            if (HasOneSelected) ResizeSelected(Axis.Y, FromReal(value));
            else ResizeGroup(Axis.Y, FromReal(value));
            RaiseReal();
        }
    }

    public float RealH
    {
        get => ToReal(HasOneSelected ? Selected!.SizeZ : GroupSpan(Axis.Z));
        set
        {
            if (HasOneSelected) ResizeSelected(Axis.Z, FromReal(value));
            else ResizeGroup(Axis.Z, FromReal(value));
            RaiseReal();
        }
    }

    /// <summary>
    /// Every box that shows a length. Changing the unit changes all of them at once, and so does
    /// moving anything, so they are raised together rather than each caller remembering the list.
    /// </summary>
    private void RaiseTransformFields()
    {
        Raise(nameof(ObjectPositionX));
        Raise(nameof(ObjectPositionY));
        Raise(nameof(ObjectPositionZ));

        Raise(nameof(GroupX));
        Raise(nameof(GroupY));
        Raise(nameof(GroupZ));

        Raise(nameof(GroupSizeX));
        Raise(nameof(GroupSizeY));
        Raise(nameof(GroupSizeZ));

        Raise(nameof(SelectionSummary));
        Raise(nameof(RealUnit));
        RaiseReal();
    }

    private void RaiseReal()
    {
        Raise(nameof(ScaleApplies));
        Raise(nameof(RealX));
        Raise(nameof(RealY));
        Raise(nameof(RealZ));
        Raise(nameof(RealW));
        Raise(nameof(RealD));
        Raise(nameof(RealH));
        Raise(nameof(RealSize));

        Raise(nameof(ObjectSizeX));
        Raise(nameof(ObjectSizeY));
        Raise(nameof(ObjectSizeZ));
    }

    /// <summary>
    /// The standard each scale belongs to. Nobody remembers that 1:87 is HO and 1:160 is N -
    /// they remember the name and have to look up the number, which is the wrong way round for
    /// a list you pick from.
    /// </summary>
    private static readonly (float Scale, string Standard)[] KnownScales =
    [
        (1f, "full size"),
        (12f, "dolls' house"),
        (24f, "G"),
        (35f, "military"),
        (48f, "O"),
        (72f, "aircraft"),
        (76f, "OO"),
        (87f, "HO"),
        (100f, "architectural"),
        (144f, "aircraft"),
        (160f, "N"),
        (200f, "architectural"),
        (220f, "Z")
    ];

    /// <summary>Scales a modeller is likely to want, named, and 1:1 for everyone else.</summary>
    public IReadOnlyList<string> ModelScales { get; } =
        KnownScales.Select(k => Named(k.Scale, k.Standard)).ToList();

    private static string Named(float scale, string standard) =>
        $"{scale:0.##} ({standard})";

    /// <summary>The name for a scale, if it has one.</summary>
    private static string? StandardFor(float scale)
    {
        foreach (var known in KnownScales)
            if (Math.Abs(known.Scale - scale) < 0.001f) return known.Standard;

        return null;
    }

    /// <summary>
    /// What the scale box shows and accepts. A plain float binding cannot do this: the list has
    /// to read "87 (HO)" to be worth having, and that string has to come back as 87 - so the
    /// number is parsed off the front and anything in brackets is a label, not input.
    /// </summary>
    public string ModelScaleText
    {
        get
        {
            var standard = StandardFor(modelScale);
            return standard is null ? $"{modelScale:0.##}" : Named(modelScale, standard);
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var text = value.Trim();
            int bracket = text.IndexOf('(');
            if (bracket >= 0) text = text[..bracket].Trim();

            if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.CurrentCulture, out float parsed)
                || float.TryParse(text, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out parsed))
            {
                ModelScale = parsed;
            }

            // Right or wrong, put the box back in step with what the scale actually is.
            Raise(nameof(ModelScaleText));
        }
    }

    public string ScaleLabel
    {
        get
        {
            if (modelScale <= 1.001f) return "1:1";

            var standard = StandardFor(modelScale);
            return standard is null ? $"1:{modelScale:0.##}" : $"1:{modelScale:0.##} ({standard})";
        }
    }

    /// <summary>
    /// The unit on the real-size boxes, with the scale it is at. "m" alone leaves the reader
    /// working out whose metres these are.
    /// </summary>
    public string RealUnit => modelScale <= 1.001f
        ? unit.Real.Label
        : $"{unit.Real.Label} at 1:{modelScale:0.##}";

    /// <summary>
    /// What the selection measures at the scene's scale, shown beside the millimetres. Metres
    /// once it passes one, because a 9570 mm wall is harder to read than 9.57 m.
    ///
    /// Any selection, not just a single object: it used to read <c>Selected</c>, which is null
    /// whenever two or more are picked, so the whole scale control did nothing visible for
    /// anyone working on more than one part - and a control that appears to do nothing is worse
    /// than no control.
    /// </summary>
    public string RealSize
    {
        get
        {
            if (modelScale <= 1.001f) return "";

            var selection = Scene.Selection;
            if (selection.Count == 0) return "";

            var size = (selection.Count == 1
                ? new Vector3(selection[0].SizeX, selection[0].SizeY, selection[0].SizeZ)
                : GroupExtent()) * modelScale;

            if (size == Vector3.Zero) return "";

            return size.X >= 1000f || size.Y >= 1000f || size.Z >= 1000f
                ? $"{size.X / 1000f:0.##} x {size.Y / 1000f:0.##} x {size.Z / 1000f:0.##} m at {ScaleLabel}"
                : $"{size.X:0.#} x {size.Y:0.#} x {size.Z:0.#} mm at {ScaleLabel}";
        }
    }

    public bool HasDamagedObjects => damaged ??= Scene.Objects.Any(o => !o.Mesh.CheckHealth().IsWatertight);

    /// <summary>The picked face, in world space, or null. Read by the renderer.</summary>
    public FacePatch? EngraveFace => engrave.Face;

    /// <summary>What the pattern would cut, drawn on the face while the settings are chosen.</summary>
    public GrooveSet EngravePreview => engrave.Preview();

    public IReadOnlyList<PatternChoice> EngravePatterns { get; } =
    [
        new(PatternKind.Brick, "Brick"),
        new(PatternKind.RoofTiles, "Roof tiles"),
        new(PatternKind.Tiles, "Tiles"),
        new(PatternKind.Planks, "Planks"),
        new(PatternKind.Wood, "Wood grain"),
        new(PatternKind.Stripes, "Stripes")
    ];
    public IReadOnlyList<PatternDirection> EngraveDirections { get; } = Enum.GetValues<PatternDirection>();

    public PatternKind EngravePattern
    {
        get => engrave.Options.Kind;

        // Aspect is cleared with the pattern. Each bond has its own proportions - a tile is not
        // three times as long as it is tall - and carrying the last one across meant picking
        // Roof tiles and getting bricks in a different colour.
        set => SetEngrave(engrave.Options with { Kind = value, Aspect = 0f });
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

    /// <summary>
    /// Where the pattern has been slid to, so the same handles that place lettering can slide it.
    /// A pattern covers the whole face, so there is nothing to turn and nothing to draw a box
    /// round - only the grip is any use here.
    /// </summary>
    public SurfacePlacement EngravePlacement
    {
        get => new(new Vector2(engrave.Options.OffsetU, engrave.Options.OffsetV), 0);
        set => SetEngrave(engrave.Options with
        {
            OffsetU = Rounded(value.OffsetMm.X),
            OffsetV = Rounded(value.OffsetMm.Y)
        });
    }

    /// <summary>The face the pattern is laid on, for the handles to be dragged over.</summary>
    public IPlacementSurface? EngraveSurface() =>
        engrave.Face is { } face ? new PlanarSurface(face) : null;

    /// <summary>Half the face, which is as far as the grip should ever need to go.</summary>
    public Vector2 EngraveExtent => engrave.Face is { } face ? face.Size * 0.5f : Vector2.Zero;

    public string EngraveSummary => engrave.Describe();
    public string EngraveAdvice => engrave.Advice();
    public bool HasEngraveAdvice => EngraveAdvice.Length > 0;

    /// <summary>
    /// How many times longer each course is than it is tall. Brick is about three; a roof tile is
    /// nearer one and a half, and before this there was no way to ask for one.
    /// </summary>
    public float EngraveAspect
    {
        get => engrave.Options.Courses;
        set => SetEngrave(engrave.Options with { Aspect = Math.Clamp(value, 0.2f, 20f) });
    }

    /// <summary>Stripes and grain have no courses, so their proportions mean nothing.</summary>
    public bool EngraveAspectApplies => GroovePattern.IsMasonry(engrave.Options.Kind);

    /// <summary>Boarding and grain can run either way; courses of brick and tile are level.</summary>
    public bool EngraveDirectionApplies => GroovePattern.Turns(engrave.Options.Kind);

    /// <summary>
    /// While this is on, the subtract panel is open and nothing has been cut yet. Its own mode
    /// for the same reason splitting and engraving are: there are settings to get right before
    /// the thing happens, and a boolean cannot be taken back except by undo.
    /// </summary>
    public bool IsSubtractMode
    {
        get => isSubtractMode;
        set
        {
            if (isSubtractMode == value) return;

            Set(ref isSubtractMode, value);
            Raise(nameof(SubtractSummary));
        }
    }

    /// <summary>
    /// How much bigger than the cutter the opening should come out, on every side.
    ///
    /// This is the number that was arithmetic by hand before: a 3 mm pin wants a 3.4 mm bore, so
    /// somebody types 3.4 somewhere and remembers why. Here the pin is subtracted with 0.2 mm of
    /// tolerance and the hole is derived from it - change the pin and the hole follows.
    /// </summary>
    public float SubtractTolerance
    {
        get => subtractTolerance;
        set => Set(ref subtractTolerance, Math.Clamp(value, 0f, 5f));
    }

    /// <summary>
    /// Leave the cutter on the plate afterwards. A pin that bored its own hole is usually a part
    /// in its own right, and modelling it twice is how the two stop matching.
    /// </summary>
    public bool SubtractKeepsCutter
    {
        get => subtractKeepsCutter;
        set => Set(ref subtractKeepsCutter, value);
    }

    /// <summary>What is about to happen to what, in the order the clicks were made.</summary>
    public string SubtractSummary
    {
        get
        {
            var picked = Scene.SelectionInPickOrder;
            if (picked.Count < 2)
                return "Click the part to keep first, then what to take away from it.";

            var cutters = string.Join(", ", picked.Skip(1).Select(o => o.Name));
            return $"Take {cutters} away from {picked[0].Name}.";
        }
    }

    /// <summary>
    /// Stand the pattern off the face rather than cutting it in.
    ///
    /// Worth having for more than looks. The bricks are separate pieces with mortar between them,
    /// where the joints of a cut pattern are one connected web - and a web is what the boolean
    /// struggles with. On a sweep of sixty-four walls, cutting tore four and raising tore two.
    /// It also prints better: a raised line is laid down by the nozzle, where a groove the same
    /// size is simply missed.
    /// </summary>
    public bool EngraveRaised
    {
        get => engrave.Options.Raised;
        set => SetEngrave(engrave.Options with { Raised = value });
    }

    private void SetEngrave(EngraveOptions options)
    {
        engrave.Options = options;

        Raise(nameof(EngravePattern));
        Raise(nameof(EngraveSize));
        Raise(nameof(EngraveGrooveWidth));
        Raise(nameof(EngraveDepth));
        Raise(nameof(EngraveDirection));
        Raise(nameof(EngraveDirectionApplies));
        Raise(nameof(EngraveAspect));
        Raise(nameof(EngraveAspectApplies));
        Raise(nameof(EngraveRaised));
        Raise(nameof(EngraveOffsetU));
        Raise(nameof(EngraveOffsetV));
        Raise(nameof(EngravePlacement));
        RaiseEngraveText();

        // The preview is drawn from these settings, so it has to be redrawn with them.
        EngraveFaceChanged?.Invoke();
        PlacementChanged?.Invoke();
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

            // Turning about the axis the plane lies on moves nothing, so which of the three
            // boxes is the dead one changes with the axis.
            Raise(nameof(SplitRollApplies));
            Raise(nameof(SplitPitchApplies));
            Raise(nameof(SplitYawApplies));
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
            RaiseSplitAngles();
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

    /// <summary>
    /// Sets the cutting plane to the face under the click.
    ///
    /// The split engine has always taken an arbitrary plane; it was only the panel that was
    /// bound to the three axes, so any sloping cut - a roof, a chamfer, a hip - meant rotating a
    /// cutter box and working out where its face landed. Picking the face says it directly, the
    /// same way Lay on face already does.
    /// </summary>
    public bool PickSplitPlane(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isSplitMode) return false;

        var face = FacePatch.Find(target.ToWorldMesh(), worldPoint, worldNormal);
        if (face is null) return false;

        SplitNormal = face.Normal;

        // The offset the split works in is measured along the normal from the plate's origin,
        // which is exactly where the face's own plane sits.
        SplitOffset = Vector3.Dot(face.Normal, face.Origin);

        var (front, back) = DominantAxisLabels();
        Status = $"Cutting on the face you picked - keeps {front} or {back}";

        Raise(nameof(KeepFrontLabel));
        Raise(nameof(KeepBackLabel));
        ViewChanged?.Invoke();
        return true;
    }

    public float SplitMinimum { get; private set; } = -100;
    public float SplitMaximum { get; private set; } = 100;

    public SplitKeep SplitKeep
    {
        get => splitKeep;
        set
        {
            Set(ref splitKeep, value);
            Raise(nameof(SplitOffcutApplies));
            Raise(nameof(SplitCutFaceApplies));
        }
    }

    /// <summary>
    /// What becomes of the half the split would throw away while the plane is being placed.
    ///
    /// Faded to begin with. The plane on its own says where the cut falls but not which side of
    /// it survives, and on anything but a simple shape that is genuinely hard to picture.
    /// </summary>
    public Render.SplitOffcut SplitOffcut
    {
        get => splitOffcut;
        set
        {
            Set(ref splitOffcut, value);
            Raise(nameof(SplitCutFaceApplies));
        }
    }

    /// <summary>Keeping both halves throws nothing away, so there is nothing to fade or hide.</summary>
    public bool SplitOffcutApplies => splitKeep is not SplitKeep.Both;

    /// <summary>And nothing is cut open to look into unless a half is being taken off.</summary>
    public bool SplitCutFaceApplies =>
        splitKeep is not SplitKeep.Both && splitOffcut is not Render.SplitOffcut.Shown;

    /// <summary>
    /// Whether the face the cut exposes is closed over.
    ///
    /// On by default: an open cut shows the inside of the shell, which reads as a hollow model
    /// rather than as a solid one that has been cut. Off is worth having anyway - looking into
    /// the piece is the quickest way to see how thick a wall is, or whether there is anything
    /// in there at all - and it costs a couple of milliseconds on a scan either way.
    /// </summary>
    public bool SplitFillsCut
    {
        get => splitFillsCut;
        set => Set(ref splitFillsCut, value);
    }

    /// <summary>
    /// Puts down whichever tool has the object, and says whether there was one.
    ///
    /// Escape is the key everybody reaches for, and every tool has its own Cancel button in its
    /// own panel - so it goes through one place here rather than being wired up six times and
    /// forgotten on the seventh.
    /// </summary>
    public bool CancelActiveTool()
    {
        if (IsSplitMode) IsSplitMode = false;
        else if (IsMeasureMode) IsMeasureMode = false;
        else if (IsEngraveMode) IsEngraveMode = false;
        else if (IsEmbossMode) IsEmbossMode = false;
        else if (IsLayMode) IsLayMode = false;
        else if (IsSubtractMode) IsSubtractMode = false;
        else return false;

        Status = "Cancelled - nothing was changed";
        return true;
    }

    /// <summary>Whether the split handles slide the plane or turn it.</summary>
    public GizmoMode SplitGizmoMode
    {
        get => splitGizmoMode;
        set
        {
            if (splitGizmoMode == value) return;
            splitGizmoMode = value;

            Raise(nameof(SplitGizmoMode));
            Raise(nameof(SplitMoveMode));
            Raise(nameof(SplitTurnMode));
        }
    }

    public bool SplitMoveMode
    {
        get => splitGizmoMode is GizmoMode.Move;
        set { if (value) SplitGizmoMode = GizmoMode.Move; }
    }

    public bool SplitTurnMode
    {
        get => splitGizmoMode is GizmoMode.Rotate;
        set { if (value) SplitGizmoMode = GizmoMode.Rotate; }
    }

    /// <summary>
    /// The plane's turn, typed rather than dragged, as the same roll, pitch and yaw an object
    /// has: turns about X, Y and Z.
    ///
    /// The rings snap to fifteen degrees, which is right for a rough cut and no use at all for a
    /// joint that has to be 22.5. Only two of the three ever do anything - a turn about the axis
    /// the plane lies on leaves it exactly where it was - so that one reads nought and is dead.
    ///
    /// The angles are read back off the plane's facing rather than kept beside it, so there is
    /// one answer to which way the plane faces and a dragged ring and a typed number cannot
    /// come to disagree about it.
    /// </summary>
    public double SplitRoll
    {
        get => TurnAbout(Axis.X);
        set => TurnAbout(Axis.X, value);
    }

    public double SplitPitch
    {
        get => TurnAbout(Axis.Y);
        set => TurnAbout(Axis.Y, value);
    }

    public double SplitYaw
    {
        get => TurnAbout(Axis.Z);
        set => TurnAbout(Axis.Z, value);
    }

    public bool SplitRollApplies => splitAxis is not Axis.X;
    public bool SplitPitchApplies => splitAxis is not Axis.Y;
    public bool SplitYawApplies => splitAxis is not Axis.Z;

    private double TurnAbout(Axis about)
    {
        var (first, second) = PlaneTilt.Angles(splitAxis, splitNormal);
        var (firstAxis, secondAxis) = PlaneTilt.AxesFor(splitAxis);
        Vector3 wanted = PlaneSplit.NormalFor(about);

        if (firstAxis == wanted) return first;
        if (secondAxis == wanted) return second;

        return 0;
    }

    private void TurnAbout(Axis about, double degrees)
    {
        var (first, second) = PlaneTilt.Angles(splitAxis, splitNormal);
        var (firstAxis, secondAxis) = PlaneTilt.AxesFor(splitAxis);
        Vector3 wanted = PlaneSplit.NormalFor(about);

        if (firstAxis == wanted) TiltTo(degrees, second);
        else if (secondAxis == wanted) TiltTo(first, degrees);
    }

    private void RaiseSplitAngles()
    {
        Raise(nameof(SplitRoll));
        Raise(nameof(SplitPitch));
        Raise(nameof(SplitYaw));
    }

    /// <summary>
    /// Turns the plane to a pair of angles without letting it wander off what it is cutting.
    ///
    /// The offset is measured along the normal, so a new normal on its own moves the plane
    /// bodily as well as turning it. Restating the offset through the point the plane already
    /// passes through leaves it where it is - the same thing the tilt rings do while dragging.
    /// </summary>
    private void TiltTo(double first, double second)
    {
        Vector3 pivot = SplitPlanePoint();

        SplitNormal = PlaneTilt.Normal(splitAxis, first, second);
        SplitOffset = Vector3.Dot(splitNormal, pivot);

        RaiseSplitAngles();

        Status = $"Split plane turned {SplitRoll:0.##}, {SplitPitch:0.##}, {SplitYaw:0.##} deg";
    }

    /// <summary>The point of the plane nearest what it is cutting, which is what it turns about.</summary>
    private Vector3 SplitPlanePoint()
    {
        var bounds = Bounds.Empty;
        foreach (var o in Scene.Selection) bounds = bounds.Union(o.WorldBounds);

        Vector3 centre = bounds.IsEmpty ? Vector3.Zero : bounds.Center;
        return centre - splitNormal * (Vector3.Dot(splitNormal, centre) - splitOffset);
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
        var name = Namer();
        var copies = Scene.Selection.Select(o =>
        {
            var copy = o.Clone();
            copy.Name = name(o.Name);
            if (offset) copy.Position += new Vector3(o.WorldBounds.Size.X + 5f, 0, 0);
            return copy;
        }).ToList();

        if (copies.Count == 0) return;

        Undo.Execute(new AddObjectsCommand(offset ? "Duplicate" : "Duplicate in place", copies));
        RefreshSelection();
        Status = offset ? "Duplicated" : "Duplicated in place";
    }

    /// <summary>
    /// Repeats the selection along a line, growing as it goes if asked.
    ///
    /// One operation and one undo, where a staircase used to be a dozen objects typed in by
    /// hand. The arithmetic lives in <see cref="RepeatArray"/> so it can be tested on its own.
    /// </summary>
    private void RepeatSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var dialog = new RepeatDialog(selection) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true) return;

        if (dialog.RingResult is { } ring)
        {
            RepeatRound(selection, ring);
            return;
        }

        if (dialog.Result is not { } settings) return;

        var copies = RepeatArray.Make(selection, settings, Namer());
        if (copies.Count == 0) return;

        Undo.Execute(new AddObjectsCommand("Repeat", copies));
        RefreshSelection();
        Status = $"Repeated - {copies.Count} new object(s)";
    }

    /// <summary>
    /// Makes a mould of the selection: a block with the model taken out of it, cut so the cast
    /// part can be got out, with a hole to pour through and vents where air would otherwise sit.
    ///
    /// Two spells of work with a dialog between them. The study has to run first, because what
    /// the dialog is chiefly for is showing what it found - which way the part comes out, and
    /// whether it comes out at all.
    /// </summary>
    private async Task MakeMould()
    {
        if (IsBusy) return;

        var selection = Scene.Selection.ToList();
        if (selection.Count != 1) return;

        var source = selection[0];
        var model = source.ToWorldMesh();

        MouldStudy study;
        var token = StartWork($"Studying {source.Name}");
        try
        {
            study = await Task.Run(() => MouldAnalysis.Study(model, 64, token));
        }
        catch (Exception ex) when (WasAborted(ex))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
            return;
        }
        catch (Exception ex)
        {
            Status = $"Could not study {source.Name}: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            EndWork();
        }

        if (study.Pulls.Count == 0)
        {
            Status = "Nothing to make a mould of";
            return;
        }

        var dialog = new MouldDialog(source.Name, study, model)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        token = StartWork($"Moulding {source.Name}");
        try
        {
            var result = await Task.Run(
                () => MouldBuilder.Build(model, dialog.Chosen, dialog.Options, token, Progress));

            if (result.Parts.Count == 0)
            {
                Status = "The mould came out empty";
                return;
            }

            var name = Namer();
            var made = result.Parts.Select(p => new SceneObject(name($"{source.Name} {p.Name}"), p.Mesh)
            {
                Colour = NextAutomaticColour()
            }.Centred()).ToList();

            Undo.Execute(new AddObjectsCommand("Mould", made));
            RefreshSelection();

            Status = result.Summary;

            if (result.Parts.Any(p => !p.Watertight))
                MessageBox.Show(
                    result.Summary + "\n\nA piece that is not watertight will not slice. Select it "
                    + "and use Rebuild on the Tools tab, which remakes a shape the boolean has "
                    + "given up on.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"The mould failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Repeats the selection round a circle: a ring of pins, a row of teeth, a spiral stair.
    ///
    /// The radius may differ from where the selection already sits, in which case the originals
    /// move onto the circle as well - a ring with one object left behind at the old radius is not
    /// something anyone asked for. That move and the copies go on the undo stack as one step.
    /// </summary>
    private void RepeatRound(IReadOnlyList<SceneObject> selection, RingSettings ring)
    {
        var before = selection.Select(TransformState.Capture).ToList();

        var made = RepeatArray.MakeRing(selection, ring, Namer());
        if (made.Copies.Count == 0) return;

        List<IUndoableCommand> steps = [];

        // Only when the ring is somewhere other than where the selection stands; asking for the
        // radius it is already at should not put a no-op move on the undo stack.
        if (before.Where((state, i) => !state.Equals(made.Seats[i])).Any())
            steps.Add(new TransformCommand("Repeat", selection, before, made.Seats));

        steps.Add(new AddObjectsCommand("Repeat", made.Copies));

        Undo.Execute(steps.Count == 1 ? steps[0] : new CompoundCommand("Repeat round a circle", steps));
        RefreshSelection();

        string where = ring.Radius < 1e-3f ? "in place" : $"at {ring.Radius:0.##} mm";
        Status = $"Repeated round a circle {where} - {made.Copies.Count} new object(s)";
    }

    /// <summary>
    /// Moves the first-picked object so its box lines up with the second's, on whichever axes
    /// they are not already level.
    ///
    /// The dowels in the house model line up because both were worked out from the same four
    /// numbers by hand. Nothing in the app could have checked it, and when they came out wrong
    /// nothing said so.
    /// </summary>
    private void AlignToSelection()
    {
        var picked = Scene.SelectionInPickOrder;
        if (picked.Count != 2) return;

        var mover = picked[0];
        var target = picked[1];
        var before = TransformState.Capture(mover);

        var shift = target.WorldBounds.Center - mover.WorldBounds.Center;
        mover.Position += shift;

        Undo.Execute(new TransformCommand("Align", [mover], [before], [TransformState.Capture(mover)]));
        RefreshSelection();
        Status = $"Aligned {mover.Name} to {target.Name}";
    }

    /// <summary>
    /// Says whether two parts actually meet, and by how much.
    ///
    /// A dowel with no hole above it, a frame too big for its opening, a wall bored through by a
    /// socket that was meant to be 4 mm deep - all of them look right on screen and none of them
    /// print. This is the cheap version of the question: where do the two overlap, and is that
    /// overlap a fit, a clash, or nothing at all.
    /// </summary>
    private void FitCheck()
    {
        var picked = Scene.SelectionInPickOrder;
        if (picked.Count != 2) return;

        var a = picked[0].WorldBounds;
        var b = picked[1].WorldBounds;

        var low = Vector3.Max(a.Min, b.Min);
        var high = Vector3.Min(a.Max, b.Max);
        var overlap = high - low;

        if (overlap.X <= 0 || overlap.Y <= 0 || overlap.Z <= 0)
        {
            var gap = Vector3.Max(Vector3.Max(b.Min - a.Max, a.Min - b.Max), Vector3.Zero);

            Status = $"{picked[0].Name} and {picked[1].Name} do not meet - "
                   + $"{gap.Length():0.##} mm apart at the nearest.";
            return;
        }

        double volume = CsgSolid.Intersect(picked[0].ToWorldMesh(), picked[1].ToWorldMesh())
            .ComputeSignedVolume();

        Status = Math.Abs(volume) < 1e-6
            ? $"{picked[0].Name} and {picked[1].Name} touch but do not overlap - a clearance fit."
            : $"{picked[0].Name} and {picked[1].Name} overlap by {overlap.X:0.##} x {overlap.Y:0.##} "
              + $"x {overlap.Z:0.##} mm, {Math.Abs(volume) / 1000.0:0.###} cm3 of shared material.";
    }

    /// <summary>
    /// Inserts a straight flight of steps.
    ///
    /// Its own tool rather than another primitive because the numbers are the whole of the job:
    /// rise, run and how many risers that divides into, with the dialog saying in real
    /// millimetres whether the result is a stair or a ladder. Built as one closed solid, so it
    /// carries none of the coplanar seams a stack of boxes does.
    /// </summary>
    private void InsertStair()
    {
        var dialog = new StairDialog(modelScale) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } s) return;

        var mesh = StairBuilder.Build(s.Rise, s.Run, s.Width, s.Steps);
        if (mesh.TriangleCount == 0) return;

        var o = new SceneObject(Scene.UniqueName("Stair"), mesh)
        {
            Colour = NextAutomaticColour(),
            Position = new Vector3(0, 0, mesh.ComputeBounds().Size.Z / 2f)
        };

        Undo.Execute(new AddObjectsCommand("Insert stair", [o]));
        RefreshSelection();

        var check = StairBuilder.Measure(s.Rise, s.Run, s.Steps, modelScale);
        Status = $"Inserted a flight of {s.Steps} - {check.RiserMm:0.#} mm risers on "
               + $"{check.GoingMm:0.#} mm treads{(check.IsClimbable ? "" : ", which is steep")}";
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

        // Rounding regenerates the shape, which also resets its scale, so the preview has to
        // stand in for both - the mesh and the transform it will be built with.
        var before = roundable.Select(o => (o.Mesh, o.Scale)).ToList();

        var dialog = new RoundDialog(roundable, meshes =>
        {
            for (int i = 0; i < roundable.Count; i++)
            {
                roundable[i].Mesh = meshes is null ? before[i].Mesh : meshes[i];
                roundable[i].Scale = meshes is null ? before[i].Scale : Vector3.One;
            }
        })
        { Owner = Application.Current?.MainWindow };

        bool accepted = dialog.ShowDialog() == true && dialog.Result is not null;

        // Whatever the preview left behind, the undo step has to start from where the user did.
        for (int i = 0; i < roundable.Count; i++)
        {
            roundable[i].Mesh = before[i].Mesh;
            roundable[i].Scale = before[i].Scale;
        }

        if (!accepted || dialog.Result is not { } radius) return;

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
        var name = Namer();
        var pasted = clipboard.Select(source =>
        {
            var copy = source.Clone();
            copy.Name = name(source.Name);
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

        // Left unwelded on purpose. Welding would fuse parts that touch into one connected run
        // of triangles, and Ungroup finds its pieces geometrically - so a group of touching
        // parts would never come apart again.
        var combined = Mesh.Combine(selection.Select(o => o.ToWorldMesh()));
        var grouped = new SceneObject(Scene.UniqueName("Group"), combined)
        {
            Colour = selection[0].Colour
        }.Centred();

        Undo.Execute(new ReplaceObjectsCommand("Group", selection, [grouped]));
        RefreshSelection();

        // Said here rather than left to surface as a torn boolean later: everything downstream
        // of a group that is not a solid inherits the damage, and by then the cause is three
        // operations back.
        // Judged on a welded copy, which is the only way to see it. Concatenating two shells that
        // meet on a face leaves that face inside the group with material both sides of it; left
        // unwelded the two copies of it never meet, every edge still counts twice, and a group
        // that is not a solid at all reports itself ready to print. Everything built on it
        // afterwards inherits the damage, and by then the cause is three operations back.
        Status = combined.Welded().CheckHealth().IsWatertight
            ? $"Grouped {selection.Count} objects"
            : $"Grouped {selection.Count} objects - they touch each other, so the group is not a "
              + "solid. Use Merge rather than Group if you meant to fuse them.";
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
                }.Centred());
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

        // In the order they were picked: for a subtraction the first is the one kept, and
        // which that is can only come from the order the clicks were made in.
        var selection = Scene.SelectionInPickOrder;
        if (selection.Count < 2) return;

        if (IsBusy) return;

        var token = StartWork(op.ToString());
        try
        {
            // Everything is baked to world space first: CSG has no concept of per-object
            // transforms. On a subtraction the cutters - everything after the first - are grown
            // by the clearance on the way, so the hole is bigger than the thing that cut it.
            float clearance = op == BooleanOp.Subtract ? subtractTolerance : 0f;

            if (clearance > 0f)
            {
                var awkward = selection.Skip(1).FirstOrDefault(o => !o.CanTakeClearance);
                if (awkward is not null)
                {
                    Status = $"{awkward.Name} cannot take a clearance";
                    MessageBox.Show(
                        $"\"{awkward.Name}\" cannot be grown by a clearance.\n\n"
                        + "A clearance is only exact on a cube, a cylinder or a sphere. On a "
                        + "sloped face - a cone, a pyramid, a wedge - growing each dimension "
                        + "leaves less clearance than you asked for, and a gap smaller than the "
                        + "number typed is the one direction that jams a printed part. It is "
                        + "refused rather than quietly under-delivered.\n\n"
                        + "Subtract with the clearance at 0 and size the cutter yourself, or "
                        + "make the cutter from a cube or a cylinder.",
                        "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            var meshes = selection
                .Select((o, i) => i == 0 ? o.ToWorldMesh() : o.ToWorldMeshGrown(clearance))
                .ToList();

            var result = await Task.Run(() =>
            {
                var accumulator = meshes[0];
                for (int i = 1; i < meshes.Count; i++)
                    accumulator = CsgSolid.Apply(accumulator, meshes[i], op, token: token);

                // Mended before it is handed over. A boolean splits one polygon without always
                // splitting the one beside it, which leaves the two sides of an edge disagreeing
                // about where their corners are - closed to look at, torn as a list of triangles,
                // and reported to the user as a broken model they could do nothing about.
                return MeshHealer.Heal(accumulator, token: token).Mesh;
            });

            if (result.TriangleCount == 0)
            {
                Status = $"{op} removed everything - nothing left to keep";
                MessageBox.Show(
                    op == BooleanOp.Subtract
                        ? $"Subtracting left nothing behind: all of \"{selection[0].Name}\" was "
                          + "inside what was taken away.\n\nThe first object you click is the one "
                          + "kept, so click the part you want to keep first."
                        : "That operation left no geometry behind.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var combined = new SceneObject(Scene.UniqueName(op.ToString()), result)
            {
                Colour = selection[0].Colour
            }.Centred();

            // Kept cutters are removed and put back, so they end up after the result in the
            // list. Their geometry is untouched: what was grown was a copy made for the cut.
            var keptCutters = op == BooleanOp.Subtract && subtractKeepsCutter
                ? selection.Skip(1).ToList()
                : [];

            List<SceneObject> added = [combined, .. keptCutters];
            Undo.Execute(new ReplaceObjectsCommand(op.ToString(), selection, added));
            RefreshSelection();

            // One line, not three. Two of these used to be set in a row and the last one won,
            // so the tolerance was applied and never mentioned.
            var health = result.CheckHealth();
            string from = op == BooleanOp.Subtract ? $" from {selection[0].Name}" : "";
            string tolerance = clearance > 0f ? $", {clearance:0.##} mm tolerance" : "";
            string keeping = keptCutters.Count > 0 ? ", cutter kept" : "";

            Status = $"{op}{from}{tolerance}{keeping}: "
                   + $"{health.TriangleCount:N0} triangles, {health.Describe()}";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"{op} failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Turns the selection into shells of a chosen wall thickness, to save material.
    /// </summary>
    private async Task HollowSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var dialog = new HollowDialog(selection) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } settings) return;

        if (IsBusy) return;

        var token = StartWork("Hollowing");
        try
        {
            // Baked to world space, as the other grid-based tools are: the grid is in world
            // millimetres, so a wall on a stretched object would otherwise come out stretched.
            var meshes = selection.Select(o => o.ToWorldMesh()).ToList();
            var shells = await Task.Run(() => meshes
                .Select(m => MeshHollow.Hollow(m, settings.WallMm, settings.Resolution, settings.Open, token, Progress))
                .ToList());

            var produced = new List<SceneObject>();
            int untouched = 0;

            for (int i = 0; i < selection.Count; i++)
            {
                if (shells[i].VolumeSavedCm3 <= 0)
                {
                    untouched++;
                    continue;
                }

                produced.Add(new SceneObject(selection[i].Name, shells[i].Mesh)
                {
                    Colour = selection[i].Colour
                }.Centred());
            }

            if (produced.Count == 0)
            {
                Status = $"Nothing to hollow - a {settings.WallMm:0.##} mm wall leaves no room in "
                         + (selection.Count == 1 ? "this part" : "any of these parts");
                return;
            }

            var consumed = selection.Where((_, i) => shells[i].VolumeSavedCm3 > 0).ToList();
            Undo.Execute(new ReplaceObjectsCommand("Hollow", consumed, produced));
            RefreshSelection();

            double saved = shells.Sum(s => s.VolumeSavedCm3);
            Status = untouched == 0
                ? $"Hollowed {produced.Count} object(s) to a {settings.WallMm:0.##} mm wall - {saved:0.#} cm3 saved"
                : $"Hollowed {produced.Count}; {untouched} had no room for a {settings.WallMm:0.##} mm wall";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Hollow failed: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Cuts the triangle count of the selection down while keeping its shape.
    ///
    /// The natural partner to Rebuild, which produces a great many triangles by design: nearly
    /// all of them describe flat faces that a handful would describe just as well.
    /// </summary>
    private async Task SimplifySelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var dialog = new SimplifyDialog(selection) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } keep) return;

        if (IsBusy) return;

        var token = StartWork("Simplifying");
        try
        {
            var meshes = selection.Select(o => o.Mesh).ToList();
            var reduced = await Task.Run(
                () => meshes.Select(m => MeshSimplify.ByFraction(m, keep, token, Progress)).ToList());

            var produced = new List<SceneObject>();
            for (int i = 0; i < selection.Count; i++)
            {
                produced.Add(new SceneObject(selection[i].Name, reduced[i])
                {
                    Position = selection[i].Position,
                    Rotation = selection[i].Rotation,
                    Scale = selection[i].Scale,
                    Colour = selection[i].Colour
                }.Centred());
            }

            Undo.Execute(new ReplaceObjectsCommand("Simplify", selection, produced));
            RefreshSelection();

            int before = meshes.Sum(m => m.TriangleCount);
            int after = reduced.Sum(m => m.TriangleCount);
            Status = $"Simplified {before:N0} triangles to {after:N0}";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Simplify failed: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Rebuilds the surface of the selection - or of everything, if nothing is selected.
    ///
    /// The heavy option, and deliberately behind a dialog: it always works, and it always costs
    /// detail, so it should never happen without someone choosing how much.
    /// </summary>
    private async Task RebuildObjects()
    {
        var targets = Scene.Selection.Count > 0 ? Scene.Selection.ToList() : Scene.Objects.ToList();
        if (targets.Count == 0) return;

        var dialog = new RebuildDialog(targets) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } resolution) return;

        if (IsBusy) return;

        var token = StartWork($"Rebuilding {targets.Count} object(s)");
        try
        {
            // Baked to world space first, as booleans are: the grid is in world millimetres, so
            // a stretched object would otherwise be voxelised at the wrong scale.
            var meshes = targets.Select(o => o.ToWorldMesh()).ToList();
            var rebuilt = await Task.Run(
                () => meshes.Select(m => VoxelRebuild.Rebuild(m, resolution, token, Progress)).ToList());

            var produced = new List<SceneObject>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (rebuilt[i].Mesh.TriangleCount == 0) continue;

                produced.Add(new SceneObject(targets[i].Name, rebuilt[i].Mesh)
                {
                    Colour = targets[i].Colour
                }.Centred());
            }

            if (produced.Count == 0)
            {
                Status = "The rebuild produced nothing - try a finer setting";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Rebuild", targets, produced));
            RefreshSelection();

            int mended = produced.Count(o => o.Mesh.CheckHealth().IsWatertight);
            Status = $"Rebuilt {produced.Count} object(s) at {rebuilt[0].VoxelSizeMm:0.###} mm - "
                     + $"{mended} watertight, {produced.Sum(o => o.Mesh.TriangleCount):N0} triangles";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Rebuild failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Rounds the facets off the selection.
    ///
    /// Behind a dialog rather than a single button, because smoothing has two settings that are
    /// easy to confuse and impossible to judge without seeing: how hard to pull the surface
    /// toward its own average, and how finely to divide the shape up first. The result is shown
    /// on the plate as the sliders move and put back if the dialog is cancelled.
    /// </summary>
    private void SmoothSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var before = selection.Select(o => o.Mesh).ToList();

        var dialog = new SmoothDialog(selection, meshes =>
        {
            for (int i = 0; i < selection.Count && i < meshes.Count; i++)
                selection[i].Mesh = meshes[i];
        })
        { Owner = Application.Current?.MainWindow };

        bool accepted = dialog.ShowDialog() == true && dialog.Result is { } settings && settings.Passes > 0;

        // Whatever the preview left on the plate, undo has to start from where the user did.
        var after = selection.Select(o => o.Mesh).ToList();
        for (int i = 0; i < selection.Count; i++) selection[i].Mesh = before[i];

        if (!accepted)
        {
            Status = "Smoothing cancelled";
            return;
        }

        var produced = new List<SceneObject>();
        for (int i = 0; i < selection.Count; i++)
        {
            produced.Add(new SceneObject(selection[i].Name, after[i])
            {
                Position = selection[i].Position,
                Rotation = selection[i].Rotation,
                Scale = selection[i].Scale,
                Colour = selection[i].Colour
                // Origin is dropped: the shape is no longer the primitive it was, so it can no
                // longer be rebuilt with rounded edges.
            }.Centred());
        }

        Undo.Execute(new ReplaceObjectsCommand("Smooth", selection, produced));
        RefreshSelection();

        Status = $"Smoothed {produced.Count} object(s) - {produced.Sum(o => o.Mesh.TriangleCount):N0} triangles";
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

        if (IsBusy) return;

        var token = StartWork($"Repairing {targets.Count} object(s)");
        try
        {
            var healed = await Task.Run(() => targets.Select(o => MeshHealer.Heal(o.Mesh, token: token)).ToList());

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
                }.Centred());

                if (healed[i].After.IsWatertight) fixedUp++;
            }

            if (replaced.Count == 0)
            {
                // Saying what is actually wrong matters more here than anywhere else: this is
                // the one path where nothing happens, and "cannot mend" on its own leaves
                // someone with no idea whether to try another tool or another model.
                var worst = targets
                    .Select(o => o.Mesh.CheckHealth())
                    .OrderByDescending(h => h.BoundaryEdges + h.NonManifoldEdges + h.InconsistentEdges)
                    .First();

                Status = targets.Count == 1
                    ? $"Cannot mend this one - {worst.Describe()}. It is unchanged."
                    : $"Cannot mend {targets.Count} object(s) - the worst has {worst.Describe()}. They are unchanged.";

                MessageBox.Show(
                    $"This model has {worst.Describe().ToLowerInvariant()}, and repairing it would "
                    + "leave it worse than it is, so nothing has been changed." + Environment.NewLine + Environment.NewLine
                    + "Filling holes and turning faces round only works when the damage is local. "
                    + "A mesh whose surface passes through itself has no well-defined inside, and "
                    + "patching it piece by piece tears more than it closes." + Environment.NewLine + Environment.NewLine
                    + "Rebuild, beside this button, mends it a different way: it works out what "
                    + "is inside the model and what is outside and builds a fresh surface between "
                    + "them, which always succeeds. The cost is that detail finer than its "
                    + "setting is lost, so it asks first.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Repair", replaced, produced));
            RefreshSelection();

            Status = beyond == 0
                ? $"Repaired {fixedUp} of {replaced.Count} object(s)"
                : $"Repaired {fixedUp} object(s); {beyond} beyond mending and left alone";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Repair failed: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    private void BeginEngrave()
    {
        if (Scene.Selection.Count != 1) return;

        // Every one of these claims the click on the object, so only one can be on.
        IsSplitMode = false;
        IsSubtractMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
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

        // A fresh face means the old shift no longer refers to anything, so it starts level.
        if (!SameFace(engrave.Face, face)) engrave.Options = engrave.Options with { OffsetU = 0, OffsetV = 0 };

        engrave.Pick(world, face);

        Raise(nameof(HasEngraveFace));
        Raise(nameof(EngraveOffsetU));
        Raise(nameof(EngraveOffsetV));
        Raise(nameof(EngravePlacement));
        RaiseEngraveText();
        EngraveFaceChanged?.Invoke();
        PlacementChanged?.Invoke();

        Status = EngraveSummary;
        return true;
    }

    private async Task ApplyEngrave()
    {
        if (Scene.Selection.Count != 1) return;
        if (engrave.Face is not { } face || engrave.WorldMesh is not { } world) return;

        var source = Scene.Selection[0];
        var options = engrave.Options;

        if (IsBusy) return;

        var token = StartWork($"Engraving {options.Kind}");
        try
        {
            var result = await Task.Run(() => Engraver.Engrave(world, face, options, token));

            if (result.Grooves == 0 || result.Mesh.TriangleCount == 0)
            {
                Status = "That pattern did not reach the face - try a smaller pattern size";
                return;
            }

            // Nothing is applied unless it would print. Leaving the object alone and saying so is
            // far better than handing back a model that looks right and slices wrong.
            if (!result.IsPrintable)
            {
                Status = $"Engraving that face came out unprintable - {result.Health.Describe()}. Nothing was changed.";
                MessageBox.Show(
                    "The pattern could not be cut into that face cleanly, so the object has been "
                    + "left as it was." + Environment.NewLine + Environment.NewLine
                    + $"The result would have had {result.Health.Describe().ToLowerInvariant()}."
                    + Environment.NewLine + Environment.NewLine
                    + "The cut tears where the pattern lands on something already in the model. It "
                    + "shows up most on a face that has been cut about already - every groove that "
                    + "reached an edge left a notch there for the next pattern to land on - and on "
                    + "a face that is one facet of something round, where a flat pattern was never "
                    + "going to sit properly anyway." + Environment.NewLine + Environment.NewLine
                    + "A coarser pattern, a shallower depth, or nudging Shift across by a fraction "
                    + "will usually get through. Rebuild, on the Tools tab, remakes the surface from "
                    + "scratch and always does.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // The mesh is already in world space, so the new object carries no transform of its
            // own. Same as a boolean, and for the same reason.
            var engraved = new SceneObject(Scene.UniqueName($"{source.Name} {options.Kind}"), result.Mesh)
            {
                Colour = source.Colour
            }.Centred();

            Undo.Execute(new ReplaceObjectsCommand($"Engrave {options.Kind}", [source], [engraved]));
            IsEngraveMode = false;
            RefreshSelection();

            var health = result.Mesh.CheckHealth();
            Status = $"Engraved {result.Grooves:N0} grooves - {health.TriangleCount:N0} triangles, {health.Describe()}";

            // A cut that will not go through is retried from a slightly different pattern. Small,
            // but the object would otherwise not be the one the panel was describing.
            var asked = options.Sane();
            var used = result.Used;

            if (MathF.Abs(used.OffsetU - asked.OffsetU) > 1e-4f
                || MathF.Abs(used.OffsetV - asked.OffsetV) > 1e-4f
                || MathF.Abs(used.Size - asked.Size) > 1e-4f
                || MathF.Abs(used.GrooveWidth - asked.GrooveWidth) > 1e-4f)
            {
                Status += " - the pattern was moved a little to get the cut through";
            }
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Engrave failed: {ex.Message}";
            MessageBox.Show(ex.Message, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndWork();
        }
    }

    private void BeginSplit()
    {
        if (Scene.Selection.Count == 0) return;

        // Every one of these claims the click on the object, so only one can be on.
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
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

        if (IsBusy) return;

        var token = StartWork(selection.Count == 1 ? "Splitting" : $"Splitting {selection.Count} objects");
        try
        {
            var halves = await Task.Run(
                () => meshes.Select(mesh => PlaneSplit.Split(mesh, normal, offset, keep, token)).ToList());

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
                    produced.Add(new SceneObject(Scene.UniqueName($"{source.Name} {KeepFrontLabel}"), front) { Colour = source.Colour }.Centred());
                if (back is not null)
                    produced.Add(new SceneObject(Scene.UniqueName($"{source.Name} {KeepBackLabel}"), back) { Colour = source.Colour }.Centred());
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
        IsSubtractMode = false;
            RefreshSelection();

            Status = missed == 0
                ? $"Split {consumed.Count} object(s) into {produced.Count} piece(s)"
                : $"Split {consumed.Count} object(s) into {produced.Count} piece(s); the plane missed {missed}";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Split failed: {ex.Message}";
        }
        finally
        {
            EndWork();
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

            // There is a way back again, so the notice has done its job.
            HistoryTrimmed = false;

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
            Filter = "3D models (*.stl;*.obj;*.3dfc)|*.stl;*.obj;*.3dfc"
                   + "|STL (*.stl)|*.stl|Wavefront OBJ (*.obj)|*.obj"
                   + $"|3DFastCraft project (*{SceneSerializer.Extension})|*{SceneSerializer.Extension}",
            Title = "Import model"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var imported = new List<SceneObject>();
            string extension = Path.GetExtension(dialog.FileName);
            var naming = Namer();

            // A project imported rather than opened joins what is already on the plate: its
            // objects keep their own names, colours and positions, and nothing here is
            // replaced. Opening it would have thrown the current work away.
            if (extension.Equals(SceneSerializer.Extension, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var loaded in SceneSerializer.Load(dialog.FileName))
                    imported.Add(new SceneObject(naming(loaded.Name), loaded.Mesh)
                    {
                        Colour = loaded.Colour
                    });
            }
            else if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (name, mesh) in ObjReader.Read(dialog.FileName))
                    imported.Add(new SceneObject(naming(name), mesh)
                    {
                        Colour = NextAutomaticColour()
                    }.Centred());
            }
            else
            {
                var mesh = StlReader.Read(dialog.FileName);
                imported.Add(new SceneObject(
                    naming(Path.GetFileNameWithoutExtension(dialog.FileName)), mesh)
                {
                    Colour = NextAutomaticColour()
                }.Centred());
            }

            if (imported.Count == 0)
            {
                Status = "Nothing to import - the file contained no triangles";
                return;
            }

            string what = imported.Count == 1 ? "object" : "objects";

            Undo.Execute(new AddObjectsCommand("Import", imported));
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();

            int triangles = imported.Sum(o => o.Mesh.TriangleCount);
            Status = triangles > 200_000
                ? $"Imported {imported.Count} {what}, {triangles:N0} triangles - boolean operations on a mesh this dense will be slow"
                : $"Imported {imported.Count} {what}, {triangles:N0} triangles";
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
        Raise(nameof(RealSize));
        Raise(nameof(HasOneSelected));
        Raise(nameof(HasManySelected));
        RaiseGroup();
        Raise(nameof(ShowManipulatorBar));
        Raise(nameof(SelectionSummary));
        Raise(nameof(SubtractSummary));
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
