using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Geometry.Moulding;
using FastCraft3D.Geometry.Sketches;
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

public sealed partial class MainViewModel : INotifyPropertyChanged
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
    private bool isExtrudeMode;
    private bool splitWithConnectors;
    private bool isConnectMode;
    private string connectSummary = "";
    private ConnectorOptions connectors = ConnectorOptions.Default;
    private float extrudeHeight = 1f;
    private Render.SplitOffcut splitOffcut = Render.SplitOffcut.Faded;
    private GizmoMode splitGizmoMode = GizmoMode.Move;
    private Vector3 splitTurn;
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
    private SurfaceProfile? embossProfile;
    private WallLoop? embossWalls;
    private WallRun? embossRun;
    private bool embossWallsTried;
    private string? embossWallsWhy;
    private SceneObject? embossObject;
    private bool isMasonryMode;
    private (TextureOptions Texture, TextProjection Projection, bool Raised, float Depth)? beforeMasonry;

    /// <summary>
    /// What Masonry was last used with, each kind of walling on its own, for as long as the app is
    /// open. Kept apart from Emboss's own texture, which Masonry puts back when it is put down, so
    /// neither tool's numbers turn up in the other.
    /// </summary>
    private readonly Dictionary<TextureKind, (TextureOptions Texture, float Depth)> masonryKept = new()
    {
        // The groove each started with is the one it got: 0.2 was raised to the least, 0.4, whatever
        // the box said. Siding's is nothing - its boards lap, with no groove between them.
        [TextureKind.Brick] = (new TextureOptions(TextureKind.Brick, 5f, 0.4f), 0.3f),
        [TextureKind.Rubble] = (new TextureOptions(TextureKind.Rubble, 7f, 0.4f), 0.8f),
        [TextureKind.CoursedStone] = (new TextureOptions(TextureKind.CoursedStone, 8f, 0.4f), 0.8f),
        [TextureKind.Siding] = (new TextureOptions(TextureKind.Siding, 2.5f, 0f), 0.6f),
        [TextureKind.Logs] = (new TextureOptions(TextureKind.Logs, 3f, 0.4f), 1f)
    };

    /// <summary>
    /// What the Texture tool was last used with this session, or null for its first texture. Kept
    /// apart from lettering, which has no texture, and from Masonry, which keeps its own.
    /// </summary>
    private (TextureOptions Texture, float Depth)? textureKept;

    /// <summary>The kind of walling Masonry was last used with.</summary>
    private TextureKind masonryKind = TextureKind.Brick;
    private string embossText = "TEXT";
    private string svgFile = "";

    // A picture stamped in place of a drawing: its ink, read once, and where to split ink from none.
    private Greyscale? embossInk;
    private string embossInkFile = "";
    private float embossThreshold = 0.5f;
    private bool embossInvert;
    private string embossFont = "Arial";
    private List<string>? installedFonts;
    private float embossHeight = 10f;
    private float embossDepth = 0.8f;
    private bool embossBold = true;
    private bool embossItalic;
    private float embossSpacing;
    private bool embossRaised;
    private bool embossSeparate;
    private int embossColumns = 1;
    private int embossRows = 1;
    private float embossGap = 2f;
    private bool embossFill;
    private TextureOptions embossTexture = TextureOptions.Default with { Kind = TextureKind.None };
    private bool isPivotMode;
    private bool isAlignFaceMode;
    private FacePatch? alignFace;
    private bool alignFacePicked;
    private string? alignFaceTargetLabel;

    /// <summary>The round or flat-sided hole under the face picked for Align to face, when it is one.</summary>
    private RoundSurface? alignSurface;

    /// <summary>The meshes of what is hovered over, as they stand: working one out is the slow part of hovering.</summary>
    private readonly Dictionary<SceneObject, Mesh> alignMeshes = [];
    private AlignMode? alignFaceModeX, alignFaceModeY, alignFaceModeZ;
    private bool isCentreFaceMode;
    private FacePatch? centreFaceA, centreFaceB;
    private bool centreFaceAPicked, centreFaceBPicked;
    private string? centreFaceALabel, centreFaceBLabel;
    private bool centreAxisX = true, centreAxisY = true, centreAxisZ = true;
    private RoundSurface? centreRoundA, centreRoundB, centreHoverRound;
    private bool centreTurnParallel, centreAlongAxis;

    /// <summary>Each object's mesh in the world while Centre face to face is out, since hovering asks on every move.</summary>
    private readonly Dictionary<SceneObject, Mesh> centreMeshes = [];
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
    private bool aroundSelectionCentre = true;
    private float groupRollSinceGrab;
    private float groupPitchSinceGrab;
    private float groupYawSinceGrab;
    private List<SceneObject> groupTurnBaseline = [];
    private bool stopOnContact;
    private bool keepOnBedMove;
    private bool keepOnBedScale = true;
    private float distributeGap = 10f;
    private bool distributeBestFace, distributeDrop = true;
    private bool showWireframe;
    private bool showXray;
    private bool showPlate = true;
    private float plateWidth = Scene.PlateSize;
    private float plateDepth = Scene.PlateSize;
    private float plateHeight = Scene.PrintHeight;
    private bool showAxes = true;
    private bool showZAxis;
    private bool showGridLabels;
    private bool showShadows;
    private bool showReflections;
    private bool showProperties = true;
    private UiLevel uiLevel = UiLevel.Advanced;
    private bool isGridPanelOpen;
    private bool isShortcutsPanelOpen;
    private float modelScale = 1f;
    /// <summary>The Studs tool's face and settings, opening on studs - the patterns it is for.</summary>
    private readonly EngraveState engrave = new() { Options = EngraveOptions.Default with { Kind = PatternKind.Studs } };
    private Vector3 splitNormal = Vector3.UnitZ;
    private readonly List<SceneObject> clipboard = new();
    private GizmoMode gizmoMode = GizmoMode.Move;
    private bool uniformScale;
    private bool scaleInPercent;
    private MeasureUnit unit = MeasureUnit.Default;
    private bool snapRotation = true;
    private double snapStep;
    private readonly RecentFiles recent = new();
    private bool stickySelection = true;

    /// <summary>The last tool-launching command run, and what it was given, for RepeatLastCommand.</summary>
    private System.Windows.Input.ICommand? lastRepeatable;
    private object? lastRepeatableParameter;

    /// <summary>
    /// Wraps a tool-launching command so Ctrl+Space can run it again with the same parameter.
    /// Reopening a dialog starts from the numbers it was last left with, since the dialog
    /// remembers those itself; a mode that takes no parameter just begins again the same way.
    /// Only the commands that open a tool are wrapped - undo, file and selection commands have
    /// nothing sensible to repeat. A bare key was ruled out: this window's shortcuts fire from
    /// Window.InputBindings no matter what has focus, so an unmodified Space or a letter would
    /// have swallowed typing into an object's name or clicking a focused button.
    /// </summary>
    private System.Windows.Input.ICommand Track(System.Windows.Input.ICommand inner) =>
        new RelayCommand(p =>
        {
            lastRepeatable = inner;
            lastRepeatableParameter = p;
            inner.Execute(p);
        }, p => inner.CanExecute(p));

    public MainViewModel()
    {
        Scene = new Scene();
        Undo = new UndoStack(Scene);
        Undo.Changed += () =>
        {
            Raise(nameof(UndoLabel));
            IsDirty = true;
        };
        WireAssemblies();
        Scene.Objects.CollectionChanged += (_, _) => RefreshSelection();
        Scene.Objects.CollectionChanged += (_, _) => RaiseHiddenAndLocked();

        InsertCommand = Track(new RelayCommand(p => Insert(p)));
        InsertCustomCommand = Track(RelayCommand.Simple(InsertCustom));
        InsertTextCommand = Track(RelayCommand.Simple(InsertText));
        InsertHoleCommand = Track(AsyncRelayCommand.Simple(InsertHole));
        InsertLithophaneCommand = Track(AsyncRelayCommand.Simple(InsertLithophane));
        HullCommand = Track(RelayCommand.Simple(HullSelection, () => Scene.Selection.Count > 0));
        BeginSketchCommand = new RelayCommand(p => BeginSketch(Enum.TryParse<SketchTool>(p as string, out var tool) ? tool : SketchTool.Line));
        SketchCloseCommand = RelayCommand.Simple(CloseSketch, () => sketch.Chain.Count >= 3 || sketch.ArcEnd is not null);
        SketchUndoCommand = RelayCommand.Simple(() => SayOfSketch(sketch.Undo()), () => !sketch.IsEmpty);
        SketchClearCommand = RelayCommand.Simple(() => { sketch.Clear(); SayOfSketch("Cleared. Start a new outline."); }, () => !sketch.IsEmpty);
        SketchLoadDrawingCommand = RelayCommand.Simple(PickSketchDrawing);
        SketchExtrudeCommand = RelayCommand.Simple(ExtrudeSketch, () => sketch.Loops.Count > 0);
        SketchRevolveCommand = RelayCommand.Simple(RevolveSketch, () => sketch.Loops.Count > 0);
        DoneSketchCommand = RelayCommand.Simple(() => IsSketchMode = false);
        DeleteCommand = RelayCommand.Simple(Delete, () => Scene.Selection.Count > 0);
        DuplicateCommand = new RelayCommand(p => Duplicate(offset: !Equals(p, "InPlace")),
            _ => Scene.Selection.Count > 0);
        MirrorCommand = new RelayCommand(p => Mirror(p), _ => Scene.Selection.Count > 0);
        AlignToPlateCommand = RelayCommand.Simple(AlignToPlate, () => Scene.Selection.Count > 0);
        DropDownCommand = new RelayCommand(p => DropDown(p is "Overlap"
            || (System.Windows.Input.Keyboard.Modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) != 0),
            _ => Scene.Selection.Count > 0);
        FitToBedCommand = RelayCommand.Simple(FitToBed, () => Scene.Selection.Count > 0);
        DistributeOnBedCommand = RelayCommand.Simple(DistributeOnBed, () => Scene.Selection.Count > 1);
        SelectAllCommand = RelayCommand.Simple(SelectAll, () => Scene.Objects.Count > 0);
        ToggleHiddenCommand = new RelayCommand(p => { if (p is SceneObject o) SetHidden([o], !o.IsHidden); });
        ToggleLockedCommand = new RelayCommand(p => { if (p is SceneObject o) SetLocked([o], !o.IsLocked); });
        PickObjectColourCommand = new RelayCommand(p => { if (p is SceneObject o) PickColourFor(o); });
        ShowAllCommand = RelayCommand.Simple(ShowAll, () => AnyHidden);
        UnlockAllCommand = RelayCommand.Simple(UnlockAll, () => AnyLocked);
        InvertSelectionCommand = RelayCommand.Simple(InvertSelection, () => Scene.Objects.Count > 0);
        GroupCommand = RelayCommand.Simple(Group, () => Scene.Selection.Count > 1);

        // The boxes' unit is shared by every panel; a new model starts it at its own, rather than
        // at whatever the last one was left on.
        View.LengthConverter.Unit = unit;
        UngroupCommand = RelayCommand.Simple(Ungroup, () => Scene.Selection.Count > 0);
        DeselectAllCommand = RelayCommand.Simple(
            () => { Scene.ClearSelection(); RefreshSelection(); },
            () => Scene.Selection.Count > 0);

        BooleanCommand = Track(new AsyncRelayCommand(p => RunBoolean(p), _ => Scene.Selection.Count >= 2));
        BeginSubtractCommand = Track(RelayCommand.Simple(BeginSubtract, () => Scene.Selection.Count >= 2));
        BeginExtrudeCommand = Track(RelayCommand.Simple(BeginExtrude, () => Scene.Selection.Count > 0));
        ApplyExtrudeCommand = AsyncRelayCommand.Simple(ApplyExtrude, () => IsExtrudeMode);
        CancelExtrudeCommand = RelayCommand.Simple(() => IsExtrudeMode = false);
        ApplySubtractCommand = new AsyncRelayCommand(_ => ApplySubtract(), _ => Scene.Selection.Count >= 2);
        CancelSubtractCommand = RelayCommand.Simple(() => IsSubtractMode = false);
        BeginSplitCommand = Track(RelayCommand.Simple(
            () => { SplitWithConnectors = false; BeginSplit(); }, () => Scene.Selection.Count > 0));
        BeginConnectCommand = Track(RelayCommand.Simple(BeginConnect, () => Scene.Selection.Count == 2));
        ApplyConnectCommand = AsyncRelayCommand.Simple(ApplyConnect, () => IsConnectMode);
        CancelConnectCommand = RelayCommand.Simple(() => IsConnectMode = false);
        BeginSplitWithConnectorsCommand = Track(RelayCommand.Simple(
            () => { SplitWithConnectors = true; BeginSplit(); }, () => Scene.Selection.Count > 0));
        AlignToAxesCommand = RelayCommand.Simple(AlignToAxes, AnythingTurned);
        ApplySplitCommand = AsyncRelayCommand.Simple(ApplySplit, () => IsSplitMode);
        CancelSplitCommand = RelayCommand.Simple(() => IsSplitMode = false);

        RepairCommand = Track(AsyncRelayCommand.Simple(RepairObjects, () => Scene.Objects.Count > 0));
        SmoothCommand = Track(RelayCommand.Simple(SmoothSelection, () => Scene.Selection.Count > 0));
        TwistCommand = Track(RelayCommand.Simple(TwistSelection, () => Scene.Selection.Count > 0));
        TaperCommand = Track(RelayCommand.Simple(TaperSelection, () => Scene.Selection.Count > 0));
        BendCommand = Track(RelayCommand.Simple(BendSelection, () => Scene.Selection.Count > 0));
        RebuildCommand = Track(AsyncRelayCommand.Simple(RebuildObjects, () => Scene.Objects.Count > 0));
        SimplifyCommand = Track(AsyncRelayCommand.Simple(SimplifySelection, () => Scene.Selection.Count > 0));
        HollowCommand = Track(AsyncRelayCommand.Simple(HollowSelection, () => Scene.Selection.Count > 0));
        VoronoiCommand = Track(AsyncRelayCommand.Simple(VoronoiSelection, () => Scene.Selection.Count > 0));
        BeginEmbossCommand = Track(RelayCommand.Simple(BeginEmboss, () => Scene.Selection.Count == 1));
        BeginMasonryCommand = Track(RelayCommand.Simple(BeginMasonry, () => Scene.Selection.Count == 1));
        BeginTextureCommand = Track(RelayCommand.Simple(BeginTexture, () => Scene.Selection.Count == 1));
        BeginLayCommand = Track(RelayCommand.Simple(BeginLay, () => Scene.Selection.Count == 1));
        BestFaceCommand = Track(AsyncRelayCommand.Simple(BestFaceDown, () => Scene.Selection.Count == 1));
        ApplyEmbossCommand = AsyncRelayCommand.Simple(
            ApplyEmboss, () => isEmbossMode && embossFace is not null && Scene.Selection.Count == 1);
        CancelEmbossCommand = RelayCommand.Simple(() => IsEmbossMode = false);
        BeginAlignFaceCommand = Track(RelayCommand.Simple(BeginAlignFace, () => Scene.Selection.Count > 0));
        ApplyAlignFaceCommand = AsyncRelayCommand.Simple(
            ApplyAlignFace, () => alignFacePicked && (alignFaceModeX is not null || alignFaceModeY is not null || alignFaceModeZ is not null));
        CancelAlignFaceCommand = RelayCommand.Simple(() => IsAlignFaceMode = false);
        BeginCentreFaceCommand = Track(RelayCommand.Simple(BeginCentreFace, () => Scene.Selection.Count > 0));
        ApplyCentreFaceCommand = AsyncRelayCommand.Simple(
            ApplyCentreFace, () => centreFaceAPicked && centreFaceBPicked && (CentreIsRound || centreAxisX || centreAxisY || centreAxisZ));
        CancelCentreFaceCommand = RelayCommand.Simple(() => IsCentreFaceMode = false);
        LoadDrawingCommand = RelayCommand.Simple(LoadDrawing);
        ClearDrawingCommand = RelayCommand.Simple(
            () => { svgFile = ""; RefreshDrawing(); }, () => svgFile.Length > 0);
        RepeatCommand = Track(RelayCommand.Simple(RepeatSelection, () => Scene.Selection.Count > 0));
        AbortCommand = RelayCommand.Simple(AbortWork, () => CanAbort);
        Undo.Trimmed += () => HistoryTrimmed = true;
        sink = new Sink(value => reported = value);
        MouldCommand = Track(new AsyncRelayCommand(_ => MakeMould(), _ => Scene.Selection.Count == 1));
        AlignToSelectionCommand = RelayCommand.Simple(
            AlignToSelection, () => Scene.Selection.Count == 2);
        FitCheckCommand = RelayCommand.Simple(FitCheck, () => Scene.Selection.Count == 2);
        SetPivotCommand = Track(RelayCommand.Simple(BeginPivot, () => Scene.Selection.Count == 1));
        PivotToCentreCommand = RelayCommand.Simple(PivotToCentre, () => Scene.Selection.Count == 1);
        BeginMeasureCommand = Track(RelayCommand.Simple(BeginMeasure, () => Scene.Objects.Count > 0));
        CancelMeasureCommand = RelayCommand.Simple(() => IsMeasureMode = false);
        BeginEngraveCommand = Track(RelayCommand.Simple(BeginEngrave, () => Scene.Selection.Count == 1));
        ApplyEngraveCommand = AsyncRelayCommand.Simple(ApplyEngrave, () => isEngraveMode && engrave.HasFace);
        CancelEngraveCommand = RelayCommand.Simple(() => IsEngraveMode = false);

        UndoCommand = RelayCommand.Simple(
            () => Run(Undo.NextUndoBytes >= SlowFromBytes, "Undoing...", () => { Undo.Undo(); RefreshSelection(); }),
            () => Undo.CanUndo);
        RedoCommand = RelayCommand.Simple(
            () => Run(Undo.NextRedoBytes >= SlowFromBytes, "Redoing...", () => { Undo.Redo(); RefreshSelection(); }),
            () => Undo.CanRedo);

        OpenRecentCommand = new RelayCommand(OpenRecent);
        recent.Changed += () => Raise(nameof(RecentFiles));
        SaveVersionCommand = RelayCommand.Simple(SaveVersion, () => Scene.Objects.Count > 0);
        VersionsCommand = RelayCommand.Simple(ShowVersions, () => projectPath is not null);
        NewCommand = RelayCommand.Simple(NewScene);
        CloseGridPanelCommand = RelayCommand.Simple(() => IsGridPanelOpen = false);
        SetOverhangColourCommand = new RelayCommand(SetOverhangColour);
        PickOverhangColourCommand = RelayCommand.Simple(PickOverhangColour);
        CloseOverhangsPanelCommand = RelayCommand.Simple(() => ShowOverhangs = false);
        CloseShortcutsPanelCommand = RelayCommand.Simple(() => IsShortcutsPanelOpen = false);
        OpenCommand = RelayCommand.Simple(OpenProject);
        SaveCommand = RelayCommand.Simple(() => SaveProject(saveAs: false));
        SaveAsCommand = RelayCommand.Simple(() => SaveProject(saveAs: true));
        SetMoveModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Move);
        SetRotateModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Rotate);
        SetScaleModeCommand = RelayCommand.Simple(() => GizmoMode = GizmoMode.Scale);
        AlignCommand = new RelayCommand(Align, CanAlign);
        RoundCommand = Track(RelayCommand.Simple(RoundSelection, () => Scene.Selection.Any(o => o.CanRound)));
        RepeatLastCommand = RelayCommand.Simple(
            () => lastRepeatable!.Execute(lastRepeatableParameter),
            () => lastRepeatable?.CanExecute(lastRepeatableParameter) ?? false);
        CopyCommand = RelayCommand.Simple(Copy, () => Scene.Selection.Count > 0);
        CutCommand = RelayCommand.Simple(Cut, () => Scene.Selection.Count > 0);
        PasteCommand = RelayCommand.Simple(Paste, () => clipboard.Count > 0 || SharedClipboardHasObjects());
        ImportCommand = RelayCommand.Simple(Import);
        ExportCommand = RelayCommand.Simple(Export, () => Scene.Objects.Count > 0);
        ExportSessionCommand = AsyncRelayCommand.Simple(ExportSession, () => Undo.History.Count > 0);
        RecordCommand = RelayCommand.Simple(ToggleRecording);
        PrintDrawingCommand = RelayCommand.Simple(PrintDrawing, () => Scene.Objects.Count > 0);
        SetColourCommand = new RelayCommand(SetColour, _ => Scene.Selection.Count > 0);
        PickColourCommand = RelayCommand.Simple(PickColour, () => Scene.Selection.Count > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Scene Scene { get; }
    public UndoStack Undo { get; }

    public System.Windows.Input.ICommand InsertCommand { get; }
    public System.Windows.Input.ICommand InsertCustomCommand { get; }
    public System.Windows.Input.ICommand InsertTextCommand { get; }
    public System.Windows.Input.ICommand InsertHoleCommand { get; }
    public System.Windows.Input.ICommand InsertLithophaneCommand { get; }
    public System.Windows.Input.ICommand HullCommand { get; }
    public System.Windows.Input.ICommand BeginSketchCommand { get; }
    public System.Windows.Input.ICommand SketchCloseCommand { get; }
    public System.Windows.Input.ICommand SketchUndoCommand { get; }
    public System.Windows.Input.ICommand SketchClearCommand { get; }
    public System.Windows.Input.ICommand SketchLoadDrawingCommand { get; }
    public System.Windows.Input.ICommand SketchExtrudeCommand { get; }
    public System.Windows.Input.ICommand SketchRevolveCommand { get; }
    public System.Windows.Input.ICommand DoneSketchCommand { get; }
    public System.Windows.Input.ICommand DeleteCommand { get; }
    public System.Windows.Input.ICommand DuplicateCommand { get; }
    public System.Windows.Input.ICommand MirrorCommand { get; }
    public System.Windows.Input.ICommand AlignToPlateCommand { get; }

    public System.Windows.Input.ICommand DropDownCommand { get; }
    public System.Windows.Input.ICommand FitToBedCommand { get; }
    public System.Windows.Input.ICommand DistributeOnBedCommand { get; }
    public System.Windows.Input.ICommand SelectAllCommand { get; }
    public System.Windows.Input.ICommand ToggleHiddenCommand { get; }
    public System.Windows.Input.ICommand ToggleLockedCommand { get; }
    public System.Windows.Input.ICommand PickObjectColourCommand { get; }
    public System.Windows.Input.ICommand ShowAllCommand { get; }
    public System.Windows.Input.ICommand UnlockAllCommand { get; }
    public System.Windows.Input.ICommand InvertSelectionCommand { get; }
    public System.Windows.Input.ICommand GroupCommand { get; }
    public System.Windows.Input.ICommand UngroupCommand { get; }
    public System.Windows.Input.ICommand DeselectAllCommand { get; }
    public System.Windows.Input.ICommand BooleanCommand { get; }
    public System.Windows.Input.ICommand BeginSubtractCommand { get; }
    public System.Windows.Input.ICommand ApplySubtractCommand { get; }
    public System.Windows.Input.ICommand CancelSubtractCommand { get; }
    public System.Windows.Input.ICommand BeginSplitCommand { get; }
    public System.Windows.Input.ICommand BeginSplitWithConnectorsCommand { get; }
    public System.Windows.Input.ICommand BeginConnectCommand { get; }
    public System.Windows.Input.ICommand ApplyConnectCommand { get; }
    public System.Windows.Input.ICommand CancelConnectCommand { get; }
    public System.Windows.Input.ICommand BeginExtrudeCommand { get; }
    public System.Windows.Input.ICommand ApplyExtrudeCommand { get; }
    public System.Windows.Input.ICommand CancelExtrudeCommand { get; }
    public System.Windows.Input.ICommand AlignToAxesCommand { get; }
    public System.Windows.Input.ICommand ApplySplitCommand { get; }
    public System.Windows.Input.ICommand CancelSplitCommand { get; }
    public System.Windows.Input.ICommand RepairCommand { get; }
    public System.Windows.Input.ICommand SmoothCommand { get; }
    public System.Windows.Input.ICommand TwistCommand { get; }
    public System.Windows.Input.ICommand TaperCommand { get; }
    public System.Windows.Input.ICommand BendCommand { get; }
    public System.Windows.Input.ICommand RebuildCommand { get; }
    public System.Windows.Input.ICommand SimplifyCommand { get; }
    public System.Windows.Input.ICommand HollowCommand { get; }
    public System.Windows.Input.ICommand VoronoiCommand { get; }

    /// <summary>Runs whatever tool-launching command last ran, with what it was given.</summary>
    public System.Windows.Input.ICommand RepeatLastCommand { get; }

    private IReadOnlyList<(Vector3 From, Vector3 To)> voronoiOutline = [];

    /// <summary>The web a Voronoi cut would leave, for the viewport to draw while it is set up.</summary>
    public IReadOnlyList<(Vector3 From, Vector3 To)> VoronoiOutline
    {
        get => voronoiOutline;
        private set => Set(ref voronoiOutline, value);
    }
    public System.Windows.Input.ICommand RepeatCommand { get; }
    public System.Windows.Input.ICommand AbortCommand { get; }
    public System.Windows.Input.ICommand MouldCommand { get; }
    public System.Windows.Input.ICommand AlignToSelectionCommand { get; }
    public System.Windows.Input.ICommand FitCheckCommand { get; }
    public System.Windows.Input.ICommand SetPivotCommand { get; }
    public System.Windows.Input.ICommand PivotToCentreCommand { get; }
    public System.Windows.Input.ICommand BeginEmbossCommand { get; }
    public System.Windows.Input.ICommand BeginMasonryCommand { get; }
    public System.Windows.Input.ICommand BeginTextureCommand { get; }
    public System.Windows.Input.ICommand BeginLayCommand { get; }
    public System.Windows.Input.ICommand BestFaceCommand { get; }
    public System.Windows.Input.ICommand ApplyEmbossCommand { get; }
    public System.Windows.Input.ICommand CancelEmbossCommand { get; }
    public System.Windows.Input.ICommand BeginAlignFaceCommand { get; }
    public System.Windows.Input.ICommand ApplyAlignFaceCommand { get; }
    public System.Windows.Input.ICommand CancelAlignFaceCommand { get; }
    public System.Windows.Input.ICommand BeginCentreFaceCommand { get; }
    public System.Windows.Input.ICommand ApplyCentreFaceCommand { get; }
    public System.Windows.Input.ICommand CancelCentreFaceCommand { get; }
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
    public System.Windows.Input.ICommand CloseGridPanelCommand { get; }
    public System.Windows.Input.ICommand CloseShortcutsPanelCommand { get; }
    public System.Windows.Input.ICommand OpenCommand { get; }
    public System.Windows.Input.ICommand SaveCommand { get; }
    public System.Windows.Input.ICommand SaveAsCommand { get; }
    public System.Windows.Input.ICommand SetMoveModeCommand { get; }
    public System.Windows.Input.ICommand SetRotateModeCommand { get; }
    public System.Windows.Input.ICommand SetScaleModeCommand { get; }
    public System.Windows.Input.ICommand AlignCommand { get; }
    public System.Windows.Input.ICommand RoundCommand { get; }
    public System.Windows.Input.ICommand CopyCommand { get; }
    public System.Windows.Input.ICommand CutCommand { get; }
    public System.Windows.Input.ICommand PasteCommand { get; }
    public System.Windows.Input.ICommand ImportCommand { get; }
    public System.Windows.Input.ICommand ExportCommand { get; }
    public System.Windows.Input.ICommand ExportSessionCommand { get; }
    public System.Windows.Input.ICommand RecordCommand { get; }
    public System.Windows.Input.ICommand PrintDrawingCommand { get; }
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
        get => unit.From(aroundSelectionCentre ? GroupCentre().X : Shared(o => o.PositionX));
        set => OnBed(moving: true, together: aroundSelectionCentre, () => { if (aroundSelectionCentre) MoveGroupTo(Axis.X, unit.To(value)); else PlaceEach(Axis.X, unit.To(value)); });
    }

    public float GroupY
    {
        get => unit.From(aroundSelectionCentre ? GroupCentre().Y : Shared(o => o.PositionY));
        set => OnBed(moving: true, together: aroundSelectionCentre, () => { if (aroundSelectionCentre) MoveGroupTo(Axis.Y, unit.To(value)); else PlaceEach(Axis.Y, unit.To(value)); });
    }

    public float GroupZ
    {
        get => unit.From(aroundSelectionCentre ? GroupCentre().Z : Shared(o => o.PositionZ));
        set => OnBed(moving: true, together: aroundSelectionCentre, () => { if (aroundSelectionCentre) MoveGroupTo(Axis.Z, unit.To(value)); else PlaceEach(Axis.Z, unit.To(value)); });
    }

    /// <summary>
    /// The turn every selected object shares, or nothing when they disagree.
    ///
    /// Typing one sets all of them to it. Dragging the rings adds the same amount to each, which
    /// is the same idea: a group has no rotation of its own, so the honest thing to show is what
    /// they have in common.
    /// </summary>
    // As one there is no angle to show - parts turned different ways share none - so the box
    // reads 0 and means "turn the lot by this much about its middle". Each on its own it shows
    // what they share and sets all of them to it.
    public float GroupRoll
    {
        get => aroundSelectionCentre ? DisplayedGroupTurn(Axis.X, groupRollSinceGrab) : Shared(o => o.RotationX);
        set { if (aroundSelectionCentre) TurnSelectionAbout(Axis.X, value); else TurnGroup(Axis.X, value); }
    }

    public float GroupPitch
    {
        get => aroundSelectionCentre ? DisplayedGroupTurn(Axis.Y, groupPitchSinceGrab) : Shared(o => o.RotationY);
        set { if (aroundSelectionCentre) TurnSelectionAbout(Axis.Y, value); else TurnGroup(Axis.Y, value); }
    }

    public float GroupYaw
    {
        get => aroundSelectionCentre ? DisplayedGroupTurn(Axis.Z, groupYawSinceGrab) : Shared(o => o.RotationZ);
        set { if (aroundSelectionCentre) TurnSelectionAbout(Axis.Z, value); else TurnGroup(Axis.Z, value); }
    }

    /// <summary>
    /// The axis a ring is mid-drag on, and how far it has turned so far - on top of whatever the
    /// box already read, since DragRotate measures the whole drag from where it started rather
    /// than ticking up from the last mouse move.
    /// </summary>
    private Axis? liveGroupTurnAxis;
    private float liveGroupTurnDegrees;

    private float DisplayedGroupTurn(Axis axis, float settled) =>
        liveGroupTurnAxis == axis ? GizmoMath.NormaliseDegrees(settled + liveGroupTurnDegrees) : settled;

    /// <summary>
    /// Zeroes the roll/pitch/yaw readout for the "as one" rotation, so it counts from whatever
    /// moment counts as fresh: a new selection, or the mode being switched on. The angles
    /// themselves live nowhere on the objects - each keeps its own - so there is nothing to zero
    /// but this reading of it.
    /// </summary>
    private void ResetGroupTurnSinceGrab()
    {
        groupRollSinceGrab = 0f;
        groupPitchSinceGrab = 0f;
        groupYawSinceGrab = 0f;
        liveGroupTurnAxis = null;
        liveGroupTurnDegrees = 0f;
        groupTurnBaseline = Scene.Selection.ToList();
    }

    private void AccumulateGroupTurn(Axis axis, float degrees)
    {
        switch (axis)
        {
            case Axis.X: groupRollSinceGrab = GizmoMath.NormaliseDegrees(groupRollSinceGrab + degrees); break;
            case Axis.Y: groupPitchSinceGrab = GizmoMath.NormaliseDegrees(groupPitchSinceGrab + degrees); break;
            default: groupYawSinceGrab = GizmoMath.NormaliseDegrees(groupYawSinceGrab + degrees); break;
        }
    }

    /// <summary>
    /// A ring drag reporting in - live on every step so the box tracks the turn as it happens,
    /// and once more, settled, when the mouse lets go.
    /// </summary>
    public void NoteGroupTurn(Axis axis, float degrees, bool settled)
    {
        if (!float.IsFinite(degrees)) return;

        if (settled)
        {
            if (MathF.Abs(degrees) >= 1e-4f) AccumulateGroupTurn(axis, degrees);
            liveGroupTurnAxis = null;
            liveGroupTurnDegrees = 0f;
        }
        else
        {
            liveGroupTurnAxis = axis;
            liveGroupTurnDegrees = degrees;
        }

        RaiseGroup();
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
        get => unit.From(aroundSelectionCentre ? GroupExtent().X : Shared(o => o.SizeX));
        set => OnBed(moving: false, together: aroundSelectionCentre, () => { if (aroundSelectionCentre) ResizeGroup(Axis.X, unit.To(value)); else ResizeEach(Axis.X, unit.To(value)); });
    }

    public float GroupSizeY
    {
        get => unit.From(aroundSelectionCentre ? GroupExtent().Y : Shared(o => o.SizeY));
        set => OnBed(moving: false, together: aroundSelectionCentre, () => { if (aroundSelectionCentre) ResizeGroup(Axis.Y, unit.To(value)); else ResizeEach(Axis.Y, unit.To(value)); });
    }

    public float GroupSizeZ
    {
        get => unit.From(aroundSelectionCentre ? GroupExtent().Z : Shared(o => o.SizeZ));
        set => OnBed(moving: false, together: aroundSelectionCentre, () => { if (aroundSelectionCentre) ResizeGroup(Axis.Z, unit.To(value)); else ResizeEach(Axis.Z, unit.To(value)); });
    }

    /// <summary>
    /// The value every selected object has, or NaN when they differ.
    ///
    /// NaN rather than 0, because 0 is a value: a box reading 0 for parts that are all somewhere
    /// else invites typing over it as though it were true. The box shows nothing instead, marked
    /// as mixed.
    /// </summary>
    private float Shared(Func<SceneObject, float> of)
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return 0f;

        float first = of(selection[0]);
        foreach (var o in selection)
            if (MathF.Abs(of(o) - first) > 0.05f) return float.NaN;

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
                // Not pristine: the turn is in the mesh now, and rounding would rebuild it upright.
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

        var centre = GroupCentre();
        float anchor = Along(centre, axis);

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

            // With the lock on, every part grows on all three axes, so the gaps have to grow on
            // all three as well - as they do when a handle is dragged. Moving the parts along the
            // typed axis alone left them growing into one another, and the lot came out the wrong
            // size on the other two, which read as every part being resized where it stood.
            o.Position = UniformScale
                ? centre + (at - centre) * ratio
                : axis switch
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

    /// <summary>
    /// Runs a typed move or resize of the selection and holds the result to the bed when Keep on
    /// bed is on for it. A resize also keeps what stood on the bed standing there; a move must
    /// not, or nothing could be lifted off it.
    /// </summary>
    private void OnBed(bool moving, bool together, Action change)
    {
        if (!(moving ? keepOnBedMove : keepOnBedScale))
        {
            change();
            return;
        }

        var objects = Scene.Selection.ToList();
        var before = objects.Select(o => o.WorldBounds).ToList();

        change();

        if (BedPlacement.HoldToBed(objects, before, together || objects.Count == 1, settle: !moving))
        {
            RaiseTransformFields();
            RaiseGroup();
        }
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

    /// <summary>Every selected object's own position on one axis set to the same value: they line up.</summary>
    private void PlaceEach(Axis axis, float where)
    {
        if (!float.IsFinite(where) || Scene.Selection.Count == 0) return;

        foreach (var o in Scene.Selection)
        {
            switch (axis)
            {
                case Axis.X: o.PositionX = where; break;
                case Axis.Y: o.PositionY = where; break;
                default: o.PositionZ = where; break;
            }
        }

        RaiseGroup();
    }

    /// <summary>Every selected object resized to the same size, each about its own centre.</summary>
    private void ResizeEach(Axis axis, float millimetres)
    {
        if (!float.IsFinite(millimetres) || millimetres < 0.01f) return;

        foreach (var o in Scene.Selection) ResizeObject(o, axis, millimetres);

        RaiseGroup();
    }

    /// <summary>
    /// The whole selection turned about the middle of the lot, as one object.
    ///
    /// Composed onto each part's own turn rather than added to one of its angles, for the reason
    /// the rings give: the three angles are applied in order, so only the last lines up with the
    /// world, and adding to the others turns a part about its own axes instead. The positions
    /// swing round the middle as well, which is what keeps the arrangement.
    /// </summary>
    private void TurnSelectionAbout(Axis axis, float degrees)
    {
        if (!float.IsFinite(degrees) || MathF.Abs(degrees) < 1e-4f || Scene.Selection.Count == 0) return;

        var centre = GroupCentre();
        float radians = degrees * MathF.PI / 180f;
        var turn = axis switch
        {
            Axis.X => Matrix4x4.CreateRotationX(radians),
            Axis.Y => Matrix4x4.CreateRotationY(radians),
            _ => Matrix4x4.CreateRotationZ(radians)
        };

        foreach (var o in Scene.Selection)
        {
            o.Rotation = MeshTransform.EulerFrom(MeshTransform.Rotation(o.Rotation) * turn);
            o.Position = centre + Vector3.Transform(o.Position - centre, turn);
        }

        AccumulateGroupTurn(axis, degrees);
        RaiseGroup();
    }

    /// <summary>
    /// "+=5" typed into a box with several objects each on their own: every object changed by that
    /// much. False when this is not that case, and the box's own value is changed instead.
    ///
    /// It has to be done here, one object at a time. Each on its own, a box shows what the parts
    /// share or nothing at all, and nothing plus five is not a number - so adding to the box would
    /// do nothing exactly when the parts differ, which is when anyone would type it.
    /// </summary>
    public bool ChangeEachBy(string property, float delta, bool times = false)
    {
        if (aroundSelectionCentre || Scene.Selection.Count < 2 || !float.IsFinite(delta)) return false;

        // Times a factor has no unit; plus an amount is in the boxes' unit.
        float d = times ? delta : unit.To(delta);
        float By(float value, float amount) => times ? value * delta : value + amount;
        var selection = Scene.Selection;
        bool known = true;
        bool moving = property is nameof(GroupX) or nameof(GroupY) or nameof(GroupZ);
        bool turning = property is nameof(GroupRoll) or nameof(GroupPitch) or nameof(GroupYaw);

        void Change()
        {
            switch (property)
            {
                case nameof(GroupX): foreach (var o in selection) o.PositionX = By(o.PositionX, d); break;
                case nameof(GroupY): foreach (var o in selection) o.PositionY = By(o.PositionY, d); break;
                case nameof(GroupZ): foreach (var o in selection) o.PositionZ = By(o.PositionZ, d); break;
                case nameof(GroupSizeX): foreach (var o in selection) ResizeObject(o, Axis.X, MathF.Max(0.01f, By(o.SizeX, d))); break;
                case nameof(GroupSizeY): foreach (var o in selection) ResizeObject(o, Axis.Y, MathF.Max(0.01f, By(o.SizeY, d))); break;
                case nameof(GroupSizeZ): foreach (var o in selection) ResizeObject(o, Axis.Z, MathF.Max(0.01f, By(o.SizeZ, d))); break;
                case nameof(GroupPercentX): foreach (var o in selection) ResizeObject(o, Axis.X, AtPercent(o, Axis.X, MathF.Max(0.01f, By(ScaleOn(o, Axis.X) * 100f, delta)))); break;
                case nameof(GroupPercentY): foreach (var o in selection) ResizeObject(o, Axis.Y, AtPercent(o, Axis.Y, MathF.Max(0.01f, By(ScaleOn(o, Axis.Y) * 100f, delta)))); break;
                case nameof(GroupPercentZ): foreach (var o in selection) ResizeObject(o, Axis.Z, AtPercent(o, Axis.Z, MathF.Max(0.01f, By(ScaleOn(o, Axis.Z) * 100f, delta)))); break;
                case nameof(GroupRoll): foreach (var o in selection) o.RotationX = GizmoMath.NormaliseDegrees(By(o.RotationX, delta)); break;
                case nameof(GroupPitch): foreach (var o in selection) o.RotationY = GizmoMath.NormaliseDegrees(By(o.RotationY, delta)); break;
                case nameof(GroupYaw): foreach (var o in selection) o.RotationZ = GizmoMath.NormaliseDegrees(By(o.RotationZ, delta)); break;
                default: known = false; break;
            }
        }

        if (turning) Change();
        else OnBed(moving, together: false, Change);

        if (!known) return false;

        RaiseGroup();
        return true;
    }

    /// <summary>
    /// The floating move/rotate/resize strip. Out of the way while a tool is running, along with
    /// the handles it drives - leaving it up would offer a resize that the tool's own handles
    /// are sitting on top of.
    /// </summary>
    public bool ShowManipulatorBar => HasAnySelection && (!IsToolRunning || panelHandles);

    /// <summary>Resize is offered except on a tool's preview, which is the size its numbers say.</summary>
    public bool ResizeOffered => !panelHandles || heldResizable;

    public string Status
    {
        get => status;
        set
        {
            Set(ref status, value);
            if (IsWarning(value)) ShowNotice(value);
        }
    }

    private string notice = "";
    private DispatcherTimer? noticeTimer;

    /// <summary>
    /// A message worth not missing - that something could not be done, or that nothing changed -
    /// shown over the viewport for a few seconds as well as in the status bar, which is at the far
    /// edge of the window from where anyone is looking when they press Apply.
    /// </summary>
    public string Notice
    {
        get => notice;
        private set
        {
            Set(ref notice, value);
            Raise(nameof(HasNotice));
        }
    }

    public bool HasNotice => notice.Length > 0;

    private string waitMessage = "";

    /// <summary>
    /// A line over the window while something runs on the UI thread that cannot report on itself,
    /// such as opening a large project. Plain text, with no counter and nothing moving: that thread
    /// is the one doing the work, so nothing here could tick or animate until it was over. The
    /// work that can be moved off it has the busy panel instead.
    /// </summary>
    public string WaitMessage
    {
        get => waitMessage;
        private set
        {
            Set(ref waitMessage, value);
            Raise(nameof(IsWaiting));
        }
    }

    public bool IsWaiting => waitMessage.Length > 0;

    /// <summary>
    /// A file smaller than this opens before the message could be read, so it is not shown: it
    /// would only be a flash of the window dimming.
    /// </summary>
    private const long WaitFromBytes = 512 * 1024;

    /// <summary>
    /// A step that puts back or takes away this much geometry is slow enough to be worth saying
    /// so: about a fifth of a second, from measuring a dense mesh. A move or a colour holds
    /// nothing and is instant, and is done on the spot as it always was.
    /// </summary>
    private const long SlowFromBytes = 2L * 1024 * 1024;

    /// <summary>Another slow job is already waiting its turn, so a second ask for one is not another job.</summary>
    private bool deferred;

    /// <summary>
    /// Does <paramref name="work"/> on the spot when it is quick, and when it is not, a moment
    /// after returning, with <paramref name="message"/> over the window meanwhile.
    ///
    /// Not on the spot, because the click or key that asks for it is on a menu or a dialog that is
    /// only closed once it has been handled, and the window cannot say anything until it has had a
    /// frame to draw it in. Done in the handler the list stayed open for the whole job with
    /// nothing to show for it, and not being able to tell it had worked, people clicked again.
    /// Waiting for the frame from inside the handler does not work either: the frame is queued
    /// by the message appearing, behind anything asked for first.
    /// </summary>
    private void Run(bool slow, string message, Action work)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (!slow || dispatcher is null)
        {
            work();
            return;
        }

        if (deferred) return;

        deferred = true;
        WaitMessage = message;

        // Below render priority, so the menu closes and the message is drawn first.
        dispatcher.BeginInvoke(() =>
        {
            try { work(); }
            finally
            {
                deferred = false;
                StopWaiting();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Takes the message away - once the window has gone idle rather than on the spot, because
    /// what the work put on the plate is drawn in the frame after it returns, and a message that
    /// went first would leave the old plate on show for that frame.
    /// </summary>
    private void StopWaiting(bool now = false)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (now || dispatcher is null) WaitMessage = "";
        else dispatcher.BeginInvoke(() => WaitMessage = "", DispatcherPriority.ContextIdle);
    }

    private static readonly string[] WarningWords =
        ["nothing was changed", "nothing was added", "could not", "cannot", "can't", "failed", "would not", "no room", "is not a", "not closed", "too small", "too big", "too short"];

    private static bool IsWarning(string? message) =>
        !string.IsNullOrEmpty(message) && WarningWords.Any(w => message.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Says that something could not be done, where it will be seen: in the status bar and over the
    /// viewport for a few seconds. A refusal worded without one of the words that mean trouble
    /// went only to the status bar, which is at the far edge of the window from the pointer.
    /// </summary>
    private void Warn(string message)
    {
        Status = message;
        ShowNotice(message);
    }

    private void ShowNotice(string message)
    {
        Notice = message;
        noticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        noticeTimer.Stop();
        noticeTimer.Tick -= ClearNotice;
        noticeTimer.Tick += ClearNotice;
        noticeTimer.Start();
    }

    private void ClearNotice(object? sender, EventArgs e)
    {
        noticeTimer?.Stop();
        Notice = "";
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
            bool was = isDirty;

            Set(ref isDirty, value);
            Raise(nameof(WindowTitle));

            if (value) Changed();
            else if (was) ForgetRecovery();
        }
    }

    // --- The crash file ------------------------------------------------------------------

    /// <summary>Bumped by every change, so the crash file can tell whether it is out of date.</summary>
    private int changes;
    private int keptChanges;

    private long lastChange = Stopwatch.GetTimestamp();
    private readonly Stopwatch sinceKept = Stopwatch.StartNew();
    private double keepTook;

    /// <summary>How still the scene has to be before the crash file is rewritten, in milliseconds.</summary>
    private const double Settle = 2_000;

    private void Changed()
    {
        changes++;
        lastChange = Stopwatch.GetTimestamp();
    }

    /// <summary>Nothing to recover any more: the scene is on disc, or the app is done with it.</summary>
    private void ForgetRecovery()
    {
        Recovery.Clear();
        keptChanges = changes;
        sinceKept.Restart();
    }

    /// <summary>
    /// Writes the crash file if it has fallen behind. Called on a timer, and directly when the
    /// app is on its way down after a fault.
    ///
    /// Not after every action, which is what it looks like from the outside but is not what it
    /// does: a drag is hundreds of changes, and a scene of any size costs real time to write. It
    /// waits for the scene to be still for a moment, so a write lands just after an action rather
    /// than in the middle of one, and then it waits again for as long as the last write took -
    /// ten times over, up to a minute. A small model is therefore kept within a second or two of
    /// every change, and a heavy one costs a fixed small share of the time rather than a stall
    /// after each edit. The same reasoning as the split preview, and for the same reason.
    /// </summary>
    public void KeepRecovery(bool now = false)
    {
        if (changes == keptChanges) return;

        if (!now)
        {
            if (IsBusy) return;
            if (Stopwatch.GetElapsedTime(lastChange).TotalMilliseconds < Settle) return;
            if (sinceKept.Elapsed.TotalMilliseconds < Math.Clamp(keepTook * 10, Settle, 60_000)) return;
        }

        long started = Stopwatch.GetTimestamp();

        Recovery.Keep(Scene, projectPath, ViewSettings);

        keepTook = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        keptChanges = changes;
        sinceKept.Restart();
    }

    /// <summary>
    /// Takes back what an earlier run left behind: the scene as it stood, still unsaved, and
    /// pointed at the project it came from so Save goes where it was going.
    /// </summary>
    public bool Recover(RecoveryPoint point)
    {
        try
        {
            var loaded = SceneSerializer.Load(point.ScenePath, out var settings);
            if (settings is { } kept) ApplySettings(kept);

            Scene.Objects.Clear();
            foreach (var o in loaded) Scene.Objects.Add(o);

            Undo.Clear();
            projectPath = point.ProjectPath;
            openedFrom = null;
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();

            // Still not written anywhere the user asked for, which is the whole point of it.
            IsDirty = true;

            Status = point.ProjectPath is null
                ? "Recovered - this model has never been saved, so save it now"
                : $"Recovered {Path.GetFileName(point.ProjectPath)} - not saved yet";

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not recover the model", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
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
            // The invented name rather than "Untitled", which named none of several open
            // windows - the same complaint that put the opened file's name here in the first
            // place. It is the name Save will offer, so the title bar is not a different answer
            // from the save dialog.
            string name = projectPath is not null ? Path.GetFileNameWithoutExtension(projectPath)
                        : openedFrom is not null ? Path.GetFileName(openedFrom)
                        : inventedName ??= ProjectNames.Suggest();

            return $"{name}{(isDirty ? " *" : string.Empty)} - 3DFastCraft {Version}";
        }
    }

    /// <summary>
    /// The model file Windows handed over, when there is no project. A file opened by
    /// double-clicking it is the thing being worked on even though it is not a project of ours, and
    /// the title bar said "Untitled" - which named none of several open windows.
    /// </summary>
    private string? openedFrom;

    /// <summary>
    /// The version, from the assembly rather than a constant here, so there is one place to
    /// change it and no way for the two to disagree. Three parts, matching what the release is
    /// called - the fourth is always zero and would be nothing but noise - and "beta" after them
    /// when it is one: read from the informational version, since the assembly's own version has
    /// no room for a word, and without the commit the SDK adds after a plus.
    /// </summary>
    public static string Version
    {
        get
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly();
            string? full = assembly?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion;

            if (full is not null) return full.Split('+')[0].Replace('-', ' ');

            var version = assembly?.GetName().Version;
            return version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public string SelectionSummary
    {
        get
        {
            var selection = Scene.Selection;
            if (selection.Count == 0) return $"{Scene.Objects.Count} object(s) on the plate";
            if (selection.Count > 1)
                return SelectedAssembly is { } assembly && Scene.IsWhollySelected(assembly) && selection.All(o => o.Assembly == assembly)
                    ? $"{assembly.Name} - {selection.Count} objects selected"
                    : $"{selection.Count} objects selected";

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
    /// clicking empty space clears the selection. The viewer's own, like the grid: remembered
    /// between sessions, but not a change to the project.
    /// </summary>
    public bool StickySelection
    {
        get => stickySelection;
        set
        {
            if (stickySelection == value) return;
            Set(ref stickySelection, value);
            Status = value
                ? "Sticky selection on - clicking an object adds or removes just that object"
                : "Sticky selection off - clicking an object selects only that object";
            SettingsChanged?.Invoke();
        }
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
            // A held preview scales only when its sizes are settings - a Library part; refusing it
            // for every held preview left the Scale button on the bar for one and doing nothing.
            if (gizmoMode == value || (value == GizmoMode.Scale && panelHandles && !heldResizable)) return;
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
            View.LengthConverter.Unit = value;
            Raise(nameof(UnitLabel));
            RaiseTransformFields();

            // Every tool panel's length box reads in the unit too; an empty name refreshes them all.
            Raise(string.Empty);
            SettingChanged();
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
    /// Whether several selected objects are moved, turned and resized as one, about the middle of
    /// the lot, or each on its own about its own centre.
    ///
    /// It decides what the numbers in the bar refer to as well as what the handles do - the
    /// selection's extent or each part's own size - so switching it re-reads every box. It does
    /// not mark the project changed: nothing in the model has moved.
    /// </summary>
    public bool AroundSelectionCentre
    {
        get => aroundSelectionCentre;
        set
        {
            if (aroundSelectionCentre == value) return;

            aroundSelectionCentre = value;
            if (value) ResetGroupTurnSinceGrab();
            Raise(nameof(AroundSelectionCentre));
            Raise(nameof(EachOnItsOwn));
            foreach (var name in GroupBoxes) Raise(name);
        }
    }

    /// <summary>
    /// The same switch the other way round, for the "Each" button. Moving a selection as one is
    /// what nearly everyone wants nearly always, so the button that is lit by default said
    /// nothing and was ignored; the exception is what gets a button, and it starts out off.
    /// </summary>
    public bool EachOnItsOwn
    {
        get => !aroundSelectionCentre;
        set => AroundSelectionCentre = !value;
    }

    private static readonly string[] GroupBoxes =
    [
        nameof(GroupX), nameof(GroupY), nameof(GroupZ),
        nameof(GroupRoll), nameof(GroupPitch), nameof(GroupYaw),
        nameof(GroupSizeX), nameof(GroupSizeY), nameof(GroupSizeZ)
    ];

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
    /// Keep on bed while moving: nothing is moved below the bed.
    ///
    /// Off by default. Sinking a part into the bed is a real way to flatten its underside, and
    /// moving is how it is done.
    /// </summary>
    /// <summary>
    /// The list of keys and drags, in the side bar. Like the grid settings it stands nothing down,
    /// so it can stay open while the keys it lists are tried.
    /// </summary>
    public bool IsShortcutsPanelOpen
    {
        get => isShortcutsPanelOpen;
        set => Set(ref isShortcutsPanelOpen, value);
    }

    /// <summary>How far one press of an arrow key moves the selection: the snap step, or a millimetre with snap off.</summary>
    public float NudgeStep => snapStep > 0 ? (float)snapStep : 1f;

    private List<SceneObject>? nudged;
    private List<TransformState> nudgedBefore = [];

    /// <summary>
    /// Moves the selection by a step, as an arrow key does, holding it to the bed when moving
    /// keeps to the bed.
    ///
    /// A held key repeats many times a second, and one undo step a press would take as many Ctrl+Z
    /// to take back; so the steps pile up until <see cref="EndNudge"/> - the key coming up - and
    /// are undone as one.
    /// </summary>
    public void Nudge(Vector3 by)
    {
        if (IsToolInHand) return;

        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        if (nudged is null || !nudged.SequenceEqual(selection))
        {
            EndNudge();
            nudged = selection;
            nudgedBefore = selection.Select(TransformState.Capture).ToList();
        }

        OnBed(moving: true, together: true, () =>
        {
            foreach (var o in selection) o.Position += by;
        });

        RaiseTransformFields();
        RaiseGroup();
    }

    /// <summary>Puts the steps taken since the key went down on the undo list, as one.</summary>
    public void EndNudge()
    {
        if (nudged is null) return;

        if (TransformCommand.CreateIfChanged("Nudge", nudged, nudgedBefore) is { } command)
            Undo.Execute(command);

        nudged = null;
        nudgedBefore = [];
    }

    public bool KeepOnBedMove
    {
        get => keepOnBedMove;
        set => Set(ref keepOnBedMove, value);
    }

    /// <summary>
    /// Keep on bed while resizing: a part standing on the bed stays standing on it, so it grows
    /// upward only instead of half up and half into the bed, and nothing is resized below it.
    ///
    /// On by default, because a part on the bed is nearly always meant to stay there, and one
    /// grown into it is cut off by the slicer without a word.
    /// </summary>
    public bool KeepOnBedScale
    {
        get => keepOnBedScale;
        set => Set(ref keepOnBedScale, value);
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
            RaiseToolInHand();
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
    public bool IsToolRunning => isSplitMode || isEngraveMode || isEmbossMode || isWallMountMode || isLayMode || isExtrudeMode || isConnectMode || isSketchMode || isPivotMode
                                 || isAlignFaceMode || isCentreFaceMode || isSurfaceInfoMode || openPanel is not null;

    /// <summary>
    /// Whether a tool has the object in hand, counting the two that do not take the handles
    /// over: a waiting subtraction and the tape.
    ///
    /// While one does, nothing else may run. The ribbon, the object list, the colours and the
    /// property boxes all stand down, and only the tool's own panel stays live. Without that the
    /// ribbon was still live behind a running tool, and Delete took the object out from under a
    /// split that was halfway through being aimed - leaving its plane hanging over an empty
    /// plate.
    /// </summary>
    public bool IsToolInHand => IsToolRunning || isSubtractMode || isMeasureMode;

    /// <summary>The same thing the other way up, for everything that has to grey out.</summary>
    public bool NothingInHand => !IsToolInHand;

    /// <summary>
    /// Whether the tool in hand works on the selection alone, so the rest of the plate stands
    /// aside while it is open - drawn again when it is put down, or seen through with x-ray.
    /// Everything but the four that pick a face on anything at all - align to a face, center
    /// face to face, surface info and the tape - which need the rest there to pick from.
    /// </summary>
    public bool ToolKeepsToSelection => IsToolInHand && !isAlignFaceMode && !isCentreFaceMode && !isSurfaceInfoMode && !isMeasureMode;

    private ToolPanel? openPanel;

    /// <summary>The settings of the tool being used - Repeat, Smooth, Mould and the rest - or null.</summary>
    public ToolPanel? OpenPanel => openPanel;

    public bool HasOpenPanel => openPanel is not null;

    /// <summary>The object's properties give way to a tool's panel, which needs the room.</summary>
    public bool PropertiesVisible => openPanel is null;

    /// <summary>
    /// Where a <see cref="ToolPanel"/> shows itself. The tool has the object in hand while it is
    /// open, so the ribbon, the list and the handles stand down as they do for Split, and only the
    /// viewport stays live to look round the preview.
    /// </summary>
    public void ShowPanel(ToolPanel? panel)
    {
        openPanel = panel;
        Raise(nameof(OpenPanel));
        Raise(nameof(HasOpenPanel));
        Raise(nameof(PropertiesVisible));
        Raise(nameof(IsToolRunning));
        RaiseToolInHand();
        Raise(nameof(ShowManipulatorBar));
    }

    private void RaiseToolInHand()
    {
        Raise(nameof(IsToolInHand));
        Raise(nameof(NothingInHand));
    }

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
            if (!value) ForgetRestingFaces();
            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
            Raise(nameof(ShowManipulatorBar));
        }
    }

    // --- Align to face ---------------------------------------------------------------

    /// <summary>
    /// While this is on, a click on any object's face picks it as the reference the selection is
    /// then lined up against on Apply. It does not touch the selection itself, the same as Split -
    /// the face being picked is not necessarily part of what is about to move.
    /// </summary>
    public bool IsAlignFaceMode
    {
        get => isAlignFaceMode;
        set
        {
            if (isAlignFaceMode == value) return;

            Set(ref isAlignFaceMode, value);
            if (!value)
            {
                alignFace = null;
                alignSurface = null;
                alignFaceTargetLabel = null;
                alignMeshes.Clear();
                RaiseAlignFace();
            }
            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
            Raise(nameof(ShowManipulatorBar));
        }
    }

    /// <summary>Raised whenever the picked or hovered face changes, so the viewport can redraw the marker.</summary>
    public event Action? AlignFaceChanged;

    /// <summary>
    /// Raised when picking a face chose how to line up for the user, so the radio buttons can show
    /// it: the tags of the ones to turn on, such as "X:Centre" and "SX:Minimum".
    /// </summary>
    public event Action<IReadOnlyList<string>>? AlignFaceDefaulted;

    /// <summary>The face last picked or hovered, for the viewport to highlight. Null shows nothing.</summary>
    public FacePatch? AlignFace => alignFace;

    /// <summary>Whether a face has been picked (as against merely hovered), so Apply has something to work with.</summary>
    public bool HasAlignFaceTarget => alignFacePicked;

    /// <summary>The point the selection is lined up against: the middle of the face shown.</summary>
    /// <summary>
    /// Where on the face the selection lines up, as the axes are set: the face's least, middle or
    /// greatest on each - the middle on an axis left alone. It follows the choices, so the place
    /// on the face is seen before anything moves.
    /// </summary>
    public IReadOnlyList<Vector3> AlignFaceAnchors =>
        alignFace is { } f ? [AlignTools.PointOf(FaceBox(f), alignFaceModeX, alignFaceModeY, alignFaceModeZ)] : [];

    /// <summary>The matching point of the selection: the one that goes to the point on the face.</summary>
    public IReadOnlyList<Vector3> AlignFaceLinkPoints =>
        isAlignFaceMode && SelectionBox() is { IsEmpty: false } b ? [AlignTools.PointOf(b, alignFromX, alignFromY, alignFromZ)] : [];

    // Which point of the selection goes to the place on the face, on each axis.
    private AlignMode alignFromX = AlignMode.Centre, alignFromY = AlignMode.Centre, alignFromZ = AlignMode.Centre;

    private Bounds SelectionBox()
    {
        var b = Bounds.Empty;
        foreach (var o in Scene.Selection) b = b.Union(o.WorldBounds);
        return b;
    }

    /// <summary>The box round a face's own triangles, in the world.</summary>
    private static Bounds FaceBox(FacePatch face)
    {
        var b = Bounds.Empty;
        foreach (int t in face.Triangles)
            for (int k = 0; k < 3; k++)
            {
                var p = face.Mesh.Positions[face.Mesh.Indices[t + k]];
                b = b.Union(new Bounds(p, p));
            }

        return b;
    }

    /// <summary>The middle of a flat face, or of the triangles of a round one.</summary>
    private static Vector3 Middle(FacePatch face) => face.Normal == Vector3.Zero ? face.Origin : face.ToLocal((face.Min + face.Max) * 0.5f);

    public string AlignFaceTargetLabel => alignFaceTargetLabel is { } what
        ? $"Picked {what}"
        : "Click a face on any object, or the wall of a hole";

    private void RaiseAlignFace()
    {
        Raise(nameof(HasAlignFaceTarget));
        Raise(nameof(AlignFaceTargetLabel));
        AlignFaceChanged?.Invoke();
    }

    private void BeginAlignFace()
    {
        if (Scene.Selection.Count == 0) return;

        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsPivotMode = false;
        IsCentreFaceMode = false;

        alignFace = null;
        alignSurface = null;
        alignFacePicked = false;
        alignFaceTargetLabel = null;
        alignMeshes.Clear();

        // The axes are not cleared: the buttons in the panel stay as they were last left, and
        // clearing these behind them showed a choice that was not there and kept Apply greyed out.
        IsAlignFaceMode = true;
        RaiseAlignFace();
        Status = "Click a face on any object to align the selection against - or the wall of a hole, to line up with its middle";
    }

    /// <summary>
    /// Shows the face under the pointer before it is picked, so the surface about to be chosen
    /// is clear rather than a guess. Does nothing once a face has been picked - the marker then
    /// stays on what was chosen instead of following the pointer round the viewport.
    /// </summary>
    public void HoverAlignFace(SceneObject? target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isAlignFaceMode || alignFacePicked) return;

        alignFace = target is null ? null : SurfaceUnder(alignMeshes, target, worldPoint, worldNormal).Face;
        AlignFaceChanged?.Invoke();
    }

    /// <summary>
    /// Picks the face the selection will be lined up against - the face's own middle, not the
    /// point clicked on it, so a click near the corner of a wall and one in the middle of it
    /// align the same way. The wall of a hole is taken whole, round or with flat sides: its middle
    /// is the hole's, and a facet of it or one side of it would put the selection off to one side.
    /// </summary>
    public bool PickAlignFace(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isAlignFaceMode) return false;

        var (face, round) = SurfaceUnder(alignMeshes, target, worldPoint, worldNormal);
        if (face is null) return false;

        alignFace = face;
        alignSurface = round;
        alignFacePicked = true;
        alignFaceTargetLabel = round?.Describe(target.Name) ?? $"a face on {target.Name}";
        DefaultAlignFace(face, round);
        RaiseAlignFace();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        Status = $"Picked {alignFaceTargetLabel} - choose how X, Y and Z should line up, then Apply";
        return true;
    }

    /// <summary>
    /// What to do with a face just picked when nothing has been chosen: stand the selection against
    /// it - the side of it nearest the face on the way the face looks, and its middle on the face's
    /// middle across. With every axis left at Don't move, Apply had nothing to do and stayed grey
    /// however the face was picked, which read as the tool being broken.
    /// </summary>
    private void DefaultAlignFace(FacePatch face, RoundSurface? round = null)
    {
        if (alignFaceModeX is not null || alignFaceModeY is not null || alignFaceModeZ is not null) return;

        // A hole: its middle on the selection's middle across the axis, and along it left as it is,
        // so what is put in keeps its depth - as Center face to face does.
        if (round is not null)
        {
            var through = round.Axis;
            var lengthwise = MathF.Abs(through.X) >= MathF.Abs(through.Y) && MathF.Abs(through.X) >= MathF.Abs(through.Z) ? Axis.X
                           : MathF.Abs(through.Y) >= MathF.Abs(through.Z) ? Axis.Y : Axis.Z;

            var chosen = new List<string>();
            foreach (var a in new[] { Axis.X, Axis.Y, Axis.Z })
            {
                SetAlignFaceMode(a, a == lengthwise ? null : AlignMode.Centre);
                SetAlignFaceMode(a, AlignMode.Centre, ofSelection: true);
                chosen.Add(a == lengthwise ? $"{a}:None" : $"{a}:Centre");
                chosen.Add($"S{a}:Centre");
            }

            AlignFaceDefaulted?.Invoke(chosen);
            return;
        }

        var n = face.Normal;
        var axis = MathF.Abs(n.X) >= MathF.Abs(n.Y) && MathF.Abs(n.X) >= MathF.Abs(n.Z) ? Axis.X
                 : MathF.Abs(n.Y) >= MathF.Abs(n.Z) ? Axis.Y : Axis.Z;
        float along = axis == Axis.X ? n.X : axis == Axis.Y ? n.Y : n.Z;

        var tags = new List<string>();
        foreach (var a in new[] { Axis.X, Axis.Y, Axis.Z })
        {
            var from = a == axis ? (along >= 0 ? AlignMode.Minimum : AlignMode.Maximum) : AlignMode.Centre;
            SetAlignFaceMode(a, AlignMode.Centre);
            SetAlignFaceMode(a, from, ofSelection: true);
            tags.Add($"{a}:Centre");
            tags.Add($"S{a}:{from}");
        }

        AlignFaceDefaulted?.Invoke(tags);
    }

    /// <summary>Which way, if any, the selection's group box should line up on this axis with the picked face.</summary>
    /// <param name="ofSelection">The point of the selection, rather than the place on the face.</param>
    public void SetAlignFaceMode(Axis axis, AlignMode? mode, bool ofSelection = false)
    {
        if (ofSelection)
        {
            var from = mode ?? AlignMode.Centre;
            switch (axis)
            {
                case Axis.X: alignFromX = from; break;
                case Axis.Y: alignFromY = from; break;
                default: alignFromZ = from; break;
            }
        }
        else
        {
            switch (axis)
            {
                case Axis.X: alignFaceModeX = mode; break;
                case Axis.Y: alignFaceModeY = mode; break;
                default: alignFaceModeZ = mode; break;
            }
        }

        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        AlignFaceChanged?.Invoke();
    }

    /// <summary>
    /// Moves the whole selection together so its combined bounding box lines up, on whichever
    /// axes were asked for, with the middle of the picked face.
    ///
    /// Everything selected moves by the same offset, so the arrangement between the objects is
    /// kept - the point is to place the group against the face, not to line its members up
    /// against each other the way Align to does.
    /// </summary>
    private async Task ApplyAlignFace()
    {
        await NotifyApplyingAsync();

        if (!alignFacePicked || alignFace is not { } face) return;
        if (alignFaceModeX is null && alignFaceModeY is null && alignFaceModeZ is null) return;

        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var bounds = SelectionBox();
        if (bounds.IsEmpty) return;

        // The point of the selection chosen, onto the place on the face chosen, on each axis not
        // left alone. Every choice used to land on the face's middle, so the selection could not be
        // put flush with the face's edge, nor stood beside it.
        var to = AlignTools.PointOf(FaceBox(face), alignFaceModeX, alignFaceModeY, alignFaceModeZ);
        var from = AlignTools.PointOf(bounds, alignFromX, alignFromY, alignFromZ);
        var offset = new Vector3(
            alignFaceModeX is null ? 0 : to.X - from.X,
            alignFaceModeY is null ? 0 : to.Y - from.Y,
            alignFaceModeZ is null ? 0 : to.Z - from.Z);

        var before = selection.Select(TransformState.Capture).ToList();
        foreach (var o in selection) o.Position += offset;

        if (TransformCommand.CreateIfChanged("Align to face", selection, before) is { } command)
        {
            Undo.Execute(command);
            Status = $"Aligned {selection.Count} object(s) to the picked face";
        }
        else
        {
            Status = "Already aligned";
        }

        IsAlignFaceMode = false;
        RefreshSelection();
    }

    // --- Centre face to face ----------------------------------------------------------

    /// <summary>
    /// While this is on, a click picks one of two faces: on a selected object it sets the face
    /// that is about to move, on any other object the face it is aimed at. Apply then moves the
    /// whole selection, as one rigid group, so the two faces' middles coincide on whichever axes
    /// were asked for - the same idea as Align to face, but against a second real face instead of
    /// a point picked once.
    /// </summary>
    public bool IsCentreFaceMode
    {
        get => isCentreFaceMode;
        set
        {
            if (isCentreFaceMode == value) return;

            Set(ref isCentreFaceMode, value);
            if (!value)
            {
                centreFaceA = centreFaceB = null;
                centreRoundA = centreRoundB = null;
                centreFaceAPicked = centreFaceBPicked = false;
                centreFaceALabel = centreFaceBLabel = null;
                centreMeshes.Clear();
                RaiseCentreFace();
            }
            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
            Raise(nameof(ShowManipulatorBar));
        }
    }

    /// <summary>Raised whenever either picked or hovered face changes, so the viewport can redraw both markers.</summary>
    public event Action? CentreFaceChanged;

    public FacePatch? CentreFaceA => centreFaceA;

    /// <summary>
    /// The two points Centre face to face brings together: each face's middle, or a round one's
    /// point on its axis - which is the one worth seeing, being inside the part.
    /// </summary>
    public IReadOnlyList<Vector3> CentreFaceAnchors =>
        new[] { centreFaceA, centreFaceB }.OfType<FacePatch>().Select(f => RoundAt(f) ?? Middle(f)).ToList();

    /// <summary>A hovered round surface's axis point: the same patch as the last one found round, if it was.</summary>
    private Vector3? RoundAt(FacePatch patch) =>
        ReferenceEquals(centreRoundA?.Patch, patch) ? centreRoundA!.Centre
        : ReferenceEquals(centreRoundB?.Patch, patch) ? centreRoundB!.Centre
        : ReferenceEquals(centreHoverRound?.Patch, patch) ? centreHoverRound!.Centre : null;
    public FacePatch? CentreFaceB => centreFaceB;
    public bool HasCentreFaceA => centreFaceAPicked;
    public bool HasCentreFaceB => centreFaceBPicked;

    public string CentreFaceStatusLabel => (centreFaceAPicked, centreFaceBPicked) switch
    {
        (false, false) => "Click a face on the selected object(s) - a flat face, or a round one such as a pin's side, or the wall of a hole",
        (true, false) => $"Picked {Picked(centreRoundA, centreFaceALabel)} - now click a face on a different, unselected object: the wall of a hole to put it in",
        (false, true) => $"Picked {Picked(centreRoundB, centreFaceBLabel)} - now click a face on the selected object(s)",
        (true, true) => $"Picked {Picked(centreRoundA, centreFaceALabel)} against {Picked(centreRoundB, centreFaceBLabel)}"
                        + (CentreIsRound ? " - Apply puts them on one axis" : " - choose the axes, then Apply")
                        + Misfit()
    };

    private static string Picked(RoundSurface? round, string? on) => round is null
        ? $"a face on {on}"
        : round.Describe(on);

    /// <summary>A pin that will not go in, or will rattle, said before it is moved rather than found in print.</summary>
    private string Misfit()
    {
        if (centreRoundA is not { } a || centreRoundB is not { } b || a.IsHole == b.IsHole) return "";
        var (pin, hole) = a.IsHole ? (b, a) : (a, b);
        float gap = hole.Narrow - pin.Wide;
        return gap < 0 ? $". The pin is {-gap:0.##} mm too big for the hole."
            : $". {gap:0.##} mm between them across.";
    }

    /// <summary>Whether either face picked is round, when the axes are lined up rather than X, Y and Z matched.</summary>
    public bool CentreIsRound => centreRoundA is not null || centreRoundB is not null;

    public bool CentreIsFlat => !CentreIsRound;

    /// <summary>First turn the selection so the two axes run the same way - for a tilted pin.</summary>
    public bool CentreTurnParallel { get => centreTurnParallel; set => Set(ref centreTurnParallel, value); }

    /// <summary>Also slide it along the axis until the two middles meet, rather than leaving its height.</summary>
    public bool CentreAlongAxis { get => centreAlongAxis; set => Set(ref centreAlongAxis, value); }

    /// <summary>Whether this axis' offset should be applied at all - the other two keep their position.</summary>
    public bool CentreAxisX { get => centreAxisX; set => Set(ref centreAxisX, value); }
    public bool CentreAxisY { get => centreAxisY; set => Set(ref centreAxisY, value); }
    public bool CentreAxisZ { get => centreAxisZ; set => Set(ref centreAxisZ, value); }

    private void RaiseCentreFace()
    {
        Raise(nameof(HasCentreFaceA));
        Raise(nameof(HasCentreFaceB));
        Raise(nameof(CentreFaceStatusLabel));
        Raise(nameof(CentreIsRound));
        Raise(nameof(CentreIsFlat));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        CentreFaceChanged?.Invoke();
    }

    /// <summary>
    /// The face under a point: the whole of a round surface when it is on one - a flat facet of a
    /// pin would put its middle off the axis by the radius - otherwise the flat face.
    /// </summary>
    private (FacePatch? Face, RoundSurface? Round) CentreSurfaceAt(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        var found = SurfaceUnder(centreMeshes, target, worldPoint, worldNormal);
        if (found.Round is { } round) centreHoverRound = round;

        return found;
    }

    /// <summary>
    /// What is under a point for the tools that line a face up with another: the whole of a round
    /// surface, or of the walls of a hole with flat sides, when it is on one - a facet of a pin or one
    /// wall of an opening would put its middle off to one side - and otherwise the flat face.
    /// </summary>
    private static (FacePatch? Face, RoundSurface? Round) SurfaceUnder(
        Dictionary<SceneObject, Mesh> meshes, SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!meshes.TryGetValue(target, out var world))
            meshes[target] = world = target.ToWorldMesh();

        var surface = RoundSurface.Find(world, worldPoint, worldNormal)
                      ?? RoundSurface.FindOpening(world, worldPoint, worldNormal);

        return surface is null
            ? (FacePatch.Find(world, worldPoint, worldNormal), null)
            : (surface.Patch, surface);
    }

    private void BeginCentreFace()
    {
        if (Scene.Selection.Count == 0) return;

        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsPivotMode = false;
        IsAlignFaceMode = false;

        centreFaceA = centreFaceB = null;
        centreRoundA = centreRoundB = null;
        centreFaceAPicked = centreFaceBPicked = false;
        centreFaceALabel = centreFaceBLabel = null;
        centreAxisX = centreAxisY = centreAxisZ = true;
        centreMeshes.Clear();

        IsCentreFaceMode = true;
        RaiseCentreFace();
        Status = "Click a face on the selected object(s)";
    }

    /// <summary>
    /// Shows, on whichever of the two faces is not yet picked, the face under the pointer - on
    /// the selection for the first, on anything else for the second. Once a face is picked only a
    /// fresh click moves it, so the marker does not chase the pointer round the viewport after it
    /// has already said what it means.
    /// </summary>
    public void HoverCentreFace(SceneObject? target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isCentreFaceMode) return;

        bool onSelection = target is not null && target.IsSelected;

        if (!centreFaceAPicked && (target is null || onSelection))
        {
            centreFaceA = target is null ? null : CentreSurfaceAt(target, worldPoint, worldNormal).Face;
            CentreFaceChanged?.Invoke();
        }
        else if (!centreFaceBPicked && (target is null || !onSelection))
        {
            centreFaceB = target is null ? null : CentreSurfaceAt(target, worldPoint, worldNormal).Face;
            CentreFaceChanged?.Invoke();
        }
    }

    /// <summary>
    /// Picks a face: one on a selected object sets the first, on anything else the second -
    /// whichever it is, re-picking replaces what was there before rather than being refused.
    /// </summary>
    public bool PickCentreFace(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isCentreFaceMode) return false;

        var (face, round) = CentreSurfaceAt(target, worldPoint, worldNormal);
        if (face is null) return false;

        if (target.IsSelected)
        {
            centreFaceA = face;
            centreRoundA = round;
            centreFaceAPicked = true;
            centreFaceALabel = target.Name;
        }
        else
        {
            centreFaceB = face;
            centreRoundB = round;
            centreFaceBPicked = true;
            centreFaceBLabel = target.Name;
        }

        RaiseCentreFace();
        Status = CentreFaceStatusLabel;
        return true;
    }

    /// <summary>
    /// Moves the whole selection together so the two picked faces' middles coincide on whichever
    /// axes were asked for. The first face belongs to what is about to move, so it moves with
    /// everything else selected; the offset is worked out from where it started.
    /// </summary>
    private async Task ApplyCentreFace()
    {
        await NotifyApplyingAsync();

        if (!centreFaceAPicked || centreFaceA is not { } faceA) return;
        if (!centreFaceBPicked || centreFaceB is not { } faceB) return;
        if (!CentreIsRound && !centreAxisX && !centreAxisY && !centreAxisZ) return;

        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var centreA = centreRoundA?.Centre ?? faceA.ToLocal((faceA.Min + faceA.Max) * 0.5f);
        var centreB = centreRoundB?.Centre ?? faceB.ToLocal((faceB.Min + faceB.Max) * 0.5f);
        var before = selection.Select(TransformState.Capture).ToList();

        Vector3 offset;
        string warn = "";
        if (CentreIsRound)
        {
            // Which way each runs: a round face's axis, a flat face's normal.
            var runA = centreRoundA?.Axis ?? faceA.Normal;
            var runB = centreRoundB?.Axis ?? faceB.Normal;
            if (Vector3.Dot(runA, runB) < 0) runB = -runB;

            if (centreTurnParallel && runA != Vector3.Zero && runB != Vector3.Zero)
            {
                // Turned about the first face's middle, so that stays where it was.
                var turn = MeshTransform.TurnFromTo(runA, runB);
                foreach (var o in selection)
                {
                    o.Rotation = MeshTransform.EulerFrom(MeshTransform.Rotation(o.Rotation) * turn);
                    o.Position = centreA + Vector3.Transform(o.Position - centreA, turn);
                }
                runA = runB;
            }

            // Across the axis always; along it only when asked, so a pin keeps its height.
            var axis = centreRoundB?.Axis ?? centreRoundA?.Axis ?? runB;
            var d = centreB - centreA;
            var along = axis * Vector3.Dot(d, axis);
            offset = d - along + (centreAlongAxis ? along : Vector3.Zero);

            if (!centreTurnParallel && MathF.Abs(Vector3.Dot(runA, runB)) < 0.9998f)
                warn = " - the two axes are not parallel: tick Turn to parallel to stand it true";
        }
        else
        {
            offset = AlignTools.OffsetBetweenPoints(centreA, centreB, centreAxisX, centreAxisY, centreAxisZ);
        }

        foreach (var o in selection) o.Position += offset;

        if (TransformCommand.CreateIfChanged("Center face to face", selection, before) is { } command)
        {
            Undo.Execute(command);
            Status = CentreIsRound
                ? $"Put {selection.Count} object(s) on the other's axis{warn}"
                : $"Aligned {selection.Count} object(s) so the two faces' centers match";
        }
        else
        {
            Status = "Already aligned";
        }

        IsCentreFaceMode = false;
        RefreshSelection();
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
            RaiseToolInHand();
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
                embossProfile = null;
                embossWalls = null;
                embossRun = null;
                embossWallsTried = false;
                embossObject = null;
                KeepTexture();
                LeaveMasonry();
                embossPlacement = SurfacePlacement.Middle;
                letteringCache = null;
            }

            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
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

    /// <summary>
    /// Every font on the machine. A drop-down and not a box to type in: the typed name waited for
    /// the box to lose focus, so a font picked from the list and one half-typed in the box could
    /// disagree, and the scene showed whichever had last got through.
    /// </summary>
    public IReadOnlyList<string> EmbossFonts => installedFonts ??= GlyphOutlines.InstalledFonts();


    public float EmbossHeight
    {
        get => embossHeight;
        set { Set(ref embossHeight, Math.Clamp(value, 1f, 500f)); RefreshLettering(); }
    }

    /// <summary>How deep it is cut, or how far it stands proud.</summary>
    public float EmbossDepth
    {
        get => embossDepth;
        set { Set(ref embossDepth, Math.Clamp(value, 0.05f, 50f)); RefreshPicture(); RefreshEmboss(); }
    }

    public bool EmbossBold
    {
        get => embossBold;
        set { Set(ref embossBold, value); RefreshLettering(); }
    }

    public bool EmbossItalic
    {
        get => embossItalic;
        set { Set(ref embossItalic, value); RefreshLettering(); }
    }

    /// <summary>Extra room after every letter, in millimetres; negative draws them closer.</summary>
    public float EmbossSpacing
    {
        get => embossSpacing;
        set { Set(ref embossSpacing, Math.Clamp(float.IsFinite(value) ? value : 0f, -5f, 50f)); RefreshLettering(); }
    }

    /// <summary>Raised off the face rather than cut into it.</summary>
    public bool EmbossRaised
    {
        get => embossRaised;
        set { Set(ref embossRaised, value); RefreshEmboss(); }
    }

    /// <summary>
    /// Whether the lettering comes out as a part of its own rather than being made part of what
    /// it is on.
    ///
    /// For printing it in another filament. A slicer gives a material to a part, so text that has
    /// been unioned into the wall it sits on is the wall - there is nothing left to point at. Cut
    /// this way the object gets the recess and the letters come back as the plug that fills it,
    /// which is an inlay flush with the face; raised, the letters stand on it.
    /// </summary>
    /// <summary>How many copies of the stamp go across the face. One is no repeat at all.</summary>
    public int EmbossColumns
    {
        get => embossColumns;
        set { Set(ref embossColumns, Math.Clamp(value, 1, 200)); RefreshEmboss(); }
    }

    /// <summary>How many rows of it go up the face.</summary>
    public int EmbossRows
    {
        get => embossRows;
        set { Set(ref embossRows, Math.Clamp(value, 1, 200)); RefreshEmboss(); }
    }

    /// <summary>
    /// The distance left between one copy and the next, measured between their edges rather than
    /// their middles - so the spacing means the same thing whatever is being stamped.
    /// </summary>
    public float EmbossGap
    {
        get => embossGap;
        set { Set(ref embossGap, Math.Clamp(value, 0f, 500f)); RefreshEmboss(); }
    }

    /// <summary>
    /// Covers the face with as many as will fit at that gap, rather than the numbers asked for.
    /// The numbers are then what it worked out, and the summary says them.
    /// </summary>
    public bool EmbossFill
    {
        get => embossFill;
        set { Set(ref embossFill, value); RefreshEmboss(); }
    }

    public bool EmbossSeparate
    {
        get => embossSeparate;
        set { Set(ref embossSeparate, value); RefreshEmboss(); }
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
            if (value == TextProjection.Walls && embossFace is not null) Status = WallsStatus();
            Raise(nameof(EmbossTurns));

            // A texture is laid out to the room it has, and wrapping changes that room from one
            // facet to the whole way round. A word does not care, so this used only to refresh.
            RefreshLettering();
        }
    }

    /// <summary>
    /// The ways this can be laid on the object.
    ///
    /// A laid texture cannot go over a ball, and the reason is the sphere's rather than the
    /// texture's. Across is arc length on the ring a piece sits on, so a field a whole
    /// circumference wide laps itself away from the equator - twice round by sixty degrees of
    /// latitude - and the tiles pile on top of each other. Overlapping solids are the one thing
    /// the boolean cannot be handed. Fixing it means the surface saying how much room each course
    /// has and every course being clipped to its own ring, which is a good deal more than a ball
    /// of tiles is worth.
    ///
    /// Any texture goes round the walls; lettering does not, since a word is placed rather than
    /// laid over everything, and one round a corner is a word nobody can read from anywhere.
    /// </summary>
    public IReadOnlyList<TextProjection> EmbossProjections =>
        isMasonryMode ? [TextProjection.Walls]
        : !embossTexture.IsOn
            ? [TextProjection.Planar, TextProjection.Cylindrical, TextProjection.Spherical]
            : embossTexture.IsProfiled
                ? [TextProjection.Planar, TextProjection.Cylindrical, TextProjection.Walls]
                : [TextProjection.Planar, TextProjection.Cylindrical, TextProjection.Spherical, TextProjection.Walls];

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

            // Which redraws the preview as well as the summary, so a texture laid out to where it
            // starts comes back rebuilt rather than merely re-labelled.
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
    /// <summary>
    /// How much of the surface the thing being placed covers, as a half-size.
    ///
    /// Lettering is as big as its own outlines, and so is a flat texture - it is laid as outlines
    /// too, which is why the grip has always worked on brick. A laid texture has none at all: it
    /// is built as slabs, so its extent came to nothing, and the handles take an extent of nothing
    /// as nothing to place and hide themselves. That is why a roof could only be moved by typing
    /// in the two fields.
    ///
    /// So it is the face's own room instead, which is what a texture covers.
    /// </summary>
    public Vector2 EmbossExtent
    {
        get
        {
            if (embossTexture.IsLaid)
            {
                var (across, up) = FaceRoom();
                return new Vector2(across, up) * 0.5f;
            }

            // A field has no outlines to measure, so it is the patch it is built over - which is
            // what the box round it then stands for, and what dragging its corners resizes.
            if (embossTexture.IsProfiled)
            {
                var (wide, tall, _) = FieldExtent();
                return new Vector2(wide, tall) * 0.5f;
            }

            return SurfacePlacement.Extent(Lettering());
        }
    }

    /// <summary>
    /// The walls a texture goes round: the one picked, and those Ctrl+clicked after it. Found once
    /// per face picked, or null - with the reason in <see cref="embossWallsWhy"/> - when the part
    /// has no closed upright outline there, or the wall picked is not a flat one.
    /// </summary>
    private WallRun? Run()
    {
        if (embossRun is not null) return embossRun;
        if (embossFace is not { } face || embossMesh is not { } mesh || embossWallsTried) return null;

        embossWallsTried = true;
        embossWalls = WallLoop.Around(mesh, face, embossPick, out embossWallsWhy);
        if (embossWalls is not { } loop) return null;

        var facing = new Vector2(face.Normal.X, face.Normal.Y);
        int wall = loop.WallNear(new Vector2(embossPick.X, embossPick.Y), Vector2.Normalize(facing), out _);
        embossRun = wall < 0 ? null : WallRun.Of(loop, mesh, wall, out embossWallsWhy);
        return embossRun;
    }

    /// <summary>Which walls the texture goes round, and how to change that.</summary>
    private string WallsStatus() =>
        Run() is not { } run
            ? embossWallsWhy ?? "There are no walls to go round here."
            : run.Closed
                ? $"All {run.Count} walls, the whole way round. {EmbossSummary}"
                : $"{run.Count} of {run.Loop.Count} walls - Ctrl+click the next to carry it round the corner, "
                  + $"or one at either end to let it go. {EmbossSummary}";

    /// <summary>
    /// Ctrl+click while a texture goes round the walls: carries it on round the corner to take in
    /// the wall clicked, and any between - or lets go of one at either end. False when the click is
    /// not that at all, so it is taken as an ordinary pick instead.
    /// </summary>
    public bool AddEmbossWall(SceneObject target, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (!isEmbossMode || embossProjection != TextProjection.Walls || !ReferenceEquals(target, embossObject)) return false;
        if (Run() is not { } run || embossMesh is not { } world) return false;

        var facing = new Vector2(worldNormal.X, worldNormal.Y);
        float off = float.MaxValue;
        int wall = MathF.Abs(worldNormal.Z) < 0.2f && facing.LengthSquared() > 1e-6f
            ? run.Loop.WallNear(new Vector2(worldPoint.X, worldPoint.Y), Vector2.Normalize(facing), out off)
            : -1;

        // Half a millimetre off the outline is a wall set back or stood forward at another height
        // - a plinth, an upper storey - which the strip round this one cannot take in.
        if (wall < 0 || off > 0.5f)
        {
            Status = "That is not one of the walls round this part - Ctrl+click an upright side beside the texture.";
            return true;
        }

        var next = run.Holds(wall) ? run.Without(world, wall, out var why) : run.With(world, wall, out why);
        if (next is null)
        {
            Status = why ?? "That wall cannot be taken in.";
            return true;
        }

        embossRun = next;
        RefreshLettering();
        Status = WallsStatus();
        return true;
    }

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

                // Measured once per face picked: every drag of the handles asks for the surface.
                embossProfile ??= embossMesh is { } mesh ? SurfaceProfile.Build(mesh, axis) : null;

                // A single stamp centres on where it was clicked - that is the point of clicking.
                // Filling the face is asking for the whole barrel regardless of where the click
                // landed, so layout Y = 0 belongs at the part's own middle instead; anchoring it
                // to the click left the field as high or low as the click happened to be, with as
                // much of it run off the top as was left below.
                // A texture is the whole barrel by definition, so it centres on the part like a
                // filled field does. Anchored to the click it hung off whichever end was nearer
                // and left the other bare.
                float originZ = embossFill || embossTexture.IsOn ? embossBounds.Center.Z : embossPick.Z;

                return new CylinderSurface(
                    new Vector3(axis.X, axis.Y, originZ), radius,
                    MathF.Atan2(outward.Y, outward.X), embossProfile);
            }

            case TextProjection.Walls:
                return Run() is { } run ? new WallsSurface(run) : new PlanarSurface(face);

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
            if (isMasonryMode && (Built(embossTexture.Kind) || embossFace is null || Run() is null))
                return MasonrySummary();
            if (embossFace is null) return "Click the face to letter.";

            if (SurfaceTexture.CoursesOf(embossTexture, embossDepth) is { } courses)
            {
                // With the room, so the figure counts what will actually be laid: a course that
                // runs across a window is broken at the reveal and comes to two pieces, not one.
                var (wide, tall, room) = TileField(EmbossSurface(), !embossRaised);
                int slabs = TileSolid.Pieces(
                    courses, wide, tall, room, !embossRaised, embossPlacement.OffsetMm).Count;

                if (slabs == 0) return "The face is smaller than one course of this - try a finer pitch.";

                return $"{TextureOptions.NameOf(embossTexture.Kind).ToLowerInvariant()}, {slabs:N0} pieces, "
                     + $"{TileSolid.ReliefOf(courses):0.##} mm of relief"
                     + $" - about {slabs * 12:N0} triangles";
            }

            if (Profile(coarse: false) is { } relief)
            {
                var (wide, tall, round) = FieldExtent();
                var cost = ReliefField.Cost(relief, MathF.Max(wide, 0.01f), MathF.Max(tall, 0.01f), round);

                if (cost.Refusal is { } why) return why;

                string heavy = cost.IsHeavy
                    ? " - heavy. Simplify afterwards, or use a coarser pitch."
                    : "";

                return $"{TextureOptions.NameOf(embossTexture.Kind).ToLowerInvariant()}, "
                     + $"{embossDepth:0.##} mm of relief, about {cost.Triangles:N0} triangles{heavy}";
            }

            var shapes = EmbossShapes();
            if (shapes.Count == 0) return "Nothing to letter - type something.";

            string what = embossRaised
                ? $"raised {embossDepth:0.##} mm"
                : $"cut {embossDepth:0.##} mm deep";

            string wrapped = embossProjection == TextProjection.Planar
                ? ""
                : $", {embossProjection.ToString().ToLowerInvariant()}";

            string bevel = embossBevel > 0 ? $", {embossBevel:0.##} mm bevel" : "";

            var (across, up) = EmbossField();
            string field = across > 1 || up > 1
                ? $"{across} x {up}, {embossGap:0.##} mm apart, "
                : "";

            return $"{field}{shapes.Count} shape(s), {embossHeight:0.#} mm tall, {what}{wrapped}{bevel}";
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
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        IsLayMode = true;

        Status = "Click the face you want it to stand on";
        FindRestingFaces(Scene.Selection.First());
    }

    private List<RestingFace> restingFaces = [];
    private SceneObject? restingTarget;
    private int restingHover = -1;
    private int restingRun;

    /// <summary>The faces the object being laid can stand on, for the viewport to show.</summary>
    public IReadOnlyList<RestingFace> RestingFaceList => restingFaces;

    /// <summary>Which of them is under the pointer, or -1.</summary>
    public int RestingHover => restingHover;

    public event Action? RestingFacesChanged;

    /// <summary>
    /// Works out the faces to offer, away from the window: the hull of a dense scan is a moment's
    /// work, and the mode is usable before it is done - a click on the model lays it as it always did.
    /// </summary>
    private async void FindRestingFaces(SceneObject target)
    {
        int run = ++restingRun;
        var world = target.ToWorldMesh();

        List<RestingFace> found;
        try
        {
            found = await Task.Run(() => RestingFaces.Find(world));
        }
        catch
        {
            // The faces are an aid to picking, not the tool itself, which works without them.
            return;
        }

        if (run != restingRun || !isLayMode) return;

        restingFaces = found;
        restingTarget = target;
        restingHover = -1;
        RestingFacesChanged?.Invoke();

        if (found.Count > 0)
            Status = $"Click a face to stand it on - the {found.Count} it can rest on are marked";
    }

    private void ForgetRestingFaces()
    {
        restingRun++;
        restingTarget = null;
        restingHover = -1;
        if (restingFaces.Count == 0) return;

        restingFaces = [];
        RestingFacesChanged?.Invoke();
    }

    /// <summary>Marks the offered face along a line of sight, redrawing only when that changes.</summary>
    public void HoverRestingFace(Vector3 origin, Vector3 direction)
    {
        int under = restingFaces.Count == 0 ? -1 : RestingFaces.Under(restingFaces, origin, direction);
        if (under == restingHover) return;

        restingHover = under;
        RestingFacesChanged?.Invoke();
    }

    /// <summary>
    /// Lays the object on the offered face along a line of sight, if there is one. Asked before the
    /// model is: a cup's mouth is a face to stand it on with nothing of the model there to click.
    /// </summary>
    public bool LayOnRestingFace(Vector3 origin, Vector3 direction)
    {
        if (!isLayMode || restingTarget is null || restingFaces.Count == 0) return false;

        int under = RestingFaces.Under(restingFaces, origin, direction);
        if (under < 0) return false;

        Lay(restingTarget, restingFaces[under].Normal);
        return true;
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

        Lay(target, facing);
        return true;
    }

    /// <summary>Turns <paramref name="facing"/>, an outward direction in world space, to point straight down.</summary>
    private void Lay(SceneObject target, Vector3 facing, string? said = null, string label = "Lay on face")
    {
        var before = new[] { TransformState.Capture(target) };

        var turn = MeshTransform.TurnFromTo(facing, -Vector3.UnitZ);
        target.Rotation = MeshTransform.EulerFrom(MeshTransform.Rotation(target.Rotation) * turn);

        // Tipping it over will have left it through the bed or above it.
        target.Position = target.Position with { Z = target.Position.Z - target.WorldBounds.Min.Z };

        if (TransformCommand.CreateIfChanged(label, [target], before) is { } command)
            Undo.Execute(command);

        IsLayMode = false;
        RefreshSelection();
        Status = said ?? $"{target.Name} laid on the picked face";
    }

    /// <summary>
    /// Stands the part on <paramref name="facing"/> from where it was, recording nothing.
    ///
    /// The preview behind Best face down, and the same arithmetic <see cref="Lay"/> commits. It
    /// works from a captured state rather than from where the object is now, so that hovering one
    /// face after another does not compound: every turn is measured from where the part started.
    /// </summary>
    private static void TurnOnto(SceneObject target, TransformState from, Vector3 facing)
    {
        var turn = MeshTransform.TurnFromTo(facing, -Vector3.UnitZ);
        target.Rotation = MeshTransform.EulerFrom(MeshTransform.Rotation(from.Rotation) * turn);

        // Measured after the turn, and from the starting position, since tipping it over will
        // have left it through the bed or above it.
        target.Position = from.Position;
        target.Position = from.Position with { Z = from.Position.Z - target.WorldBounds.Min.Z };
    }

    /// <summary>
    /// Works out every way up the part could be printed, and what each would cost.
    ///
    /// Lay on face answers "stand it on this one". This answers the question before it, which is
    /// the one people actually have: which face. The parts were all here already - the hull's
    /// resting faces, the overhang measure, the turn itself - and what was missing was only
    /// putting a number against each and saying them in an order.
    ///
    /// Nothing is decided for the user. Support, height and footing disagree on plenty of parts,
    /// and which is short - filament, time or nerve - is not something this can know.
    /// </summary>
    private async Task BestFaceDown()
    {
        if (Scene.Selection.Count != 1 || IsBusy) return;

        var target = Scene.Selection.First();
        var world = target.ToWorldMesh();
        float angle = overhangAngle;

        var token = StartWork($"Weighing up {target.Name}");
        List<StandingChoice> choices;
        try
        {
            choices = await Task.Run(() => BestFace.Rank(world, RestingFaces.Find(world), angle, token: token), token);
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = "Best face down stopped - nothing was changed";
            return;
        }
        catch (Exception ex)
        {
            Status = $"Best face down failed: {ex.Message}";
            return;
        }
        finally
        {
            EndWork();
        }

        if (choices.Count == 0)
        {
            Status = $"{target.Name} has no face it would stay on - its weight falls outside every one";
            return;
        }

        var before = TransformState.Capture(target);

        // The pointer leaving the row comes after the click that closed the panel - the row goes
        // from under it - and putting the part back then undid the turn just made.
        bool settled = false;
        var panel = new BestFaceDialog(target.Name, choices, choice =>
        {
            if (settled) return;
            if (choice is null) before.ApplyTo(target);
            else TurnOnto(target, before, choice.Normal);

            RefreshSelection();
        });

        bool accepted = panel.ShowDialog() == true && panel.Result is not null;
        settled = true;

        // Whatever the hovering left on the plate, the undo step has to start from where the user
        // did - the same rule the smoothing preview follows.
        before.ApplyTo(target);
        RefreshSelection();

        if (!accepted)
        {
            Status = "Best face down canceled";
            return;
        }

        var picked = panel.Result!;
        string cost = picked.SupportMm2 < 1.0
            ? "nothing to support"
            : $"{picked.SupportMm2 / 100.0:0.#} cm² to support";

        Lay(target, picked.Normal,
            said: $"{target.Name} stood on its best face - {cost}, {picked.HeightMm:0.#} mm tall",
            label: "Best face down");
    }

    private void BeginEmboss()
    {
        if (Scene.Selection.Count != 1) return;

        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        LeaveMasonry();
        KeepTexture();
        IsEmbossMode = true;

        // Lettering. A texture is the Texture tool's, and is put back when that is picked up.
        EmbossTexture = TextureKind.None;

        Status = "Click the face you want to letter";
    }

    /// <summary>
    /// A texture over a face, flat or wrapped round. It is the Emboss tool underneath - the same
    /// panel, picking, preview and Apply - with a texture in hand instead of lettering, starting
    /// where it was last left this session.
    /// </summary>
    private void BeginTexture()
    {
        if (Scene.Selection.Count != 1) return;

        BeginEmboss();
        var (texture, depth) = textureKept ?? (TextureOptions.Default, embossDepth);
        EmbossTexture = texture.Kind;
        SetTexture(texture);
        EmbossDepth = depth;

        Status = "Click the face you want to texture";
    }

    /// <summary>Remembers the Texture tool's texture for the next time it is picked up this session.</summary>
    private void KeepTexture()
    {
        if (!isMasonryMode && embossTexture.IsOn) textureKept = (embossTexture, embossDepth);
    }

    /// <summary>Whether the Emboss panel is the Texture tool's: a texture in hand, and not Masonry.</summary>
    public bool IsTextureTool => !isMasonryMode && embossTexture.IsOn;

    /// <summary>Whether the panel offers a choice of texture: the Texture tool's and Masonry's do, lettering's does not.</summary>
    public bool ChoosesTexture => isMasonryMode || embossTexture.IsOn;

    /// <summary>Whether a drawing can be stamped instead of lettering - the Emboss tool's alone.</summary>
    public bool ShowsDrawing => !isMasonryMode && !embossTexture.IsOn;

    /// <summary>
    /// A texture that Masonry lays as walls are built rather than as a pattern - with corner
    /// bricks, quoins or corner posts - so the Texture tool says so.
    /// </summary>
    public bool SuggestsMasonry => IsTextureTool && embossTexture.Kind
        is TextureKind.Brick or TextureKind.Rubble or TextureKind.CoursedStone or TextureKind.Siding;

    /// <summary>
    /// Brickwork round the walls picked. It is the Emboss tool underneath - the same picking, the
    /// same Ctrl+click round the corners, the same preview and the same Apply - with the bricks laid
    /// as a bricklayer lays them in place of a pattern laid over the walls. See <see cref="BrickBond"/>.
    /// What Emboss had in hand is put back when the tool is put down.
    /// </summary>
    private void BeginMasonry()
    {
        if (Scene.Selection.Count != 1) return;

        BeginEmboss();
        beforeMasonry = (embossTexture, embossProjection, embossRaised, embossDepth);
        isMasonryMode = true;

        EmbossTexture = masonryKind;
        TakeMasonry(masonryKind);
        EmbossProjection = TextProjection.Walls;
        EmbossRaised = true;
        RaiseMasonry();

        Status = "Click a wall, then Ctrl+click the walls either side to carry the walling round the corners";
    }

    private void LeaveMasonry()
    {
        if (!isMasonryMode) return;

        KeepMasonry();
        masonryKind = embossTexture.Kind;
        isMasonryMode = false;

        if (beforeMasonry is { } was)
        {
            EmbossTexture = was.Texture.Kind;
            SetTexture(was.Texture);
            EmbossProjection = was.Projection;
            EmbossRaised = was.Raised;
            EmbossDepth = was.Depth;
        }

        beforeMasonry = null;
        RaiseMasonry();
    }

    /// <summary>Remembers the walling in hand, for the next time it is picked this session.</summary>
    private void KeepMasonry() => masonryKept[embossTexture.Kind] = (embossTexture, embossDepth);

    /// <summary>Takes up a kind of walling where it was last left, or at its first numbers.</summary>
    private void TakeMasonry(TextureKind kind)
    {
        if (!masonryKept.TryGetValue(kind, out var kept)) return;

        SetTexture(kept.Texture);
        EmbossDepth = kept.Depth;
    }

    private void RaiseMasonry()
    {
        RaiseTool();
        Raise(nameof(IsMasonryMode));
        Raise(nameof(NotMasonry));
        Raise(nameof(EmbossTitle));
        Raise(nameof(EmbossTextures));
        Raise(nameof(EmbossProjections));
        Raise(nameof(EmbossTurns));
        RefreshLettering();
    }

    /// <summary>Whether the Emboss panel is laying brickwork - see <see cref="BeginMasonry"/>.</summary>
    public bool IsMasonryMode => isMasonryMode;

    /// <summary>For the rows of the panel that brickwork has no use for.</summary>
    public bool NotMasonry => !isMasonryMode;

    public string EmbossTitle => isMasonryMode ? "Cladding and siding" : embossTexture.IsOn ? "Texture" : "Emboss";

    /// <summary>The rows of the panel that follow which of the three tools it is.</summary>
    private void RaiseTool()
    {
        Raise(nameof(EmbossTitle));
        Raise(nameof(IsTextureTool));
        Raise(nameof(ChoosesTexture));
        Raise(nameof(ShowsDrawing));
        Raise(nameof(SuggestsMasonry));
    }

    /// <summary>The walling Masonry builds piece by piece rather than as a field: bricks and logs.</summary>
    private static bool Built(TextureKind kind) => kind is TextureKind.Brick or TextureKind.Logs;

    /// <summary>The logs round the walls picked, or null while there are none to lay.</summary>
    private Mesh? Logwork(IPlacementSurface surface, out int logs)
    {
        logs = 0;
        if (surface is not WallsSurface walls || BrickBond.Refusal(walls.Run) is not null) return null;

        var o = embossTexture.Sane();
        var laid = LogWalls.Build(walls.Run, o.PitchMm, o.LineMm, embossDepth, out logs);
        return laid.TriangleCount == 0 ? null : laid;
    }

    /// <summary>Siding with its corner posts joined over the ends of the boards - see <see cref="CornerPosts"/>.</summary>
    private static Mesh WithPosts(Mesh siding, WallRun run, in TileCourses courses)
    {
        var posts = CornerPosts.Build(run, courses.CourseMm, TileSolid.ReliefOf(courses), out int count);
        if (count == 0) return siding;

        return ManifoldCsg.Union(siding, posts) is { TriangleCount: > 0 } joined && joined.CheckHealth().IsWatertight
            ? joined
            : Mesh.Combine([siding, posts]);
    }

    /// <summary>
    /// How far what is laid stands off the walls at the most: the depth, or more for what Masonry
    /// builds round the corners - the end of a log past the corner, a post over the boards. It is
    /// how far out a raised texture is kept to its walls, and a log end trimmed to the depth is cut
    /// off short of its own end.
    /// </summary>
    private float Proud(IPlacementSurface surface)
    {
        if (!isMasonryMode || surface is not WallsSurface walls) return embossDepth;

        return embossTexture.Kind switch
        {
            TextureKind.Logs => LogWalls.Reach(walls.Run, embossTexture.Sane().PitchMm, embossDepth),
            TextureKind.Siding when SurfaceTexture.CoursesOf(embossTexture, embossDepth) is { } courses =>
                MathF.Max(embossDepth, CornerPosts.ProudFor(TileSolid.ReliefOf(courses))),
            _ => embossDepth
        };
    }

    /// <summary>The bricks round the walls picked, or null while there are none to lay.</summary>
    private Mesh? Brickwork(IPlacementSurface surface, out int bricks)
    {
        bricks = 0;
        if (surface is not WallsSurface walls || BrickBond.Refusal(walls.Run) is not null) return null;

        var o = embossTexture.Sane();
        var laid = BrickBond.Build(walls.Run, o.PitchMm, o.Courses, o.LineMm, embossDepth, out bricks);
        return laid.TriangleCount == 0 ? null : laid;
    }

    private string MasonrySummary()
    {
        if (embossFace is null) return "Click a wall to build on.";
        if (Run() is not { } run) return embossWallsWhy ?? "There are no walls to build on here.";
        if (BrickBond.Refusal(run) is { } why) return why;

        // Logs are counted rather than laid: laying them joins every crossing at the corners.
        if (embossTexture.Kind == TextureKind.Logs)
        {
            int logs = LogWalls.Count(run, embossTexture.Sane().PitchMm);
            return logs == 0
                ? "The walls are lower than one log - try thinner logs."
                : $"{logs:N0} logs, {embossDepth:0.##} mm proud.";
        }

        return Brickwork(new WallsSurface(run), out int bricks) is null
            ? "The walls are lower than one course - try a smaller brick."
            : $"{bricks:N0} bricks, {embossDepth:0.##} mm proud.";
    }

    /// <summary>
    /// Adds a shaped texture to the object: the profile built as a solid and joined on.
    ///
    /// One boolean with one connected solid, rather than the several hundred separate pads a flat
    /// texture comes to - which is what makes a wall of siding cheaper to apply than a word is.
    /// Cut instead of raised takes the same solid the other way and sinks the shape into the face,
    /// which is the view from inside the mould and is occasionally what somebody wants.
    /// </summary>
    private async Task ApplyProfile(Mesh world, IPlacementSurface surface)
    {
        var source = Scene.Selection[0];
        bool raised = embossRaised;

        if (IsBusy) return;

        var token = StartWork(raised ? "Laying the texture on" : "Cutting the texture in");
        try
        {
            // Raised, kept off any window or doorway already cut in the face. See TextCutter.OnTheFace.
            var built = await Task.Run(() =>
            {
                var solid = ProfiledSolid(surface, coarse: false, sunk: !raised);
                return raised && solid is not null ? KeptToTheWall(solid, surface, token) : solid;
            }, token);

            if (built is null || built.TriangleCount == 0)
            {
                if (isMasonryMode && Built(embossTexture.Kind))
                {
                    Status = MasonrySummary();
                    return;
                }

                // Whatever the panel would have said, said here too, so the reason lands in the one
                // place somebody looks after pressing a button that appeared to do nothing.
                var (wide, tall, round) = FieldExtent();
                var cost = !embossTexture.IsLaid && Profile(coarse: false) is { } relief
                    ? ReliefField.Cost(relief, MathF.Max(wide, 0.01f), MathF.Max(tall, 0.01f), round)
                    : default;

                Status = cost.Refusal
                    ?? "The face is smaller than one course of this - try a finer pitch";
                return;
            }

            var joined = await Task.Run(
                () => raised ? LocalCsg.Union(world, built, token) : LocalCsg.Subtract(world, built, token),
                token);

            if (!joined.CheckHealth().IsWatertight)
            {
                Status = $"The texture came out unprintable - {joined.CheckHealth().Describe()}. "
                       + "Nothing was changed.";
                MessageBox.Show(
                    "The texture could not be added cleanly, so the object has been left as it "
                    + "was." + Environment.NewLine + Environment.NewLine + WayRound(),
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var textured = new SceneObject(source.Name, joined)
            {
                Colour = source.Colour,
                Filament = source.Filament
            }.Centred();

            string? walling = !isMasonryMode ? null : embossTexture.Kind switch
            {
                TextureKind.Brick => "brickwork",
                TextureKind.Siding => "siding",
                TextureKind.Logs => "log walls",
                _ => $"{TextureOptions.NameOf(embossTexture.Kind).ToLowerInvariant()} walling"
            };
            Undo.Execute(new ReplaceObjectsCommand(
                walling is not null ? "Lay walling" : raised ? "Lay texture" : "Cut texture", [source], [textured]));

            IsEmbossMode = false;
            RefreshSelection();

            Status = $"{walling ?? TextureOptions.NameOf(embossTexture.Kind).ToLowerInvariant()} on {source.Name}"
                   + $" - {built.TriangleCount:N0} triangles of texture,"
                   + $" {joined.TriangleCount:N0} in all";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"The texture failed: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Letters the object and keeps the lettering as a second part, ready to be given its own
    /// filament.
    ///
    /// The two are put on different filaments straight away - the body stays on whatever it was,
    /// the letters go to the next one up - because a part made for a second material and left on
    /// the first is a part nobody notices is wrong until it comes off the printer one colour. The
    /// colour follows, so the letters can be seen on the model rather than being found by name.
    /// </summary>
    private async Task LetterAsAPart(
        SceneObject source, Mesh world, IReadOnlyList<TextShape> shapes, IPlacementSurface surface,
        bool raised, float amount, float bevel, CancellationToken token)
    {
        var made = await Task.Run(
            () => TextCutter.Separate(world, shapes, surface, raised, amount, bevel, token));

        if (made is not { } parts || parts.Lettering.TriangleCount == 0)
        {
            Status = "The lettering produced no geometry";
            return;
        }

        if (!parts.Body.CheckHealth().IsWatertight || !parts.Lettering.CheckHealth().IsWatertight)
        {
            Status = "Lettering that face came out unprintable. Nothing was changed.";
            MessageBox.Show(
                "The lettering could not be made cleanly, so the object has been left as it was."
                + Environment.NewLine + Environment.NewLine + WayRound(),
                "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int next = Math.Min(source.Filament + 1, SceneObject.MostFilaments);

        var body = new SceneObject(source.Name, parts.Body)
        {
            Colour = source.Colour,
            Filament = source.Filament
        }.Centred();

        var letters = new SceneObject(Scene.UniqueName($"{source.Name} text"), parts.Lettering)
        {
            Colour = Contrasting(source.Colour),
            Filament = next
        }.Centred();

        Undo.Execute(new ReplaceObjectsCommand(
            raised ? "Raise text as a part" : "Inlay text", [source], [body, letters]));

        IsEmbossMode = false;
        Scene.SelectOnly(letters);
        RefreshSelection();

        Status = $"{(raised ? "Raised" : "Inlaid")} {Stamped()} as its own part, on filament {next}"
               + $" - {parts.Lettering.TriangleCount:N0} triangles";
    }

    /// <summary>
    /// Something that will be seen against the given colour: ink on light, paper on dark.
    ///
    /// The complement was the obvious answer and is the wrong one - mid grey is its own
    /// complement, and half the palette is near enough mid grey that the lettering came out
    /// invisible on exactly the models where it mattered.
    /// </summary>
    private static Vector3 Contrasting(Vector3 colour) =>
        0.299f * colour.X + 0.587f * colour.Y + 0.114f * colour.Z > 0.5f
            ? new Vector3(0.13f, 0.14f, 0.16f)
            : new Vector3(0.94f, 0.92f, 0.87f);

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
        if (!SameFace(embossFace, face))
        {
            embossPlacement = SurfacePlacement.Middle;
            embossTextureArea = null;
        }

        embossMesh = world;
        embossProfile = null;
        embossWalls = null;
        embossRun = null;
        embossWallsTried = false;
        embossObject = target;
        embossFace = face;
        embossPick = worldPoint;
        embossBounds = world.ComputeBounds();
        letteringCache = null;

        Raise(nameof(HasEmbossFace));
        Raise(nameof(EmbossAcross));
        Raise(nameof(EmbossUp));
        Raise(nameof(EmbossAngle));
        Raise(nameof(DrawingShapes));
        RefreshEmboss();
        Status = embossProjection == TextProjection.Walls ? WallsStatus() : EmbossSummary;
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
        // Read without a face: the preview in the panel is how anyone sees the file came in right,
        // or which font is being used, and that is wanted before a face has been picked. Waiting
        // for the face left the preview blank and said a good drawing had no filled shape in it.
        if (letteringCache is not null) return letteringCache;

        if (embossTexture.IsOn) return letteringCache = Textured();

        if (svgFile.Length > 0)
        {
            try
            {
                if (PictureReader.Reads(svgFile))
                    return letteringCache = PictureTrace.Outlines(EmbossInk(), embossThreshold, embossHeight);

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
            .Build(embossText, embossFont, embossHeight, embossBold, embossItalic, embossSpacing)
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
            Filter = "Drawings and pictures (*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.svg;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff"
                     + "|Drawing (*.svg)|*.svg|" + PictureReader.Filter,
            Title = "Stamp a drawing, or the dark parts of a picture"
        };
        if (dialog.ShowDialog() != true) return;

        svgFile = dialog.FileName;

        // A picture is split into ink and none where it best divides, and can be moved from there.
        if (PictureReader.Reads(svgFile))
        {
            embossInvert = false;
            try
            {
                embossThreshold = PictureTrace.Threshold(EmbossInk());
            }
            catch (Exception ex)
            {
                Status = $"Could not read {SvgName}: {ex.Message}";
                svgFile = "";
            }
            Raise(nameof(EmbossThreshold));
            Raise(nameof(EmbossInvert));
        }

        // A drawing and a texture are two answers to the same question, and the texture is asked
        // first, so leaving it on would swallow the file that was just chosen.
        if (embossTexture.IsOn) EmbossTexture = TextureKind.None;

        RefreshDrawing();

        if (svgFile.Length > 0 && Lettering().Count == 0)
            Status = $"{SvgName} has no filled shape in it - lines on their own have no area to stamp.";
    }

    private void RefreshDrawing()
    {
        // Emptied before anything asks for the shapes: the preview is raised first, and it was
        // drawing whatever the cache still held - the typed TEXT, not the drawing just loaded.
        letteringCache = null;
        Raise(nameof(HasDrawing));
        Raise(nameof(IsPictureDrawing));
        Raise(nameof(UsesText));
        Raise(nameof(SvgName));
        Raise(nameof(DrawingShapes));
        RefreshLettering();
    }

    /// <summary>
    /// How much room a texture has to fill, which is not the question a word asks.
    ///
    /// A word is a stamp: laid out at its own size and then placed. A texture is the field itself
    /// and has to be told how far it runs before it can be laid out at all. On a flat face that is
    /// the face, less the strip the retiling keeps to hold the face's own outline together;
    /// wrapped, it is the whole way round the part.
    ///
    /// Before a face is picked there is no such figure and the panel still wants to show what the
    /// pattern looks like, so a patch big enough to show several cells stands in.
    /// </summary>
    private List<TextShape> Textured()
    {
        const float PatchMm = 24f;

        var (across, up) = FaceRoom();
        bool wrapped = embossProjection != TextProjection.Planar;

        // Round some of the walls the strip has two ends, as a face has, and keeps clear of them
        // as a face does; round all of them it closes on itself, as round a barrel.
        bool seamless = embossProjection == TextProjection.Walls ? Run() is { Closed: true } : wrapped;

        // An area set by dragging the corners of its box, which can run past the face's edges:
        // raised, what is past them is trimmed off; cut, it runs off the edge as a groove should.
        if (!wrapped && embossTextureArea is { } set && across > 0.01f)
            return SurfaceTexture.Over(set.X, set.Y, embossTexture, wrapped);

        if (across <= 0.01f || up <= 0.01f)
        {
            (across, up) = (PatchMm, PatchMm);
        }
        else if (!seamless)
        {
            // Twice the strip the retiling needs, so the field is clear of it either side and the
            // fast path is not thrown away over a hundredth of a millimetre.
            float margin = Engraver.RaisedInset * 4f;
            across = MathF.Max(across - margin, 0.01f);
            up = MathF.Max(up - margin, 0.01f);
        }

        return SurfaceTexture.Over(across, up, embossTexture, seamless);
    }

    /// <summary>The area a flat texture covers, when its corners have been dragged; null is the whole face.</summary>
    private Vector2? embossTextureArea;

    /// <summary>
    /// A corner of the box on the face dragged and let go. Lettering, a drawing or a picture grows
    /// in proportion; a texture keeps its cells the size they are and covers a different area, as
    /// wide and as tall as the box was made, so a corner brick can be brought to the corner of the face.
    /// </summary>
    public void ResizeEmbossBy(Vector2 factor)
    {
        if (!embossTexture.IsOn)
        {
            EmbossHeight *= factor.X;
            return;
        }

        if (embossProjection != TextProjection.Planar) return;

        var size = EmbossExtent * 2f;
        embossTextureArea = new Vector2(MathF.Max(size.X * factor.X, 1f), MathF.Max(size.Y * factor.Y, 1f));
        RefreshLettering();
    }

    /// <summary>
    /// Which texture is being laid on, or None while a word or a drawing is being stamped.
    ///
    /// A texture, a drawing and typed letters are three answers to one question, so choosing any
    /// of them puts the others down.
    /// </summary>
    public TextureKind EmbossTexture
    {
        get => embossTexture.Kind;
        set
        {
            if (embossTexture.Kind == value) return;

            if (isMasonryMode) KeepMasonry();

            // Siding starts with no groove between its boards. The rest keep what was typed, raised to
            // the least the new texture allows: a groove of nought on a knurl is no knurl.
            embossTexture = embossTexture with
            {
                Kind = value,
                LineMm = value == TextureKind.Siding
                    ? 0f
                    : MathF.Max(embossTexture.LineMm, TextureOptions.LeastLineOf(value))
            };
            if (value != TextureKind.None) svgFile = "";

            // Brickwork arrives raised. Cutting the pieces sinks the bricks and leaves the mortar
            // standing, which is a wall seen from inside its own mould and is not what anybody
            // picking "Brick" is after; raising them is the same relief the right way round, and it
            // is also the one case that never reaches the boolean at all on a flat face.
            if (embossTexture.IsMasonry && !embossRaised) EmbossRaised = true;

            // Said rather than silently corrected. The wrap is a setting somebody chose, and a
            // combo box quietly changing under them while the model does not is worse than being
            // told which way it went.
            if (!embossTexture.IsOn && embossProjection == TextProjection.Walls)
            {
                EmbossProjection = TextProjection.Planar;
                Status = "Lettering is laid on the face - only a texture goes round the walls.";
            }

            if (embossTexture.IsProfiled && embossProjection == TextProjection.Spherical)
            {
                EmbossProjection = TextProjection.Cylindrical;
                Status = $"{TextureOptions.NameOf(value).ToLowerInvariant()} cannot be wrapped over a ball - "
                       + "the courses lap themselves away from the equator. Wrapped round instead.";
            }

            Raise(nameof(EmbossTexture));
            Raise(nameof(UsesTexture));
            Raise(nameof(UsesProfile));
            Raise(nameof(EmbossTextureLine));
            Raise(nameof(ShowsTexturePictures));
            Raise(nameof(ShowsOutlinePreview));
            Raise(nameof(UsesStamp));
            Raise(nameof(TextureLeans));
            Raise(nameof(TextureRuns));
            Raise(nameof(TextureHasCourses));
            Raise(nameof(TextureTilts)); Raise(nameof(EmbossTurns));
            Raise(nameof(EmbossTextureAspect));
            Raise(nameof(EmbossProjections));
            RefreshDrawing();

            if (isMasonryMode) TakeMasonry(value);
            RaiseTool();
        }
    }

    public IReadOnlyList<TextureKind> EmbossTextures => isMasonryMode ? MasonryTextures : AllTextures;

    /// <summary>
    /// What Masonry lays: bricks with corner bricks, stone walling with quoins, siding with corner
    /// posts and log walls crossing at the corners.
    /// </summary>
    private static readonly IReadOnlyList<TextureKind> MasonryTextures =
        [TextureKind.Brick, TextureKind.Rubble, TextureKind.CoursedStone, TextureKind.Siding, TextureKind.Logs];

    /// <summary>What the Texture tool lays. Lettering, which is no texture, is the Emboss tool's.</summary>
    private static readonly IReadOnlyList<TextureKind> AllTextures =
    [
        TextureKind.Knurl, TextureKind.Ribs,
        TextureKind.Hex, TextureKind.Dots, TextureKind.Tread,
        TextureKind.Brick, TextureKind.RoofTiles, TextureKind.Tiles, TextureKind.Planks,
        TextureKind.Siding, TextureKind.Rubble, TextureKind.CoursedStone, TextureKind.Bark, TextureKind.Grain
    ];

    /// <summary>
    /// The profile behind a shaped texture, or null while a flat one is being laid.
    ///
    /// Siding, roof tiles and boarding are a height over the face rather than a set of outlines -
    /// a lap, a slope and a grain cannot be said any other way - so they leave the outline path
    /// entirely and are built and unioned as a solid.
    /// </summary>
    /// <param name="coarse">
    /// For the preview, which is redrawn on every keystroke. A board's grain is sampled to the
    /// nozzle, and at a nozzle's width a wall of it is a hundred thousand triangles - far too many
    /// to rebuild while a number is being typed. Three times as coarse is a ninth of the work and
    /// reads the same at the size a panel shows it.
    /// </param>
    private IRelief? Profile(bool coarse) =>
        embossTexture.IsProfiled
            ? SurfaceTexture.ProfileOf(embossTexture, embossDepth, coarse ? 1.2f : 0.4f, RingMm())
            : null;

    /// <summary>
    /// The way round the part, when a shaped texture is wrapped round it as a ring; nought
    /// otherwise. The field is then told it, so it repeats exactly on it and closes on itself
    /// with no seam - the stones, bark and grain only, which are the fields that know how.
    /// </summary>
    private float RingMm()
    {
        if (embossFace is null || !embossTexture.Rings) return 0f;
        if (embossProjection == TextProjection.Walls) return Run() is { Closed: true } run ? run.Length : 0f;
        if (embossProjection != TextProjection.Cylindrical) return 0f;

        var axis = new Vector2(embossBounds.Center.X, embossBounds.Center.Y);
        float radius = (new Vector2(embossPick.X, embossPick.Y) - axis).Length();

        // The same test EmbossSurface makes before it gives up on the barrel for the face.
        return radius < 0.05f ? 0f : MathF.Tau * radius;
    }

    /// <summary>
    /// The rectangle a shaped texture is laid over, and whether it is a ring: the whole way round
    /// the part, rather than the way round less the strip a flat face keeps clear at its edges.
    /// That strip is what the seam was - a field a whole circumference wide less a margin leaves
    /// the margin as a slot down the barrel where the two ends stop short of each other.
    /// </summary>
    private (float Wide, float Tall, bool Round) FieldExtent()
    {
        var (wide, tall) = FieldRoom();
        float ring = RingMm();

        if (ring > 0f && tall > 0.01f) return (ring, tall, true);

        // An area set by dragging the corners of its box, as a flat texture's is. It may run past
        // the face: raised, what is past it is trimmed off.
        if (embossProjection == TextProjection.Planar && embossTextureArea is { } set && wide > 0.01f)
            return (set.X, set.Y, false);

        return (wide, tall, false);
    }

    /// <summary>
    /// Whether the texture covers the whole of what it is laid on and can only slide: a laid one,
    /// and a field wrapped round a barrel as a ring. Neither turns - a turned ring would not close
    /// on itself, and that seam is what the ring is for - and neither has corners to drag.
    /// </summary>
    public bool TextureSlides =>
        embossTexture.IsLaid
        || (embossTexture.IsProfiled && embossProjection == TextProjection.Walls)
        || (embossTexture.Rings && embossProjection == TextProjection.Cylindrical);

    /// <summary>Whether the texture in hand is one with a shape rather than an outline.</summary>
    public bool UsesProfile => embossTexture.IsProfiled;

    /// <summary>The solid a shaped texture comes to on the face that was picked.</summary>
    /// <param name="sunk">
    /// For a cut: the field mirrored about the face, so taking it away sinks the tiles into the
    /// object. The preview asks for the raised one whichever way it is going, since a preview sunk
    /// into the object would be hidden by the very face it is being placed on.
    /// </param>
    private Mesh? ProfiledSolid(IPlacementSurface surface, bool coarse, bool sunk = false)
    {
        if (isMasonryMode && embossTexture.Kind == TextureKind.Brick) return Brickwork(surface, out _);
        if (isMasonryMode && embossTexture.Kind == TextureKind.Logs) return Logwork(surface, out _);

        // Tiles and siding are built as the slabs they are. Boarding, stone, bark and grain are
        // sampled fields, because each of them is genuinely a height that changes everywhere.
        if (SurfaceTexture.CoursesOf(embossTexture, embossDepth) is { } courses)
        {
            var (across, up, room) = TileField(surface, sunk);

            // Across and Up move the lattice. A stamp is placed somewhere on the face; a texture
            // fills it, so the only thing those two numbers can mean here is where the courses
            // start - which is what somebody lining a joint up with a window wants from them.
            if (across <= 0.01f || up <= 0.01f) return null;

            var laid = TileSolid.Build(surface, courses, across, up, sunk, room, embossPlacement.OffsetMm);
            return isMasonryMode && surface is WallsSurface sided ? WithPosts(laid, sided.Run, courses) : laid;
        }

        var (wide, tall, round) = FieldExtent();
        if (wide <= 0.01f || tall <= 0.01f || Profile(coarse) is not { } relief) return null;

        // A ring fills the barrel and stays put; Across and Up slide its pattern round and up it.
        // Round the walls it fills them in the same way, with a sample line at every corner where
        // the wall turns. Anything else is a patch, placed where the handles put it as a flat
        // texture is.
        IRelief slid = new SlidRelief(relief, embossPlacement.OffsetMm);
        if (surface is WallsSurface walls)
        {
            // Stone walling laid by Masonry has its cornerstones set into it.
            IRelief laid = new LinedRelief(slid, surface);
            if (isMasonryMode)
            {
                var o = embossTexture.Sane();
                laid = new SurfaceProfiles.Quoins(
                    laid, walls.Run, o.Kind, o.PitchMm, o.Courses, o.LineMm, embossDepth, coarse ? 1.2f : 0.4f);
            }

            return ReliefField.Build(surface, laid, wide, tall, sunk, round);
        }
        if (!round) return ReliefField.Build(new PlacedSurface(surface, embossPlacement), relief, wide, tall, sunk);

        return ReliefField.Build(surface, slid, wide, tall, sunk, round);
    }

    /// <summary>
    /// The rectangle a laid texture covers, and where it is allowed to stand.
    ///
    /// Raised, it keeps clear of the outline by the same strip a flat texture keeps, so nothing is
    /// laid right at the edge of the face. Cut, it does the opposite and runs past: a cut that
    /// stops short leaves the material between it and the edge standing as a rib a fifth of a
    /// millimetre wide and as deep as the cut, and a seven to one knife edge is what the boolean
    /// tears on. The same overshoot the engraver has always used, for the same reason.
    /// </summary>
    private (float Across, float Up, TileRoom? Room) TileField(IPlacementSurface? surface, bool sunk)
    {
        var (across, up) = FaceRoom();
        if (across <= 0.01f || up <= 0.01f) return (0f, 0f, null);

        float margin = sunk ? -Engraver.EdgeOvershoot : Engraver.RaisedInset * 4f;
        float clearance = sunk ? -Engraver.EdgeOvershoot : TileRoom.ClearanceMm;

        return (MathF.Max(across - margin, 0.01f), MathF.Max(up - margin, 0.01f),
                TileRoom.Of(surface, embossMesh, clearance));
    }

    /// <summary>
    /// The rectangle a shaped texture is laid over: the face, less the strip a flat texture keeps
    /// clear so that nothing is cut right at the outline.
    /// </summary>
    private (float Wide, float Tall) FieldRoom()
    {
        var (across, up) = FaceRoom();
        if (across <= 0.01f || up <= 0.01f) return (0f, 0f);

        float margin = Engraver.RaisedInset * 4f;

        return (MathF.Max(across - margin, 0.01f), MathF.Max(up - margin, 0.01f));
    }

    /// <summary>Whether a texture is being laid rather than a stamp placed.</summary>
    public bool UsesTexture => embossTexture.IsOn;

    /// <summary>
    /// The other way round, for the rows a texture has no use for: its own size, where it sits,
    /// how often it repeats. A field fills what it is given and none of those mean anything to it.
    /// </summary>
    public bool UsesStamp => !embossTexture.IsOn;

    public float EmbossTexturePitch
    {
        get => embossTexture.PitchMm;
        set => SetTexture(embossTexture with { PitchMm = value });
    }

    public float EmbossTextureLine
    {
        get => embossTexture.LineMm;
        set
        {
            // Raised to the least this texture allows, and the box told so. It used to go on showing
            // what was typed while the picture, the preview and the part all used the least.
            SetTexture(embossTexture with { LineMm = MathF.Max(value, embossTexture.LeastLine) });
            Raise(nameof(EmbossTextureLine));
        }
    }

    public float EmbossTextureAngle
    {
        get => embossTexture.AngleDegrees;
        set => SetTexture(embossTexture with { AngleDegrees = value });
    }

    public bool EmbossTextureAcross
    {
        get => embossTexture.Across;
        set => SetTexture(embossTexture with { Across = value });
    }

    /// <summary>Whether the lean matters: only knurling is built on a slanted lattice.</summary>
    public bool TextureLeans => embossTexture.Kind == TextureKind.Knurl;

    /// <summary>
    /// Whether the direction matters. Ribs and boards can run either way; courses of brick and
    /// tile are level by definition, and a roof laid in vertical columns is not a roof.
    /// </summary>
    public bool TextureRuns => embossTexture.Turns;

    /// <summary>Whether the pattern is made of pieces, which have proportions to argue about.</summary>
    public bool TextureHasCourses => embossTexture.IsMasonry;

    /// <summary>
    /// Whether the pieces are laid at an angle, which only the ones built as slabs are. A flat pad
    /// has one height and nothing to tilt.
    /// </summary>
    public bool TextureTilts => embossTexture.IsLaid;

    /// <summary>
    /// Whether what is on the face turns - lettering, a drawing, a picture, a flat texture, a
    /// field on a face; not a laid one, whose courses run with the face, nor a ring round a barrel.
    /// </summary>
    public bool EmbossTurns => !TextureSlides && !isMasonryMode;

    /// <summary>How far each piece is tilted, in degrees. Nought lays them flat.</summary>
    public float EmbossTextureSlope
    {
        get => embossTexture.SlopeDegrees;
        set => SetTexture(embossTexture with { SlopeDegrees = value });
    }

    public TileSlope EmbossTextureSlopeWay
    {
        get => embossTexture.Slope;
        set => SetTexture(embossTexture with { Slope = value });
    }

    public IReadOnlyList<TileSlope> EmbossTextureSlopeWays { get; } =
        [TileSlope.Roll, TileSlope.Pitch];

    /// <summary>
    /// How many times longer each piece is than it is deep. Zero takes whatever the pattern
    /// normally is, which is the only figure most people ever want.
    /// </summary>
    public float EmbossTextureAspect
    {
        get => embossTexture.Courses;
        set => SetTexture(embossTexture with { Aspect = value });
    }

    private void SetTexture(TextureOptions wanted)
    {
        if (embossTexture == wanted) return;

        embossTexture = wanted;

        Raise(nameof(EmbossTexturePitch));
        Raise(nameof(EmbossTextureLine));
        Raise(nameof(ShowsTexturePictures));
        Raise(nameof(ShowsOutlinePreview));
        Raise(nameof(EmbossTextureAngle));
        Raise(nameof(EmbossTextureAcross));
        Raise(nameof(TextureLeans));
        Raise(nameof(TextureRuns));
        Raise(nameof(TextureHasCourses));
        Raise(nameof(TextureTilts)); Raise(nameof(EmbossTurns));
        Raise(nameof(EmbossTextureAspect));
        Raise(nameof(EmbossTextureSlope));
        Raise(nameof(EmbossTextureSlopeWay));
        RefreshLettering();
    }

    /// <summary>Whether the lettering is coming from a drawing rather than from the text box.</summary>
    public bool HasDrawing => svgFile.Length > 0;

    /// <summary>Whether what is stamped is a picture, traced, rather than a drawing's own shapes.</summary>
    public bool IsPictureDrawing => svgFile.Length > 0 && PictureReader.Reads(svgFile);

    /// <summary>The picture's ink, read once per file, the other way round when asked.</summary>
    private Greyscale EmbossInk()
    {
        if (embossInk is null || embossInkFile != svgFile)
        {
            embossInk = PictureReader.ReadInk(svgFile);
            embossInkFile = svgFile;
        }

        return embossInvert
            ? embossInk with { Samples = embossInk.Samples.Select(v => 1f - v).ToArray() }
            : embossInk;
    }

    /// <summary>How much ink counts as ink, from nothing to one. Lower takes in the paler parts.</summary>
    public float EmbossThreshold
    {
        get => embossThreshold;
        set
        {
            if (!float.IsFinite(value)) return;
            embossThreshold = Math.Clamp(value, 0.02f, 0.98f);
            Raise(nameof(EmbossThreshold));
            RefreshDrawing();
        }
    }

    /// <summary>Stamp what is not ink instead: a white logo on a dark ground.</summary>
    public bool EmbossInvert
    {
        get => embossInvert;
        set
        {
            embossInvert = value;
            Raise(nameof(EmbossInvert));
            RefreshDrawing();
        }
    }

    /// <summary>
    /// The lettering or the loaded drawing's outlines, for the panel to show. The very ones that
    /// will be stamped, so what is on screen is what will be on the object - which is the only way
    /// to see that a drawing came in with its holes intact, or that a font has the letters typed.
    /// </summary>
    public IReadOnlyList<TextShape> DrawingShapes => Lettering();

    private TexturePreview.Pictures? texturePictures;
    private int texturePictureAt;

    /// <summary>
    /// Two pictures of the texture in the panel, of the same patch of face: the pattern flat, from
    /// above, and the relief shaded. Made off the UI thread and only the latest kept, since they are
    /// asked for on every keystroke in a pitch box and some of them take a moment.
    /// </summary>
    public System.Windows.Media.ImageSource? TextureFlatPicture => texturePictures?.Flat;

    public System.Windows.Media.ImageSource? TextureReliefPicture => texturePictures?.Relief;

    /// <summary>What the second picture is of: the relief, or - for log walls - a corner of the wall.</summary>
    public string TextureReliefCaption => texturePictures?.ReliefCaption ?? "Relief";

    /// <summary>
    /// <summary>
    /// Whether the panel shows the two pictures: every texture has them, log walls too. Lettering
    /// and a drawing are shown as the outlines they are.
    /// </summary>
    public bool ShowsTexturePictures => embossTexture.IsOn;

    public bool ShowsOutlinePreview => !ShowsTexturePictures;

    private void RefreshPicture()
    {
        int at = ++texturePictureAt;
        var options = embossTexture;
        float depth = embossDepth;

        if (!ShowsTexturePictures)
        {
            if (texturePictures is null) return;

            texturePictures = null;
            RaisePictures();
            return;
        }

        var ui = SynchronizationContext.Current is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() =>
        {
            try { return TexturePreview.Render(options, depth); }
            catch (Exception) { return null; }
        }).ContinueWith(done =>
        {
            if (at != texturePictureAt) return;

            texturePictures = done.Result;
            RaisePictures();
        }, ui);
    }

    private void RaisePictures()
    {
        Raise(nameof(TextureFlatPicture));
        Raise(nameof(TextureReliefPicture));
        Raise(nameof(TextureReliefCaption));
    }

    public bool UsesText => svgFile.Length == 0 && !embossTexture.IsOn;

    public string SvgName => Path.GetFileName(svgFile);

    /// <summary>What is being stamped, for the messages that have to name it.</summary>
    private string Stamped() =>
        embossTexture.IsOn ? $"{TextureOptions.NameOf(embossTexture.Kind).ToLowerInvariant()} texture"
        : svgFile.Length > 0 ? SvgName
        : $"\"{embossText}\"";

    /// <summary>Throws the laid-out lettering away, for whatever would change how it reads.</summary>
    private void RefreshLettering()
    {
        letteringCache = null;
        Raise(nameof(DrawingShapes));
        RefreshPicture();
        RefreshEmboss();
    }

    /// <summary>The outlines where they have been put, ready to lay on the surface.</summary>
    private IReadOnlyList<TextShape> EmbossShapes() =>
        // A texture is already the whole field, cut to the room it has, so repeating it would
        // only tile a tiling. It is still placed, though: sliding a field up or round is the one
        // adjustment it wants, and the handles in the viewport drive the same two numbers.
        embossPlacement.Apply(embossTexture.IsOn ? Lettering() : Repeated(Lettering()));

    /// <summary>
    /// The stamp laid out over and over: a chosen number across and up, or as many as the face
    /// will take.
    ///
    /// Repeated before the placement rather than after it, so the whole field moves and turns
    /// together - tiling the placed shapes would turn each copy on the spot and leave the grid
    /// square to the face.
    ///
    /// The pitch is the stamp's own size plus the gap, measured edge to edge, so a gap of 2 mm
    /// means two millimetres of blank between one and the next whether the stamp is a heart or a
    /// word. Round a barrel the field meets itself, so the count has to divide the way round or
    /// there is a seam down one side; the gap takes up the difference.
    /// </summary>
    private IReadOnlyList<TextShape> Repeated(IReadOnlyList<TextShape> shapes)
    {
        if (shapes.Count == 0) return shapes;
        if (!embossFill && embossColumns <= 1 && embossRows <= 1) return shapes;

        var (low, high) = Extent(shapes);
        float pitchX = high.X - low.X + embossGap;
        float pitchY = high.Y - low.Y + embossGap;

        if (pitchX < 0.01f || pitchY < 0.01f) return shapes;

        var (across, up) = Counts(pitchX, pitchY);

        // Round a barrel the field meets itself, so the count has to divide the way round or
        // there is a seam down one side; the gap takes up the difference.
        if (embossFill && embossProjection != TextProjection.Planar)
        {
            float room = FaceRoom().Across;
            if (room > 0.01f) pitchX = room / across;
        }

        // Past this the boolean has more cutter than model and nothing good happens.
        if (across * up > 1200) return shapes;

        var field = new List<TextShape>(shapes.Count * across * up);

        for (int j = 0; j < up; j++)
        for (int i = 0; i < across; i++)
        {
            var shift = new Vector2(
                (i - (across - 1) / 2f) * pitchX,
                (j - (up - 1) / 2f) * pitchY);

            if (shift == Vector2.Zero)
            {
                field.AddRange(shapes);
                continue;
            }

            foreach (var shape in shapes)
                field.Add(new TextShape(
                    shape.Outline.Select(p => p + shift).ToList(),
                    shape.Holes.Select(h => (IReadOnlyList<Vector2>)h.Select(p => p + shift).ToList()).ToList()));
        }

        return field;
    }

    /// <summary>
    /// How many copies go down and across: the numbers asked for, or as many as the face will
    /// take at that pitch. Worked out rather than stored, so the boxes go on saying what was
    /// typed while Fill is on and reading them cannot change them.
    /// </summary>
    private (int Across, int Up) Counts(float pitchX, float pitchY)
    {
        if (!embossFill) return (Math.Max(embossColumns, 1), Math.Max(embossRows, 1));

        var (room, height) = FaceRoom();

        // The last one needs no gap after it, so there is a gap more room than it looks.
        return (Math.Clamp((int)MathF.Floor((room + embossGap) / pitchX), 1, 200),
                Math.Clamp((int)MathF.Floor((height + embossGap) / pitchY), 1, 200));
    }

    /// <summary>What the field actually comes to, for the panel to report.</summary>
    public (int Across, int Up) EmbossField()
    {
        var (low, high) = Extent(Lettering());
        float pitchX = high.X - low.X + embossGap;
        float pitchY = high.Y - low.Y + embossGap;

        return pitchX < 0.01f || pitchY < 0.01f ? (1, 1) : Counts(pitchX, pitchY);
    }

    /// <summary>The rectangle the stamp covers in its own flat layout.</summary>
    private static (Vector2 Low, Vector2 High) Extent(IReadOnlyList<TextShape> shapes)
    {
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);

        foreach (var shape in shapes)
        foreach (var p in shape.Outline)
        {
            low = Vector2.Min(low, p);
            high = Vector2.Max(high, p);
        }

        return low.X > high.X ? (Vector2.Zero, Vector2.Zero) : (low, high);
    }

    /// <summary>
    /// How much room there is to fill. On a flat face that is the face; wrapped, it is the whole
    /// way round the part and its full height, since the stamp is no longer confined to the one
    /// facet that was clicked.
    /// </summary>
    private (float Across, float Up) FaceRoom()
    {
        if (embossFace is not { } face) return (0f, 0f);

        if (embossProjection == TextProjection.Planar)
            return (face.Max.X - face.Min.X, face.Max.Y - face.Min.Y);

        // Along the walls picked, and as high as the tallest of them.
        if (embossProjection == TextProjection.Walls)
            return Run() is { } run
                ? (run.Length, run.High - run.Low)
                : (face.Max.X - face.Min.X, face.Max.Y - face.Min.Y);

        var axis = new Vector2(embossBounds.Center.X, embossBounds.Center.Y);
        float radius = (new Vector2(embossPick.X, embossPick.Y) - axis).Length();

        return radius < 0.05f
            ? (face.Max.X - face.Min.X, face.Max.Y - face.Min.Y)
            : (2f * MathF.PI * radius, embossBounds.Size.Z);
    }

    /// <summary>
    /// A raised solid kept to the wall it is on, so none of it stands over an opening: a flat
    /// face's own region, or the regions of every wall it goes round.
    /// </summary>
    private Mesh KeptToTheWall(Mesh solid, IPlacementSurface surface, CancellationToken token = default) =>
        TextCutter.KeptOn(solid, surface, Proud(surface), token);

    /// <summary>A thin slab of the lettering, laid on the shape so the placement can be seen.</summary>
    public Mesh? EmbossPreview()
    {
        if (EmbossSurface() is not { } surface) return null;

        // A shaped texture is its own preview: what is drawn is the very solid that will be
        // added, only sampled more coarsely so it can be rebuilt as the numbers move. Raised, it
        // is kept to the face as it will be, so a patch slid or turned past the edge shows cut off.
        // Brickwork too, which is no heavier to build than to draw.
        if (embossTexture.IsProfiled || isMasonryMode)
        {
            var field = ProfiledSolid(surface, coarse: true);
            return embossRaised && field is not null && !embossTexture.IsLaid
                ? KeptToTheWall(field, surface)
                : field;
        }

        var shapes = EmbossShapes();

        // Standing proud whichever way it will go: a preview sunk into the object would be
        // hidden by the very face it is being placed on.
        float clear = surface.ClearanceMm + 0.06f;
        if (shapes.Count == 0) return null;

        // Raised lettering is shown as it will print, at its full height and bevel: it is only
        // added to the object, so the solid alone is the whole of it. Cut lettering stays a thin
        // slab - seeing it sunk in would take the boolean itself on every change.
        // Raised, kept to the face as it will be made, so a doorway already cut in it shows clear.
        // Cut is shown whole, as it is applied: a groove runs off the face's edge.
        return embossRaised
            ? TextCutter.KeptOn(TextSolid.Build(shapes, surface, clear, Math.Max(embossDepth, clear + 0.03f), embossBevel), surface, embossDepth)
            : TextSolid.Build(shapes, surface, clear, clear + 0.03f);
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
        await NotifyApplyingAsync();

        // Said rather than simply returning. A click that lands on the plate rather than on the
        // model clears the selection, the panel stays open, and Apply then did nothing at all -
        // no message, no change, a button that looked live and was not.
        if (Scene.Selection.Count != 1)
        {
            Status = Scene.Selection.Count == 0
                ? "Nothing is selected - click the object again, then Apply"
                : "Emboss works on one object at a time - select just the one";
            return;
        }

        if (embossMesh is not { } world || EmbossSurface() is not { } surface)
        {
            Status = "Pick the face again - the one that was picked has gone";
            return;
        }

        if (embossProjection == TextProjection.Walls && Run() is null)
        {
            Status = embossWallsWhy ?? "There are no walls to go round here.";
            return;
        }

        if (embossTexture.IsProfiled || isMasonryMode)
        {
            await ApplyProfile(world, surface);
            return;
        }

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
        bool apart = embossSeparate;
        float amount = embossDepth;

        // A bevel that would meet in the middle before it reached the far end leaves nothing
        // there to cap, so it is held to the depth it has room to slope over.
        float bevel = Math.Min(embossBevel, amount * 0.9f);

        if (IsBusy) return;

        var token = StartWork(raised ? "Raising lettering" : "Cutting lettering");
        try
        {
            if (apart)
            {
                await LetterAsAPart(source, world, shapes, surface, raised, amount, bevel, token);
                return;
            }

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
            RaiseToolInHand();
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

    /// <summary>
    /// While this is on, the Extrude down panel is open and the plane shows where the cut will go.
    /// </summary>
    public bool IsExtrudeMode
    {
        get => isExtrudeMode;
        set
        {
            if (isExtrudeMode == value) return;

            Set(ref isExtrudeMode, value);
            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
            RaiseToolInHand();
        }
    }

    /// <summary>
    /// Where the model is cut before it is built down to the plate, in whatever unit is chosen.
    ///
    /// Everything below it is replaced, so it is the one number that decides what survives: set it
    /// above the ragged part of a scan and the plinth is clean, set it through the ragged part and
    /// the plinth follows the rag.
    /// </summary>
    public float ExtrudeHeight
    {
        get => unit.From(extrudeHeight);
        set
        {
            float mm = unit.To(value);
            if (!float.IsFinite(mm)) return;

            extrudeHeight = MathF.Max(ExtrudeDown.MinimumHeight, mm);
            Raise(nameof(ExtrudeHeight));
            Raise(nameof(ExtrudeHeightMillimetres));
        }
    }

    /// <summary>The same height in millimetres, for the plane on the model and the arrows that drag it.</summary>
    public float ExtrudeHeightMillimetres
    {
        get => extrudeHeight;
        set
        {
            if (!float.IsFinite(value)) return;

            extrudeHeight = MathF.Max(ExtrudeDown.MinimumHeight, value);
            Raise(nameof(ExtrudeHeight));
            Raise(nameof(ExtrudeHeightMillimetres));
        }
    }

    private void BeginExtrude()
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return;

        var bounds = Bounds.Empty;
        foreach (var o in selection) bounds = bounds.Union(o.WorldBounds);

        // A millimetre into the model from its lowest point: enough to cut clear of a ragged edge
        // on most scans, and a starting point that always crosses the model rather than missing it.
        extrudeHeight = MathF.Max(ExtrudeDown.MinimumHeight, MathF.Min(bounds.Min.Z + 1f, bounds.Center.Z));
        Raise(nameof(ExtrudeHeight));
        Raise(nameof(ExtrudeHeightMillimetres));

        IsExtrudeMode = true;
        Status = "Extrude down: set the height, everything below it goes straight down to the plate";
    }

    private async Task ApplyExtrude()
    {
        await NotifyApplyingAsync();

        var selection = Scene.Selection.ToList();
        if (selection.Count == 0 || IsBusy) return;

        float height = extrudeHeight;
        var meshes = selection.Select(o => o.ToWorldMesh()).ToList();

        var token = StartWork("Extruding down");
        try
        {
            var results = await Task.Run(() => meshes.Select(m => ExtrudeDown.Apply(m, height)).ToList(), token);

            var removed = new List<SceneObject>();
            var added = new List<SceneObject>();
            var refused = new List<string>();

            for (int i = 0; i < selection.Count; i++)
            {
                if (results[i] is not { } mesh)
                {
                    refused.Add(selection[i].Name);
                    continue;
                }

                removed.Add(selection[i]);
                added.Add(new SceneObject(selection[i].Name, mesh) { Colour = selection[i].Colour }.Centred());
            }

            if (added.Count == 0)
            {
                Status = "Extrude down changed nothing";
                MessageBox.Show(
                    "Nothing could be extruded down at that height.\n\n"
                    + "The height has to cross the model - above its lowest point and below its top - "
                    + "and the model has to be closed where it is cut, or the walls would have nothing "
                    + "to join. Repair the model first if it has holes of its own.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Extrude down", removed, added));
            IsExtrudeMode = false;
            RefreshSelection();

            Status = refused.Count == 0
                ? $"Extruded {added.Count} object(s) down to the plate from {ExtrudeHeight:0.##} {UnitLabel}"
                : $"Extruded {added.Count} object(s); {string.Join(", ", refused)} not closed at that height, left as it was";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        finally
        {
            EndWork();
        }
    }

    private void BeginSubtract()
    {
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsLayMode = false;
        IsMeasureMode = false;
        IsSplitMode = false;
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;

        IsSubtractMode = true;
        Raise(nameof(SubtractSummary));
        Status = SubtractSummary;
    }

    private async Task ApplySubtract()
    {
        await NotifyApplyingAsync();

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
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
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

    private bool showOutlines = true;
    private IReadOnlyList<SceneObject>? previewOnly;

    /// <summary>
    /// The white line traced round a selected object's own edges. Worth turning off on a dense
    /// mesh, where every feature edge is an edge and the outline covers the surface itself.
    /// </summary>
    public bool ShowOutlines
    {
        get => showOutlines;
        set { Set(ref showOutlines, value); ViewChanged?.Invoke(); }
    }

    /// <summary>
    /// What a tool is previewing, drawn on its own while the tool's panel is open - a gear tool
    /// shows a pair. Null the rest of the time, which is everything drawn as usual.
    /// </summary>
    public IReadOnlyList<SceneObject>? PreviewOnly
    {
        get => previewOnly;
        set { Set(ref previewOnly, value); ViewChanged?.Invoke(); }
    }

    private bool showOverhangs;
    private float overhangAngle = Overhangs.DefaultAngle;
    private Vector3 overhangColour = Palette.WarningSwatches[0].Colour;

    /// <summary>
    /// Faces that will need support, marked in <see cref="OverhangColour"/>. Said in numbers on
    /// the status line as well, so a part that has none says so rather than leaving you to look
    /// for a colour that is not there.
    /// </summary>
    public bool ShowOverhangs
    {
        get => showOverhangs;
        set
        {
            Set(ref showOverhangs, value);
            ViewChanged?.Invoke();
            if (value) DescribeOverhangs();
        }
    }

    /// <summary>How far from upright a face may lean before it counts, in degrees.</summary>
    public float OverhangAngle
    {
        get => overhangAngle;
        set
        {
            Set(ref overhangAngle, Math.Clamp(value, 10f, 80f));
            ViewChanged?.Invoke();
            if (showOverhangs) DescribeOverhangs();
        }
    }

    /// <summary>What an overhanging face is marked in. The viewer's own, like the grid: not saved with the project.</summary>
    public Vector3 OverhangColour
    {
        get => overhangColour;
        set
        {
            Set(ref overhangColour, value);
            ViewChanged?.Invoke();
        }
    }

    public IReadOnlyList<float> OverhangAngles { get; } = [30f, 40f, 45f, 50f, 60f];

    public IReadOnlyList<Swatch> OverhangSwatches => Palette.WarningSwatches;

    public System.Windows.Input.ICommand SetOverhangColourCommand { get; }
    public System.Windows.Input.ICommand PickOverhangColourCommand { get; }
    public System.Windows.Input.ICommand CloseOverhangsPanelCommand { get; }

    private void SetOverhangColour(object? parameter)
    {
        if (TryReadColour(parameter, out var colour)) OverhangColour = colour;
    }

    private void PickOverhangColour()
    {
        var dialog = new ColourDialog(overhangColour) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result is { } picked) OverhangColour = picked;
    }

    private string betterFaceNote = "";
    private int overhangRun;

    /// <summary>
    /// Whether standing the part another way up would need less support, said under the angle.
    ///
    /// The overhang figure on its own is a verdict with nothing to do about it. This is the part
    /// worth knowing: one number, and where the tool that acts on it lives. Only for a single
    /// object, because turning one part over is a thing a person does and turning a plateful is
    /// not.
    /// </summary>
    public string BetterFaceNote
    {
        get => betterFaceNote;
        private set
        {
            Set(ref betterFaceNote, value);
            Raise(nameof(HasBetterFaceNote));
        }
    }

    public bool HasBetterFaceNote => betterFaceNote.Length > 0;

    private void DescribeOverhangs()
    {
        double area = Scene.Shown.Sum(o => Overhangs.Area(o.ToWorldMesh(), overhangAngle));
        Status = area < 1.0
            ? $"No overhangs steeper than {overhangAngle:0} degrees - nothing needs support"
            : $"{area / 100.0:0.#} cm² steeper than {overhangAngle:0} degrees from upright will need support or a bridge";

        SayWhatTurningWouldSave(area);
    }

    /// <summary>
    /// Away from the window, since it is the hull and a pass over every triangle for each face -
    /// and stamped with a run number, because the angle box can be changed faster than this
    /// answers and a late reply must not overwrite a later question.
    /// </summary>
    private async void SayWhatTurningWouldSave(double asItStands)
    {
        int run = ++overhangRun;
        BetterFaceNote = "";

        if (asItStands < 1.0 || Scene.Shown.Count != 1) return;

        var world = Scene.Shown[0].ToWorldMesh();
        float angle = overhangAngle;

        List<StandingChoice> choices;
        try
        {
            choices = await Task.Run(() => BestFace.Rank(world, RestingFaces.Find(world), angle));
        }
        catch
        {
            // An aid to the figure above, not the figure itself. If it cannot be worked out, the
            // panel simply does not offer it.
            return;
        }

        if (run != overhangRun || !showOverhangs || choices.Count == 0) return;

        double saved = asItStands - choices[0].SupportMm2;

        BetterFaceNote = saved < 10.0
            ? "No face it can stand on would need much less support than this one."
            : $"Another face would need {saved / 100.0:0.#} cm² less support - Best face down, on the Align tab.";
    }

    /// <summary>
    /// The printable area, in millimetres: across X, across Y and up. A guide rather than a limit -
    /// nothing stops an object being placed off it - so any printer can be dialled in. The bed was
    /// one square size picked from a list, which no printer with a rectangular bed could be.
    /// </summary>
    public float PlateWidth
    {
        get => plateWidth;
        set => SetPlate(ref plateWidth, value);
    }

    public float PlateDepth
    {
        get => plateDepth;
        set => SetPlate(ref plateDepth, value);
    }

    public float PlateHeight
    {
        get => plateHeight;
        set => SetPlate(ref plateHeight, value);
    }

    private void SetPlate(ref float field, float value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        float wanted = Math.Clamp(float.IsFinite(value) ? value : field, 20f, 2000f);
        if (Math.Abs(wanted - field) < 0.01f) return;

        field = wanted;
        Raise(property);
        ViewChanged?.Invoke();
        SettingChanged();
    }

    /// <summary>The X and Y lines across the plate, crossing at the origin.</summary>
    public bool ShowAxes
    {
        get => showAxes;
        set => SetGrid(ref showAxes, value);
    }

    /// <summary>The upright line, as tall as the printable height.</summary>
    public bool ShowZAxis
    {
        get => showZAxis;
        set => SetGrid(ref showZAxis, value);
    }

    /// <summary>Distances written along the positive X and Y axes, in the current unit.</summary>
    public bool ShowGridLabels
    {
        get => showGridLabels;
        set => SetGrid(ref showGridLabels, value);
    }

    /// <summary>
    /// Objects cast a shadow onto the plate and each other, from the key light. Off by default:
    /// a shadow pass costs a render of the scene from the light's own view every frame, which is
    /// wasted while nothing on the plate benefits from it - a couple of primitives read fine
    /// without it, and it is exactly the kind of thing a slower machine feels first.
    /// </summary>
    public bool ShowShadows
    {
        get => showShadows;
        set => SetGrid(ref showShadows, value);
    }

    /// <summary>
    /// The plate reflects what stands on it, the way a glossy print bed does. Also off by
    /// default, and for the same reason as shadows - a live reflection is a second render of the
    /// scene, doubling the cost of every frame it is on.
    /// </summary>
    public bool ShowReflections
    {
        get => showReflections;
        set => SetGrid(ref showReflections, value);
    }

    /// <summary>
    /// How much of the ribbon is shown. Only ribbon buttons are ever hidden - never a tool's own
    /// settings - and every key works the same at both levels, so neither a project nor a
    /// habit depends on the mode. The viewer's own, like the grid: remembered, not saved with a
    /// project.
    /// </summary>
    public UiLevel UiLevel
    {
        get => uiLevel;
        set
        {
            if (uiLevel == value) return;

            Set(ref uiLevel, value);
            Raise(nameof(IsAdvancedMode));
            Raise(nameof(UiMode));
            Raise(nameof(ShortcutGroups));

            // Otherwise the red stays drawn over the model with no button left to turn it off.
            if (value == UiLevel.Classic && ShowOverhangs) ShowOverhangs = false;
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>Whether every tool is shown, and not only the ones 3D Builder had.</summary>
    public bool IsAdvancedMode
    {
        get => uiLevel >= UiLevel.Advanced;
        set => UiLevel = value ? UiLevel.Advanced : UiLevel.Classic;
    }

    public IReadOnlyList<UiModeChoice> UiModes { get; } =
    [
        new(UiLevel.Classic, "Classic mode", "#FF3FA34D", "The tools 3D Builder had"),
        new(UiLevel.Advanced, "Advanced mode", "#FFD9482B", "Every tool - sketches, holes, threads, gears, pivots, the session recorder")
    ];

    public UiModeChoice UiMode
    {
        get => UiModes[(int)uiLevel];
        set { if (value is not null) UiLevel = value.Level; }
    }

    /// <summary>What F1 lists: in Classic, not the keys of tools it does not show - though they still work.</summary>
    public IReadOnlyList<ShortcutGroup> ShortcutGroups =>
        IsAdvancedMode ? Shortcuts.Groups : Shortcuts.ClassicGroups;

    /// <summary>
    /// Whether the selection's colour, position, size and rotation are open in the side panel.
    /// The viewer's own, like the grid: remembered, but not a change to the project.
    /// </summary>
    public bool ShowProperties
    {
        get => showProperties;
        set
        {
            if (showProperties == value) return;
            Set(ref showProperties, value);
            SettingsChanged?.Invoke();
        }
    }

    /// <summary>How the grid is drawn is the viewer's own, not the project's: remembered, but not a change to save.</summary>
    private void SetGrid(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (field == value) return;

        field = value;
        Raise(property);
        ViewChanged?.Invoke();
        SettingsChanged?.Invoke();
    }

    /// <summary>Whether the grid settings are showing in the side panel.</summary>
    public bool IsGridPanelOpen
    {
        get => isGridPanelOpen;
        set => Set(ref isGridPanelOpen, value);
    }

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
            SettingChanged();
        }
    }

    /// <summary>The bed, the unit and the scale, as the project file and the local settings keep them.</summary>
    public ProjectSettings ViewSettings => new(plateWidth, plateDepth, plateHeight, unit.Label, modelScale);

    /// <summary>Raised when the bed, the unit or the scale changes, for the window to remember it.</summary>
    public event Action? SettingsChanged;

    public void ApplySettings(ProjectSettings settings)
    {
        PlateWidth = settings.PlateWidth;
        PlateDepth = settings.PlateDepth;
        PlateHeight = settings.PlateHeight;
        Unit = MeasureUnit.All.FirstOrDefault(u => u.Label == settings.Unit, MeasureUnit.Default);
        ModelScale = settings.ModelScale;
    }

    /// <summary>The bed and unit last used; the scale belongs to a project and is not carried over.</summary>
    public void ApplySettings(RememberedSettings settings)
    {
        // Nothing stored for a size, as a file from before the printable area had three, means the default.
        PlateWidth = settings.PlateWidth > 0 ? settings.PlateWidth : Scene.PlateSize;
        PlateDepth = settings.PlateDepth > 0 ? settings.PlateDepth : Scene.PlateSize;
        PlateHeight = settings.PlateHeight > 0 ? settings.PlateHeight : Scene.PrintHeight;
        Unit = MeasureUnit.All.FirstOrDefault(u => u.Label == settings.Unit, MeasureUnit.Default);
        ShowAxes = settings.ShowAxes;
        ShowZAxis = settings.ShowZAxis;
        ShowGridLabels = settings.ShowGridLabels;
        ShowProperties = !settings.FoldProperties;
        // A file written when there was an Extended level has its flag still, and it is not read:
        // those who chose it have Advanced, which now has everything it had.
        UiLevel = settings.ClassicMode ? UiLevel.Classic : UiLevel.Advanced;
        ShowShadows = settings.ShowShadows;
        ShowReflections = settings.ShowReflections;
        StickySelection = !settings.SingleSelection;
        sidePanelWidth = settings.SidePanelWidth;
        RestoreCustomButtons(settings.CustomButtons);
    }

    public RememberedSettings Remembered =>
        new(plateWidth, plateDepth, plateHeight, unit.Label, showAxes, showZAxis, showGridLabels, !showProperties,
            uiLevel == UiLevel.Classic, showShadows, showReflections, !stickySelection,
            sidePanelWidth, customButtons.ToArray());

    private float sidePanelWidth;

    /// <summary>How wide the side panel was last dragged; nought for the default. Remembered, not part of a project.</summary>
    public float SidePanelWidth
    {
        get => sidePanelWidth;
        set
        {
            if (MathF.Abs(sidePanelWidth - value) < 0.5f) return;
            sidePanelWidth = value;
            SettingsChanged?.Invoke();
        }
    }

    private void SettingChanged()
    {
        // Part of the project now, so an open project has something unsaved - or a bed set
        // for this model would be lost by closing it without being asked.
        if (projectPath is not null) IsDirty = true;
        SettingsChanged?.Invoke();
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
            OnBed(moving: true, together: true, () =>
            {
                if (HasOneSelected) Selected!.PositionX = FromReal(value);
                else MoveGroupTo(Axis.X, FromReal(value));
            });
            RaiseReal();
        }
    }

    public float RealY
    {
        get => ToReal(HasOneSelected ? Selected!.PositionY : GroupAt(Axis.Y));
        set
        {
            OnBed(moving: true, together: true, () =>
            {
                if (HasOneSelected) Selected!.PositionY = FromReal(value);
                else MoveGroupTo(Axis.Y, FromReal(value));
            });
            RaiseReal();
        }
    }

    public float RealZ
    {
        get => ToReal(HasOneSelected ? Selected!.PositionZ : GroupAt(Axis.Z));
        set
        {
            OnBed(moving: true, together: true, () =>
            {
                if (HasOneSelected) Selected!.PositionZ = FromReal(value);
                else MoveGroupTo(Axis.Z, FromReal(value));
            });
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
    /// <summary>
    /// Which filament prints the selection, for a printer that has more than one.
    ///
    /// Reads the first selected object and writes to all of them, as the colour swatches do: the
    /// point of it is to take the lettering, or the inlay, or half the parts of an assembly, and
    /// put the lot on the second spool in one go.
    /// </summary>
    public int ObjectFilament
    {
        get => Scene.Selection.Count > 0 ? Scene.Selection[0].Filament : 1;
        set
        {
            var selection = Scene.Selection.ToList();
            int wanted = Math.Clamp(value, 1, SceneObject.MostFilaments);

            if (FilamentCommand.CreateIfChanged(
                    selection.Count == 1 ? "Filament" : $"Filament for {selection.Count} objects",
                    selection, wanted) is { } command)
            {
                Undo.Execute(command);
            }

            Raise(nameof(ObjectFilament));
            Status = wanted == 1
                ? "Prints in the first filament"
                : $"Prints in filament {wanted}";
        }
    }

    /// <summary>Whether the pivot boxes are worth showing: only a pivot somebody chose.</summary>
    public bool ShowPivotFields => Selected is { PivotIsOwn: true };

    /// <summary>
    /// Where the pivot sits in the part, measured from the middle of the part's own box.
    ///
    /// The pivot is the object's origin, so in the object's own coordinates it is nought and
    /// saying so would tell nobody anything. What is worth reading - and worth typing - is where
    /// in the part it sits, and the only fixed thing to measure that from is the box. A gear's
    /// shaft reads 0, 0 and whatever height; a hinge pin on the edge of a lid reads half the
    /// lid's width.
    ///
    /// Typing a value slides the geometry one way and the position the other, so the part does
    /// not move on the plate - the same as picking the point with the tool.
    /// </summary>
    public float ObjectPivotX
    {
        get => unit.From(Selected is { } o ? -o.LocalCentre.X : 0f);
        set => MovePivot(p => p with { X = unit.To(value) });
    }

    public float ObjectPivotY
    {
        get => unit.From(Selected is { } o ? -o.LocalCentre.Y : 0f);
        set => MovePivot(p => p with { Y = unit.To(value) });
    }

    public float ObjectPivotZ
    {
        get => unit.From(Selected is { } o ? -o.LocalCentre.Z : 0f);
        set => MovePivot(p => p with { Z = unit.To(value) });
    }

    /// <summary>
    /// Puts the pivot at a named place in the part.
    ///
    /// Shifting the geometry moves the box's middle along with it, so the shift that lands the
    /// pivot at <c>wanted</c> from the middle is <c>wanted</c> plus where the middle is now -
    /// which is also why asking for nought is exactly what centring already did.
    /// </summary>
    private void MovePivot(Func<Vector3, Vector3> change)
    {
        if (Selected is not { } o) return;

        var centre = o.Mesh.ComputeBounds().Center;
        var wanted = change(-centre);
        var shift = wanted + centre;

        if ((shift - centre).LengthSquared() < 1e-10f && !o.PivotIsOwn) return;
        if (shift.LengthSquared() < 1e-10f) return;

        Undo.Execute(new PivotCommand("Move pivot", o, shift, own: true, wasOwn: o.PivotIsOwn));
        RefreshSelection();
        Status = $"{o.Name} turns about {ObjectPivotX:0.##}, {ObjectPivotY:0.##}, {ObjectPivotZ:0.##} {UnitLabel} from its middle";
    }

    public float ObjectPositionX
    {
        get => unit.From(Selected?.PositionX ?? 0f);
        set { if (Selected is { } o) OnBed(moving: true, together: true, () => { o.PositionX = unit.To(value); RaiseTransformFields(); }); }
    }

    public float ObjectPositionY
    {
        get => unit.From(Selected?.PositionY ?? 0f);
        set { if (Selected is { } o) OnBed(moving: true, together: true, () => { o.PositionY = unit.To(value); RaiseTransformFields(); }); }
    }

    public float ObjectPositionZ
    {
        get => unit.From(Selected?.PositionZ ?? 0f);
        set { if (Selected is { } o) OnBed(moving: true, together: true, () => { o.PositionZ = unit.To(value); RaiseTransformFields(); }); }
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
    /// The resize boxes in per cent rather than in lengths. One object's per cent is of the size
    /// it came in at - its scale - so 100 takes it back to that. Several as one have no size they
    /// came in at, so theirs is of the size of the lot as it stands: 100 until something is typed.
    /// </summary>
    public bool ScaleInPercent
    {
        get => scaleInPercent;
        set
        {
            Set(ref scaleInPercent, value);
            Raise(nameof(ScaleInLengths));
        }
    }

    public bool ScaleInLengths
    {
        get => !scaleInPercent;
        set => ScaleInPercent = !value;
    }

    public float ObjectPercentX { get => PercentOf(Axis.X); set => ResizeToPercent(Axis.X, value); }
    public float ObjectPercentY { get => PercentOf(Axis.Y); set => ResizeToPercent(Axis.Y, value); }
    public float ObjectPercentZ { get => PercentOf(Axis.Z); set => ResizeToPercent(Axis.Z, value); }

    public float GroupPercentX { get => GroupPercent(Axis.X); set => GroupToPercent(Axis.X, value); }
    public float GroupPercentY { get => GroupPercent(Axis.Y); set => GroupToPercent(Axis.Y, value); }
    public float GroupPercentZ { get => GroupPercent(Axis.Z); set => GroupToPercent(Axis.Z, value); }

    private static float ScaleOn(SceneObject o, Axis axis) =>
        MathF.Abs(axis switch { Axis.X => o.Scale.X, Axis.Y => o.Scale.Y, _ => o.Scale.Z });

    private static float SizeOn(SceneObject o, Axis axis) =>
        axis switch { Axis.X => o.SizeX, Axis.Y => o.SizeY, _ => o.SizeZ };

    /// <summary>The length on an axis that is the given per cent of the size the object came in at.</summary>
    private static float AtPercent(SceneObject o, Axis axis, float percent)
    {
        float scale = ScaleOn(o, axis);
        return scale < 1e-6f ? SizeOn(o, axis) : SizeOn(o, axis) / scale * percent / 100f;
    }

    private float PercentOf(Axis axis) => Selected is { } o ? ScaleOn(o, axis) * 100f : 100f;

    private void ResizeToPercent(Axis axis, float percent)
    {
        if (Selected is not { } o || !float.IsFinite(percent) || percent <= 0f) return;
        ResizeSelected(axis, AtPercent(o, axis, percent));
    }

    private float GroupPercent(Axis axis) => aroundSelectionCentre ? 100f : Shared(o => ScaleOn(o, axis) * 100f);

    private void GroupToPercent(Axis axis, float percent)
    {
        if (!float.IsFinite(percent) || percent <= 0f || Scene.Selection.Count == 0) return;

        OnBed(moving: false, together: aroundSelectionCentre, () =>
        {
            if (aroundSelectionCentre) ResizeGroup(axis, GroupSpan(axis) * percent / 100f);
            else foreach (var o in Scene.Selection) ResizeObject(o, axis, AtPercent(o, axis, percent));
        });
        RaiseGroup();
    }

    /// <summary>
    /// Sets one of the selected object's own dimensions, taking the other two with it when the
    /// proportions are locked.
    /// </summary>
    private void ResizeSelected(Axis axis, float millimetres)
    {
        if (Selected is not { } o) return;
        if (!float.IsFinite(millimetres) || millimetres < 0.01f) return;

        OnBed(moving: false, together: true, () => ResizeObject(o, axis, millimetres));
        RaiseReal();
    }

    /// <summary>One object to a size on one axis, about its own centre, honouring the proportions lock.</summary>
    private void ResizeObject(SceneObject o, Axis axis, float millimetres)
    {
        float now = axis switch { Axis.X => o.SizeX, Axis.Y => o.SizeY, _ => o.SizeZ };

        if (!UniformScale || now < 1e-4f)
        {
            switch (axis)
            {
                case Axis.X: o.SizeX = millimetres; break;
                case Axis.Y: o.SizeY = millimetres; break;
                default: o.SizeZ = millimetres; break;
            }

            return;
        }

        // Each axis read and written on its own, because they are independent: scaling one does
        // not move the others, so the same ratio applied three times is the whole of it.
        float ratio = millimetres / now;

        o.SizeX *= ratio;
        o.SizeY *= ratio;
        o.SizeZ *= ratio;
    }

    public float RealW
    {
        get => ToReal(HasOneSelected ? Selected!.SizeX : GroupSpan(Axis.X));
        set
        {
            if (HasOneSelected) ResizeSelected(Axis.X, FromReal(value));
            else OnBed(moving: false, together: true, () => ResizeGroup(Axis.X, FromReal(value)));
            RaiseReal();
        }
    }

    public float RealD
    {
        get => ToReal(HasOneSelected ? Selected!.SizeY : GroupSpan(Axis.Y));
        set
        {
            if (HasOneSelected) ResizeSelected(Axis.Y, FromReal(value));
            else OnBed(moving: false, together: true, () => ResizeGroup(Axis.Y, FromReal(value)));
            RaiseReal();
        }
    }

    public float RealH
    {
        get => ToReal(HasOneSelected ? Selected!.SizeZ : GroupSpan(Axis.Z));
        set
        {
            if (HasOneSelected) ResizeSelected(Axis.Z, FromReal(value));
            else OnBed(moving: false, together: true, () => ResizeGroup(Axis.Z, FromReal(value)));
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

        Raise(nameof(ShowPivotFields));
        Raise(nameof(ObjectPivotX));
        Raise(nameof(ObjectPivotY));
        Raise(nameof(ObjectPivotZ));

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
        Raise(nameof(ObjectPercentX));
        Raise(nameof(ObjectPercentY));
        Raise(nameof(ObjectPercentZ));
        Raise(nameof(GroupPercentX));
        Raise(nameof(GroupPercentY));
        Raise(nameof(GroupPercentZ));
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

    /// <summary>
    /// What the Studs tool puts on a face. It was Engrave, with brick, tiles, planks, wood grain
    /// and stripes as well; those are the Texture tool's now, which does all of them and wraps
    /// them round curves, and the studs are what it had that nothing else has.
    /// </summary>
    public IReadOnlyList<PatternChoice> EngravePatterns { get; } =
    [
        new(PatternKind.Studs, "Studs (brick-compatible)"),
        new(PatternKind.StudUnderside, "Brick underside")
    ];
    public IReadOnlyList<PatternDirection> EngraveDirections { get; } = Enum.GetValues<PatternDirection>();

    public PatternKind EngravePattern
    {
        get => engrave.Options.Kind;

        // Aspect is cleared with the pattern. Each bond has its own proportions - a tile is not
        // three times as long as it is tall - and carrying the last one across meant picking
        // Roof tiles and getting bricks in a different colour.
        set
        {
            var options = engrave.Options with { Kind = value, Aspect = 0f };

            // An underside's depth is the hollow's, and a groove's 0.6 mm is no hollow at all. It
            // starts as deep as a real brick's, or as deep as this part allows under a roof.
            if (value == PatternKind.StudUnderside && engrave.Options.Kind != PatternKind.StudUnderside)
            {
                depthBeforeUnderside = engrave.Options.Depth;

                float deepest = engrave.WorldMesh is { } mesh && engrave.Face is { } face
                    ? BrickStuds.DeepestHollow(mesh, face)
                    : BrickStuds.BrickHollow;
                options = options with { Depth = MathF.Round(MathF.Min(BrickStuds.BrickHollow, deepest), 1) };
            }

            // And back again on the way out. Keeping the hollow's depth left a brick pattern set to
            // cut 8.6 mm deep - through most walls it would be put on.
            else if (engrave.Options.Kind == PatternKind.StudUnderside && value != PatternKind.StudUnderside)
            {
                options = options with { Depth = depthBeforeUnderside };
            }

            SetEngrave(options);
        }
    }

    /// <summary>The groove depth to go back to when the brick underside, which sets its own, is left.</summary>
    private float depthBeforeUnderside = EngraveOptions.Default.Depth;

    /// <summary>Studs and brick undersides have their own settings, and none of the groove ones.</summary>
    public bool EngraveIsStuds => BrickStuds.Handles(engrave.Options.Kind);

    public bool EngraveIsGrooves => !EngraveIsStuds;

    /// <summary>A stud's height is the standard one; only the underside's hollow has a depth to choose.</summary>
    public bool EngraveDepthApplies => engrave.Options.Kind != PatternKind.Studs;

    public string EngraveDepthLabel => engrave.Options.Kind == PatternKind.StudUnderside ? "Hollow" : "Depth";

    /// <summary>
    /// How much fatter than standard the studs, tubes and walls are made, in millimetres across.
    /// Held small: past a few tenths it is no longer a fit, it is a different size.
    /// </summary>
    public float EngraveStudFit
    {
        get => engrave.Options.StudFit;
        set => SetEngrave(engrave.Options with { StudFit = Math.Clamp(value, -0.4f, 0.4f) });
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
    public bool EngraveDirectionApplies => !EngraveIsStuds && GroovePattern.Turns(engrave.Options.Kind);

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
            RaiseToolInHand();
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
            var (targets, cutters) = Scene.SplitLastPick();
            if (targets.Count == 0)
                return "Click the parts to cut first, then the cutter last.";

            var cut = string.Join(", ", targets.Select(o => o.Name));
            return $"Take {NameOfPick(cutters)} away from {cut}. "
                 + $"{targets.Count} object(s) cut, each kept separate.";
        }
    }

    /// <summary>One object by its own name; an assembly picked by its name, by that.</summary>
    private static string NameOfPick(IReadOnlyList<SceneObject> pick) =>
        pick.Count > 1 && pick[0].Assembly is { } assembly ? $"{assembly.Name} ({pick.Count} parts)"
        : pick.Count > 0 ? pick[^1].Name
        : "";

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
        Raise(nameof(EngraveIsStuds));
        Raise(nameof(EngraveIsGrooves));
        Raise(nameof(EngraveDepthApplies));
        Raise(nameof(EngraveDepthLabel));
        Raise(nameof(EngraveStudFit));
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

            // Straight onto the new axis, with the turn it had let go of: the angles are
            // measured from whichever axis was chosen, so keeping them would mean the plane
            // ended up somewhere neither the buttons nor the boxes had asked for.
            splitTurn = Vector3.Zero;
            SplitNormal = PlaneSplit.NormalFor(value);
            ResetSplitOffset();
            RaiseSplitAngles();
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
        // Clamped here rather than trusted to callers: the slider already limits itself through
        // its own Minimum/Maximum binding, but the gizmo arrows report a raw drag distance and
        // would otherwise slide the plane past the solid into empty air.
        set => Set(ref splitOffset, Math.Clamp(value, SplitMinimum, SplitMaximum));
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

        // The plane keeps its turn in three angles, so a facing picked off a face has to be
        // written back as one - otherwise the boxes would go on describing the old plane.
        splitTurn = MeshTransform.EulerFrom(
            MeshTransform.RotationBetween(PlaneSplit.NormalFor(splitAxis), face.Normal));

        SplitNormal = face.Normal;
        RaiseSplitAngles();
        RecomputeSplitRange(SelectionWorldBounds(), splitNormal);

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

    /// <summary>
    /// How far the plane can travel along its own normal and still meet what is selected -
    /// recomputed whenever the normal changes, since a tilt or a face pick reads a different
    /// span off the same bounds than the axis it replaced did.
    /// </summary>
    private void RecomputeSplitRange(Bounds bounds, Vector3 normal)
    {
        var (min, max) = PlaneSplit.OffsetRange(bounds, normal);
        SplitMinimum = min;
        SplitMaximum = max;
        Raise(nameof(SplitMinimum));
        Raise(nameof(SplitMaximum));
    }

    private Bounds SelectionWorldBounds()
    {
        var bounds = Bounds.Empty;
        foreach (var o in Scene.Selection) bounds = bounds.Union(o.WorldBounds);
        return bounds;
    }

    public SplitKeep SplitKeep
    {
        // Both, whatever was last chosen, while connectors are on: a connector joins two halves,
        // and throwing one away would leave pins with nothing to go into.
        get => splitWithConnectors ? SplitKeep.Both : splitKeep;
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
    public bool SplitOffcutApplies => SplitKeep is not SplitKeep.Both;

    /// <summary>
    /// Whether this split joins its halves with pins or pegs - the Split with connectors tool on
    /// the Tools tab, which is the same split with one more section in its panel.
    /// </summary>
    public bool SplitWithConnectors
    {
        get => splitWithConnectors;
        set
        {
            if (splitWithConnectors == value) return;

            Set(ref splitWithConnectors, value);
            Raise(nameof(SplitKeep));
            Raise(nameof(SplitKeepChoosable));
            Raise(nameof(SplitOffcutApplies));
            Raise(nameof(SplitCutFaceApplies));
        }
    }

    /// <summary>Keeping one half only makes sense without connectors, so the choice is hidden with them.</summary>
    public bool SplitKeepChoosable => !splitWithConnectors;

    public bool ConnectorsArePins
    {
        get => connectors.Style == ConnectorStyle.Pins;
        set { if (value) SetConnectorStyle(ConnectorStyle.Pins); }
    }

    public bool ConnectorsArePegs
    {
        get => connectors.Style == ConnectorStyle.Pegs;
        set { if (value) SetConnectorStyle(ConnectorStyle.Pegs); }
    }

    /// <summary>Brick studs on the lower half, a shallow brick underside in the upper.</summary>
    public bool ConnectorsAreBricks
    {
        get => connectors.Style == ConnectorStyle.Bricks;
        set { if (value) SetConnectorStyle(ConnectorStyle.Bricks); }
    }

    /// <summary>Pins and pegs have a size, depth, clearance and direction; brick studs have a grid and a fit.</summary>
    public bool ConnectorsAreRound => !ConnectorsAreBricks;

    /// <summary>
    /// How much fatter than standard the studs and what grips them are made, on each half. Held
    /// small: past a few tenths it is a different size, not a fit.
    /// </summary>
    public float ConnectorBrickFit
    {
        get => connectors.BrickFit;
        set { if (float.IsFinite(value)) connectors = connectors with { BrickFit = Math.Clamp(value, -0.4f, 0.4f) }; Raise(nameof(ConnectorBrickFit)); }
    }

    private void SetConnectorStyle(ConnectorStyle style)
    {
        connectors = connectors with { Style = style };
        Raise(nameof(ConnectorsArePins));
        Raise(nameof(ConnectorsArePegs));
        Raise(nameof(ConnectorsAreBricks));
        Raise(nameof(ConnectorsAreRound));
        Raise(nameof(ConnectorSizeLabel));
    }

    public bool ConnectorPegRound
    {
        get => connectors.Shape == PegShape.Round;
        set { if (value) SetPegShape(PegShape.Round); }
    }

    /// <summary>Square pegs and sockets: one cannot turn, so a single peg keeps the halves lined up.</summary>
    public bool ConnectorPegSquare
    {
        get => connectors.Shape == PegShape.Square;
        set { if (value) SetPegShape(PegShape.Square); }
    }

    /// <summary>What the size box is: across a round connector, or along the side of a square one.</summary>
    public string ConnectorSizeLabel => connectors.IsSquare ? "Width" : "Diameter";

    private void SetPegShape(PegShape shape)
    {
        connectors = connectors with { Shape = shape };
        Raise(nameof(ConnectorPegRound));
        Raise(nameof(ConnectorPegSquare));
        Raise(nameof(ConnectorSizeLabel));
    }

    /// <summary>How many to place, at most. A face with room for fewer gets fewer, and the status line says so.</summary>
    public int ConnectorCount
    {
        get => connectors.Count;
        set { connectors = connectors with { Count = Math.Clamp(value, 1, 6) }; Raise(nameof(ConnectorCount)); }
    }

    /// <summary>The pin or peg, in millimetres - a size to print, not a reading at the scene's scale.</summary>
    public float ConnectorDiameter
    {
        get => connectors.Diameter;
        set { if (float.IsFinite(value)) connectors = connectors with { Diameter = Math.Clamp(value, 1f, 50f) }; Raise(nameof(ConnectorDiameter)); }
    }

    public float ConnectorDepth
    {
        get => connectors.Depth;
        set { if (float.IsFinite(value)) connectors = connectors with { Depth = Math.Clamp(value, 1f, 100f) }; Raise(nameof(ConnectorDepth)); }
    }

    public float ConnectorClearance
    {
        get => connectors.Clearance;
        set { if (float.IsFinite(value)) connectors = connectors with { Clearance = Math.Clamp(value, 0f, 2f) }; Raise(nameof(ConnectorClearance)); }
    }

    public bool ConnectorsPerpendicular
    {
        get => connectors.Direction == ConnectorDirection.Perpendicular;
        set { if (value) SetConnectorDirection(ConnectorDirection.Perpendicular); }
    }

    public bool ConnectorsVertical
    {
        get => connectors.Direction == ConnectorDirection.Vertical;
        set { if (value) SetConnectorDirection(ConnectorDirection.Vertical); }
    }

    public bool ConnectorsHorizontal
    {
        get => connectors.Direction == ConnectorDirection.Horizontal;
        set { if (value) SetConnectorDirection(ConnectorDirection.Horizontal); }
    }

    private void SetConnectorDirection(ConnectorDirection direction)
    {
        connectors = connectors with { Direction = direction };
        Raise(nameof(ConnectorsPerpendicular));
        Raise(nameof(ConnectorsVertical));
        Raise(nameof(ConnectorsHorizontal));
    }

    /// <summary>Material left between a hole and the outside of the face, in millimetres.</summary>
    public float ConnectorEdgeDistance
    {
        get => connectors.EdgeDistance;
        set { if (float.IsFinite(value)) connectors = connectors with { EdgeDistance = Math.Clamp(value, 0f, 100f) }; Raise(nameof(ConnectorEdgeDistance)); }
    }

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
    /// Applies whichever tool has the object, as its Apply button would, and says whether it did:
    /// what Enter does. A tool panel of the other kind - Repeat, Smooth and the rest - keeps its
    /// own buttons.
    /// </summary>
    public bool ApplyActiveTool()
    {
        System.Windows.Input.ICommand? apply =
            isSplitMode ? ApplySplitCommand
            : isEmbossMode ? ApplyEmbossCommand
            : isEngraveMode ? ApplyEngraveCommand
            : isWallMountMode ? ApplyWallMountCommand
            : isAlignFaceMode ? ApplyAlignFaceCommand
            : isCentreFaceMode ? ApplyCentreFaceCommand
            : isExtrudeMode ? ApplyExtrudeCommand
            : isConnectMode ? ApplyConnectCommand
            : isSubtractMode ? ApplySubtractCommand
            : null;

        if (apply is null || !apply.CanExecute(null)) return false;
        apply.Execute(null);
        return true;
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
        // In a sketch the first Escape drops the outline half drawn, as it would in any drawing
        // program; the next leaves the sketch.
        // An arc waiting to be bent: Escape takes back only where it ends, not the outline behind it.
        if (openPanel is null && isSketchMode && sketch.ArcEnd is not null)
        {
            SketchMessage = sketch.Undo() + " Escape again drops the outline being drawn.";
            RaiseSketch();
            return true;
        }

        if (openPanel is null && isSketchMode && sketch.IsDrawing)
        {
            sketch.DropChain();
            SketchMessage = "Dropped the outline being drawn. Escape again leaves the sketch.";
            RaiseSketch();
            return true;
        }

        if (openPanel is not null) openPanel.DialogResult = false;
        else if (IsSketchMode) IsSketchMode = false;
        else if (IsExtrudeMode) IsExtrudeMode = false;
        else if (IsConnectMode) IsConnectMode = false;
        else if (IsSplitMode) IsSplitMode = false;
        else if (IsMeasureMode) IsMeasureMode = false;
        else if (IsEngraveMode) IsEngraveMode = false;
        else if (IsEmbossMode) IsEmbossMode = false;
        else if (IsLayMode) IsLayMode = false;
        else if (IsPivotMode) IsPivotMode = false;
        else if (IsAlignFaceMode) IsAlignFaceMode = false;
        else if (IsCentreFaceMode) IsCentreFaceMode = false;
        else if (IsWallMountMode) IsWallMountMode = false;
        else if (IsSurfaceInfoMode) IsSurfaceInfoMode = false;
        else if (IsSubtractMode) IsSubtractMode = false;
        else return false;

        Status = "Canceled - nothing was changed";
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
    /// The plane's turn, as the same roll, pitch and yaw an object has: turns about X, Y and Z,
    /// applied in that order, measured from whichever axis the plane started on.
    ///
    /// Kept as three angles rather than worked out from the facing. Two would describe the
    /// facing completely - a plane has no third degree of freedom, and turning it about its own
    /// facing leaves the cut exactly where it was - but then the third ring would have nowhere
    /// to put what it was given, and there would be two rings for three boxes. A plane that
    /// remembers its own turn has three of each, and the one that changes nothing changes
    /// nothing, in the same way that spinning a cylinder about its axis does not move it.
    /// </summary>
    public double SplitRoll
    {
        get => splitTurn.X;
        set => TurnTo(new Vector3((float)value, splitTurn.Y, splitTurn.Z));
    }

    public double SplitPitch
    {
        get => splitTurn.Y;
        set => TurnTo(new Vector3(splitTurn.X, (float)value, splitTurn.Z));
    }

    public double SplitYaw
    {
        get => splitTurn.Z;
        set => TurnTo(new Vector3(splitTurn.X, splitTurn.Y, (float)value));
    }

    /// <summary>
    /// Turns the plane about a world axis, the way dragging one of its rings does.
    ///
    /// Composed onto the turn it already had rather than added to one of the three angles. The
    /// three are applied in order, so only the last lines up with the world and the other two
    /// turn the plane about its own axes; adding to them sends a second drag somewhere other
    /// than where the ring said it would go. The object handles learned this first.
    /// </summary>
    public void TurnSplitPlane(Axis about, double degrees)
    {
        float radians = (float)(degrees * Math.PI / 180.0);

        var turn = about switch
        {
            Axis.X => Matrix4x4.CreateRotationX(radians),
            Axis.Y => Matrix4x4.CreateRotationY(radians),
            _ => Matrix4x4.CreateRotationZ(radians)
        };

        TurnTo(MeshTransform.EulerFrom(MeshTransform.Rotation(splitTurn) * turn));
    }

    /// <summary>
    /// Puts the plane on a new turn without letting it wander off what it is cutting.
    ///
    /// The offset is measured along the facing, so a new facing on its own moves the plane
    /// bodily as well as turning it. Restating the offset through the point the plane already
    /// passes through leaves it where it is and only turns it.
    /// </summary>
    private void TurnTo(Vector3 angles)
    {
        Vector3 pivot = SplitPlanePoint();

        splitTurn = angles;
        SplitNormal = Vector3.Transform(PlaneSplit.NormalFor(splitAxis), MeshTransform.Rotation(angles));
        RecomputeSplitRange(SelectionWorldBounds(), splitNormal);
        SplitOffset = Vector3.Dot(splitNormal, pivot);

        RaiseSplitAngles();
        Status = $"Split plane turned {SplitRoll:0.##}, {SplitPitch:0.##}, {SplitYaw:0.##} deg";
    }

    private void RaiseSplitAngles()
    {
        Raise(nameof(SplitRoll));
        Raise(nameof(SplitPitch));
        Raise(nameof(SplitYaw));
    }

    /// <summary>The point of the plane nearest what it is cutting, which is what it turns about.</summary>
    private Vector3 SplitPlanePoint()
    {
        var bounds = SelectionWorldBounds();

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

        // The old app's round primitives were smooth at a glance; 32 facets read as a facet
        // count, not a curve. Torus stays at the default - it uses its segment count twice over
        // (round the ring and round the tube), so 100 there is 10,000 triangles rather than 100.
        int segments = kind is PrimitiveKind.Cylinder or PrimitiveKind.Cone or PrimitiveKind.Sphere
            ? 100 : Primitives.DefaultSegments;
        var mesh = Primitives.Create(kind, segments: segments);
        var o = new SceneObject(Scene.UniqueName(kind.ToString()), mesh)
        {
            Colour = NextAutomaticColour(),
            Origin = kind,
            IsPristine = true
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

        var dialog = new RepeatDialog(selection);
        if (dialog.ShowDialog() != true) return;

        if (dialog.RingResult is { } ring)
        {
            RepeatRound(selection, ring);
            return;
        }

        if (dialog.GridResult is { } grid)
        {
            var placed = RepeatArray.MakeGrid(selection, grid, Namer());
            if (placed.Count == 0) return;

            Undo.Execute(new AddObjectsCommand("Repeat in a grid", placed));
            RefreshSelection();
            Status = $"Repeated in a {grid.Columns} x {grid.Rows}{(grid.Layers > 1 ? $" x {grid.Layers}" : "")} grid - {placed.Count} new object(s)";
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
            Status = "Nothing to make a mold of";
            return;
        }

        var dialog = new MouldDialog(source.Name, study, model, CurrentPrinter.XyClearance);
        if (dialog.ShowDialog() != true) return;

        token = StartWork($"Molding {source.Name}");
        try
        {
            var result = await Task.Run(
                () => MouldBuilder.Build(model, dialog.Chosen, dialog.Options, token, Progress));

            if (result.Parts.Count == 0)
            {
                Status = "The mold came out empty";
                return;
            }

            var name = Namer();
            var made = result.Parts.Select(p => new SceneObject(name($"{source.Name} {p.Name}"), p.Mesh)
            {
                Colour = NextAutomaticColour()
            }.Centred()).ToList();

            AssembleNew($"{source.Name} mold", made);
            Undo.Execute(new AddObjectsCommand("Mold", made));
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
            Status = $"The mold failed: {ex.Message}";
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

        mover.Position += target.WorldCentre - mover.WorldCentre;

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

        // Through LocalCsg for Manifold first: a torn intersection measures the wrong volume.
        double volume = LocalCsg.Apply(picked[0].ToWorldMesh(), picked[1].ToWorldMesh(), BooleanOp.Intersect)
            .ComputeSignedVolume();

        Status = Math.Abs(volume) < 1e-6
            ? $"{picked[0].Name} and {picked[1].Name} touch but do not overlap - a clearance fit."
            : $"{picked[0].Name} and {picked[1].Name} overlap by {overlap.X:0.##} x {overlap.Y:0.##} "
              + $"x {overlap.Z:0.##} mm, {Math.Abs(volume) / 1000.0:0.###} cm3 of shared material.";
    }

    /// <summary>The last lithophane made, so the next starts where the last was left.</summary>
    private LithophaneOptions lastLithophane = new();

    /// <summary>
    /// A photograph as a lithophane: a thin plate carrying it as thickness, shown on the plate
    /// while the numbers are chosen and built at full detail only once it is added.
    ///
    /// The picture is asked for first. Everything in the panel is about a picture, and a panel
    /// that opens with nothing in it has nothing to say.
    /// </summary>
    private async Task InsertLithophane()
    {
        if (IsBusy) return;

        var chosen = new OpenFileDialog { Filter = PictureReader.Filter, Title = "A picture for the lithophane, or several for a lamp", Multiselect = true };
        if (chosen.ShowDialog() != true) return;

        var colour = NextAutomaticColour();
        SceneObject? shown = null;

        var dialog = new LithophaneDialog(chosen.FileNames, lastLithophane, mesh =>
        {
            if (mesh is null)
            {
                if (shown is not null) Scene.Objects.Remove(shown);
                shown = null;
                PreviewOnly = null;
                return;
            }

            if (shown is null)
            {
                shown = new SceneObject("Lithophane", mesh) { Colour = colour };
                Scene.Objects.Add(shown);

                // On the plate on its own, and not selected. Anything else standing there is
                // read as part of the picture, and the white outline of a selected lithophane
                // traces every feature edge - which is the picture itself.
                Scene.ClearSelection();
                PreviewOnly = [shown];
                RefreshSelection();
            }
            else
            {
                shown.Mesh = mesh;
            }

            // Standing on the plate, wherever the picture's own size has moved its middle to.
            shown.Position = shown.Position with { Z = shown.Position.Z - shown.WorldBounds.Min.Z };
        });

        bool accepted;
        try
        {
            accepted = dialog.ShowDialog() == true;
        }
        finally
        {
            // However it ended: the preview comes off the plate - it was never in the undo
            // history - and everything else is drawn again.
            PreviewOnly = null;
            if (shown is not null) Scene.Objects.Remove(shown);
        }
        if (!accepted || dialog.Result is not { } options || dialog.Picture is not { } picture) return;

        lastLithophane = options;

        // The real one is a few hundred thousand triangles: off the window's thread, with the
        // usual way out if it turns out to be more than anyone wanted to wait for.
        var token = StartWork($"Building {dialog.PictureName}");
        try
        {
            var pictures = dialog.Pictures;
            var built = await Task.Run(() => options.Shape == LithophaneShape.Lamp ? Lithophane.BuildLamp(pictures, options) : Lithophane.Build(picture, options), token);

            var o = new SceneObject(Scene.UniqueName(dialog.PictureName), built) { Colour = colour }.Centred();
            o.Position = o.Position with { Z = o.Position.Z - o.WorldBounds.Min.Z };

            Undo.Execute(new AddObjectsCommand($"Insert {dialog.PictureName}", [o]));
            RefreshSelection();

            var grid = Lithophane.Grid(picture.Width, picture.Height, options);
            Status = $"Added {dialog.PictureName} - {grid.Width:0.#} x {grid.Height:0.#} mm, "
                   + $"{Lithophane.GreyLevels(options)} grays, {built.TriangleCount:N0} triangles. It needs a light behind it.";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was added";
        }
        catch (Exception ex)
        {
            Status = $"The lithophane could not be built: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>The last custom shape added, so the next one starts from it rather than from scratch.</summary>
    private CustomShape lastCustom = CustomShape.Default;

    /// <summary>
    /// Inserts a shape with its size, segments and roundness chosen first, shown on the plate while
    /// they are set.
    ///
    /// The preview is a real object on the plate, added and taken away outside the undo history,
    /// so what is seen is exactly what Add puts there, lit and placed the same way.
    /// </summary>
    private void InsertCustom()
    {
        var colour = NextAutomaticColour();
        bool wireframeBefore = ShowWireframe;
        SceneObject? shown = null;

        var dialog = new CustomShapeDialog(lastCustom, wireframeBefore, mesh =>
        {
            if (mesh is null)
            {
                if (shown is not null) Scene.Objects.Remove(shown);
                shown = null;
                PreviewOnly = null;
                return;
            }

            float lift = mesh.ComputeBounds().Size.Z / 2f;
            if (shown is null)
            {
                shown = new SceneObject("Custom shape", mesh) { Colour = colour, Position = new Vector3(0, 0, lift) };
                Scene.Objects.Add(shown);
                PreviewOnly = [shown];
            }
            else
            {
                shown.Mesh = mesh;
                shown.Position = new Vector3(0, 0, lift);
            }
        }, on => ShowWireframe = on);

        bool accepted = dialog.ShowDialog() == true && dialog.Result is not null;

        if (shown is not null) Scene.Objects.Remove(shown);
        PreviewOnly = null;
        ShowWireframe = wireframeBefore;

        if (!accepted || dialog.Result is not { } shape) return;
        lastCustom = shape;

        var built = shape.Build();
        var o = new SceneObject(Scene.UniqueName(shape.Kind.ToString()), built)
        {
            Colour = colour,
            Origin = shape.Kind,
            IsPristine = true,
            Position = new Vector3(0, 0, built.ComputeBounds().Size.Z / 2f)
        };

        Undo.Execute(new AddObjectsCommand($"Insert custom {shape.Kind}", [o]));
        RefreshSelection();
        Status = $"Inserted a custom {shape.Kind.ToString().ToLowerInvariant()} - {built.TriangleCount:N0} triangles";
    }

    // --- Sketch ----------------------------------------------------------------------

    private readonly Sketch sketch = new();
    private bool isSketchMode;
    private SketchTool sketchTool = SketchTool.Line;
    private Vector2? sketchCursor;
    private int sketchSnapIndex = 2;
    private string sketchMessage = "";
    private string sketchReadout = "";
    private float sketchHeight = 10f;
    private float sketchDrawingWidth = 50f;
    private bool revolveAboutY = true;
    private float revolveAngle = 360f;
    private int revolveSegments = 64;

    /// <summary>Raised when the sketch or the pointer over it changes, so the plate can draw it.</summary>
    public event Action? SketchChanged;

    /// <summary>Asks the window to look from the top, or back at the isometric view.</summary>
    public event Action<bool>? LookFromTopRequested;

    /// <summary>
    /// Asks the window to put the camera back where it was before the sketch took it to the top.
    /// Not raised when a solid is made from the sketch: that goes round to a view of the solid.
    /// </summary>
    public event Action? ViewRestoreRequested;

    private bool sketchLeavesForSolid;

    /// <summary>The outlines being drawn, and the one in progress. Kept when the sketch is left, for another go.</summary>
    public Sketch CurrentSketch => sketch;

    public Vector2? SketchCursor => sketchCursor;

    /// <summary>
    /// Drawing outlines on the plate. Looked at from above, a click places a point instead of
    /// selecting, and the handles stand down; the sketch itself is kept when it is left, so an
    /// outline can be extruded one way and turned another.
    /// </summary>
    public bool IsSketchMode
    {
        get => isSketchMode;
        set
        {
            if (isSketchMode == value) return;

            Set(ref isSketchMode, value);
            if (!value)
            {
                sketch.Chain.Clear();
                sketchCursor = null;
                sketchDragged = null;
                if (!sketchLeavesForSolid) ViewRestoreRequested?.Invoke();
            }

            Raise(nameof(IsToolRunning));
            RaiseToolInHand();
            Raise(nameof(ShowManipulatorBar));
            RaiseSketch();
        }
    }

    public SketchTool CurrentSketchTool => sketchTool;

    public bool SketchLine { get => sketchTool == SketchTool.Line; set { if (value) SetSketchTool(SketchTool.Line); } }
    public bool SketchRectangle { get => sketchTool == SketchTool.Rectangle; set { if (value) SetSketchTool(SketchTool.Rectangle); } }
    public bool SketchCircle { get => sketchTool == SketchTool.Circle; set { if (value) SetSketchTool(SketchTool.Circle); } }
    public bool SketchArc { get => sketchTool == SketchTool.Arc; set { if (value) SetSketchTool(SketchTool.Arc); } }
    public bool SketchCurve { get => sketchTool == SketchTool.Curve; set { if (value) SetSketchTool(SketchTool.Curve); } }
    public bool SketchFreehand { get => sketchTool == SketchTool.Freehand; set { if (value) SetSketchTool(SketchTool.Freehand); } }

    private void SetSketchTool(SketchTool tool)
    {
        bool dropped = sketchTool != tool && sketch.Retool(sketchTool, tool);
        sketchTool = tool;
        Raise(nameof(SketchLine));
        Raise(nameof(SketchRectangle));
        Raise(nameof(SketchCircle));
        Raise(nameof(SketchArc));
        Raise(nameof(SketchCurve));
        Raise(nameof(SketchFreehand));

        string how = tool switch
        {
            SketchTool.Rectangle => "Click one corner, then the opposite corner.",
            SketchTool.Circle => "Click the middle, then a point on the edge.",
            SketchTool.Arc => sketch.IsDrawing
                ? "Click where the arc ends, then move to bend it and click again."
                : "Click where the arc starts and where it ends, then move to bend it and click again. Arcs and lines join into one outline.",
            SketchTool.Curve => "Click the points a smooth curve passes through. Click the first point again, or press Enter, to close it.",
            SketchTool.Freehand => "Press and drag to draw round the shape; let go to close it.",
            _ => "Click to place points. Click the first point again, or press Enter, to close the outline."
        };
        SayOfSketch(dropped ? "Dropped the outline being drawn. " + how : how);
    }

    /// <summary>The grid points are placed on: off, half a millimetre, one or five.</summary>
    public int SketchSnapIndex
    {
        get => sketchSnapIndex;
        set => Set(ref sketchSnapIndex, Math.Clamp(value, 0, 3));
    }

    private float SketchSnap => sketchSnapIndex switch { 1 => 0.5f, 2 => 1f, 3 => 5f, _ => 0f };

    public string SketchMessage
    {
        get => sketchMessage;
        private set => Set(ref sketchMessage, value);
    }

    public string SketchReadout
    {
        get => sketchReadout;
        private set => Set(ref sketchReadout, value);
    }

    public float SketchHeight
    {
        get => sketchHeight;
        set { if (float.IsFinite(value)) Set(ref sketchHeight, Math.Clamp(value, 0.1f, 2000f)); }
    }

    /// <summary>How wide a drawing loaded into the sketch comes in, since its own units are not worth trusting.</summary>
    public float SketchDrawingWidth
    {
        get => sketchDrawingWidth;
        set { if (float.IsFinite(value)) Set(ref sketchDrawingWidth, Math.Clamp(value, SvgImport.MinimumSize, 2000f)); }
    }

    public bool RevolveAboutY { get => revolveAboutY; set { Set(ref revolveAboutY, value); Raise(nameof(RevolveAboutX)); } }
    public bool RevolveAboutX { get => !revolveAboutY; set { Set(ref revolveAboutY, !value); Raise(nameof(RevolveAboutY)); } }

    public float RevolveAngle
    {
        get => revolveAngle;
        set { if (float.IsFinite(value)) Set(ref revolveAngle, Math.Clamp(value, 1f, 360f)); }
    }

    public int RevolveSegments
    {
        get => revolveSegments;
        set => Set(ref revolveSegments, Math.Clamp(value, 8, 256));
    }

    private void BeginSketch(SketchTool tool)
    {
        IsSketchMode = true;
        SetSketchTool(tool);
        LookFromTopRequested?.Invoke(true);
        Status = "Sketching on the plate - Escape or Done when finished";
    }

    private Vector2 Snapped(Vector2 point)
    {
        // A hand-drawn line snapped to a grid comes out as steps.
        float step = sketchTool == SketchTool.Freehand ? 0f : SketchSnap;
        return step > 0f ? new Vector2(MathF.Round(point.X / step) * step, MathF.Round(point.Y / step) * step) : point;
    }

    /// <summary>A click on the plate while sketching. <paramref name="onFirst"/>: it landed on the first point of the line.</summary>
    public void PlaceSketchPoint(Vector2 onPlate, bool onFirst)
    {
        var before = SketchState();
        string said = sketch.Place(Snapped(onPlate), sketchTool, onFirst);
        SayOfSketch(said);

        // A click that changed nothing was refused: the reason is shown over the plate, where the
        // click was, and not only in the small print of the panel.
        if (said.Length > 0 && sketchTool != SketchTool.Freehand && SketchState() == before) Warn(said);
    }

    private (int Loops, int Points, Vector2? ArcEnd) SketchState() => (sketch.Loops.Count, sketch.Chain.Count, sketch.ArcEnd);

    /// <summary>
    /// Finishes the outline with something. If that did not make one - it crosses itself, or has
    /// too few points - the reason is shown over the plate rather than only in the panel, so the
    /// right button is never a click that appears to do nothing.
    /// </summary>
    private void FinishSketch(Func<string> finish)
    {
        int outlines = sketch.Loops.Count;
        string said = finish();
        SayOfSketch(said);

        if (said.Length > 0 && sketch.Loops.Count == outlines) Warn(said);
    }

    public void MoveSketchCursor(Vector2 onPlate)
    {
        if (sketch.IsDrawingStroke) sketch.ExtendStroke(onPlate);
        sketchCursor = Snapped(onPlate);
        SketchReadout = sketch.Readout(sketchCursor.Value, sketchTool);
        SketchChanged?.Invoke();
    }

    public void CloseSketch()
    {
        if (!sketch.IsDrawing) return;

        FinishSketch(() => sketch.Close(sketchCursor));
    }

    private void PickSketchDrawing()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Drawing (*.svg)|*.svg|All files (*.*)|*.*",
            Title = "Load a drawing into the sketch"
        };
        if (dialog.ShowDialog() == true) LoadSketchDrawing(dialog.FileName);
    }

    /// <summary>
    /// The filled shapes of an SVG drawing as sketch outlines, as wide as <see cref="SketchDrawingWidth"/>.
    ///
    /// Read by the same reader Import and Emboss use, so the same things are left out: strokes, and
    /// text not turned to paths. Its lower left corner goes on the origin, which puts the whole
    /// drawing on the side of both axes a Revolve needs - centred, as Import puts a drawing, it
    /// would reach across both and could only be extruded. Shapes that overlap cannot be outlines
    /// of one sketch, so they are left out and counted rather than refusing the drawing.
    /// </summary>
    public void LoadSketchDrawing(string file)
    {
        string name = Path.GetFileName(file);
        List<TextShape> shapes;

        try
        {
            float aspect = SvgImport.Aspect(SvgImport.Outlines(file));
            if (aspect <= 0f)
            {
                SayOfSketch($"{name} has no filled shape in it - lines on their own have no area.");
                return;
            }

            shapes = SvgOutlines.Read(file, sketchDrawingWidth / aspect);
        }
        catch (Exception ex)
        {
            SayOfSketch($"Could not read {name}: {ex.Message}");
            return;
        }

        var loops = shapes.SelectMany(s => s.Holes.Prepend(s.Outline)).ToList();
        if (loops.Count == 0)
        {
            SayOfSketch($"{name} has no filled shape in it - lines on their own have no area.");
            return;
        }

        var corner = loops.SelectMany(l => l).Aggregate(new Vector2(float.MaxValue), Vector2.Min);
        var placed = loops.Select(l => (IReadOnlyList<Vector2>)l.Select(p => p - corner).ToList());

        var (added, leftOut) = sketch.AddOutlines(placed, 0.02f);
        SayOfSketch(added == 0
            ? $"Nothing from {name} could be used: every shape crosses itself or another."
            : $"Loaded {added} outline(s) from {name}, {sketchDrawingWidth:0.##} mm wide, its corner on the origin"
              + (leftOut > 0 ? $". {leftOut} left out for crossing another shape or itself - join overlapping shapes in the drawing program first." : ".")
              + " Undo takes it back.");
        Status = $"Loaded {name} into the sketch";
    }

    /// <summary>The pointer pressed on the plate with Freehand.</summary>
    public void BeginSketchStroke(Vector2 onPlate)
    {
        sketch.BeginStroke(onPlate);
        SayOfSketch("Drawing - let go to close the outline.");
    }

    /// <summary>The pointer let go: the stroke closes into an outline.</summary>
    public void EndSketchStroke() => SayOfSketch(sketch.EndStroke());

    public void UndoSketch() => SayOfSketch(sketch.Undo());

    /// <summary>The points of the sketch that can be dragged. See Sketch.Handles.</summary>
    public IReadOnlyList<(int Loop, int Index, Vector2 At)> SketchHandles() => sketch.Handles();

    private (int Loop, int Index)? sketchDragged;

    /// <summary>
    /// The lengths to write on the plate: the sides either side of the point being dragged, or
    /// what the next click would draw. Nothing while the pointer is off the plate.
    /// </summary>
    public IReadOnlyList<Sketch.Dimension> SketchDimensions() =>
        sketchCursor is not { } at ? []
        : sketchDragged is { } grabbed ? sketch.SidesAt(grabbed.Loop, grabbed.Index)
        : sketch.Dimensions(at, sketchTool);

    /// <summary>A sketch point being dragged, snapped as a placed one is.</summary>
    public void MoveSketchHandle(int loop, int index, Vector2 onPlate)
    {
        var to = Snapped(onPlate);
        sketchDragged = (loop, index);
        sketch.MoveHandle(loop, index, to);
        SayOfSketch($"{to.X:0.#}, {to.Y:0.#} mm");
    }

    /// <summary>The drag let go: a finished outline goes back if the move has spoiled it.</summary>
    public void SettleSketchHandle(int loop, int index, Vector2 from)
    {
        sketchDragged = null;
        string said = sketch.SettleHandle(loop, index, from);

        if (said.Length > 0) SayOfSketch(said);
        else RaiseSketch();
    }

    /// <summary>The right button: the outline being drawn is finished where it stands.</summary>
    public void EndSketchLine()
    {
        if (!sketch.IsDrawing) return;

        FinishSketch(() => sketch.EndLine(sketchCursor, sketchTool));
    }

    private void SayOfSketch(string message)
    {
        SketchMessage = message;
        RaiseSketch();
    }

    private void RaiseSketch()
    {
        Raise(nameof(CurrentSketch));
        SketchChanged?.Invoke();
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void ExtrudeSketch()
    {
        var solid = SketchSolids.Extrude(sketch, sketchHeight);
        if (solid.TriangleCount == 0)
        {
            SayOfSketch("Nothing to extrude - draw a closed outline first.");
            return;
        }

        PutSketchSolid(solid, "Extrusion", $"Extruded {sketch.Loops.Count} outline(s) {sketchHeight:0.##} mm");
    }

    private void RevolveSketch()
    {
        var solid = SketchSolids.Revolve(sketch, revolveAboutY ? RevolveAxis.Y : RevolveAxis.X, revolveAngle, revolveSegments, out string? why);
        if (solid is null)
        {
            SayOfSketch(why ?? "It cannot be revolved.");
            Status = why ?? "It cannot be revolved";
            return;
        }

        PutSketchSolid(solid, "Revolved", $"Revolved {revolveAngle:0.#} degrees about the {(revolveAboutY ? "Y" : "X")} axis");
    }

    /// <summary>
    /// The solid made from the sketch, put where it was drawn and standing on the plate, as one undo
    /// step; then out of the sketch and round to a view that shows it in three dimensions.
    /// </summary>
    private void PutSketchSolid(Mesh solid, string name, string said)
    {
        var o = new SceneObject(Scene.UniqueName(name), solid) { Colour = NextAutomaticColour() }.Centred();
        o.Position = o.Position with { Z = o.Position.Z - o.WorldBounds.Min.Z };

        sketchLeavesForSolid = true;
        IsSketchMode = false;
        sketchLeavesForSolid = false;
        Undo.Execute(new AddObjectsCommand(said, [o]));
        RefreshSelection();
        LookFromTopRequested?.Invoke(false);
        Status = $"{said} - the sketch is kept, for another go";
    }

    private HoleOptions lastHole = new();

    /// <summary>
    /// Screw holes or insert pockets: one, a row or a bolt circle, cut into the parts selected or
    /// added as a cutter to place and Subtract. With several selected - a lid on its box, plates to
    /// be bolted together - the holes go through all of them at once, lined up, so they join.
    ///
    /// The red cutter has the handles while the panel is open. It starts at the middle of the part's
    /// top, pointing down; moved and turned, the holes go wherever it is put - into a side, at an
    /// angle - and are cut where it stands. All the way through, each hole is as long as the part is
    /// deep along that hole, found by casting down its axis, so a nut pocket lands in the far face
    /// rather than somewhere past it. The cut is refused rather than kept if the part comes back with
    /// holes in its surface.
    /// </summary>
    private async Task InsertHole()
    {
        if (IsBusy) return;

        var targets = Scene.Selection.ToList();
        var target = targets.FirstOrDefault();

        // All of them as one, for the holes to be measured through: the last surface along a
        // hole's axis is the far side of the last part it goes through.
        var targetWorld = targets.Count == 0 ? null : Mesh.Combine(targets.Select(t => t.ToWorldMesh()));
        var targetBounds = targetWorld?.ComputeBounds();
        string? targetName = targets.Count switch { 0 => null, 1 => target!.Name, _ => $"the {targets.Count} selected objects, through all of them" };
        var red = new Vector3(0.88f, 0.3f, 0.28f);

        SceneObject? shown = null;
        HoleOptions showing = lastHole;
        bool showingThrough = false;
        bool rebuildQueued = false;

        // Where the cutter was left and what it was, read as the preview is taken away. The panel
        // takes it away as it closes - before the button's answer comes back here - so it has to be
        // read then: read afterwards, there was no cutter left to read, and Cut quietly did nothing.
        TransformState? place = null;

        // Each hole's cutter in the preview's own frame, the handles at its origin.
        List<Mesh>? Cutters(HoleOptions options, bool through, TransformState at)
        {
            var turn = MeshTransform.Rotation(at.Rotation);
            var down = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, turn));
            var cutters = new List<Mesh>();

            foreach (var station in HoleCutter.Stations(options))
            {
                float depth = options.Depth;
                bool comesOut = false;
                if (through && targetWorld is not null)
                {
                    // Cast from a little above the mouth, so a hole starting on the surface still
                    // sees it; where the ray misses the part, the depth typed stands.
                    var mouth = at.Position + Vector3.TransformNormal(new Vector3(station, 0f), turn);
                    if (HoleCutter.FarSide(targetWorld, mouth - down * 1f, down) is { } far && far > 1f)
                    {
                        depth = far - 1f;
                        comesOut = true;
                    }
                }

                if (HoleCutter.Build(options, depth, comesOut) is not { } one) return null;
                cutters.Add(MeshTransform.Transformed(one, Matrix4x4.CreateTranslation(station.X, station.Y, 0f)));
            }

            return cutters;
        }

        void Rebuild()
        {
            rebuildQueued = false;
            if (shown is null) return;
            if (Cutters(showing, showingThrough, TransformState.Capture(shown)) is { } cutters)
                shown.Mesh = Mesh.Combine(cutters);
        }

        var dialog = new HoleDialog(lastHole, targetName, (options, through) =>
        {
            if (options is null)
            {
                if (shown is not null)
                {
                    place = TransformState.Capture(shown);
                    Scene.Objects.Remove(shown);
                }

                shown = null;
                return true;
            }

            showing = options;
            showingThrough = through;

            var at = shown is not null
                ? TransformState.Capture(shown)
                : new TransformState(
                    targetBounds is { } reach
                        ? new Vector3(reach.Center.X, reach.Center.Y, reach.Max.Z)
                        : Vector3.Zero,
                    Vector3.Zero, Vector3.One);

            if (Cutters(options, through, at) is not { } cutters) return false;
            var mesh = Mesh.Combine(cutters);

            if (shown is null)
            {
                if (targetWorld is null) at = at with { Position = new Vector3(0, 0, -mesh.ComputeBounds().Min.Z) };
                shown = new SceneObject("Hole", mesh) { Colour = red };
                at.ApplyTo(shown);
                Scene.Objects.Add(shown);
                HoldPreview(shown);

                // The parts it goes into and the cutter, which holds the selection while the
                // panel is open - so the selection alone would leave the parts out.
                PreviewOnly = [.. targets, shown];

                // Through a part, how long each hole has to be depends on where it is and which
                // way it points, so a move or a turn builds the cutter again - once the drag has
                // stopped asking, rather than on every step of it.
                shown.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(SceneObject.Transform) || !showingThrough || targetWorld is null || rebuildQueued) return;
                    rebuildQueued = true;
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background, new Action(Rebuild));
                };
            }
            else
            {
                shown.Mesh = mesh;
            }

            return true;
        });

        bool accepted = dialog.ShowDialog() == true;

        if (shown is not null)
        {
            place = TransformState.Capture(shown);
            Scene.Objects.Remove(shown);
        }

        PreviewOnly = null;
        ReleasePreview();
        Scene.SelectOnly(target);
        foreach (var t in targets) t.IsSelected = true;
        RefreshSelection();

        if (!accepted || dialog.Result is not { } chosen || place is not { } where) return;
        lastHole = chosen;

        if (Cutters(chosen, dialog.Through, where) is not { } made) return;
        var frame = MeshTransform.Compose(where.Position, where.Rotation, where.Scale);

        string one = chosen.Kind == HoleKind.Insert
            ? $"{chosen.Size} insert pocket"
            : $"{chosen.Size}{chosen.Head switch { HoleHead.Countersunk => " countersunk", HoleHead.Counterbored => " counterbored", _ => "" }} hole";
        string what = made.Count == 1 ? $"an {one}" : $"{made.Count} {one}s";

        if (target is null || dialog.AddsCutter)
        {
            // One object however many holes, joined where they meet, so Subtract takes them all at once.
            var local = made.Count == 1 ? made[0] : ManifoldCsg.UnionAll(made) ?? Mesh.Combine(made);
            var cutter = new SceneObject(Scene.UniqueName(made.Count == 1 ? $"Cutter {one}" : $"Cutter {made.Count} x {one}"), local) { Colour = red };
            where.ApplyTo(cutter);
            cutter.Centred();

            Undo.Execute(new AddObjectsCommand("Insert hole cutter", [cutter]));
            RefreshSelection();
            Status = $"Added the cutter for {what} - select the part, then the cutter, and Subtract";
            return;
        }

        var token = StartWork(made.Count == 1 ? $"Cutting {what}" : $"Cutting {what}");
        try
        {
            var cutters = made.Select(m => MeshTransform.Transformed(m, frame)).ToList();
            var parts = targets.Select(t => (Object: t, World: t.ToWorldMesh())).ToList();
            var results = await Task.Run(() => parts.Select(p =>
            {
                // One at a time, each locally: a hole touches a little of the part, and cutting
                // each where it is costs what the hole costs rather than what the part does. A
                // part a hole misses is left alone.
                var box = p.World.ComputeBounds();
                var into = cutters.Where(c => Overlap(box, c.ComputeBounds())).ToList();
                if (into.Count == 0) return (Mesh?)null;

                var part = p.World;
                foreach (var cutter in into) part = LocalCsg.Subtract(part, cutter, token);
                return MeshHealer.Heal(part, token: token).Mesh;
            }).ToList());

            var cutParts = new List<SceneObject>();
            var replaced = new List<SceneObject>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (results[i] is not { } result) continue;
                var t = parts[i].Object;

                // All or nothing: half a stack drilled is worse than none.
                if (result.TriangleCount == 0 || !result.CheckHealth().IsWatertight)
                {
                    Status = $"The holes would not cut cleanly into {t.Name} - nothing was changed";
                    return;
                }

                replaced.Add(t);
                cutParts.Add(new SceneObject(t.Name, result) { Colour = t.Colour, Filament = t.Filament }.Centred());
            }

            if (replaced.Count == 0)
            {
                Status = "The holes miss everything selected - nothing was changed";
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand(made.Count == 1 ? "Hole" : "Holes", replaced, cutParts));
            RefreshSelection();
            Status = replaced.Count == 1 ? $"Cut {what} into {replaced[0].Name}" : $"Cut {what} through {replaced.Count} objects";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"The holes could not be cut: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    private static bool Overlap(Bounds a, Bounds b) =>
        a.Min.X <= b.Max.X && b.Min.X <= a.Max.X && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

    private bool panelHandles;
    private GizmoMode modeBeforeHandles;

    /// <summary>
    /// Whether the handles are on a tool's preview while its panel is open: Thread and Hole let
    /// what they are making be moved and turned into place before it is made. Resizing stays off -
    /// a thread or a hole is the size its numbers say.
    /// </summary>
    public bool PanelHandles => panelHandles;

    /// <summary>Selects a tool's preview and gives it the move and rotate handles.</summary>
    private void HoldPreview(SceneObject preview, bool resizable = false)
    {
        Scene.SelectOnly(preview);

        // A Library part whose sizes are settings can be resized as well: the stretch becomes the
        // settings when it is let go. See SettleHeldSize.
        if (resizable != heldResizable || preview != heldForSizing)
        {
            if (heldForSizing is not null) heldForSizing.PropertyChanged -= OnHeldResized;
            heldResizable = resizable;
            heldForSizing = resizable ? preview : null;
            if (heldForSizing is not null) heldForSizing.PropertyChanged += OnHeldResized;
            Raise(nameof(ResizeOffered));
        }

        if (!panelHandles)
        {
            modeBeforeHandles = GizmoMode;
            if (GizmoMode == GizmoMode.Scale && !resizable) GizmoMode = GizmoMode.Move;
            panelHandles = true;
            Raise(nameof(PanelHandles));
            Raise(nameof(ResizeOffered));
            Raise(nameof(ShowManipulatorBar));
        }

        RefreshSelection();
    }

    private bool heldResizable;
    private SceneObject? heldForSizing;

    /// <summary>
    /// A size typed into the boxes is settled at once; a handle being dragged is left alone until
    /// it is let go, when the window settles it - the stretch is the preview while it lasts.
    /// </summary>
    private void OnHeldResized(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SceneObject.Transform) || System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) return;
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background, new Action(SettleHeldSize));
    }

    private void ReleasePreview()
    {
        if (heldForSizing is not null) heldForSizing.PropertyChanged -= OnHeldResized;
        heldForSizing = null;
        heldResizable = false;

        if (!panelHandles) return;

        panelHandles = false;
        Raise(nameof(PanelHandles));
        Raise(nameof(ResizeOffered));
        Raise(nameof(ShowManipulatorBar));
        GizmoMode = modeBeforeHandles;
    }

    /// <summary>
    /// Wraps the selection in one skin, as cling film pulled tight round it would: two cylinders
    /// become a slot, a row of spheres a rounded bar, and one hollow part the solid it fits inside.
    /// The pieces are replaced by it, as Merge replaces them, in one undo step.
    /// </summary>
    private void HullSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var points = selection.SelectMany(o => o.ToWorldMesh().Positions).Distinct().ToList();
        var triangles = ConvexHull.Build(points);
        if (triangles.Count == 0)
        {
            Status = "The selection is flat - there is nothing to wrap";
            return;
        }

        var mesh = new Mesh();
        foreach (var (a, b, c) in triangles) mesh.AddTriangle(points[a], points[b], points[c]);
        mesh = mesh.Welded();

        if (!mesh.CheckHealth().IsWatertight)
        {
            Status = "The hull would not close - nothing was changed";
            return;
        }

        var hull = new SceneObject(Scene.UniqueName("Hull"), mesh) { Colour = selection[0].Colour }.Centred();
        Undo.Execute(new ReplaceObjectsCommand("Hull", selection, [hull]));
        RefreshSelection();
        Status = $"Wrapped {(selection.Count == 1 ? selection[0].Name : $"{selection.Count} objects")} in a hull - {mesh.TriangleCount:N0} triangles";
    }

    /// <summary>What the lettering panel was last left at, so the next starts from it.</summary>
    private TextOptions lastText = new();

    /// <summary>
    /// Lettering as an object of its own, from the panel: shown on the plate as it is typed, as a
    /// custom shape is, and put down as one undo step, standing on the plate in the middle.
    /// </summary>
    private void InsertText()
    {
        var colour = NextAutomaticColour();
        SceneObject? shown = null;

        var dialog = new TextDialog(lastText, EmbossFonts, mesh =>
        {
            if (shown is not null) Scene.Objects.Remove(shown);
            shown = null;
            if (mesh is null) return;

            shown = new SceneObject("Text", mesh) { Colour = colour }.Centred();
            shown.Position = shown.Position with { Z = shown.Position.Z - shown.WorldBounds.Min.Z };
            Scene.Objects.Add(shown);
            PreviewOnly = [shown];
        });

        bool accepted = dialog.ShowDialog() == true;
        if (shown is not null) Scene.Objects.Remove(shown);
        PreviewOnly = null;

        if (!accepted || dialog.Result is not { } chosen) return;
        lastText = chosen;

        var built = TextObject.Build(chosen);
        if (built.TriangleCount == 0) return;

        var o = new SceneObject(Scene.UniqueName(TextObject.NameFor(chosen)), built) { Colour = colour }.Centred();
        o.Position = o.Position with { Z = o.Position.Z - o.WorldBounds.Min.Z };

        Undo.Execute(new AddObjectsCommand("Insert text", [o]));
        RefreshSelection();
        Status = $"Inserted {o.Name} - {built.TriangleCount:N0} triangles";
    }

    // --- The pivot ------------------------------------------------------------------------

    /// <summary>While this is on, the next click on the selected object sets its pivot.</summary>
    public bool IsPivotMode
    {
        get => isPivotMode;
        set
        {
            if (isPivotMode == value) return;

            Set(ref isPivotMode, value);
            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
            RaiseToolInHand();
        }
    }

    private void BeginPivot()
    {
        if (Scene.Selection.Count != 1) return;

        IsSplitMode = false;
        IsSubtractMode = false;
        IsEngraveMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        IsPivotMode = true;

        Status = "Click the point to measure and turn this object about";
    }

    /// <summary>
    /// Puts the object's pivot on a point of it that was clicked.
    ///
    /// The pivot is the origin, so this slides the geometry one way and the position the other:
    /// nothing moves on the plate, but from now on the position boxes read that point, a turn
    /// goes round it, and lining this part up with another compares it rather than the middle of
    /// its box. Which is what a gear wants - it is about its shaft, not about the outline of its
    /// teeth - and a good deal else besides: a hinge, a lid, an arm on a linkage.
    /// </summary>
    public bool TakePivot(SceneObject target, Vector3 worldPoint)
    {
        if (!isPivotMode) return false;

        // Nothing happens to something that is not the one being worked on, and saying so beats
        // leaving the tool looking broken - which, with nothing on the plate moving, it does.
        if (!Scene.Selection.Contains(target))
        {
            Status = $"{target.Name} is not the object being given a pivot - pick a point on "
                   + $"{Scene.Selection[0].Name}, or press Escape";
            return false;
        }

        if (!Matrix4x4.Invert(target.Transform, out var toLocal))
        {
            Status = "That object is flattened to nothing, so it has no inside to pivot about";
            return false;
        }

        var local = Vector3.Transform(worldPoint, toLocal);

        Undo.Execute(new PivotCommand("Set pivot", target, local, own: true, wasOwn: target.PivotIsOwn));
        IsPivotMode = false;
        RefreshSelection();

        Status = $"{target.Name} turns about {ObjectPivotX:0.##}, {ObjectPivotY:0.##}, "
               + $"{ObjectPivotZ:0.##} {UnitLabel} from its middle";

        return true;
    }

    /// <summary>Puts the pivot back in the middle of the object's box, where it starts out.</summary>
    private void PivotToCentre()
    {
        if (Scene.Selection.Count != 1) return;

        var target = Scene.Selection[0];
        var centre = target.Mesh.ComputeBounds().Center;

        if (!target.PivotIsOwn && centre.LengthSquared() < 1e-10f)
        {
            Status = $"{target.Name} already turns about its middle";
            return;
        }

        Undo.Execute(new PivotCommand("Pivot to center", target, centre, own: false, wasOwn: target.PivotIsOwn));
        RefreshSelection();
        Status = $"{target.Name} turns about its middle again";
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

        // With Each off an assembly goes down as one block, so its parts keep their places relative
        // to each other: set down one by one the roof would land on the plate beside the walls.
        // A part outside any assembly, and every part with Each on, is set down by itself.
        foreach (var block in selection.GroupBy(o => o.Assembly is { } assembly && !EachOnItsOwn ? (object)assembly : o))
        {
            var parts = block.ToList();
            float bottom = parts.Min(o => o.WorldBounds.Min.Z);

            foreach (var o in parts) o.Position = o.Position with { Z = o.Position.Z - bottom };
        }

        if (TransformCommand.CreateIfChanged("Align to plate", selection, before) is { } command)
            Undo.Execute(command);

        RefreshSelection();
        Status = "Aligned to the build plate";
    }

    /// <summary>How far Drop down sinks a part into the one under it, when asked to, for Merge to join them.</summary>
    public const float DropOverlap = 0.2f;

    /// <summary>
    /// Moves the selection straight down, as one, until it rests on whatever is under it - or on
    /// the plate, when nothing is. With <paramref name="overlap"/> it goes a little way into the
    /// part it lands on, so the two share material rather than a face and Merge joins them into
    /// one solid; meeting face to face, the union is left to decide whether they touch.
    /// </summary>
    private void DropDown(bool overlap)
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var moving = Mesh.Combine(selection.Select(o => o.ToWorldMesh()));
        var reach = moving.ComputeBounds();

        // Only what is under some of it can be landed on.
        (SceneObject Object, float Distance)? under = null;
        foreach (var o in Scene.Objects)
        {
            if (o.IsSelected || o.IsHidden) continue;
            var b = o.WorldBounds;
            if (b.Min.X > reach.Max.X || reach.Min.X > b.Max.X || b.Min.Y > reach.Max.Y || reach.Min.Y > b.Max.Y || b.Min.Z >= reach.Max.Z) continue;

            if (VerticalDrop.Distance(moving, o.ToWorldMesh()) is { } d && (under is null || d < under.Value.Distance))
                under = (o, d);
        }

        float drop;
        string said;
        if (under is { } landed && landed.Distance <= reach.Min.Z + 1e-4f)
        {
            if (landed.Distance <= 1e-3f)
            {
                Status = $"Already resting on {landed.Object.Name} - nothing was moved";
                return;
            }

            drop = landed.Distance + (overlap ? DropOverlap : 0f);
            said = overlap
                ? $"Dropped onto {landed.Object.Name}, {DropOverlap:0.#} mm into it - Merge to join them"
                : $"Dropped onto {landed.Object.Name}";
        }
        else
        {
            drop = reach.Min.Z;
            said = "Nothing under it - dropped to the plate";
            if (MathF.Abs(drop) < 1e-4f)
            {
                Status = "Already on the plate, with nothing under it - nothing was moved";
                return;
            }
        }

        var before = selection.Select(TransformState.Capture).ToList();
        foreach (var o in selection) o.Position = o.Position with { Z = o.Position.Z - drop };

        if (TransformCommand.CreateIfChanged("Drop down", selection, before) is { } command)
            Undo.Execute(command);

        RefreshSelection();
        Status = said;
    }

    /// <summary>
    /// Shrinks the selection to the printable area if it is too big for it, keeping a margin clear
    /// round the edge, and stands it in the middle of the bed. It never makes anything bigger:
    /// a part that already fits is only moved, so Fit is safe to press on anything.
    /// </summary>
    private void FitToBed()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var before = selection.Select(TransformState.Capture).ToList();
        float ratio = BedPlacement.FitRatio(BedPlacement.Reach(selection).Size, plateWidth, plateDepth, plateHeight);
        BedPlacement.Fit(selection, ratio);

        if (TransformCommand.CreateIfChanged("Fit to plate", selection, before) is { } command)
            Undo.Execute(command);

        RefreshSelection();
        ZoomExtentsRequested?.Invoke();

        Status = ratio < 1f
            ? $"Scaled to {ratio * 100f:0.#}% to fit the plate, and centered on it"
            : "Centered on the plate - it already fitted, so nothing was scaled";
    }

    /// <summary>
    /// Sets the selection out in rows across the bed, a gap apart. The parts move while the gap is
    /// being typed and go back where they were if the panel is cancelled.
    /// </summary>
    private void DistributeOnBed()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count < 2) return;

        var before = selection.Select(TransformState.Capture).ToList();

        // The face each would best be printed on, worked out once and only if asked for: judging
        // every part takes a moment, and the gap is retyped over and over.
        Vector3?[]? faces = null;
        void Weigh()
        {
            if (faces is not null) return;

            faces = new Vector3?[selection.Count];
            var cursor = System.Windows.Input.Mouse.OverrideCursor;
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    before[i].ApplyTo(selection[i]);
                    var world = selection[i].ToWorldMesh();
                    var ranked = BestFace.Rank(world, RestingFaces.Find(world), overhangAngle);
                    faces[i] = ranked.Count > 0 ? ranked[0].Normal : null;
                }
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = cursor;
            }
        }

        Vector2 Arrange(float gap, bool bestFace, bool drop)
        {
            for (int i = 0; i < selection.Count; i++) before[i].ApplyTo(selection[i]);

            if (bestFace)
            {
                Weigh();
                for (int i = 0; i < selection.Count; i++)
                    if (faces![i] is { } facing) TurnOnto(selection[i], before[i], facing);
            }

            return BedPlacement.Distribute(selection, gap, plateWidth, drop);
        }

        var panel = new DistributeDialog(
            selection.Count, distributeGap, distributeBestFace, distributeDrop,
            new Vector2(plateWidth, plateDepth), Arrange);
        if (panel.ShowDialog() != true || panel.Result is not { } chosen)
        {
            for (int i = 0; i < selection.Count; i++) before[i].ApplyTo(selection[i]);
            RefreshSelection();
            return;
        }

        distributeGap = chosen;
        distributeBestFace = panel.BestFace;
        distributeDrop = panel.Drop;
        var covers = Arrange(chosen, panel.BestFace, panel.Drop);

        if (TransformCommand.CreateIfChanged("Distribute", selection, before) is { } command)
            Undo.Execute(command);

        RefreshSelection();
        Status = covers.X <= plateWidth + 0.01f && covers.Y <= plateDepth + 0.01f
            ? $"Distributed {selection.Count} objects {chosen:0.##} mm apart"
            : $"Distributed {selection.Count} objects {chosen:0.##} mm apart - they cover {covers.X:0} x {covers.Y:0} mm, more than the plate";
    }

    /// <summary>Whether anything is hidden, for the list's Show all.</summary>
    public bool AnyHidden => Scene.Objects.Any(o => o.IsHidden);

    /// <summary>Whether anything is locked, for the list's Unlock all.</summary>
    public bool AnyLocked => Scene.Objects.Any(o => o.IsLocked);

    public void HideSelection() => SetHidden(Scene.Selection.ToList(), true);

    public void ShowAll() => SetHidden(Scene.Objects.Where(o => o.IsHidden).ToList(), false);

    public void LockSelection() => SetLocked(Scene.Selection.ToList(), true);

    public void UnlockAll() => SetLocked(Scene.Objects.Where(o => o.IsLocked).ToList(), false);

    /// <summary>
    /// Hides or shows objects. Not an undo step: it changes what is in view, not the model, and a
    /// Ctrl+Z that brought back a hidden part instead of the last cut would be taking back the
    /// wrong thing. The project is marked changed, since the file keeps it.
    /// </summary>
    private void SetHidden(IReadOnlyList<SceneObject> objects, bool hidden)
    {
        if (objects.Count == 0) return;

        foreach (var o in objects) o.IsHidden = hidden;
        AfterHidingOrLocking();

        Status = hidden
            ? $"Hid {Count(objects)} - Alt+H shows everything again"
            : $"Showing {Count(objects)} again";
    }

    private void SetLocked(IReadOnlyList<SceneObject> objects, bool locked)
    {
        if (objects.Count == 0) return;

        foreach (var o in objects) o.IsLocked = locked;
        AfterHidingOrLocking();

        Status = locked
            ? $"Locked {Count(objects)} - nothing can select or change it until it is unlocked (Alt+L unlocks everything)"
            : $"Unlocked {Count(objects)}";
    }

    private static string Count(IReadOnlyList<SceneObject> objects) =>
        objects.Count == 1 ? objects[0].Name : $"{objects.Count} objects";

    private void AfterHidingOrLocking()
    {
        IsDirty = true;
        RaiseHiddenAndLocked();
        RefreshSelection();
    }

    private void RaiseHiddenAndLocked()
    {
        Raise(nameof(AnyHidden));
        Raise(nameof(AnyLocked));
    }

    private void SelectAll()
    {
        foreach (var o in Scene.Objects) o.IsSelected = true;
        RefreshSelection();
    }

    /// <summary>
    /// Lines the selection up along an axis. The parameter is "axis:mode", for example "X:Centre".
    /// </summary>
    /// <summary>Whether the Align button for this axis and mode has anything to do.</summary>
    private bool CanAlign(object? parameter)
    {
        // By picks: an assembly picked by its name is one thing to line up, however many parts.
        int count = Scene.SelectionInPicks.Count;
        bool distribute = parameter is string text && text.EndsWith(":Distribute", StringComparison.Ordinal);
        return distribute ? count >= 3 : count >= 1;
    }

    /// <summary>The bed as a box: its middle at the origin, its surface at Z = 0, as the bed itself is drawn.</summary>
    private Bounds PlateBounds => new(
        new Vector3(-plateWidth / 2f, -plateDepth / 2f, 0f), new Vector3(plateWidth / 2f, plateDepth / 2f, plateHeight));

    /// <summary>
    /// Lines the selection up along an axis. The parameter is "axis:mode", for example "X:Centre".
    ///
    /// Everything moves to match the last object picked - the same object <c>Align to</c> moves
    /// towards and Subtract cuts with, so which one stays put follows the same rule everywhere in
    /// the app rather than happening to be whichever already sits furthest along the axis. With
    /// one object selected there is nothing else to match, so it lines up on the bed instead.
    ///
    /// An assembly picked by its name is one block, on either side: lined up against, it stays
    /// put and the rest line up with its box; lined up itself, every part moves by the same amount
    /// and the assembly keeps its shape.
    /// </summary>
    private void Align(object? parameter)
    {
        if (parameter is not string text) return;
        var parts = text.Split(':');
        if (parts.Length != 2) return;
        if (!Enum.TryParse<Axis>(parts[0], out var axis)) return;
        if (!Enum.TryParse<AlignMode>(parts[1], out var mode)) return;

        var picks = Scene.SelectionInPicks;
        if (picks.Count == 0) return;
        if (mode == AlignMode.Distribute && picks.Count < 3) return;

        var selection = picks.SelectMany(p => p).ToList();
        var before = selection.Select(TransformState.Capture).ToList();
        var offsets = AlignTools.BlockOffsets(picks, axis, mode, PlateBounds);

        for (int i = 0; i < picks.Count; i++)
            foreach (var o in picks[i]) o.Position += offsets[i];

        if (TransformCommand.CreateIfChanged($"Align {axis} {mode}", selection, before) is { } command)
        {
            Undo.Execute(command);
            Status = mode switch
            {
                AlignMode.Distribute => $"Spread {picks.Count} objects evenly along {axis}",
                _ when picks.Count == 1 => $"Aligned {NameOfPick(picks[0])} to {mode} on the plate's {axis}",
                _ => $"Aligned {picks.Count - 1} object(s) to {mode} on {axis}, against {NameOfPick(picks[^1])}"
            };
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

    /// <summary>
    /// The colour dot in the objects list. It paints the object it is on, or the whole selection
    /// when that object is part of it - as a swatch would - and leaves the selection as it was, so
    /// a part can be recoloured without losing what is selected. A locked part keeps its colour.
    /// </summary>
    private void PickColourFor(SceneObject o)
    {
        if (o.IsLocked) return;

        var targets = o.IsSelected ? Scene.Selection.ToList() : [o];
        var dialog = new ColourDialog(o.Colour) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } picked) return;

        ApplyColour(picked, targets);
    }

    private void ApplyColour(Vector3 colour, List<SceneObject>? targets = null)
    {
        var selection = targets ?? Scene.Selection.ToList();
        if (selection.Count == 0) return;

        string hex = Palette.ToHex(colour);
        string label = selection.Count == 1 ? "Color" : $"Color {selection.Count} objects";

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
        });

        bool accepted = dialog.ShowDialog() == true && dialog.Result is not null;

        // Whatever the preview left behind, the undo step has to start from where the user did.
        for (int i = 0; i < roundable.Count; i++)
        {
            roundable[i].Mesh = before[i].Mesh;
            roundable[i].Scale = before[i].Scale;
        }

        if (!accepted || dialog.Result is not { } radius) return;

        var edges = dialog.Edges;
        int arcSteps = dialog.Bevel ? 1 : 5;
        var produced = roundable.Select(o =>
        {
            var size = new Vector3(o.SizeX, o.SizeY, o.SizeZ);
            var mesh = RoundedPrimitives.Create(o.Origin!.Value, size, radius, edges, arcSteps);

            // Scale is deliberately left at its default: the mesh is already the right size.
            return new SceneObject(o.Name, mesh)
            {
                Position = o.Position,
                Rotation = o.Rotation,
                Colour = o.Colour,
                Origin = o.Origin,
                IsPristine = true
            };
        }).ToList();

        Undo.Execute(new ReplaceObjectsCommand(dialog.Bevel ? "Bevel edges" : "Round edges", roundable, produced));
        RefreshSelection();

        string which = edges == RoundEdges.All ? "all edges" : edges.ToString().ToLowerInvariant();
        string done = dialog.Bevel ? "Bevelled" : "Rounded";
        Status = skipped == 0
            ? $"{done} {which} of {produced.Count} object(s) to {radius:0.##} mm"
            : $"{done} {produced.Count} object(s); {skipped} skipped - only a cube or cylinder can be rounded";
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
        ShareClipboard();

        Status = selection.Count == 1 ? "Copied 1 object" : $"Copied {selection.Count} objects";
    }

    /// <summary>Copies the selection and takes it away, as one step that Ctrl+Z puts back.</summary>
    private void Cut()
    {
        var selection = Scene.Selection;
        if (selection.Count == 0) return;

        clipboard.Clear();
        foreach (var o in selection) clipboard.Add(o.Clone());
        ShareClipboard();

        int taken = selection.Count;
        Undo.Execute(new DeleteObjectsCommand(selection));
        RefreshSelection();

        Status = taken == 1 ? "Cut 1 object" : $"Cut {taken} objects";
    }

    /// <summary>
    /// Puts the clipboard back exactly where it was taken from.
    ///
    /// It used to arrive shifted along X, on the reasoning that a copy hidden inside its original
    /// looks like nothing happened. That is true of the copy and false of everything else a paste
    /// is for: pasting into another project, or back after a Cut, or onto a part that has to line
    /// up with what it was measured against. Moving it was the app deciding a position it had no
    /// business deciding, and there was no way to ask for the real one. Duplicate is still there
    /// for a copy set beside the original.
    ///
    /// Cloned again on the way out, so pasting twice does not hand out the same instance twice.
    /// </summary>
    private void Paste()
    {
        TakeSharedClipboard();
        if (clipboard.Count == 0) return;

        var name = Namer();
        var pasted = clipboard.Select(source =>
        {
            var copy = source.Clone();
            copy.Name = name(source.Name);
            return copy;
        }).ToList();

        Undo.Execute(new AddObjectsCommand("Paste", pasted));
        RefreshSelection();

        // Said plainly, because what has just landed is invisible when it has landed on top of
        // what it was copied from - and it is the paste that is selected, so a drag moves it.
        Status = pasted.Count == 1
            ? "Pasted 1 object where it came from"
            : $"Pasted {pasted.Count} objects where they came from";
    }

    // --- The clipboard shared with other windows of the app ---------------------------

    /// <summary>What the objects are put on the Windows clipboard as. Only this app reads it.</summary>
    private const string ClipboardFormat = "3DFastCraft.Objects";

    /// <summary>
    /// The mark put with this window's last copy. A paste that finds it on the clipboard uses the
    /// copies already held here rather than reading them back.
    /// </summary>
    private Guid sharedCopy;

    /// <summary>
    /// Puts the copied objects on the Windows clipboard as well, so another window of the app can
    /// paste them. Best effort: the clipboard can be held by another program for a moment, and a
    /// copy within this window still works without it.
    /// </summary>
    private void ShareClipboard()
    {
        try
        {
            sharedCopy = Guid.NewGuid();
            var bytes = SceneSerializer.ToBytes(clipboard);
            var data = new System.Windows.DataObject();
            data.SetData(ClipboardFormat, new MemoryStream([.. sharedCopy.ToByteArray(), .. bytes]));
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch (Exception)
        {
            // Left to this window alone.
        }
    }

    private static bool SharedClipboardHasObjects()
    {
        try { return System.Windows.Clipboard.ContainsData(ClipboardFormat); }
        catch (Exception) { return false; }
    }

    /// <summary>Objects copied in another window of the app, taken in place of this window's own.</summary>
    private void TakeSharedClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.GetData(ClipboardFormat) is not MemoryStream stream) return;

            var all = stream.ToArray();
            if (all.Length <= 16) return;
            var mark = new Guid(all.AsSpan(0, 16));
            if (mark == sharedCopy && clipboard.Count > 0) return;

            var objects = SceneSerializer.FromBytes(all[16..]);
            if (objects.Count == 0) return;

            clipboard.Clear();
            clipboard.AddRange(objects);
            sharedCopy = mark;
        }
        catch (Exception ex)
        {
            Status = $"Could not read what was copied in the other window: {ex.Message}";
        }
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
    /// tell them apart again afterwards. Use Merge on the Edit tab for a true boolean union.
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
            Colour = selection[0].Colour,

            // Remembered now, because in a moment there will be nothing left to ask. A group of
            // cylinders is the usual cutter for a row of dowel holes, and it was refused a
            // clearance for want of this one fact.
            PiecesTakeClearance = selection.All(o => o.CanTakeClearance)
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
        // With an assembly picked by its name, it is the assembly that comes apart, and its parts
        // keep their shapes: splitting every part into its pieces as well would be two things at
        // once, and the assembly's own Ungroup and this one would do different things.
        if (Scene.Assemblies.Any(a => a.IsSelected))
        {
            UngroupAssemblies();
            return;
        }

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

            // The last pick cuts: one object, or every part of an assembly picked by its name.
            var (targets, cutters) = Scene.SplitLastPick();

            if (clearance > 0f && op == BooleanOp.Subtract)
            {
                var awkward = cutters.FirstOrDefault(c => !c.CanTakeClearance);
                if (awkward is not null)
                {
                    // Not refused outright: growing by axis is only exact on a cube, a cylinder
                    // or a sphere, but it is a fair approximation on anything roughly round about
                    // its own centre - a gear about its axis, say - since most of the surface
                    // then sits at much the same distance from the middle. Asked rather than
                    // assumed, because on an eccentric shape it is a poor one.
                    var sure = MessageBox.Show(
                        $"\"{awkward.Name}\" is not a cube, a cylinder or a sphere, so growing it "
                        + "by a clearance is not exact.\n\n"
                        + "On a sloped or off-axis face - a cone, a pyramid, a wedge - it delivers "
                        + "less than the number typed, by roughly the cosine of the slope: a real "
                        + "risk of a gap too tight to fit. On a shape that is roughly round about "
                        + "its own center - a gear about its own axis - most of the surface sits "
                        + "at much the same distance from the middle, so it comes out close to "
                        + "even.\n\n"
                        + "Grow it anyway?",
                        "3DFastCraft", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

                    if (sure != MessageBoxResult.Yes)
                    {
                        Status = $"{awkward.Name} was not grown - a clearance is not exact on it";
                        return;
                    }
                }
            }

            if (op == BooleanOp.Subtract)
            {
                await SubtractFromEach(targets, cutters, clearance, token);
                return;
            }

            var meshes = selection.Select(o => o.ToWorldMesh()).ToList();

            var result = await Task.Run(() =>
            {
                var accumulator = meshes[0];
                for (int i = 1; i < meshes.Count; i++)
                    // Locally where the cutter is small: drilling a 10 mm hole in a 300,000
                    // triangle mould half took a minute and a half through the whole engine,
                    // nearly all of it building a tree over material the cutter cannot reach.
                    accumulator = LocalCsg.Apply(accumulator, meshes[i], op, token);

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
                    "That operation left no geometry behind.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var combined = new SceneObject(Scene.UniqueName(op.ToString()), result)
            {
                Colour = selection[0].Colour
            }.Centred();

            // Kept cutters are removed and put back, so they end up after the result in the
            // list. Their geometry is untouched: what was grown was a copy made for the cut.
            Undo.Execute(new ReplaceObjectsCommand(op.ToString(), selection, [combined]));
            RefreshSelection();

            var health = result.CheckHealth();
            Status = $"{op}: {health.TriangleCount:N0} triangles, {health.Describe()}";
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
    /// Takes the cutter out of every target, each one on its own.
    ///
    /// The last object picked is the cutter and everything picked before it is cut. That is the
    /// rule 3D Builder taught everyone, and it is the one that covers every case: select all and
    /// then re-pick the cutter, and you have 3D Builder's "take it out of whatever it touches"
    /// without the part that makes that rule dangerous - it reaching objects nobody selected.
    ///
    /// Each target comes back as itself. It used to be one boolean folded over the whole
    /// selection, which turned two cubes and a pin into a single object called "Subtract": two
    /// halves of an assembly fused into one thing, and the second cube gone as a part.
    ///
    /// An assembly picked last cuts with every one of its parts, one after another. Not as one
    /// mesh: parts of an assembly touch and overlap, and overlapping pieces handed to the boolean
    /// as one solid is exactly what tears it.
    /// </summary>
    private async Task SubtractFromEach(
        IReadOnlyList<SceneObject> targets, IReadOnlyList<SceneObject> cutters, float clearance, CancellationToken token)
    {
        // Grown once. Every target is cut by the same tools, and growing them per target would be
        // the same arithmetic on the same meshes as many times as there are parts.
        var tools = cutters.Select(c => c.ToWorldMeshGrown(clearance)).ToList();
        var subjects = targets.Select(o => o.ToWorldMesh()).ToList();

        var results = await Task.Run(() =>
        {
            var cut = new List<Mesh>(subjects.Count);
            foreach (var subject in subjects)
            {
                var worked = subject;
                foreach (var tool in tools)
                {
                    token.ThrowIfCancellationRequested();
                    if (worked.TriangleCount == 0) break;

                    // Locally where the cutter is small: a pin against a scan is a thousandth of
                    // it, and the whole engine would build a tree over the other nine hundred and
                    // ninety.
                    worked = LocalCsg.Apply(worked, tool, BooleanOp.Subtract, token);

                    // Mended before it is handed on. A boolean splits one polygon without always
                    // splitting the one beside it, which leaves the two sides of an edge
                    // disagreeing about where their corners are - closed to look at, torn as a
                    // list of triangles - and the next cut is the one that would trip on it.
                    worked = MeshHealer.Heal(worked, token: token).Mesh;
                }

                cut.Add(worked);
            }

            return cut;
        });

        var kept = new List<SceneObject>();
        var swallowed = new List<string>();
        var successors = new List<(SceneObject From, SceneObject To)>();

        for (int i = 0; i < targets.Count; i++)
        {
            if (results[i].TriangleCount == 0)
            {
                // All of it was inside the cutter. Dropped rather than left as an empty object,
                // and named in the status line so it is not a part that quietly went missing.
                swallowed.Add(targets[i].Name);
                continue;
            }

            var made = new SceneObject(targets[i].Name, results[i])
            {
                Colour = targets[i].Colour,
                Origin = targets[i].Origin,
                PiecesTakeClearance = targets[i].PiecesTakeClearance
            }.Centred();

            kept.Add(made);
            successors.Add((targets[i], made));
        }

        if (kept.Count == 0)
        {
            Status = "Subtract removed everything - nothing left to keep";
            MessageBox.Show(
                "Subtracting left nothing behind: every part was inside the cutter.\n\n"
                + "The last object you click is the cutter; everything picked before it is what "
                + "gets cut.",
                "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // The cutter is taken off and put back after the results, so it ends up below them in the
        // list. Putting it back without taking it off first listed the same object twice.
        List<SceneObject> added = subtractKeepsCutter ? [.. kept, .. cutters] : [.. kept];
        List<SceneObject> removed = [.. targets, .. cutters];

        // Each cut part in the place of the one it was cut from: the cutter can be in another
        // assembly, or in none, and a guess from the whole lot would then put nothing anywhere.
        Undo.Execute(new ReplaceObjectsCommand("Subtract", removed, added, successors));
        RefreshSelection();

        int torn = kept.Count(o => !o.Mesh.CheckHealth().IsWatertight);
        string tolerance = clearance > 0f ? $", {clearance:0.##} mm tolerance" : "";
        string keeping = subtractKeepsCutter ? ", cutter kept" : "";
        string gone = swallowed.Count > 0
            ? $", {string.Join(", ", swallowed)} entirely inside it and dropped"
            : "";
        string health = torn == 0
            ? "all watertight"
            : $"{torn} of {kept.Count} not watertight - Repair may mend them";

        Status = $"Subtract {NameOfPick(cutters)} from {kept.Count} object(s)"
               + $"{tolerance}{keeping}{gone}: {health}";
    }

    /// <summary>
    /// Turns the selection into shells of a chosen wall thickness, to save material.
    /// </summary>
    private async Task HollowSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var dialog = new HollowDialog(selection);
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
    /// Cuts the selection into a web of struts along the walls of a Voronoi tessellation.
    ///
    /// Everything is worked out on the same grid the rebuilder and the hollower use, so nothing
    /// is subtracted from anything and the result comes back watertight however torn a web of
    /// hundreds of thin struts would have left a boolean. See <see cref="Voronoi"/>.
    /// </summary>
    private async Task VoronoiSelection()
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        // Taken once: the outline is redrawn on every slider move, and baking each object to
        // world space each time would copy every triangle for nothing.
        var world = selection.Select(o => o.ToWorldMesh()).ToList();

        var dialog = new VoronoiDialog(selection, options =>
        {
            VoronoiOutline = options is { } wanted
                ? world.SelectMany(m => Voronoi.Outline(m, wanted)).ToList()
                : [];
        });

        bool accepted = dialog.ShowDialog() == true;
        VoronoiOutline = [];

        if (!accepted || dialog.Result is not { } settings) return;

        if (IsBusy) return;

        var token = StartWork("Cutting cells");
        try
        {
            // Baked to world space, as the other grid-based tools are: the grid is in world
            // millimetres, so struts on a stretched object would otherwise come out stretched.
            var webs = await Task.Run(() => world
                .Select(m => Voronoi.Build(m, settings, token, Progress))
                .ToList());

            var produced = new List<SceneObject>();
            var consumed = new List<SceneObject>();

            for (int i = 0; i < selection.Count; i++)
            {
                if (webs[i].Refusal is not null || webs[i].Mesh.TriangleCount == 0) continue;

                consumed.Add(selection[i]);
                produced.Add(new SceneObject(selection[i].Name, webs[i].Mesh)
                {
                    Colour = selection[i].Colour,
                    Filament = selection[i].Filament
                }.Centred());
            }

            if (produced.Count == 0)
            {
                Status = webs.Select(w => w.Refusal).FirstOrDefault(r => r is not null)
                         ?? "Nothing came out of it";
                MessageBox.Show(Status, "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Undo.Execute(new ReplaceObjectsCommand("Voronoi", consumed, produced));
            RefreshSelection();

            long triangles = produced.Sum(o => (long)o.Mesh.TriangleCount);
            int cells = webs.Where(w => w.Refusal is null).Sum(w => w.Cells);

            float kept = webs.Where(w => w.Refusal is null).Average(w => w.Kept);

            // The share of material left is the one number that says whether these settings made
            // a web or a block with holes in it, and nothing else on screen says it.
            Status = $"{cells} cells, {settings.StrutMm:0.##} mm struts on a {webs[0].VoxelMm:0.###} mm grid"
                   + $" - {kept:P0} of the material kept, {triangles:N0} triangles"
                   + (webs.Sum(w => w.Loose) is var loose && loose > 0
                       ? $"; {loose} piece(s) are not joined to the rest" : "")
                   + (selection.Count > produced.Count ? $"; {selection.Count - produced.Count} were refused" : "");
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Voronoi failed: {ex.Message}";
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

        var dialog = new SimplifyDialog(selection);
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
        var targets = Scene.Selection.Count > 0 ? Scene.Selection.ToList() : Scene.Workable.ToList();
        if (targets.Count == 0) return;

        var dialog = new RebuildDialog(targets);
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

    /// <summary>The last value each shape-bending tool was used with, so a second part done to match starts there.</summary>
    private readonly Dictionary<string, (float Value, Axis Axis)> lastDeform = new()
    {
        ["Twist"] = (90f, Axis.X),
        ["Taper"] = (50f, Axis.X),
        ["Bend"] = (45f, Axis.X)
    };

    private static readonly DeformSpec TwistSpec = new(
        "Twist", "Twisting", "It turns about its upright axis, more the higher up; the bottom stays where it is.",
        "Turn", "degrees at the top", -720, 720, 15, 0, "clockwise", "anticlockwise", false,
        (world, value, _) => MeshDeform.Twist(world, value));

    private static readonly DeformSpec TaperSpec = new(
        "Taper", "Tapering", "It narrows or widens toward the top, about its upright axis; the bottom keeps its size.",
        "Top", "% of the bottom", MeshDeform.SmallestTaper * 100, 300, 5, 100, "narrower", "wider", false,
        (world, value, _) => MeshDeform.Taper(world, value / 100f));

    private static readonly DeformSpec BendSpec = new(
        "Bend", "Bending", "It curves over as if round a pipe; the bottom stays where it is and the top leans over.",
        "Bend", "degrees", -360, 360, 5, 0, "the other way", "toward +X or +Y", true,
        (world, value, axis) => MeshDeform.Bend(world, value, axis),
        (world, value, axis) => MeshDeform.Folds(world, value, axis)
            ? $"Bent this far the inside of the curve would fold through itself. This shape takes at most "
              + $"{MeshDeform.LargestBend(world, axis):0}° toward {axis}; a taller or thinner shape takes more."
            : null);

    private void TwistSelection() => DeformSelection(TwistSpec);

    private void TaperSelection() => DeformSelection(TaperSpec);

    private void BendSelection() => DeformSelection(BendSpec);

    /// <summary>
    /// Twists, tapers or bends each selected object, shown on the plate as the value is set.
    ///
    /// Each object is worked in plate coordinates, so a part lying on its side twists about the
    /// plate's upright, as it is seen, not about the axis it was modelled on.
    /// </summary>
    private void DeformSelection(DeformSpec spec)
    {
        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var before = selection.Select(o => o.Mesh).ToList();
        var worlds = selection.Select(o => o.ToWorldMesh()).ToList();
        var (start, startAxis) = lastDeform[spec.Title];

        string subject = selection.Count == 1 ? selection[0].Name : $"{selection.Count} objects";
        var dialog = new DeformDialog(spec, subject, worlds, start, startAxis, changed =>
        {
            for (int i = 0; i < selection.Count; i++)
            {
                if (changed is null)
                {
                    selection[i].Mesh = before[i];
                    continue;
                }

                // Shown through the object's own transform, so the preview is brought back into
                // its coordinates; what is kept is rebuilt from the plate coordinates instead.
                selection[i].Mesh = Matrix4x4.Invert(selection[i].Transform, out var toLocal)
                    ? MeshTransform.Transformed(changed[i], toLocal)
                    : before[i];
            }
        });

        bool accepted = dialog.ShowDialog() == true && dialog.Result is not null;

        for (int i = 0; i < selection.Count; i++) selection[i].Mesh = before[i];

        if (!accepted || dialog.Result is not { } value || Math.Abs(value - spec.Identity) < 1e-3) return;

        var axis = dialog.ResultAxis;
        lastDeform[spec.Title] = (value, axis);

        var produced = selection.Select((o, i) =>
            new SceneObject(o.Name, spec.Deform(worlds[i], value, axis)) { Colour = o.Colour }.Centred()).ToList();

        string amount = $"{value:0.#}{(spec == TaperSpec ? "%" : "°")}";
        Undo.Execute(new ReplaceObjectsCommand($"{spec.Title} {amount}", selection, produced));
        RefreshSelection();

        Status = $"{spec.Title} {amount} on {produced.Count} object(s) - {produced.Sum(o => o.Mesh.TriangleCount):N0} triangles";
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
        });

        bool accepted = dialog.ShowDialog() == true && dialog.Result is { } settings && settings.Passes > 0;

        // Whatever the preview left on the plate, undo has to start from where the user did.
        var after = selection.Select(o => o.Mesh).ToList();
        for (int i = 0; i < selection.Count; i++) selection[i].Mesh = before[i];

        if (!accepted)
        {
            Status = "Smoothing canceled";
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
    /// Two passes, quietly, because there is nothing for anyone to choose between them. The local
    /// one runs first: it welds, caps holes and turns faces round, and is the whole answer for a
    /// mesh that merely arrived torn. Whatever it cannot close goes to the engine Windows carries,
    /// which re-solves the shell instead of patching it and so can settle a surface that passes
    /// through itself. Both are measured; neither result is kept unless it is less broken than
    /// what went in.
    ///
    /// Objects neither pass can help are left exactly as they were and counted, rather than being
    /// quietly replaced by something no better.
    /// </summary>
    private async Task RepairObjects()
    {
        var targets = (Scene.Selection.Count > 0 ? Scene.Selection.ToList() : Scene.Workable.ToList())
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

            // Step two, for whatever the local pass could not close. Nothing is asked here because
            // there is nothing to decide: the result either comes back measurably less broken, in
            // which case it is what the object becomes, or it does not and nothing has changed.
            var mended = new Mesh?[targets.Count];
            string? windowsProblem = null;
            int byWindows = 0;

            for (int i = 0; i < targets.Count && WindowsRepair.IsAvailable; i++)
            {
                if (healed[i].After.IsWatertight) continue;
                token.ThrowIfCancellationRequested();

                Progress.Report(WorkProgress.Doing(targets.Count == 1
                    ? "Asking Windows' own repair engine"
                    : $"Asking Windows' own repair engine about {targets[i].Name}"));

                var best = healed[i].Improved ? healed[i].Mesh : targets[i].Mesh;
                var attempt = await WindowsRepair.TryRepairAsync(best, token);

                // Kept in case nothing works: a machine without the engine has to read
                // differently from a model that is genuinely past mending.
                windowsProblem ??= attempt.Problem;
                if (attempt.Mesh is null) continue;

                // Welded before it is judged. What comes back is somebody else's idea of a mesh
                // and its corners need not be shared, and an unwelded mesh reads as nothing but
                // open edges - a sound repair would be thrown away for looking like a colander.
                var judged = await Task.Run(() =>
                {
                    var welded = attempt.Mesh.Welded();
                    return (Mesh: welded, Damage: Damage(welded.CheckHealth()));
                }, token);

                // Somebody else's engine, so the answer is measured rather than believed - the
                // same bar the local pass has to clear before its result is kept.
                if (judged.Damage < Damage(best.CheckHealth()))
                {
                    mended[i] = judged.Mesh;
                    byWindows++;
                }
            }

            var replaced = new List<SceneObject>();
            var produced = new List<SceneObject>();
            int fixedUp = 0, beyond = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                var mesh = mended[i] ?? (healed[i].Improved ? healed[i].Mesh : null);
                if (mesh is null) { beyond++; continue; }

                replaced.Add(targets[i]);
                produced.Add(new SceneObject(targets[i].Name, mesh)
                {
                    Position = targets[i].Position,
                    Rotation = targets[i].Rotation,
                    Scale = targets[i].Scale,
                    Colour = targets[i].Colour,
                    Origin = targets[i].Origin
                }.Centred());

                if (mesh.CheckHealth().IsWatertight) fixedUp++;
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

                // Only the path where nothing worked says the second pass happened at all. Told
                // here it saves someone trying the same model in a slicer and finding out the
                // hard way; said on every success it would be noise about a step they did not ask
                // for and do not have to think about.
                string alsoTried =
                    windowsProblem is not null
                        ? windowsProblem + Environment.NewLine + Environment.NewLine
                    : WindowsRepair.IsAvailable
                        ? "Windows' own repair engine - the one slicers offer as a last resort - "
                          + "was tried as well, and could not close it either."
                          + Environment.NewLine + Environment.NewLine
                        : "";

                MessageBox.Show(
                    $"This model has {worst.Describe().ToLowerInvariant()}, and repairing it would "
                    + "leave it worse than it is, so nothing has been changed." + Environment.NewLine + Environment.NewLine
                    + "Filling holes and turning faces round only works when the damage is local. "
                    + "A mesh whose surface passes through itself has no well-defined inside, and "
                    + "patching it piece by piece tears more than it closes." + Environment.NewLine + Environment.NewLine
                    + alsoTried
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

            // Said only when it did something. It is not a setting and not a choice, but it does
            // explain why a repair that used to fail now takes longer and works.
            if (byWindows > 0)
                Status += byWindows == replaced.Count && replaced.Count == 1
                    ? " - by Windows' own repair engine"
                    : $" - {byWindows} of them by Windows' own repair engine";
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

    /// <summary>
    /// How broken a mesh is, in one number, so that two repairs of the same object can be put
    /// side by side. Triangle count deliberately plays no part: a repair is free to add or drop
    /// as many as it likes, and only the bad edges say whether it helped.
    /// </summary>
    private static int Damage(MeshHealth health) =>
        health.BoundaryEdges + health.NonManifoldEdges + health.InconsistentEdges;

    private void BeginEngrave()
    {
        if (Scene.Selection.Count != 1) return;

        // Every one of these claims the click on the object, so only one can be on.
        IsSplitMode = false;
        IsSubtractMode = false;
        IsEmbossMode = false;
        IsMeasureMode = false;
        IsLayMode = false;
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        IsEngraveMode = true;
        if (!EngraveIsStuds) EngravePattern = PatternKind.Studs;
        Status = "Click the face to put studs on";
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
        await NotifyApplyingAsync();

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
        IsAlignFaceMode = false;
        IsCentreFaceMode = false;
        IsSplitMode = true;
        ResetSplitOffset();
        Status = "Drag the arrows to slide the split plane, the rings to tilt it";
    }

    private void ResetSplitOffset()
    {
        if (Scene.Selection.Count == 0) return;

        RecomputeSplitRange(SelectionWorldBounds(), splitNormal);
        SplitOffset = (SplitMinimum + SplitMaximum) / 2f;
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
        await NotifyApplyingAsync();

        var selection = Scene.Selection.ToList();
        if (selection.Count == 0) return;

        var meshes = selection.Select(o => o.ToWorldMesh()).ToList();
        var normal = splitNormal;
        float offset = splitOffset;
        var keep = SplitKeep;
        bool joining = splitWithConnectors;
        var options = connectors;

        bool bricks = options.Style == ConnectorStyle.Bricks;

        // Asked before anything is cut, since no amount of room fixes a pin that runs along the cut.
        // Brick studs stand square to the cut whatever the direction says, so they are not asked.
        if (joining && !bricks && Connectors.Axis(normal, options.Direction) is null)
        {
            string which = options.Direction == ConnectorDirection.Vertical ? "Vertical" : "Horizontal";
            string runs = options.Direction == ConnectorDirection.Vertical ? "upright" : "level";
            Status = $"{which} connectors cannot cross this cut";
            MessageBox.Show(
                $"{which} connectors cannot cross this cut.\n\n"
                + $"The cut is too near {runs} itself, so they would lie along it rather than go through it. "
                + "Choose Square to cut, or turn the plane.",
                "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsBusy) return;

        var token = StartWork(selection.Count == 1 ? "Splitting" : $"Splitting {selection.Count} objects");
        try
        {
            var halves = await Task.Run(() => meshes.Select(mesh =>
            {
                var (front, back) = PlaneSplit.Split(mesh, normal, offset, keep, token);
                if (!joining || front is null || back is null) return new SplitOutcome(front, back, [], 0, false, 0f, 0f);

                if (bricks)
                {
                    return Connectors.JoinBricks(front, back, normal, options, token) is { } built
                        ? new SplitOutcome(built.Front, built.Back, [], built.Studs, false, 0f, 0f)
                        : new SplitOutcome(front, back, [], 0, true, 0f, 0f);
                }

                // Placed on the whole object's section, then cut into the halves: the place a pin
                // can go is a question about the cut face, which both halves share.
                var layout = Connectors.Survey(mesh, normal, offset, options);
                if (layout.Points.Count == 0)
                    return new SplitOutcome(front, back, [], 0, false, layout.ThickestWall, layout.WallNeeded);

                return Connectors.Join(front, back, layout.Points, normal, options, token) is { } joined
                    ? new SplitOutcome(joined.Front, joined.Back, joined.Pins, layout.Points.Count, false, layout.ThickestWall, layout.WallNeeded, layout.BreaksOut)
                    : new SplitOutcome(front, back, [], 0, true, layout.ThickestWall, layout.WallNeeded);
            }).ToList());

            // Connectors asked for, and not one fits anywhere: nothing is split, and the two numbers
            // say why. It used to split plain with a note in the status line, which is how a hollow
            // box came back with no pins and nobody the wiser as to what was wrong with it.
            if (joining && halves.Any(h => h.Front is not null && h.Back is not null)
                && halves.All(h => h.Placed == 0 && !h.Refused))
            {
                if (bricks)
                {
                    float needs = BrickStuds.StudDiameter + 2 * BrickStuds.Wall(options.BrickFit);
                    Status = "No brick stud fits on the cut face - nothing was split";
                    MessageBox.Show(
                        "No brick stud fits on this cut face, so nothing was split.\n\n"
                        + $"A stud keeps the upper half's wall clear of it, so it needs a circle about {needs:0.#} mm across "
                        + "inside the cut face of both halves, on the 8 mm grid. A thin wall above a wide ledge "
                        + "takes none: there is nowhere above it for the socket.\n\n"
                        + "Move the plane to a part where both halves are wide, or use pins or pegs.",
                        "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                float thickest = halves.Max(h => h.Thickest);
                float needed = halves.Max(h => h.WallNeeded);
                float wall = MathF.Max(options.EdgeDistance, Connectors.MinimumWall);

                Status = "No room for connectors on the cut face - nothing was split";
                MessageBox.Show(
                    "No connector fits on this cut face, so nothing was split.\n\n"
                    + $"A {options.Diameter:0.##} mm connector with {options.Clearance:0.##} mm clearance and "
                    + $"{wall:0.##} mm of wall either side needs solid at least {needed:0.#} mm thick. "
                    + $"The thickest this cut face gets is about {thickest:0.#} mm.\n\n"
                    + "Make the diameter or From edge smaller, move the plane to a thicker part, "
                    + "or use the plain Split tool.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (joining && halves.Any(h => h.BreaksOut) && !GoThrough(options))
            {
                Status = "Nothing was split";
                return;
            }

            var consumed = new List<SceneObject>();
            var produced = new List<SceneObject>();
            int missed = 0;
            int placed = 0;
            var tooSmall = new List<string>();
            var torn = new List<string>();
            bool fewer = false;

            for (int i = 0; i < selection.Count; i++)
            {
                var source = selection[i];
                var (front, back, pins, count, refused, _, _, _) = halves[i];

                if (joining && front is not null && back is not null)
                {
                    placed += count;
                    if (refused) torn.Add(source.Name);
                    else if (count == 0) tooSmall.Add(source.Name);
                    else if (count < options.Count) fewer = true;
                }

                // Pins lie flat beside the part they belong to, in a row, printed on their side so
                // the layers run along them and not across.
                var box = source.WorldBounds;
                for (int p = 0; p < pins.Count; p++)
                {
                    produced.Add(new SceneObject($"{source.Name} pin {p + 1}", pins[p])
                    {
                        Colour = source.Colour,
                        Origin = PrimitiveKind.Cylinder,
                        IsPristine = true,
                        Rotation = new Vector3(0f, 90f, 0f),
                        Position = new Vector3(
                            box.Max.X + 10f + options.Depth,
                            box.Min.Y + options.Radius + p * (options.Diameter + 4f),
                            options.Radius)
                    });
                }

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

            // The pieces are separate objects, to be moved, printed and used on their own - not an
            // assembly. A split part that was in an assembly still leaves its pieces in it.
            Undo.Execute(new ReplaceObjectsCommand("Split", consumed, produced));
            IsSplitMode = false;
        IsSubtractMode = false;
            RefreshSelection();

            string joined = !joining ? ""
                : torn.Count > 0 ? $"; connectors would have torn {string.Join(", ", torn)}, so it was split plain"
                : tooSmall.Count > 0 && placed == 0 ? "; the cut face has no room for a connector"
                : $"; {placed} {options.Style switch { ConnectorStyle.Pins => "pin(s), made beside the parts", ConnectorStyle.Bricks => "brick stud(s)", _ => "peg(s)" }}"
                  + (fewer || tooSmall.Count > 0 ? " - fewer than asked, the face has no room for more" : "");

            Status = (missed == 0
                ? $"Split {consumed.Count} object(s) into {produced.Count} piece(s)"
                : $"Split {consumed.Count} object(s) into {produced.Count} piece(s); the plane missed {missed}") + joined;
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

    /// <summary>While this is on, the Connect objects panel is open and nothing has been cut yet.</summary>
    public bool IsConnectMode
    {
        get => isConnectMode;
        set
        {
            if (isConnectMode == value) return;

            Set(ref isConnectMode, value);
            if (!value) connectContact = null;
            Raise(nameof(IsToolRunning));
            Raise(nameof(ShowManipulatorBar));
            RaiseToolInHand();
        }
    }

    /// <summary>
    /// Where the connectors would land, for the viewport to mark. Read off the section rather than
    /// from a cut, so it costs about what moving the plane costs and can follow the settings.
    /// </summary>
    public IReadOnlyList<Connectors.Mark> ConnectorMarks()
    {
        if (isSplitMode && splitWithConnectors && Scene.Selection.Count == 1)
            return Connectors.Preview(Scene.Selection[0].ToWorldMesh(), SplitNormal, SplitOffset, connectors);

        if (isConnectMode && Contact() is { } contact)
        {
            var picked = Scene.SelectionInPickOrder;
            var front = contact.SecondIsFront ? picked[1] : picked[0];
            var back = contact.SecondIsFront ? picked[0] : picked[1];
            return Connectors.Preview(front.ToWorldMesh(), back.ToWorldMesh(), contact, connectors);
        }

        return [];
    }

    /// <summary>
    /// The face the two picked parts rest against each other on, worked out when the panel opens.
    ///
    /// Kept rather than asked for again, because finding it now reads the triangles rather than
    /// the two boxes and the marks are redrawn whenever a setting changes. Nothing can move
    /// underneath it: the handles are off while a tool is running.
    /// </summary>
    private Connectors.Contact? connectContact;

    /// <summary>The face two picked parts rest against each other on, or null.</summary>
    private Connectors.Contact? Contact() =>
        Scene.SelectionInPickOrder.Count == 2 ? connectContact : null;

    /// <summary>
    /// The plane the connectors cross, while a tool is setting them up: the cut, or the face two
    /// parts share. The viewport lays the marks on it and fades whichever half is on top, since a
    /// mark inside solid material is a mark nobody can see.
    /// </summary>
    public (Vector3 Normal, float Offset)? ConnectorPlane =>
        isSplitMode && splitWithConnectors && Scene.Selection.Count == 1 ? (SplitNormal, SplitOffset)
        : isConnectMode && Contact() is { } contact ? (contact.Normal, contact.Offset)
        : null;

    /// <summary>Which face the two parts share, said in the panel so the user can see it was the right one.</summary>
    public string ConnectSummary
    {
        get => connectSummary;
        private set => Set(ref connectSummary, value);
    }

    private void BeginConnect()
    {
        var picked = Scene.SelectionInPickOrder;
        if (picked.Count != 2) return;

        if (Connectors.SharedFace(picked[0].ToWorldMesh(), picked[1].ToWorldMesh()) is not { } contact)
        {
            Status = "Those two do not rest against each other";
            MessageBox.Show(
                $"\"{picked[0].Name}\" and \"{picked[1].Name}\" do not rest against each other on a flat face.\n\n"
                + $"Connect objects joins two parts that touch face to face - one standing on the other, or side by side - "
                + $"square to the plate and no more than {Connectors.ContactGap:0.#} mm apart. "
                + "Drop one onto the other first, or use Split with connectors on a single part.",
                "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string face = contact.Normal.Z > 0.5f ? "the level face"
            : contact.Normal.X > 0.5f ? "the face square to X"
            : "the face square to Y";

        ConnectSummary = $"Connectors go through {face} \"{picked[0].Name}\" and \"{picked[1].Name}\" share"
                       + (contact.Gap > 0.01f ? $", {contact.Gap:0.##} mm apart." : ".");

        connectContact = contact;
        IsConnectMode = true;
        Status = ConnectSummary;
    }

    /// <summary>
    /// Pins or pegs through the face two parts rest against each other on, each part kept as itself.
    ///
    /// Split with connectors with the split already done: the same survey, read inside both parts
    /// at once so a connector goes only where both have material, and the same cutting.
    /// </summary>
    /// <summary>
    /// Connect objects with brick studs: studs on the lower part's face, the shallow underside in
    /// the upper part's, both on one grid. Each part is kept as itself.
    /// </summary>
    private async Task ConnectWithBricks(List<SceneObject> picked, Connectors.Contact contact, ConnectorOptions options)
    {
        var front = contact.SecondIsFront ? picked[1] : picked[0];
        var back = contact.SecondIsFront ? picked[0] : picked[1];
        var frontMesh = front.ToWorldMesh();
        var backMesh = back.ToWorldMesh();

        var token = StartWork("Connecting");
        try
        {
            var joined = await Task.Run(() => Connectors.JoinBricks(frontMesh, backMesh, contact.Normal, options, token));

            if (joined is not { } done)
            {
                Status = "Connecting would have torn one of the parts - nothing was changed";
                return;
            }

            if (done.Studs == 0)
            {
                Status = "No brick stud fits where the two meet - nothing was changed";
                MessageBox.Show(
                    "No brick stud fits where these two meet, so nothing was changed.\n\n"
                    + "A stud needs room for itself and the upper part's wall round it, on the 8 mm grid. "
                    + "Use pins or pegs for a narrow joint.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var added = new List<SceneObject>
            {
                new SceneObject(front.Name, done.Front) { Colour = front.Colour }.Centred(),
                new SceneObject(back.Name, done.Back) { Colour = back.Colour }.Centred()
            };

            AssembleNew($"{front.Name} and {back.Name}", added, [front, back]);
            Undo.Execute(new ReplaceObjectsCommand("Connect with brick studs", [front, back], added));
            IsConnectMode = false;
            RefreshSelection();
            Status = $"Connected {front.Name} and {back.Name} with {done.Studs} brick stud(s)";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"Connect failed: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    private async Task ApplyConnect()
    {
        await NotifyApplyingAsync();

        var picked = Scene.SelectionInPickOrder.ToList();
        if (picked.Count != 2 || IsBusy) return;
        if (connectContact is not { } contact) return;

        var options = connectors;
        if (options.Style == ConnectorStyle.Bricks)
        {
            await ConnectWithBricks(picked, contact, options);
            return;
        }

        if (Connectors.Axis(contact.Normal, options.Direction) is null)
        {
            string which = options.Direction == ConnectorDirection.Vertical ? "Vertical" : "Horizontal";
            MessageBox.Show(
                $"{which} connectors cannot cross the face these two share - they would run along it. Choose Square to cut.",
                "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var front = contact.SecondIsFront ? picked[1] : picked[0];
        var back = contact.SecondIsFront ? picked[0] : picked[1];
        var frontMesh = front.ToWorldMesh();
        var backMesh = back.ToWorldMesh();

        var token = StartWork("Connecting");
        try
        {
            var (layout, joined) = await Task.Run(() =>
            {
                var survey = Connectors.Survey(frontMesh, backMesh, contact, options);
                return survey.Points.Count == 0
                    ? (survey, null)
                    : (survey, Connectors.Join(frontMesh, backMesh, survey.Points, contact.Normal, options, token));
            });

            if (layout.Points.Count == 0)
            {
                float wall = MathF.Max(options.EdgeDistance, Connectors.MinimumWall);
                Status = "No room for connectors where the two meet - nothing was changed";
                MessageBox.Show(
                    "No connector fits where these two meet, so nothing was changed.\n\n"
                    + $"A {options.Diameter:0.##} mm connector with {options.Clearance:0.##} mm clearance and "
                    + $"{wall:0.##} mm of wall either side needs both parts to be at least {layout.WallNeeded:0.#} mm thick "
                    + $"in the same place. Where they meet, the thickest that gets is about {layout.ThickestWall:0.#} mm.\n\n"
                    + "Make the diameter or From edge smaller.",
                    "3DFastCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (layout.BreaksOut && !GoThrough(options))
            {
                Status = "Nothing was changed";
                return;
            }

            if (joined is not { } done)
            {
                Status = "Connecting would have torn one of the parts - nothing was changed";
                return;
            }

            var added = new List<SceneObject>
            {
                new SceneObject(front.Name, done.Front) { Colour = front.Colour }.Centred(),
                new SceneObject(back.Name, done.Back) { Colour = back.Colour }.Centred()
            };

            var box = front.WorldBounds.Union(back.WorldBounds);
            for (int p = 0; p < done.Pins.Count; p++)
            {
                added.Add(new SceneObject($"{front.Name} pin {p + 1}", done.Pins[p])
                {
                    Colour = front.Colour,
                    Origin = PrimitiveKind.Cylinder,
                    IsPristine = true,
                    Rotation = new Vector3(0f, 90f, 0f),
                    Position = new Vector3(
                        box.Max.X + 10f + options.Depth,
                        box.Min.Y + options.Radius + p * (options.Diameter + 4f),
                        options.Radius)
                });
            }

            AssembleNew($"{front.Name} and {back.Name}", added, [front, back]);
            Undo.Execute(new ReplaceObjectsCommand("Connect objects", [front, back], added));
            IsConnectMode = false;
            RefreshSelection();

            Status = $"Connected {front.Name} and {back.Name} with {layout.Points.Count} "
                   + (options.Style switch { ConnectorStyle.Pins => "pin(s), made beside them", ConnectorStyle.Bricks => "brick stud(s)", _ => "peg(s)" })
                   + (layout.Points.Count < options.Count ? " - fewer than asked, there is no room for more" : "");
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Asked when a connector is deeper than a part it goes into is thick.
    ///
    /// It used to be refused outright, which was the wrong call: a pin through a lid, a peg standing
    /// proud of the top, are things people make on purpose. Breaking out of a thin floor by accident
    /// is worth a question, not a veto.
    /// </summary>
    private static bool GoThrough(ConnectorOptions options)
    {
        string what = options.Style == ConnectorStyle.Pins ? "pins" : "pegs";
        string shows = options.Style == ConnectorStyle.Pins
            ? "the holes will show on its far side"
            : "the pegs will stand out of its far side, and the sockets go right through";

        return MessageBox.Show(
            $"The {what} are {options.Depth:0.##} mm deep, which goes right through part of the model - {shows}.\n\n"
            + "Go ahead?",
            "3DFastCraft", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>What one object's split came to: its halves, any pins, and how the connectors went.</summary>
    private sealed record SplitOutcome(
        Mesh? Front, Mesh? Back, List<Mesh> Pins, int Placed, bool Refused, float Thickest, float WallNeeded,
        bool BreaksOut = false);

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
        openedFrom = null;
        inventedName = null; // a new project, so a new name for it
        ModelScale = 1f; // the scale was the last project's, not this one's
        IsDirty = false;
        RefreshSelection();
        Status = "New scene";
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardChanges()) return;

        var dialog = new OpenFileDialog
        {
            // A plain 3MF chosen here opens as a model on a new plate: nothing but its contents
            // says which kind it is. A .3dfc is imported, from Import.
            Filter = $"3DFastCraft project (*{SceneSerializer.Extension})|*{SceneSerializer.Extension}",
            Title = "Open project"
        };
        if (dialog.ShowDialog() != true) return;

        LoadProject(dialog.FileName);
    }

    /// <summary>Opens a project or a model - with "Opening..." over the window if the file is big. See <see cref="Run"/>.</summary>
    private void LoadProject(string path) =>
        Run(IsBig(path), "Opening...", () => LoadProjectNow(path));

    /// <summary>Whether a file is big enough to take a moment to read.</summary>
    private static bool IsBig(string path) => File.Exists(path) && new FileInfo(path).Length >= WaitFromBytes;

    private void LoadProjectNow(string path)
    {
        try
        {
            if (!SceneSerializer.IsProject(path))
            {
                OpenModel(path);
                return;
            }

            var loaded = SceneSerializer.Load(path, out var settings);
            // A file from before the scale was kept is taken as life size rather than inheriting
            // whatever the last project was drawn at.
            if (settings is { } kept) ApplySettings(kept);
            else ModelScale = 1f;
            Scene.Objects.Clear();
            foreach (var o in loaded) Scene.Objects.Add(o);
            Undo.Clear();
            projectPath = path;
            openedFrom = null;
            IsDirty = false;
            recent.Add(path);
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();
            Status = $"Opened {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            // Before the message box, not behind it: the dimmed window is not what to look at then.
            StopWaiting(now: true);
            MessageBox.Show(ex.Message, "Could not open project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// A model opened where a project was expected - a 3MF from a slicer, a project a slicer has
    /// saved over, a .3dfc from before 4.3 - on a plate of its own. Not as the project: saving it back would put this app's
    /// project over the slicer's, so it is saved under a name of its own.
    /// </summary>
    private void OpenModel(string path)
    {
        Scene.Objects.Clear();
        Undo.Clear();
        projectPath = null;
        ImportFiles([path]);
        Undo.Clear();
        openedFrom = path;
        IsDirty = false;
        Raise(nameof(WindowTitle));
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
                FileName = SuggestedName() + SceneSerializer.Extension,
                InitialDirectory = ProjectFolder()
            };
            if (dialog.ShowDialog() != true) return;
            target = dialog.FileName;
        }

        try
        {
            SceneSerializer.Save(target, Scene, ViewSettings, versionsFrom: projectPath);
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
            SceneSerializer.SaveVersion(projectPath, Scene, prompt.VersionLabel, ViewSettings);
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

        string path = projectPath;
        Run(IsBig(path), "Restoring...", () => RestoreVersion(path, index));
    }

    private void RestoreVersion(string path, int index)
    {
        try
        {
            var restored = SceneSerializer.LoadVersion(path, index);

            // Routed through undo, so restoring a version is as reversible as anything else.
            // The version's parts arrive in its own assemblies, which are not to be guessed at.
            Undo.Execute(new ReplaceObjectsCommand("Restore version", Scene.Objects.ToList(), restored, carryAssemblies: false));
            RefreshSelection();
            ZoomExtentsRequested?.Invoke();
            Status = $"Restored a version with {restored.Count} object(s) - Ctrl+Z to go back";
        }
        catch (Exception ex)
        {
            StopWaiting(now: true);
            MessageBox.Show(ex.Message, "Could not restore that version", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Models and drawings (*.stl;*.obj;*.3mf;*.svg;*.3dfc)|*.stl;*.obj;*.3mf;*.svg;*.3dfc"
                   + "|STL (*.stl)|*.stl|Wavefront OBJ (*.obj)|*.obj|3MF, a project too (*.3mf)|*.3mf|SVG drawing (*.svg)|*.svg"
                   + $"|3DFastCraft project, before 4.3 (*{SceneSerializer.LegacyExtension})|*{SceneSerializer.LegacyExtension}",
            Title = "Import model",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        ImportFiles(dialog.FileNames);
    }

    /// <summary>
    /// Brings files onto the plate beside whatever is already there.
    ///
    /// Shared by the Import button and by dropping files on the window, so the two cannot come
    /// to disagree about what a .stl, an .obj or a project turns into. Several files land in one
    /// undo step: importing a house in five parts and then wanting them gone is one Ctrl+Z.
    /// </summary>
    public void ImportFiles(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        try
        {
            var imported = new List<SceneObject>();
            var naming = Namer();

            foreach (string path in paths)
            {
                string extension = Path.GetExtension(path);

                // A project from before 4.3, the only way it still comes in: it joins what is on the
                // plate, its objects as they were - placed, turned, colored, still remade by Edit
                // settings. Only its versions stay behind. Taking the mesh and colour alone, as
                // this did, put every part at the origin, unturned. A project saved as a 3MF comes
                // in through the 3MF's own model below, as any 3MF.
                if (extension.Equals(SceneSerializer.LegacyExtension, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var loaded in SceneSerializer.Load(path))
                    {
                        loaded.Name = naming(loaded.Name);
                        imported.Add(loaded);
                    }
                }
                else if (extension.Equals(".3mf", StringComparison.OrdinalIgnoreCase))
                {
                    // Colours are kept when the file has them: a part painted in 3D Builder or a
                    // slicer comes in the colour it was, and only an uncoloured one takes the next.
                    foreach (var (name, mesh, colour) in ThreeMf.Read(path))
                        imported.Add(new SceneObject(naming(name), mesh)
                        {
                            Colour = colour ?? NextAutomaticColour()
                        }.Centred());
                }
                else if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    imported.AddRange(ImportDrawing(path, naming));
                }
                else if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var (name, mesh) in ObjReader.Read(path))
                        imported.Add(new SceneObject(naming(name), mesh)
                        {
                            Colour = NextAutomaticColour()
                        }.Centred());
                }
                else
                {
                    var mesh = StlReader.Read(path);
                    imported.Add(new SceneObject(
                        naming(Path.GetFileNameWithoutExtension(path)), mesh)
                    {
                        Colour = NextAutomaticColour()
                    }.Centred());
                }
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

    /// <summary>The last drawing import's settings, so a second drawing to match starts at the same size.</summary>
    private SvgImportOptions lastSvgImport = SvgImportOptions.Default;

    /// <summary>
    /// A drawing, made solid: asks for its size and thickness, then builds its filled shapes lying
    /// flat on the plate. Nothing when it is cancelled or has nothing filled in it - the second said
    /// so, since an empty result from a drawing that plainly has lines in it needs explaining.
    /// </summary>
    private List<SceneObject> ImportDrawing(string path, Func<string, string> naming)
    {
        var outlines = SvgImport.Outlines(path);
        float aspect = SvgImport.Aspect(outlines);
        string name = Path.GetFileNameWithoutExtension(path);

        if (aspect <= 0f)
        {
            MessageBox.Show(
                $"{Path.GetFileName(path)} has no filled shape in it, so there is nothing to make solid.\n\n"
                + "Lines on their own have no area. Give them a fill, or turn strokes and text into paths, in the drawing program.",
                "Import drawing", MessageBoxButton.OK, MessageBoxImage.Information);
            return [];
        }

        var dialog = new SvgImportDialog(path, aspect, outlines.Count, lastSvgImport) { Owner = Application.Current?.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Result is not { } options) return [];
        lastSvgImport = options;

        var solids = SvgImport.BuildFile(path, options);
        var colour = NextAutomaticColour();

        var objects = solids.Select((mesh, i) => new SceneObject(
            naming(solids.Count == 1 ? name : $"{name} {i + 1}"), mesh)
        {
            // Separate shapes of one drawing share its colour, so they still read as one drawing.
            Colour = colour
        }.Centred()).ToList();

        var torn = objects.Where(o => !o.Mesh.CheckHealth().IsWatertight).Select(o => o.Name).ToList();
        if (torn.Count > 0)
        {
            MessageBox.Show(
                $"Part of {Path.GetFileName(path)} did not come out as a closed solid: {string.Join(", ", torn)}.\n\n"
                + "It is imported anyway so it can be seen; Repair or Rebuild on the Tools tab can close it. "
                + "Shapes that cross themselves in the drawing are the usual cause.",
                "Import drawing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        return objects;
    }

    /// <summary>
    /// Opens what Windows handed over on the command line: a project as a project, a model onto
    /// the empty plate the app has just started with. Either way the file names the window, and
    /// nothing has been changed yet, so there is nothing to ask about on the way out.
    /// </summary>
    public void OpenFromWindows(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;

        if (IncomingFiles.IsProject(files[0]))
        {
            LoadProject(files[0]);
            return;
        }

        ImportFiles(files);
        openedFrom = files[0];
        IsDirty = false;
        Raise(nameof(WindowTitle));
    }

    /// <summary>
    /// Opens a project that was dropped on the window, asking first if there is work to lose.
    /// </summary>
    public void OpenDropped(string path)
    {
        if (!ConfirmDiscardChanges()) return;

        LoadProject(path);
    }

    /// <summary>
    /// What a file is offered as: the project's own name once it has one, and otherwise dated, so
    /// one export is not left waiting to be overwritten by the next.
    /// </summary>
    /// <summary>
    /// The name this scene has been carrying since it was started, for anything that has to
    /// suggest one.
    ///
    /// Invented once and kept, not made up afresh each time it is asked for. Save, Export and the
    /// drawing's title block all ask, and a name that changed between them would be three names
    /// for one thing - which was the old timestamp's real fault, not its ugliness.
    /// </summary>
    private string? inventedName;

    private string SuggestedName() =>
        projectPath is not null ? Path.GetFileNameWithoutExtension(projectPath)
        : openedFrom is not null ? Path.GetFileNameWithoutExtension(openedFrom)
        : inventedName ??= ProjectNames.Suggest();

    /// <summary>
    /// A three-view drawing of the selection, or of everything when nothing is selected - as Export
    /// takes it - with its overall dimensions, in the unit the boxes are in.
    /// </summary>
    private void PrintDrawing()
    {
        var subjects = Scene.Selection.Count > 0 ? Scene.Selection.ToList() : Scene.Shown.ToList();
        if (subjects.Count == 0) return;

        string title = projectPath is not null || openedFrom is not null ? SuggestedName()
            : subjects.Count == 1 ? subjects[0].Name
            : "Untitled";

        var window = new DrawingWindow(subjects.Select(o => o.ToWorldMesh()).ToList(), title, unit.Label, unit.Millimetres, modelScale,
                                       subjects.Select(o => o.Name).ToList())
        {
            Owner = Application.Current?.MainWindow
        };
        window.ShowDialog();
    }

    /// <summary>The project's folder, or empty to let Windows pick the last one used.</summary>
    private string ProjectFolder() => Path.GetDirectoryName(projectPath) ?? "";

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
            FileName = SuggestedName() + chosen.Extension,
            InitialDirectory = ProjectFolder(),
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
    /// Writes the undo history out as a numbered file per step - the plate as it was before
    /// anything was done, then again after each step in order - so what a tool did can be looked
    /// at afterwards, or shown, without replaying the session by hand.
    ///
    /// Rewinds the real undo stack to reach each state rather than working on a copy: every
    /// command but the geometry-replacing ones acts on the scene object it was captured against
    /// directly, not through a scene passed in, so a second scene built to replay into would
    /// still be mutating the objects the live one is showing. Undo and Redo are already exactly
    /// this operation, proven correct by being what the app runs on every Ctrl+Z; the plate is
    /// left exactly as it was found once every step has been redone back onto it.
    /// </summary>
    private async Task ExportSession()
    {
        var steps = Undo.History.ToList();
        if (steps.Count == 0)
        {
            Status = "Nothing to export yet - no steps taken since the plate was last empty";
            return;
        }

        var pick = new ExportSessionDialog(steps.Count) { Owner = Application.Current?.MainWindow };
        if (pick.ShowDialog() != true || pick.Result is not { } format) return;

        var folder = new OpenFolderDialog { Title = "Choose a folder for the session export" };
        if (folder.ShowDialog() != true || folder.FolderName is not { } destination) return;

        if (IsBusy) return;
        var token = StartWork("Exporting session");
        int digits = (steps.Count + 1).ToString().Length;

        try
        {
            for (int i = 0; i < steps.Count; i++) Undo.Undo();

            ExportSessionFrame(destination, 0, digits, "Start", format);
            for (int i = 0; i < steps.Count && !token.IsCancellationRequested; i++)
            {
                Undo.Redo();
                ExportSessionFrame(destination, i + 1, digits, steps[i].Label, format);

                // Writing a file is fast enough that without this the whole export runs as one
                // block on the UI thread - the progress dialog would show but never move, and
                // Abort would do nothing until it was too late to matter.
                await Task.Yield();
            }

            Status = token.IsCancellationRequested
                ? "Export session stopped partway through"
                : $"Exported {steps.Count + 1} file(s) to {destination}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not export session", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EndWork();
            RefreshSelection();
        }
    }

    /// <summary>Writes one step of a session export, in whichever format was asked for.</summary>
    private void ExportSessionFrame(string folder, int index, int digits, string label, SessionExportFormat format)
    {
        string stem = Path.Combine(folder, $"{index.ToString().PadLeft(digits, '0')}-{SafeFileName(label)}");

        switch (format)
        {
            case SessionExportFormat.Project:
                SceneSerializer.SaveSnapshot(stem + SceneSerializer.Extension, Scene);
                break;

            default:
                var merged = ExportComposer.MergeForStl(Scene.Shown);
                StlWriter.Write(stem + ".stl", merged);
                break;
        }
    }

    internal static string SafeFileName(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(label.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "step" : cleaned;
    }

    // --- Recording ---------------------------------------------------------------------

    private bool isRecording;
    private string? recordFolder;

    /// <summary>
    /// Whether a screenshot is saved after every action - and, for a tool with an Apply button,
    /// one just before it too. The window itself is what MainWindow saves; this only says
    /// whether it should, and where.
    /// </summary>
    public bool IsRecording
    {
        get => isRecording;
        private set
        {
            Set(ref isRecording, value);
            Raise(nameof(RecordButtonLabel));
        }
    }

    /// <summary>Where a recording is being saved, or null while nothing is recording.</summary>
    public string? RecordFolder => recordFolder;

    public string RecordButtonLabel => isRecording ? "Stop record" : "Record";

    private void ToggleRecording()
    {
        if (isRecording)
        {
            IsRecording = false;
            recordFolder = null;
            Status = "Stopped recording";
            return;
        }

        var folder = new OpenFolderDialog { Title = "Choose a folder to save screenshots to while recording" };
        if (folder.ShowDialog() != true || folder.FolderName is not { } chosen) return;

        recordFolder = chosen;
        IsRecording = true;
        Status = $"Recording to {chosen} - a screenshot is saved after every action";
    }

    /// <summary>Raised the instant a tool's Apply button is clicked, before the click does anything at all.</summary>
    public event Action? Applying;

    /// <summary>
    /// Called first thing by every Apply method, before it changes anything.
    ///
    /// Awaited so that whatever Applying triggers - Record's "before" shot, while recording - has
    /// time to actually capture the screen before this method goes on to mutate the scene the
    /// screenshot was meant to be taken of. The wait is skipped outright unless something is
    /// recording, so a click costs nothing extra the rest of the time.
    /// </summary>
    private async Task NotifyApplyingAsync()
    {
        Applying?.Invoke();
        if (isRecording) await Task.Delay(300);
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

        // The "as one" turn readout counts from the moment a selection was made, not from
        // whatever it happened to read before - so it only resets when the set of objects
        // actually changes, not on every refresh a move or resize also asks for.
        if (!selection.SequenceEqual(groupTurnBaseline)) ResetGroupTurnSinceGrab();

        // Every box that reads off the selection, because the selection has just changed.
        //
        // Setting Selected announced the sizes but not the positions, and nothing else here
        // announced them either - so picking a second object left the X, Y and Z boxes showing
        // the first one's position. Two objects a plate apart both read the same, and typing a
        // value into a stale box then moved the new object by the old one's numbers.
        RaiseTransformFields();

        // Before the list hears of it, so a heading whose part was just let go of goes dark with it.
        RefreshAssemblies();

        SelectionChanged?.Invoke();
        Raise(nameof(HasAnySelection));
        Raise(nameof(ObjectFilament));
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
