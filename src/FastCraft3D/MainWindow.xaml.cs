using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Engraving;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.Render;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX;
using Axis = FastCraft3D.Geometry.Axis;
using HitTestResult = HelixToolkit.SharpDX.Core.HitTestResult;
using Media3D = System.Windows.Media.Media3D;
using SharpDXVector2 = SharpDX.Vector2;
using SharpDXVector3 = SharpDX.Vector3;

namespace FastCraft3D;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    private SceneRenderer? renderer;

    /// <summary>The sketch point taken hold of, and where it was before the drag started.</summary>
    private (int Loop, int Index, Vector2 From)? sketchGrab;

    private bool sketchDragging;

    /// <summary>Where the right button went down, to tell a click from the drag that pans.</summary>
    private Point rightPressScreen;

    /// <summary>Asks the viewport for a frame. The renderer is given this same one.</summary>
    private ViewportRepaint? repaint;
    private MeasureOverlay? measure;

    /// <summary>Which end of the tape is being dragged: 0, 1, or -1 for none.</summary>
    private int heldMeasureEnd = -1;
    private PlateLook? plateShown;
    private GizmoController? gizmo;
    private SplitPlaneGizmo? splitGizmo;
    private SurfacePlacementGizmo? placeGizmo;
    private SelectionListSync? listSync;
    private MeshGeometryModel3D? splitPlaneVisual;

    // Drag-to-move state. A press only becomes a move once the pointer actually travels,
    // so a plain click still reads as a selection.
    private SceneObject? dragTarget;
    private Point dragStartScreen;
    private Vector3 dragStartHit;
    private List<SceneObject> dragObjects = [];
    private List<TransformState> dragBefore = [];
    private bool dragMoved;
    private bool pendingToggleOff;
    private bool pendingExclusive;
    private bool pendingClear;

    /// <summary>Where a Shift range starts: the object last clicked without Shift.</summary>
    private SceneObject? selectionAnchor;
    private Point pressScreen;

    /// <summary>Alt went down during a drag, so its release belongs to the drag too.</summary>
    private bool swallowAltUp;

    /// <summary>How far the pointer may wander and still count as a click rather than a drag.</summary>
    private const double ClickSlopPixels = 3.0;

    // Captured when a properties field takes focus, so one edit is one undo step.
    private List<SceneObject> editObjects = [];
    private List<TransformState> editBefore = [];

    /// <summary>The viewport behind the plate: as it sits normally, and while a tool is running.</summary>
    private static readonly Color PlainBackground = Color.FromRgb(0xB8, 0xBC, 0xC2);
    private static readonly Color ToolBackground = Color.FromRgb(0xA6, 0xB2, 0xC4);

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;

        // Before the plate is first drawn, or it is drawn at the default size and then redrawn.
        if (LocalSettings.Load() is { } remembered) viewModel.ApplySettings(remembered);
        viewModel.SettingsChanged += () => LocalSettings.Save(viewModel.Remembered);

        ToolPanel.Host = viewModel.ShowPanel;

        EffectsManager = new DefaultEffectsManager();
        View.EffectsManager = EffectsManager;
        View.Camera = CreateCamera();
        ConfigureCameraGestures();

        repaint = new ViewportRepaint(View);

        RebuildPlate();
        viewModel.ViewChanged += ApplyViewSettings;

        renderer = new SceneRenderer(ContentGroup, viewModel.Scene, repaint);

        measure = new MeasureOverlay(MeasureLayer, new Viewport3DXProjector(View));
        viewModel.MeasureChanged += () => measure.Show(viewModel.MeasureFrom, viewModel.MeasureTo);
        viewModel.SketchChanged += () =>
        {
            renderer?.ShowSketch(
                viewModel.IsSketchMode ? viewModel.CurrentSketch : null, viewModel.SketchCursor, viewModel.CurrentSketchTool);

            // Entering and leaving sketch mode comes through here as well.
            RefreshFocus();
        };
        viewModel.LookFromTopRequested += top =>
        {
            if (top) OnViewTop(this, new RoutedEventArgs());
            else OnViewIso(this, new RoutedEventArgs());
        };

        viewModel.EngraveFaceChanged += ShowFacePreview;
        viewModel.RestingFacesChanged += () => renderer?.ShowRestingFaces(viewModel.RestingFaceList, viewModel.RestingHover);

        listSync = new SelectionListSync(ObjectList, viewModel.Scene);
        listSync.ChangedFromList += viewModel.RefreshSelection;
        viewModel.SelectionChanged += () => listSync.PushToList();

        gizmo = new GizmoController(GizmoLayer, new Viewport3DXProjector(View), viewModel.Scene, viewModel.Undo);
        gizmo.Feedback += text => viewModel.Status = text;
        gizmo.KeepOnBedMove = viewModel.KeepOnBedMove;
        gizmo.KeepOnBedScale = viewModel.KeepOnBedScale;
        GizmoLayer.PreviewMouseLeftButtonDown += OnGizmoDown;
        GizmoLayer.PreviewMouseMove += OnGizmoMove;
        GizmoLayer.PreviewMouseLeftButtonUp += OnGizmoUp;

        splitGizmo = new SplitPlaneGizmo(SplitGizmoLayer, new Viewport3DXProjector(View));
        splitGizmo.Feedback += text => viewModel.Status = text;
        splitGizmo.Changed += (offset, normal) =>
        {
            // The same arrows slide Extrude down's plane, which only ever lies flat: the height is
            // all there is to change, and the plane follows through the height's own notification.
            if (viewModel.IsExtrudeMode)
            {
                viewModel.ExtrudeHeightMillimetres = offset;
                return;
            }

            viewModel.SplitOffset = offset;
            viewModel.SplitNormal = normal;
            UpdateSplitPlane();
            RefreshSplitPreview();
        };
        splitGizmo.Turned += (axis, degrees) => viewModel.TurnSplitPlane(axis, degrees);
        SplitGizmoLayer.PreviewMouseLeftButtonDown += OnSplitGizmoDown;
        SplitGizmoLayer.PreviewMouseMove += OnSplitGizmoMove;
        SplitGizmoLayer.PreviewMouseLeftButtonUp += OnSplitGizmoUp;

        placeGizmo = new SurfacePlacementGizmo(PlacementGizmoLayer, new Viewport3DXProjector(View));
        placeGizmo.Feedback += text => viewModel.Status = text;
        placeGizmo.Changed += placement =>
        {
            if (viewModel.IsEmbossMode) viewModel.EmbossPlacement = placement;
            else if (viewModel.IsEngraveMode) viewModel.EngravePlacement = placement;
        };
        PlacementGizmoLayer.PreviewMouseLeftButtonDown += OnTextGizmoDown;
        PlacementGizmoLayer.PreviewMouseMove += OnTextGizmoMove;
        PlacementGizmoLayer.PreviewMouseLeftButtonUp += OnTextGizmoUp;

        viewModel.PlacementChanged += RefreshPlacementGizmo;

        // The handles are projected from the camera, so they have to follow it. Repositioning
        // only when something actually moved keeps this off the per-frame allocation path.
        CompositionTarget.Rendering += OnFrame;

        // A resize changes where everything projects to without touching the camera, so the
        // dirty check above would not notice on its own. The scene has to be asked for as well:
        // the frame the host draws as it resizes can go out before it has the meshes to draw,
        // which is the plate that comes back with its squares missing.
        View.SizeChanged += (_, _) =>
        {
            gizmo?.Reposition();
            measure?.Reposition();
            Repaint();
        };

        viewModel.ZoomExtentsRequested += () => Dispatcher.BeginInvoke(new Action(ZoomExtents));
        viewModel.PropertyChanged += OnViewModelChanged;

        // One undo entry per field edit rather than one per keystroke.
        AddHandler(GotFocusEvent, new RoutedEventHandler(OnFieldGotFocus), true);
        AddHandler(LostFocusEvent, new RoutedEventHandler(OnFieldLostFocus), true);

        // "+=5" is read before the box loses focus, because losing focus is when the binding
        // writes the text back - and "+=5" is not a number it can write.
        AddHandler(PreviewLostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFieldLeaving), true);

        // Reaching a numeric field selects what is in it, so a new value can just be typed.
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFieldFocused), true);
        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnFieldClicked), true);

        // Ahead of everything else: while a long operation runs the keyboard has to be shut
        // as firmly as the panel shuts the mouse.
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnBusyKey), true);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnToolKey), true);

        // Ctrl, Alt and Shift change a resize while it is under way, so they are read going down
        // and coming up - a key let go without the mouse moving still has to show.
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnDragModifier), true);
        AddHandler(PreviewKeyUpEvent, new KeyEventHandler(OnDragModifier), true);

        // Arrow keys and the wheel nudge the numeric fields.
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnFieldKey), true);
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnFieldWheel), true);

        PreviewKeyDown += OnWindowKeyDown;
        PreviewKeyUp += OnWindowKeyUp;
        Deactivated += (_, _) => viewModel.EndNudge();
        BindShortcuts();

        // The panel appears with nothing focused, so Tab would start from wherever the user
        // last was - behind it. Focusing Abort puts the only thing they can still do first.
        BusyOverlay.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) AbortButton.Focus();
        };

        MeasureLayer.PreviewMouseLeftButtonDown += OnMeasureDown;
        MeasureLayer.PreviewMouseMove += OnMeasureMove;
        MeasureLayer.PreviewMouseLeftButtonUp += OnMeasureUp;

        View.PreviewMouseLeftButtonDown += OnViewportLeftDown;
        View.PreviewMouseMove += OnViewportMove;
        View.PreviewMouseLeftButtonUp += OnViewportLeftUp;
        View.PreviewMouseRightButtonDown += OnViewportRightDown;
        View.PreviewMouseRightButtonUp += OnViewportRightUp;

        // The last chance to save: this is the only path where unsaved work would vanish
        // without the user having asked for anything.
        Closing += (_, e) =>
        {
            // A tool's panel is waiting in the middle of its command, and closing under it would
            // run the rest of that command against a window already torn down. The tool is put
            // down first, and the close asked for again once it has let go.
            if (ToolPanel.Open is { } panel)
            {
                e.Cancel = true;
                panel.DialogResult = false;
                Dispatcher.BeginInvoke(new Action(Close));
                return;
            }

            e.Cancel = !viewModel.ConfirmDiscardChanges();
        };

        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            listSync?.Dispose();
            renderer?.Dispose();
            (EffectsManager as IDisposable)?.Dispose();
        };

        // Whatever the app was double-clicked with. Queued rather than done here: loading raises
        // the busy panel and can put a message box up, and neither has a window to belong to
        // until this constructor has finished.
        if (App.Opening.Count > 0)
        {
            var opening = App.Opening;
            Dispatcher.BeginInvoke(new Action(() => OpenOnStartup(opening)));
        }
    }

    /// <summary>
    /// Opens what the command line asked for.
    ///
    /// No dialog, unlike a drop. A file the window is dropped on could reasonably mean either
    /// thing - put this up in place of what I have, or add it to what is here - but a file handed
    /// over by Windows was double-clicked, and the plate behind it is the empty one the app just
    /// started with. There is nothing to ask about and nothing to lose.
    /// </summary>
    private void OpenOnStartup(IReadOnlyList<string> files) => viewModel.OpenFromWindows(files);

    /// <summary>
    /// Swallows the keyboard while a long operation runs.
    ///
    /// The panel over the window stops the mouse, but a shortcut does not need the pointer: the
    /// InputBindings on the window fire wherever focus is, so Ctrl+Z during a rebuild would undo
    /// the step the rebuild is about to replace. Handling the event here is also what stops
    /// those, since a KeyBinding only runs on a key that came through unhandled.
    /// </summary>
    private void OnBusyKey(object sender, KeyEventArgs e)
    {
        if (!viewModel.IsBusy) return;

        if (e.Key == Key.Escape)
        {
            if (viewModel.CanAbort) viewModel.AbortCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Tab and the keys that press a button belong to the panel - it cycles focus within
        // itself, so they cannot reach anything behind it.
        if (e.Key is Key.Tab or Key.Space or Key.Enter) return;

        e.Handled = true;
    }

    /// <summary>
    /// Which of the dropped files this app has anything to say about.
    ///
    /// Everything else is ignored rather than refused: a folder or a photograph dragged over by
    /// accident should do nothing, not raise a dialog about itself.
    /// </summary>
    private static List<string> Droppable(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return [];

        return files.Where(IncomingFiles.Understood).ToList();
    }

    private void OnFilesDraggedOver(object sender, DragEventArgs e)
    {
        e.Effects = Droppable(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Takes files dropped on the window, having asked what they are for.
    ///
    /// It asks rather than guessing. A dropped project could reasonably mean either thing - put
    /// this up in place of what I have, or add it to it - and guessing the first way throws away
    /// work that was never saved.
    /// </summary>
    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        var files = Droppable(e.Data);
        e.Handled = true;

        if (files.Count == 0 || viewModel.IsBusy || viewModel.IsToolInHand) return;

        var asking = new DropDialog(files) { Owner = this };
        if (asking.ShowDialog() != true) return;

        if (asking.OpenAsProject) viewModel.OpenDropped(files[0]);
        else viewModel.ImportFiles(files);
    }

    /// <summary>
    /// Swallows the shortcuts while a tool has the object.
    ///
    /// The ribbon greys out, but a shortcut does not need a button: the InputBindings on the
    /// window fire wherever focus is, so Delete during a split took the object out from under
    /// it. Typing is left alone - the tool's own boxes are the one thing still live - and so is
    /// Escape, which is how you put the tool down.
    /// </summary>
    private void OnToolKey(object sender, KeyEventArgs e)
    {
        if (!viewModel.IsToolInHand || e.Key is Key.Escape) return;

        bool typing = Keyboard.FocusedElement is TextBox;

        // While sketching, Enter closes the outline and Backspace takes back a point.
        if (viewModel.IsSketchMode && !typing && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Enter or Key.Back)
        {
            if (e.Key == Key.Enter) viewModel.CloseSketch();
            else viewModel.UndoSketch();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers is ModifierKeys.Control)
        {
            // Cut, copy, paste and select-all belong to the box being typed in.
            if (typing && e.Key is Key.C or Key.V or Key.X or Key.A) return;

            e.Handled = true;
            return;
        }

        if (!typing && e.Key is Key.Delete or Key.Back) e.Handled = true;
    }

    /// <summary>
    /// The keys that run a view-model command, bound from the table F1 lists them from, so the
    /// list and the keys cannot come apart.
    /// </summary>
    private void BindShortcuts()
    {
        foreach (var shortcut in Shortcuts.Keyed.Where(s => s.Command is not null))
        {
            var command = (ICommand)typeof(MainViewModel).GetProperty(shortcut.Command!)!.GetValue(viewModel)!;

            // Set property by property rather than through the constructor, which refuses a plain
            // key such as Delete on its own.
            InputBindings.Add(new KeyBinding
            {
                Command = command,
                CommandParameter = shortcut.Parameter,
                Key = shortcut.Key,
                Modifiers = shortcut.Modifiers
            });
        }
    }

    /// <summary>
    /// The keys the window answers itself - modes, nudges, views and F1. Deliberately not
    /// Window.InputBindings: an unmodified key binding there fires no matter what has focus, so
    /// typing an "s" into the name field would silently switch tool instead of typing.
    /// </summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // Escape first, and before the checks below: it has to work while the caret is sitting
        // in one of the tool's own boxes, which is exactly where it usually is.
        if (e.Key is Key.Escape && Keyboard.Modifiers is ModifierKeys.None
            && viewModel.CancelActiveTool())
        {
            e.Handled = true;
            return;
        }

        var action = Shortcuts.ActionFor(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        if (action == KeyAction.None) return;

        // The list is worth having whatever else is going on, a tool in hand included.
        if (action == KeyAction.ShowShortcuts)
        {
            viewModel.IsShortcutsPanelOpen = !viewModel.IsShortcutsPanelOpen;
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox) return;
        if (viewModel.IsToolInHand) return; // the mode buttons are greyed out with the rest
        if (Shortcuts.IsNudge(action) && FocusWantsArrows()) return;

        float step = viewModel.NudgeStep * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10f : 1f);

        switch (action)
        {
            case KeyAction.Move: viewModel.GizmoMode = GizmoMode.Move; break;
            case KeyAction.Rotate: viewModel.GizmoMode = GizmoMode.Rotate; break;
            case KeyAction.Resize: viewModel.GizmoMode = GizmoMode.Scale; break;
            case KeyAction.OneWayOnly: viewModel.ScaleOneSide = !viewModel.ScaleOneSide; break;
            case KeyAction.StopOnContact: viewModel.StopOnContact = !viewModel.StopOnContact; break;
            case KeyAction.NudgeLeft: viewModel.Nudge(new Vector3(-step, 0, 0)); break;
            case KeyAction.NudgeRight: viewModel.Nudge(new Vector3(step, 0, 0)); break;
            case KeyAction.NudgeBack: viewModel.Nudge(new Vector3(0, step, 0)); break;
            case KeyAction.NudgeForward: viewModel.Nudge(new Vector3(0, -step, 0)); break;
            case KeyAction.NudgeUp: viewModel.Nudge(new Vector3(0, 0, step)); break;
            case KeyAction.NudgeDown: viewModel.Nudge(new Vector3(0, 0, -step)); break;
            case KeyAction.ZoomToFit: ZoomExtents(); break;
            case KeyAction.ViewTop: OnViewTop(this, e); break;
            case KeyAction.ViewFront: OnViewFront(this, e); break;
            case KeyAction.ViewRight: OnViewRight(this, e); break;
            case KeyAction.ViewIsometric: OnViewIso(this, e); break;
            case KeyAction.HideSelection: viewModel.HideSelection(); break;
            case KeyAction.ShowAll: viewModel.ShowAll(); break;
            case KeyAction.LockSelection: viewModel.LockSelection(); break;
            case KeyAction.UnlockAll: viewModel.UnlockAll(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>An arrow key coming up ends the nudge it was making, as one undo step.</summary>
    private void OnWindowKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown)
            viewModel.EndNudge();
    }

    /// <summary>
    /// Whether the arrow keys already mean something where the focus is: moving through the object
    /// list or a drop-down, along a slider, or through text. There they are left alone.
    /// </summary>
    private static bool FocusWantsArrows()
    {
        for (var at = Keyboard.FocusedElement as DependencyObject; at is not null;
             at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is TextBoxBase or ListBox or ComboBox or Slider) return true;
            if (at is Window) break;
        }

        return false;
    }

    // --- Manipulator ------------------------------------------------------------------

    private void OnGizmoDown(object sender, MouseButtonEventArgs e)
    {
        if (gizmo is null) return;

        gizmo.Modifiers = Keyboard.Modifiers;
        if (!gizmo.TryBeginDrag(e.GetPosition(GizmoLayer), e.OriginalSource)) return;

        GizmoLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnGizmoMove(object sender, MouseEventArgs e)
    {
        if (gizmo is not { IsDragging: true }) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        gizmo.Modifiers = Keyboard.Modifiers;
        gizmo.ContinueDrag(e.GetPosition(GizmoLayer));
        e.Handled = true;
    }

    /// <summary>
    /// Re-reads Ctrl, Alt and Shift while a handle is being dragged.
    ///
    /// Alt is swallowed for the whole of it, going down and coming up. Let through, Alt on its own
    /// is Windows' key for the menu: letting it go would put the ribbon into keyboard mode halfway
    /// through a resize, with the pointer still captured by the handle. And it is swallowed even
    /// when it comes up after the mouse, because the drag it belonged to has only just ended.
    /// </summary>
    private void OnDragModifier(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool alt = key is Key.LeftAlt or Key.RightAlt;

        if (!alt && key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift))
            return;

        if (gizmo is { IsDragging: true })
        {
            gizmo.Modifiers = Keyboard.Modifiers;
            gizmo.Refresh();

            if (alt)
            {
                swallowAltUp = e.IsDown;
                e.Handled = true;
            }
            return;
        }

        if (alt && e.IsUp && swallowAltUp)
        {
            swallowAltUp = false;
            e.Handled = true;
        }
    }

    private void OnGizmoUp(object sender, MouseButtonEventArgs e)
    {
        if (gizmo is not { IsDragging: true }) return;

        GizmoLayer.ReleaseMouseCapture();
        gizmo.EndDrag();
        viewModel.RefreshSelection();
        e.Handled = true;
    }

    private void OnTextGizmoDown(object sender, MouseButtonEventArgs e)
    {
        if (placeGizmo is null) return;
        if (!placeGizmo.TryBeginDrag(e.GetPosition(PlacementGizmoLayer), e.OriginalSource)) return;

        PlacementGizmoLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnTextGizmoMove(object sender, MouseEventArgs e)
    {
        if (placeGizmo is not { IsDragging: true }) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        placeGizmo.ContinueDrag(e.GetPosition(PlacementGizmoLayer));
        e.Handled = true;
    }

    private void OnTextGizmoUp(object sender, MouseButtonEventArgs e)
    {
        if (placeGizmo is not { IsDragging: true }) return;

        PlacementGizmoLayer.ReleaseMouseCapture();
        placeGizmo.EndDrag();
        ShowFacePreview();
        e.Handled = true;
    }

    /// <summary>
    /// Redraws whatever the running tool is showing on the face.
    ///
    /// Both tools share the one highlight, so whichever is running has to be the one asked. Going
    /// straight to the lettering while engraving was what made the pattern vanish the moment its
    /// grip was let go: there is no lettering face then, and drawing nothing takes the highlight,
    /// the outline and the pattern away with it.
    /// </summary>
    private void ShowFacePreview()
    {
        if (renderer is null) return;

        if (viewModel.IsEmbossMode) ShowEmbossPreview();
        else renderer.ShowFace(viewModel.EngraveFace, viewModel.EngravePreview);
    }

    /// <summary>
    /// The lettering laid on the object, so its placement can be seen before it is committed.
    ///
    /// Wrapped lettering has to be divided finely enough to follow the curve, which is far too
    /// much geometry to rebuild on every mouse move. During a drag the dashed box carries the
    /// placement on its own and the lettering catches up when the handle is let go.
    /// </summary>
    private void ShowEmbossPreview()
    {
        if (renderer is null) return;

        bool tooHeavy = viewModel.IsEmbossWrapped && placeGizmo is { IsDragging: true };
        renderer.ShowFace(viewModel.EmbossFace, null, tooHeavy ? null : viewModel.EmbossPreview());
    }

    /// <summary>
    /// Keeps the placement handles on whatever is being placed, and hides them outside the tools
    /// that use them. Lettering gets all three handles; an engraved pattern covers the whole face
    /// and has no angle, so it gets the grip alone.
    /// </summary>
    private void RefreshPlacementGizmo()
    {
        if (placeGizmo is null) return;

        // A drag is already moving the handles; re-showing them from the value it just published
        // would fight the pointer.
        if (placeGizmo.IsDragging) return;

        if (viewModel.IsEmbossMode)
        {
            placeGizmo.Noun = "Lettering";
            placeGizmo.Show(
                viewModel.HasEmbossFace, viewModel.EmbossSurface(),
                viewModel.EmbossPlacement, viewModel.EmbossExtent);
        }
        else if (viewModel.IsEngraveMode)
        {
            placeGizmo.Noun = "Pattern";
            placeGizmo.Show(
                viewModel.HasEngraveFace, viewModel.EngraveSurface(),
                viewModel.EngravePlacement, viewModel.EngraveExtent, PlacementHandles.Move);
        }
        else
        {
            placeGizmo.Show(false, null, SurfacePlacement.Middle, Vector2.Zero);
        }
    }

    private void OnSplitGizmoDown(object sender, MouseButtonEventArgs e)
    {
        if (splitGizmo is null) return;
        if (!splitGizmo.TryBeginDrag(e.GetPosition(SplitGizmoLayer), e.OriginalSource)) return;

        SplitGizmoLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnSplitGizmoMove(object sender, MouseEventArgs e)
    {
        if (splitGizmo is not { IsDragging: true }) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        splitGizmo.ContinueDrag(e.GetPosition(SplitGizmoLayer));
        e.Handled = true;
    }

    private void OnSplitGizmoUp(object sender, MouseButtonEventArgs e)
    {
        if (splitGizmo is not { IsDragging: true }) return;

        SplitGizmoLayer.ReleaseMouseCapture();
        splitGizmo.EndDrag();

        // Whatever the throttle was waiting for, the drag is over and the preview should be
        // showing where the plane actually ended up.
        RefreshSplitPreview(now: true);
        e.Handled = true;
    }

    /// <summary>Keeps the split handles on the plane, and hides them outside split mode.</summary>
    private bool splitPreviewStale;
    private bool connectorMarksStale;
    private readonly Stopwatch splitPreviewClock = Stopwatch.StartNew();
    private double splitPreviewWait;

    private void RefreshSplitGizmo()
    {
        if (splitGizmo is null) return;

        var selection = viewModel.Scene.Selection;
        bool extrude = viewModel.IsExtrudeMode;
        bool on = (viewModel.IsSplitMode || extrude) && selection.Count > 0;

        // Centred on everything the plane will cut, not on whichever object came first.
        var bounds = Bounds.Empty;
        if (on) foreach (var o in selection) bounds = bounds.Union(o.WorldBounds);
        Vector3 centre = bounds.IsEmpty ? Vector3.Zero : bounds.Center;

        // Extrude down's plane only slides: arrows, never rings, and always flat.
        splitGizmo.Mode = extrude ? GizmoMode.Move : viewModel.SplitGizmoMode;

        if (extrude) splitGizmo.Show(on, Vector3.UnitZ, viewModel.ExtrudeHeightMillimetres, centre);
        else splitGizmo.Show(on, viewModel.SplitNormal, viewModel.SplitOffset, centre);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        // The gizmo decides for itself whether it is out of date, by checking where the
        // selection currently projects to rather than by watching the camera and the window
        // size. Those inputs miss a resize that the viewport has not finished applying yet -
        // which is what left the handles stranded in a corner.
        if (gizmo is null) return;
        if (gizmo.NeedsReposition || gizmo.IsStale()) gizmo.Reposition();
        if (viewModel.IsSplitMode || viewModel.IsExtrudeMode) splitGizmo?.Reposition();
        SettleSplitPreview();
        SettleConnectorMarks();
        if (viewModel.IsEmbossMode || viewModel.IsEngraveMode) placeGizmo?.Reposition();

        // The tape is anchored to the model rather than to the screen, so it is reprojected with
        // the camera. Only while it is out: this runs on every frame.
        if (viewModel.IsMeasureMode) measure?.Reposition();
    }

    public IEffectsManager EffectsManager { get; }

    /// <summary>
    /// Left drag orbits, right drag pans, the wheel zooms.
    ///
    /// Two separate things have to agree that Z is up. The camera's own UpDirection only decides
    /// which way the picture is oriented; the turntable spins about <c>ModelUpDirection</c>, a
    /// property of the viewport that defaults to Y. Leaving it at the default made a horizontal
    /// drag orbit around the green axis and let the scene roll onto its side, which is wrong for
    /// a build plate. The view cube and zoom-to-extents read the same property. The toolkit
    /// already defaults to turntable rotation, which is the behaviour we want once it is told
    /// which way is up.
    ///
    /// The gestures are set explicitly too: the toolkit's defaults bind panning to the middle
    /// button, which is awkward on a trackpad and not what 3D Builder does.
    /// </summary>
    private void ConfigureCameraGestures()
    {
        View.ModelUpDirection = new Media3D.Vector3D(0, 0, 1);

        View.UseDefaultGestures = false;
        View.InputBindings.Clear();
        View.InputBindings.Add(new MouseBinding(ViewportCommands.Rotate, new MouseGesture(MouseAction.LeftClick)));
        View.InputBindings.Add(new MouseBinding(ViewportCommands.Pan, new MouseGesture(MouseAction.RightClick)));
        View.InputBindings.Add(new MouseBinding(ViewportCommands.Zoom, new MouseGesture(MouseAction.MiddleClick)));
    }

    private static PerspectiveCamera CreateCamera() => new()
    {
        // Z-up to match the STL/slicer convention and the build plate.
        Position = new Media3D.Point3D(180, -240, 160),
        LookDirection = new Media3D.Vector3D(-180, 240, -160),
        UpDirection = new Media3D.Vector3D(0, 0, 1),
        NearPlaneDistance = 0.5,
        FarPlaneDistance = 20000,
        FieldOfView = 45
    };

    // --- Dragging the ends of the tape -------------------------------------------------
    //
    // The first click of a measurement is rarely on the exact corner meant, and correcting it
    // used to mean starting the measurement again - the third click begins a fresh one. The
    // markers are hit-testable now, so an end can simply be taken hold of and moved.

    private void OnMeasureDown(object sender, MouseButtonEventArgs e)
    {
        if (measure is null || !viewModel.IsMeasureMode) return;

        heldMeasureEnd = measure.EndAt(e.GetPosition(View));
        if (heldMeasureEnd < 0) return;

        MeasureLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnMeasureMove(object sender, MouseEventArgs e)
    {
        if (heldMeasureEnd < 0 || e.LeftButton != MouseButtonState.Pressed) return;

        var screen = e.GetPosition(View);
        var hit = FirstHit(screen);
        var target = renderer?.Resolve(hit?.ModelHit);

        // Off the model there is nothing to measure to, so the end simply stays where it was
        // rather than flying off to wherever the ground plane happens to be.
        if (target is null) return;

        viewModel.MoveMeasurePoint(heldMeasureEnd == 1, SnappedPoint(target, ToVector3(hit!.PointHit)));
        e.Handled = true;
    }

    private void OnMeasureUp(object sender, MouseButtonEventArgs e)
    {
        if (heldMeasureEnd < 0) return;

        heldMeasureEnd = -1;
        MeasureLayer.ReleaseMouseCapture();
        e.Handled = true;
    }

    // --- Selection and dragging ------------------------------------------------------

    private void OnViewportLeftDown(object sender, MouseButtonEventArgs e)
    {
        var screen = e.GetPosition(View);
        var hit = FirstHit(screen, selectable: true);
        var target = renderer?.Resolve(hit?.ModelHit);

        pendingToggleOff = false;
        pendingExclusive = false;
        pendingClear = false;
        pressScreen = screen;

        // A tool's panel leaves the viewport live for looking round its preview, not for picking:
        // a click that changed the selection would change what the tool is working on.
        if (viewModel.HasOpenPanel) return;

        // Sketching: a click places a point on the plate and nothing else. The press is kept from
        // the camera, so clicks do not turn the view; right-drag and the wheel still pan and zoom.
        if (viewModel.IsSketchMode)
        {
            if (TryIntersectPlane(screen, 0f, out var onPlate))
            {
                if (viewModel.CurrentSketchTool == FastCraft3D.Geometry.Sketches.SketchTool.Freehand)
                {
                    // Held, so the stroke still ends if the pointer is let go off the viewport.
                    viewModel.BeginSketchStroke(new Vector2(onPlate.X, onPlate.Y));
                    View.CaptureMouse();
                }
                else if (SketchHandleAt(screen) is { } grabbed)
                {
                    // Taken hold of, not yet moved: a press that goes nowhere is still a click,
                    // and a click on the first point is what closes the outline. Which of the two
                    // it was is settled when the button comes up.
                    sketchGrab = grabbed;
                    sketchDragging = false;
                    View.CaptureMouse();
                }
                else
                {
                    viewModel.PlaceSketchPoint(new Vector2(onPlate.X, onPlate.Y), OnFirstSketchPoint(screen));
                }
            }

            e.Handled = true;
            return;
        }

        if (viewModel.IsLayMode && SightLine(screen) is var (origin, direction)
            && viewModel.LayOnRestingFace(origin, direction))
        {
            e.Handled = true;
            return;
        }

        if (target is null)
        {
            // With sticky selection on, empty space never changes anything. With it off, a
            // click out here clears the selection - but only a click: the same press is also
            // how a camera orbit begins, so the decision waits for the release.
            if (!viewModel.StickySelection && !IsShiftDown && !IsControlDown) pendingClear = true;
            return; // unhandled, so the camera gesture takes over
        }

        // Measuring takes over the click before anything else does; the tape is the only
        // thing a click means while it is out.
        if (viewModel.IsMeasureMode)
        {
            viewModel.TakeMeasurePoint(SnappedPoint(target, ToVector3(hit!.PointHit)));
            e.Handled = true;
            return;
        }

        if (viewModel.IsLayMode)
        {
            viewModel.LayOnFace(target, ToVector3(hit!.PointHit), ToVector3(hit.NormalAtHit));
            e.Handled = true;
            return;
        }

        if (viewModel.IsEmbossMode)
        {
            if (!viewModel.Scene.Selection.Contains(target))
            {
                PressLikeFileExplorer(target);
                viewModel.RefreshSelection();
            }

            viewModel.PickEmbossFace(target, ToVector3(hit!.PointHit), ToVector3(hit.NormalAtHit));
            e.Handled = true;
            return;
        }

        // Splitting on a picked face: the click sets the plane rather than the selection. The
        // gizmo handles its own drags before this, so only a click on the model itself lands here.
        if (viewModel.IsSplitMode && viewModel.PickSplitPlane(
                target, ToVector3(hit!.PointHit), ToVector3(hit.NormalAtHit)))
        {
            e.Handled = true;
            return;
        }

        // While a face is being picked the click means something else entirely, so it never
        // reaches the selection logic. Clicking a different object switches the selection to it
        // first, which is the only way to engrave something else without leaving the mode.
        if (viewModel.IsEngraveMode)
        {
            if (!viewModel.Scene.Selection.Contains(target))
            {
                PressLikeFileExplorer(target);
                viewModel.RefreshSelection();
            }

            viewModel.PickEngraveFace(target, ToVector3(hit!.PointHit), ToVector3(hit.NormalAtHit));
            e.Handled = true;
            return;
        }

        if (viewModel.StickySelection) PressWithStickySelection(target);
        else PressLikeFileExplorer(target);

        dragTarget = target;
        dragStartScreen = screen;
        dragMoved = false;
        dragObjects = viewModel.Scene.Selection.ToList();
        dragBefore = dragObjects.Select(TransformState.Capture).ToList();
        dragStartHit = ToVector3(hit!.PointHit);

        View.CaptureMouse();
        e.Handled = true; // suppress camera orbit while moving an object
    }

    /// <summary>
    /// Pulls a picked point onto the nearest corner or edge midpoint of the object clicked.
    ///
    /// The radius is generous in world terms but the click has already landed on this object, so
    /// there is no risk of catching something else; measuring corner to corner is the common case
    /// and worth being forgiving about.
    /// </summary>
    private Vector3 SnappedPoint(SceneObject target, Vector3 hit)
    {
        if (!viewModel.MeasureSnap) return hit;

        var world = target.ToWorldMesh();
        float radius = Math.Max(target.WorldBounds.Diagonal * 0.04f, 0.5f);

        return VertexSnap.Nearest(world, hit, radius);
    }

    /// <summary>
    /// Sticky selection: a click toggles just the object clicked and never touches the rest.
    /// No modifier keys, because accumulating is already the default.
    /// </summary>
    private void PressWithStickySelection(SceneObject target)
    {
        if (target.IsSelected)
        {
            // Acting on the release, not the press. Changing the selection under the pointer now
            // would make an existing selection impossible to drag - the moment you grabbed it,
            // it would be gone.
            pendingToggleOff = true;
        }
        else
        {
            target.IsSelected = true;
            viewModel.RefreshSelection();
        }

        selectionAnchor = target;
    }

    /// <summary>
    /// Sticky selection off: the conventions from File Explorer. A plain click selects one
    /// object, Ctrl adds or removes one, and Shift takes everything between the last object
    /// clicked and this one, in the order they appear in the object list.
    /// </summary>
    private void PressLikeFileExplorer(SceneObject target)
    {
        if (IsShiftDown && selectionAnchor is not null)
        {
            SelectRange(selectionAnchor, target);
            return; // the anchor deliberately stays put, so the range can be re-dragged
        }

        if (IsControlDown)
        {
            target.IsSelected = !target.IsSelected;
            viewModel.RefreshSelection();
            selectionAnchor = target;
            return;
        }

        if (target.IsSelected)
        {
            // Part of a multi-selection: keep the group so it can be dragged, and narrow to
            // this one object only if the press turns out to be a plain click.
            pendingExclusive = viewModel.Scene.Selection.Count > 1;
        }
        else
        {
            viewModel.Scene.ClearSelection();
            target.IsSelected = true;
            viewModel.RefreshSelection();
        }

        selectionAnchor = target;
    }

    /// <summary>Selects everything between two objects inclusive, by their order in the scene.</summary>
    private void SelectRange(SceneObject from, SceneObject to)
    {
        var objects = viewModel.Scene.Objects;
        int a = objects.IndexOf(from), b = objects.IndexOf(to);
        if (a < 0 || b < 0) return;

        if (a > b) (a, b) = (b, a);

        viewModel.Scene.ClearSelection();
        for (int i = a; i <= b; i++) objects[i].IsSelected = true;
        viewModel.RefreshSelection();
    }

    private void OnViewportRightDown(object sender, MouseButtonEventArgs e) => rightPressScreen = e.GetPosition(View);

    /// <summary>
    /// The right button, clicked rather than dragged, finishes the outline being sketched: the
    /// usual way out of a polyline, and one hand's worth of mouse nearer than Enter. A right-drag
    /// is the camera's pan and is left to it.
    /// </summary>
    private void OnViewportRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!viewModel.IsSketchMode || !viewModel.CurrentSketch.IsDrawing) return;
        if (!IsClick(e.GetPosition(View), rightPressScreen)) return;

        // Not marked handled, however tempting. The camera's pan starts on the press and has the
        // mouse; if it never sees the release it keeps it, and the plate then slides about under
        // a button nobody is holding. A pan that went nowhere has moved nothing anyway.
        viewModel.EndSketchLine();
    }

    /// <summary>Whether a click is on the first point of the line being sketched, which closes it.</summary>
    private bool OnFirstSketchPoint(Point screen)
    {
        var chain = viewModel.CurrentSketch.Chain;
        if (chain.Count < 2 || !new Viewport3DXProjector(View).TryProject(new Vector3(chain[0], 0f), out var first)) return false;
        return (first - screen).Length <= 10;
    }

    /// <summary>
    /// The sketch point within reach of a place on screen, if there is one. Measured on screen
    /// rather than on the plate, so how near is near enough does not depend on the zoom.
    /// </summary>
    private (int Loop, int Index, Vector2 From)? SketchHandleAt(Point screen)
    {
        var projector = new Viewport3DXProjector(View);
        (int Loop, int Index, Vector2 From)? best = null;
        double nearest = 10;

        foreach (var (loop, index, at) in viewModel.SketchHandles())
        {
            if (!projector.TryProject(new Vector3(at, 0f), out var where)) continue;

            double away = (where - screen).Length;
            if (away > nearest) continue;

            nearest = away;
            best = (loop, index, at);
        }

        return best;
    }

    private void OnViewportMove(object sender, MouseEventArgs e)
    {
        if (viewModel.IsSketchMode && TryIntersectPlane(e.GetPosition(View), 0f, out var onPlate))
        {
            viewModel.MoveSketchCursor(new Vector2(onPlate.X, onPlate.Y));

            if (sketchGrab is { } grab && e.LeftButton == MouseButtonState.Pressed)
            {
                // Still within a click's slack of where it was pressed: it may yet turn out to
                // have been a click rather than a drag.
                if (!sketchDragging && IsClick(e.GetPosition(View), pressScreen)) return;

                sketchDragging = true;
                viewModel.MoveSketchHandle(grab.Loop, grab.Index, new Vector2(onPlate.X, onPlate.Y));
            }
        }

        if (viewModel.IsLayMode && e.LeftButton != MouseButtonState.Pressed
            && SightLine(e.GetPosition(View)) is var (origin, direction))
            viewModel.HoverRestingFace(origin, direction);

        if (dragTarget is null || e.LeftButton != MouseButtonState.Pressed) return;

        var screen = e.GetPosition(View);
        if (!dragMoved)
        {
            // A few pixels of slop so a click with a shaky hand is still a click.
            if (IsClick(screen, dragStartScreen)) return;
            dragMoved = true;
        }

        // Objects slide on the horizontal plane through the point originally grabbed.
        if (!TryIntersectPlane(screen, dragStartHit.Z, out var current)) return;

        Vector3 delta = current - dragStartHit;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Constrain to the dominant axis.
            if (Math.Abs(delta.X) >= Math.Abs(delta.Y)) delta.Y = 0;
            else delta.X = 0;
        }
        delta.Z = 0;

        // Snapped against the object actually grabbed, with the same correction applied to the
        // rest of the selection so the group keeps its shape.
        double step = viewModel.SnapStep;
        if (step > 0)
        {
            delta.X = (float)GizmoMath.SnapTravel(dragBefore[0].Position.X, delta.X, step);
            delta.Y = (float)GizmoMath.SnapTravel(dragBefore[0].Position.Y, delta.Y, step);
        }

        for (int i = 0; i < dragObjects.Count; i++)
            dragObjects[i].Position = dragBefore[i].Position + delta;

        viewModel.Status = $"Moved {delta.X:0.#}, {delta.Y:0.#} mm";
    }

    private void OnViewportLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.IsSketchMode)
        {
            if (View.IsMouseCaptured) View.ReleaseMouseCapture();
            if (viewModel.CurrentSketch.IsDrawingStroke) viewModel.EndSketchStroke();

            if (sketchGrab is { } grab)
            {
                if (sketchDragging)
                {
                    viewModel.SettleSketchHandle(grab.Loop, grab.Index, grab.From);
                }
                else if (TryIntersectPlane(e.GetPosition(View), 0f, out var onPlate))
                {
                    // It never moved, so it was a click on the point after all.
                    viewModel.PlaceSketchPoint(new Vector2(onPlate.X, onPlate.Y), OnFirstSketchPoint(e.GetPosition(View)));
                }

                sketchGrab = null;
                sketchDragging = false;
            }

            return;
        }

        if (pendingClear)
        {
            pendingClear = false;
            if (IsClick(e.GetPosition(View), pressScreen))
            {
                viewModel.Scene.ClearSelection();
                viewModel.RefreshSelection();
            }
            return;
        }

        if (dragTarget is null) return;
        View.ReleaseMouseCapture();

        if (dragMoved)
        {
            if (TransformCommand.CreateIfChanged("Move", dragObjects, dragBefore) is { } command)
                viewModel.Undo.Execute(command);
        }
        else if (pendingToggleOff)
        {
            // A click, not a drag, on something already selected - so remove just that object.
            dragTarget.IsSelected = false;
        }
        else if (pendingExclusive)
        {
            // Non-sticky: a plain click on one of several picks out just that object.
            viewModel.Scene.SelectOnly(dragTarget);
        }

        pendingToggleOff = false;
        pendingExclusive = false;
        dragTarget = null;
        dragObjects = [];
        dragBefore = [];
        viewModel.RefreshSelection();
    }

    /// <summary>A press and release close enough together to count as a click, not a drag.</summary>
    private static bool IsClick(Point release, Point press) =>
        Math.Abs(release.X - press.X) < ClickSlopPixels && Math.Abs(release.Y - press.Y) < ClickSlopPixels;

    /// <param name="selectable">
    /// Only an object that can be selected: a click to select goes straight through a locked one
    /// to whatever is behind it. Measuring still lands on locked objects - they are there.
    /// </param>
    private HitTestResult? FirstHit(Point screen, bool selectable = false)
    {
        var hits = View.FindHits(screen);
        if (hits is null) return null;

        foreach (var hit in hits)
            if (hit.IsValid && renderer?.Resolve(hit.ModelHit) is { } o && (!selectable || o.CanBeSelected))
                return hit;

        return null;
    }

    /// <summary>
    /// Projects a screen point onto the horizontal plane at height <paramref name="z"/>.
    /// Returns false when the view direction is parallel to that plane.
    /// </summary>
    private bool TryIntersectPlane(Point screen, float z, out Vector3 point)
    {
        point = default;
        var ray = View.UnProject(new SharpDXVector2((float)screen.X, (float)screen.Y));
        var origin = ray.Position;
        var direction = ray.Direction;
        if (Math.Abs(direction.Z) < 1e-6f) return false;

        float t = (z - origin.Z) / direction.Z;
        if (t < 0) return false;

        var hit = origin + direction * t;
        point = new Vector3(hit.X, hit.Y, hit.Z);
        return true;
    }

    private static Vector3 ToVector3(SharpDXVector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The line from the eye through a point on the screen, in world space.</summary>
    private (Vector3 Origin, Vector3 Direction)? SightLine(Point screen)
    {
        var ray = View.UnProject(new SharpDXVector2((float)screen.X, (float)screen.Y));
        var direction = ToVector3(ray.Direction);
        return direction.LengthSquared() < 1e-12f ? null : (ToVector3(ray.Position), Vector3.Normalize(direction));
    }

    private static bool IsControlDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
    private static bool IsShiftDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    // --- Properties-panel undo coalescing --------------------------------------------

    private void OnFieldGotFocus(object sender, RoutedEventArgs e)
    {
        // A tool's preview is placed, not edited: it goes when the panel closes, and an undo step
        // for it would only undo nothing.
        if (e.OriginalSource is not TextBox { Tag: "transform" } || viewModel.PanelHandles) return;
        editObjects = viewModel.Scene.Selection.ToList();
        editBefore = editObjects.Select(TransformState.Capture).ToList();
    }

    /// <summary>
    /// Selects the whole value when a numeric field is reached, so typing replaces it rather than
    /// landing in the middle of what is already there.
    /// </summary>
    private void OnFieldFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBox { Tag: "transform" or "number" } box) box.SelectAll();
    }

    /// <summary>
    /// The first click on a field focuses it and goes no further.
    ///
    /// Without this the selection made above is undone immediately: focus arrives first, and the
    /// click that caused it then puts the caret where the pointer was and clears the selection.
    /// Swallowing that one click is the usual way round it, and a second click still places the
    /// caret, so picking out a single digit still works.
    /// </summary>
    private void OnFieldClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;

        var box = FindTextBox(source);
        if (box is null || box.IsKeyboardFocusWithin) return;

        box.Focus();
        e.Handled = true;
    }

    /// <summary>
    /// The numeric field a click landed in. A click lands on whatever part of the box's template
    /// is under the pointer, not on the box, so the tree has to be walked back up.
    /// </summary>
    private static TextBox? FindTextBox(DependencyObject from)
    {
        for (var at = from; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is TextBox { Tag: "transform" or "number" } box)
                return box;

        return null;
    }

    private void OnFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TextBox { Tag: "transform" } || editObjects.Count == 0) return;

        if (TransformCommand.CreateIfChanged("Edit transform", editObjects, editBefore) is { } command)
            viewModel.Undo.Execute(command);

        editObjects = [];
        editBefore = [];
        viewModel.RefreshSelection();
    }

    private void OnFieldLeaving(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox box) ChangeFieldBy(box);
    }

    /// <summary>
    /// Applies "+=5", "-=5" or "+5" typed into a transform box as a change by that much.
    ///
    /// A plain number, negative ones included, still sets the value: positions below zero are
    /// normal on a plate centred on the origin, so "-5" has to go on meaning minus five.
    ///
    /// Several objects each on their own are changed one by one, since the box shows nothing when
    /// they differ. Everything else - one object, or several as one - has a single value behind
    /// the box, and that value is read, added to and written back through the same property the
    /// box is bound to, so the unit, the lock and the pivot all apply exactly as for a typed number.
    /// </summary>
    private bool ChangeFieldBy(TextBox box)
    {
        if (box.Tag is not "transform") return false;
        if (!FieldInput.TryParseRelative(box.Text, out float delta)) return false;

        var binding = box.GetBindingExpression(TextBox.TextProperty);
        if (binding?.ResolvedSource is not { } source || binding.ResolvedSourcePropertyName is not { } name)
            return false;

        if (!(ReferenceEquals(source, viewModel) && viewModel.ChangeEachBy(name, delta)))
        {
            var property = source.GetType().GetProperty(name);
            if (property is null || property.PropertyType != typeof(float) || !property.CanWrite) return false;

            float now = (float)property.GetValue(source)!;
            if (!float.IsFinite(now)) return false;

            property.SetValue(source, now + delta);
        }

        // Shows the new value, which also means the binding has nothing left to write on the way out.
        binding.UpdateTarget();
        return true;
    }

    // --- Nudging the numeric fields ---------------------------------------------------

    /// <summary>
    /// How much one nudge moves a value. Shift takes bigger steps and Ctrl finer ones, which is
    /// the convention everywhere else and saves reaching for the keyboard to type an exact figure.
    /// </summary>
    /// <summary>
    /// How much one press moves a field.
    ///
    /// Taken from the unit rather than fixed at one, because one of whatever the box holds is the
    /// wrong amount everywhere except millimetres: one inch is a long way and one thousandth of a
    /// metre is nothing. Shift is ten of them and Ctrl a tenth, as before.
    /// </summary>
    private double NudgeStep()
    {
        double step = viewModel.UnitStep;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return step * 10;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return step / 10;
        return step;
    }

    private void OnFieldKey(object sender, KeyEventArgs e)
    {
        // Enter commits whatever has been typed, in any box. Everything here writes back on
        // losing focus, which is right for undo - but it means a value typed and left sitting
        // there never takes effect, and the last field anyone fills in is exactly the one they
        // do not tab out of.
        if (e.Key == Key.Enter && e.OriginalSource is TextBox typed)
        {
            if (ChangeFieldBy(typed)) typed.SelectAll();
            else typed.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            return;
        }

        if (e.OriginalSource is not TextBox { Tag: "transform" or "number" } box) return;

        double direction = e.Key switch { Key.Up => 1, Key.Down => -1, _ => 0 };
        if (direction == 0) return;

        Nudge(box, direction * NudgeStep());
        e.Handled = true;
    }

    private void OnFieldWheel(object sender, MouseWheelEventArgs e)
    {
        // Only while the field has focus, so scrolling the properties panel still scrolls it.
        if (e.OriginalSource is not TextBox { Tag: "transform" or "number" } box
            || !box.IsKeyboardFocusWithin) return;

        Nudge(box, Math.Sign(e.Delta) * NudgeStep());
        e.Handled = true;
    }

    /// <summary>
    /// Adds to the value in a field and pushes it through immediately.
    ///
    /// These fields commit on focus loss, which keeps typing from being applied a digit at a
    /// time; a nudge is a finished edit on its own, so it updates the source at once. The undo
    /// entry still covers the whole time the field held focus, so holding an arrow key down
    /// produces one step rather than fifty.
    /// </summary>
    private static void Nudge(TextBox box, double amount)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            return;

        double updated = current + amount;

        // Snap onto the step grid so repeated nudges tidy up a value like 4.37 rather than
        // carrying its rounding error along forever.
        double step = Math.Abs(amount);
        if (step > 0) updated = Math.Round(updated / step) * step;

        box.Text = updated.ToString("0.####", CultureInfo.CurrentCulture);
        box.CaretIndex = box.Text.Length;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    // --- Cut plane preview -----------------------------------------------------------

    /// <summary>
    /// Puts the view settings into the scene. The plate is rebuilt rather than scaled: its
    /// squares are 10 mm so that it doubles as a ruler, and stretching it would make them lie.
    /// </summary>
    private void ApplyViewSettings()
    {
        if (renderer is null) return;

        renderer.Wireframe = viewModel.ShowWireframe;
        renderer.Xray = viewModel.ShowXray;
        renderer.ShowOutlines = viewModel.ShowOutlines;
        RefreshFocus();
        renderer.ShowOverhangs(viewModel.ShowOverhangs, viewModel.OverhangAngle, viewModel.OverhangColour);

        if (plateShown != PlateNow()) RebuildPlate();

        PlateGroup.Visibility = viewModel.ShowPlate ? Visibility.Visible : Visibility.Collapsed;
        Repaint();
    }

    /// <summary>
    /// Asks for a frame after something outside the renderer has changed what is drawn: the
    /// plate, the split plane, a view setting, the window's own size. See ViewportRepaint for
    /// why anything has to ask at all.
    /// </summary>
    private void Repaint() => repaint?.Ask();

    private void RebuildPlate()
    {
        Repaint();

        foreach (var element in PlateGroup.Children) element.Dispose();
        PlateGroup.Children.Clear();

        var look = PlateNow();
        foreach (var element in BuildPlateVisual.Create(look))
            PlateGroup.Children.Add(element);

        plateShown = look;
    }

    /// <summary>Everything the plate is drawn from, so a change to any of it redraws it.</summary>
    private PlateLook PlateNow() => new(
        viewModel.PlateWidth, viewModel.PlateDepth, viewModel.PlateHeight,
        viewModel.ShowAxes, viewModel.ShowZAxis, viewModel.ShowGridLabels,
        viewModel.Unit.Label, viewModel.Unit.Millimetres);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.GizmoMode) or nameof(MainViewModel.Selected))
        {
            if (gizmo is not null)
            {
                gizmo.Mode = viewModel.GizmoMode;
                gizmo.Rebuild();
            }
        }

        // The labels are written in the current unit, so a change of unit redraws them.
        if (e.PropertyName is nameof(MainViewModel.UnitLabel)) ApplyViewSettings();

        if (e.PropertyName is nameof(MainViewModel.UniformScale) && gizmo is not null)
            gizmo.UniformScale = viewModel.UniformScale;

        if (e.PropertyName is nameof(MainViewModel.ScaleOneSide) && gizmo is not null)
            gizmo.ScaleOneSide = viewModel.ScaleOneSide;

        if (e.PropertyName is nameof(MainViewModel.AroundSelectionCentre) && gizmo is not null)
            gizmo.AroundSelectionCentre = viewModel.AroundSelectionCentre;

        if (e.PropertyName is nameof(MainViewModel.StopOnContact) && gizmo is not null)
            gizmo.StopOnContact = viewModel.StopOnContact;

        if (e.PropertyName is nameof(MainViewModel.KeepOnBedMove) && gizmo is not null)
            gizmo.KeepOnBedMove = viewModel.KeepOnBedMove;

        if (e.PropertyName is nameof(MainViewModel.KeepOnBedScale) && gizmo is not null)
            gizmo.KeepOnBedScale = viewModel.KeepOnBedScale;

        if (e.PropertyName is nameof(MainViewModel.SnapStep) && gizmo is not null)
            gizmo.SnapStep = viewModel.SnapStep;

        if (e.PropertyName is nameof(MainViewModel.SnapRotation))
        {
            if (gizmo is not null) gizmo.SnapRotation = viewModel.SnapRotation;
            if (splitGizmo is not null) splitGizmo.SnapRotation = viewModel.SnapRotation;
            if (placeGizmo is not null) placeGizmo.SnapRotation = viewModel.SnapRotation;
        }

        if (e.PropertyName is nameof(MainViewModel.IsToolRunning)
            or nameof(MainViewModel.IsEmbossMode)
            or nameof(MainViewModel.IsEngraveMode)
            or nameof(MainViewModel.PanelHandles))
        {
            // A tool that has taken the object over owns the handles on it, so the move and
            // resize ones stand down rather than sitting underneath the placement ones - unless the
            // tool hands them to its preview, to be put where it is wanted.
            if (gizmo is not null)
            {
                gizmo.Enabled = !viewModel.IsToolRunning || viewModel.PanelHandles;
                gizmo.RecordsUndo = !viewModel.PanelHandles;
            }
            RefreshPlacementGizmo();
        }

        // A tool has the object: the plate goes a shade cooler, so which mode the window is in
        // can be seen rather than worked out from which buttons have gone grey.
        if (e.PropertyName is nameof(MainViewModel.IsToolInHand))
            View.BackgroundColor = viewModel.IsToolInHand ? ToolBackground : PlainBackground;

        if (e.PropertyName is nameof(MainViewModel.IsSplitMode) or nameof(MainViewModel.IsConnectMode)
            or nameof(MainViewModel.SplitNormal) or nameof(MainViewModel.SplitOffset)
            or nameof(MainViewModel.SplitWithConnectors) or nameof(MainViewModel.Selected)
            || e.PropertyName?.StartsWith("Connector") == true)
        {
            connectorMarksStale = true;
            if (e.PropertyName?.StartsWith("Connector") == true) RefreshSplitPreview();
        }

        if (e.PropertyName is nameof(MainViewModel.SplitGizmoMode) && splitGizmo is not null)
            splitGizmo.Mode = viewModel.SplitGizmoMode;

        if (e.PropertyName is nameof(MainViewModel.IsSplitMode)
            or nameof(MainViewModel.SplitGizmoMode)
            or nameof(MainViewModel.SplitNormal)
            or nameof(MainViewModel.SplitOffset)
            or nameof(MainViewModel.IsExtrudeMode)
            or nameof(MainViewModel.ExtrudeHeightMillimetres)
            or nameof(MainViewModel.Selected))
        {
            RefreshSplitGizmo();
        }

        // Connect objects is in the list as well: it fades the upper part so the marks on the face
        // the two share can be seen. Left out, the fade neither arrived when the tool opened nor
        // went when it closed, and a part stayed see-through after Cancel.
        if (e.PropertyName is nameof(MainViewModel.IsSplitMode)
            or nameof(MainViewModel.IsConnectMode)
            or nameof(MainViewModel.SplitWithConnectors)
            or nameof(MainViewModel.SplitNormal)
            or nameof(MainViewModel.SplitOffset)
            or nameof(MainViewModel.SplitKeep)
            or nameof(MainViewModel.SplitOffcut)
            or nameof(MainViewModel.SplitFillsCut)
            or nameof(MainViewModel.Selected))
        {
            RefreshSplitPreview();
        }

        if (e.PropertyName is nameof(MainViewModel.IsSplitMode)
            or nameof(MainViewModel.SplitAxis)
            or nameof(MainViewModel.SplitNormal)
            or nameof(MainViewModel.SplitOffset)
            or nameof(MainViewModel.SplitPlaneVisible)
            or nameof(MainViewModel.IsExtrudeMode)
            or nameof(MainViewModel.ExtrudeHeightMillimetres))
        {
            UpdateSplitPlane();
        }
    }

    private void UpdateSplitPlane()
    {
        Repaint();

        if (splitPlaneVisual is not null)
        {
            OverlayGroup.Children.Remove(splitPlaneVisual);
            splitPlaneVisual.Dispose();
            splitPlaneVisual = null;
        }

        if (!viewModel.IsSplitMode && !viewModel.IsExtrudeMode) return;

        var selection = viewModel.Scene.Selection;
        if (selection.Count == 0) return;

        // One plane cuts everything selected, so the slab has to span the whole group.
        var bounds = Bounds.Empty;
        foreach (var o in selection) bounds = bounds.Union(o.WorldBounds);
        // Extrude down shows the same plane, lying flat at the height it will cut.
        Vector3 normal = viewModel.IsExtrudeMode ? Vector3.UnitZ : viewModel.SplitNormal;
        float offset = viewModel.IsExtrudeMode ? viewModel.ExtrudeHeightMillimetres : viewModel.SplitOffset;

        // Wide enough to overhang the solid whichever way the plane is tilted.
        float width = bounds.Diagonal + 12f;
        const float thickness = 0.15f; // a thin slab, not a zero-thickness quad, which z-fights

        // Built flat on XY, then swung onto the normal and moved onto the plane. The anchor is
        // the point of the plane nearest the solid, so the slab stays over the object as it tilts.
        float distance = Vector3.Dot(normal, bounds.Center) - offset;
        Vector3 anchor = bounds.Center - normal * distance;

        var slab = MeshTransform.Transformed(
            Primitives.Box(width, width, thickness),
            MeshTransform.RotationBetween(Vector3.UnitZ, normal) * Matrix4x4.CreateTranslation(anchor));

        splitPlaneVisual = new MeshGeometryModel3D
        {
            Geometry = MeshConverter.ToGeometry(slab),
            Material = new PhongMaterial
            {
                // Light enough to see the model through. At nearly half opaque it painted
                // everything behind it orange, which mattered little while the whole object was
                // drawn and a great deal once the off cut could be taken away.
                DiffuseColor = new SharpDX.Color4(1f, 0.6f, 0.2f, 0.22f),
                AmbientColor = new SharpDX.Color4(0.35f, 0.18f, 0.04f, 1f)
            },
            IsTransparent = true,
            IsHitTestVisible = false
        };

        OverlayGroup.Children.Add(splitPlaneVisual);
    }

    private void OnSplitAxisChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out Axis axis))
            viewModel.SplitAxis = axis;
    }

    private void OnSplitKeepChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out SplitKeep keep))
            viewModel.SplitKeep = keep;
    }

    private void OnSplitOffcutChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out SplitOffcut offcut))
            viewModel.SplitOffcut = offcut;
    }

    /// <summary>
    /// Says the split preview needs redoing. Leaving is done at once; anything else waits for
    /// <see cref="SettleSplitPreview"/>, because a drag asks for this on every mouse move.
    /// </summary>
    /// <summary>
    /// Redraws the marks on the cut face when they are out of date. On the frame tick with the
    /// split preview, since reading the section is the same order of work and both follow the
    /// plane as it slides.
    /// </summary>
    private void SettleConnectorMarks()
    {
        if (!connectorMarksStale || renderer is null) return;

        connectorMarksStale = false;
        renderer.ShowConnectorMarks(viewModel.ConnectorMarks(), viewModel.ConnectorPlane?.Normal ?? viewModel.SplitNormal);
    }

    /// <summary>
    /// Tells the renderer what a tool has in hand, so everything else can stand aside. The one
    /// place that decides it: a split and a tool's preview both want the plate to themselves,
    /// and they are set from different corners of the window - each having its own way of asking
    /// meant either could undo the other's.
    /// </summary>
    private void RefreshFocus()
    {
        if (renderer is null) return;

        IReadOnlyList<SceneObject>? working = null;

        // Nothing in hand and everything aside: a sketch is drawn on the plate itself, and
        // anything standing on it is in the way of both the drawing and the clicks.
        if (viewModel.IsSketchMode) working = [];
        else if (viewModel.PreviewOnly is { Count: > 0 } previewing) working = previewing;
        else if (viewModel.IsSplitMode || viewModel.ConnectorPlane is not null) working = viewModel.Scene.Selection;

        renderer.Focus(working);
    }

    private void RefreshSplitPreview(bool now = false)
    {
        if (renderer is null) return;

        if (!viewModel.IsSplitMode && viewModel.ConnectorPlane is null)
        {
            splitPreviewStale = false;
            RefreshFocus();
            renderer.ClearSplit();
            return;
        }

        // Immediately, not on the throttle below: this is a visibility flag, not geometry.
        RefreshFocus();

        splitPreviewStale = true;
        if (now) splitPreviewWait = 0;
    }

    /// <summary>
    /// Redraws the selection as the split would leave it, at a rate the machine can stand.
    ///
    /// Cutting the triangles is one pass, but the shading normals have to be worked out again
    /// afterwards and on a dense model that is not free. So the preview is redone at most every
    /// so often, and the wait is set from how long the last one actually took: a light model
    /// follows the plane about, a heavy one catches up a few times a second, and neither leaves
    /// the pointer waiting on the geometry.
    /// </summary>
    private void SettleSplitPreview()
    {
        if (!splitPreviewStale || renderer is null) return;
        if (!viewModel.IsSplitMode && viewModel.ConnectorPlane is null) return;
        if (splitPreviewClock.Elapsed.TotalMilliseconds < splitPreviewWait) return;

        splitPreviewStale = false;

        // Connectors keep both halves, so nothing is being thrown away to fade - but the marks on
        // the cut face are inside the model, and the half above them has to come off to see any of
        // it. The upper half is the one faded, whichever way the plane faces.
        var plane = viewModel.ConnectorPlane;
        var normal = plane?.Normal ?? viewModel.SplitNormal;
        float offset = plane?.Offset ?? viewModel.SplitOffset;
        var keep = plane is null ? viewModel.SplitKeep
            : normal.Z >= 0f ? SplitKeep.Back : SplitKeep.Front;
        var offcut = plane is null ? viewModel.SplitOffcut : SplitOffcut.Faded;

        long started = Stopwatch.GetTimestamp();
        renderer.ShowSplit(viewModel.Scene.Selection, normal, offset, keep, offcut,
                           plane is null && viewModel.SplitFillsCut);

        splitPreviewWait = Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds * 4, 60, 600);
        splitPreviewClock.Restart();
    }

    // --- View presets ----------------------------------------------------------------

    /// <summary>
    /// Reframes the scene while keeping the current viewing direction and Z pointing up.
    ///
    /// The toolkit's own ZoomExtents does not preserve the up vector, and on a small part it
    /// can swing the camera under the build plate, which is disorienting and hard to recover
    /// from. Recomputing the position from the existing direction keeps the view predictable.
    /// </summary>
    private void ZoomExtents()
    {
        if (viewModel.Scene.Objects.Count == 0) return;
        if (View.Camera is not PerspectiveCamera camera) return;

        var back = -camera.LookDirection;
        if (back.Length < 1e-6) return;

        // Keep whichever way up the view already is. Forcing Z-up reads as harmless until the
        // camera is looking straight down, where up and the view direction are the same line -
        // the camera degenerates and the viewport comes back empty, build plate and all. That is
        // what Zoom to fit did in the Top view.
        LookFrom(back, camera.UpDirection);
    }

    /// <summary>
    /// Drops the recent-files menu under its button. A context menu is used rather than a popup
    /// so the list styles and behaves like every other menu in Windows.
    /// </summary>
    private void OnShowRecent(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not { } menu) return;

        if (viewModel.RecentFiles.Count == 0)
        {
            viewModel.Status = "No recent projects yet";
            return;
        }

        // The menu inherits the window's DataContext so its items can reach the command.
        menu.DataContext = viewModel;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// A scale picked from the list takes effect at once. Typing still waits for the field to
    /// be left, so "160" is not read as 1 and then 16 on its way in.
    /// </summary>
    private void OnScaleChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox box && box.SelectedItem is string chosen)
            viewModel.ModelScaleText = chosen;
    }

    private void OnAbout(object sender, RoutedEventArgs e) =>
        new AboutDialog { Owner = this }.ShowDialog();

    private void OnZoomExtents(object sender, RoutedEventArgs e) => ZoomExtents();

    private void OnViewTop(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0, 0, 1), new Media3D.Vector3D(0, 1, 0));

    private void OnViewFront(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0, -1, 0), new Media3D.Vector3D(0, 0, 1));

    private void OnViewRight(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(1, 0, 0), new Media3D.Vector3D(0, 0, 1));

    private void OnViewIso(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0.8, -1, 0.7), new Media3D.Vector3D(0, 0, 1));

    // A building has four walls and the views only reached two of them. Without these, bringing
    // the back or the left of a model round to face the camera meant turning the object itself
    // half a revolution and turning it back afterwards - which bakes the rotation into the mesh
    // and shifts its centre a little every time.
    private void OnViewBack(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0, 1, 0), new Media3D.Vector3D(0, 0, 1));

    private void OnViewLeft(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(-1, 0, 0), new Media3D.Vector3D(0, 0, 1));

    private void OnViewBottom(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0, 0, -1), new Media3D.Vector3D(0, 1, 0));

    /// <summary>
    /// An up vector the camera can actually use. Anything parallel to the way it is looking
    /// leaves the view matrix with no left and no right, and the renderer draws nothing at all.
    /// </summary>
    private static Media3D.Vector3D Upright(Media3D.Vector3D direction, Media3D.Vector3D up)
    {
        direction.Normalize();

        if (up.Length < 1e-6) up = new Media3D.Vector3D(0, 0, 1);
        up.Normalize();

        if (Math.Abs(Media3D.Vector3D.DotProduct(direction, up)) < 0.999) return up;

        // Looking along the axis it was using: pick another. Straight up or down wants Y;
        // everything else can have Z.
        return Math.Abs(direction.Z) > 0.9
            ? new Media3D.Vector3D(0, 1, 0)
            : new Media3D.Vector3D(0, 0, 1);
    }

    private void LookFrom(Media3D.Vector3D direction, Media3D.Vector3D up)
    {
        if (View.Camera is not PerspectiveCamera camera) return;

        up = Upright(direction, up);

        var bounds = viewModel.Scene.ComputeBounds();
        var centre = bounds.IsEmpty ? new Vector3(0, 0, 0) : bounds.Center;
        double distance = bounds.IsEmpty ? 320 : Math.Max(bounds.Diagonal * 1.8, 60);

        direction.Normalize();
        camera.Position = new Media3D.Point3D(
            centre.X + direction.X * distance,
            centre.Y + direction.Y * distance,
            centre.Z + direction.Z * distance);
        camera.LookDirection = new Media3D.Vector3D(-direction.X * distance, -direction.Y * distance, -direction.Z * distance);
        camera.UpDirection = up;
    }
}
