using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FastCraft3D.Geometry;
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
    private float plateShown;
    private GizmoController? gizmo;
    private SplitPlaneGizmo? splitGizmo;
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

    /// <summary>How far the pointer may wander and still count as a click rather than a drag.</summary>
    private const double ClickSlopPixels = 3.0;

    // Captured when a properties field takes focus, so one edit is one undo step.
    private List<SceneObject> editObjects = [];
    private List<TransformState> editBefore = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;

        EffectsManager = new DefaultEffectsManager();
        View.EffectsManager = EffectsManager;
        View.Camera = CreateCamera();
        ConfigureCameraGestures();

        RebuildPlate();
        viewModel.ViewChanged += ApplyViewSettings;

        renderer = new SceneRenderer(ContentGroup, viewModel.Scene);

        viewModel.EngraveFaceChanged += () =>
            renderer.ShowFace(viewModel.EngraveFace, viewModel.EngravePreview);

        listSync = new SelectionListSync(ObjectList, viewModel.Scene);
        listSync.ChangedFromList += viewModel.RefreshSelection;
        viewModel.SelectionChanged += () => listSync.PushToList();

        gizmo = new GizmoController(GizmoLayer, new Viewport3DXProjector(View), viewModel.Scene, viewModel.Undo);
        gizmo.Feedback += text => viewModel.Status = text;
        GizmoLayer.PreviewMouseLeftButtonDown += OnGizmoDown;
        GizmoLayer.PreviewMouseMove += OnGizmoMove;
        GizmoLayer.PreviewMouseLeftButtonUp += OnGizmoUp;

        splitGizmo = new SplitPlaneGizmo(SplitGizmoLayer, new Viewport3DXProjector(View));
        splitGizmo.Feedback += text => viewModel.Status = text;
        splitGizmo.Changed += (offset, normal) =>
        {
            viewModel.SplitOffset = offset;
            viewModel.SplitNormal = normal;
            UpdateSplitPlane();
        };
        SplitGizmoLayer.PreviewMouseLeftButtonDown += OnSplitGizmoDown;
        SplitGizmoLayer.PreviewMouseMove += OnSplitGizmoMove;
        SplitGizmoLayer.PreviewMouseLeftButtonUp += OnSplitGizmoUp;

        // The handles are projected from the camera, so they have to follow it. Repositioning
        // only when something actually moved keeps this off the per-frame allocation path.
        CompositionTarget.Rendering += OnFrame;

        // A resize changes where everything projects to without touching the camera, so the
        // dirty check above would not notice on its own.
        View.SizeChanged += (_, _) => gizmo?.Reposition();

        viewModel.ZoomExtentsRequested += () => Dispatcher.BeginInvoke(new Action(ZoomExtents));
        viewModel.PropertyChanged += OnViewModelChanged;

        // One undo entry per field edit rather than one per keystroke.
        AddHandler(GotFocusEvent, new RoutedEventHandler(OnFieldGotFocus), true);
        AddHandler(LostFocusEvent, new RoutedEventHandler(OnFieldLostFocus), true);

        // Arrow keys and the wheel nudge the numeric fields.
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnFieldKey), true);
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnFieldWheel), true);

        PreviewKeyDown += OnWindowKeyDown;

        View.PreviewMouseLeftButtonDown += OnViewportLeftDown;
        View.PreviewMouseMove += OnViewportMove;
        View.PreviewMouseLeftButtonUp += OnViewportLeftUp;

        // The last chance to save: this is the only path where unsaved work would vanish
        // without the user having asked for anything.
        Closing += (_, e) => e.Cancel = !viewModel.ConfirmDiscardChanges();

        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            listSync?.Dispose();
            renderer?.Dispose();
            (EffectsManager as IDisposable)?.Dispose();
        };
    }

    /// <summary>
    /// Single-key mode shortcuts. Deliberately not Window.InputBindings: an unmodified key
    /// binding there fires no matter what has focus, so typing an "s" into the name field
    /// would silently switch tool instead of typing.
    /// </summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is TextBox) return;

        switch (e.Key)
        {
            case Key.M: viewModel.GizmoMode = GizmoMode.Move; break;
            case Key.R: viewModel.GizmoMode = GizmoMode.Rotate; break;
            case Key.S: viewModel.GizmoMode = GizmoMode.Scale; break;
            case Key.O: viewModel.ScaleOneSide = !viewModel.ScaleOneSide; break;
            default: return;
        }
        e.Handled = true;
    }

    // --- Manipulator ------------------------------------------------------------------

    private void OnGizmoDown(object sender, MouseButtonEventArgs e)
    {
        if (gizmo is null) return;
        if (!gizmo.TryBeginDrag(e.GetPosition(GizmoLayer), e.OriginalSource)) return;

        GizmoLayer.CaptureMouse();
        e.Handled = true;
    }

    private void OnGizmoMove(object sender, MouseEventArgs e)
    {
        if (gizmo is not { IsDragging: true }) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        gizmo.ContinueDrag(e.GetPosition(GizmoLayer));
        e.Handled = true;
    }

    private void OnGizmoUp(object sender, MouseButtonEventArgs e)
    {
        if (gizmo is not { IsDragging: true }) return;

        GizmoLayer.ReleaseMouseCapture();
        gizmo.EndDrag();
        viewModel.RefreshSelection();
        e.Handled = true;
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
        e.Handled = true;
    }

    /// <summary>Keeps the split handles on the plane, and hides them outside split mode.</summary>
    private void RefreshSplitGizmo()
    {
        if (splitGizmo is null) return;

        var selection = viewModel.Scene.Selection;
        bool on = viewModel.IsSplitMode && selection.Count > 0;

        // Centred on everything the plane will cut, not on whichever object came first.
        var bounds = Bounds.Empty;
        if (on) foreach (var o in selection) bounds = bounds.Union(o.WorldBounds);
        Vector3 centre = bounds.IsEmpty ? Vector3.Zero : bounds.Center;

        splitGizmo.Show(on, viewModel.SplitNormal, viewModel.SplitOffset, centre);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        // The gizmo decides for itself whether it is out of date, by checking where the
        // selection currently projects to rather than by watching the camera and the window
        // size. Those inputs miss a resize that the viewport has not finished applying yet -
        // which is what left the handles stranded in a corner.
        if (gizmo is null) return;
        if (gizmo.NeedsReposition || gizmo.IsStale()) gizmo.Reposition();
        if (viewModel.IsSplitMode) splitGizmo?.Reposition();
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

    // --- Selection and dragging ------------------------------------------------------

    private void OnViewportLeftDown(object sender, MouseButtonEventArgs e)
    {
        var screen = e.GetPosition(View);
        var hit = FirstHit(screen);
        var target = renderer?.Resolve(hit?.ModelHit);

        pendingToggleOff = false;
        pendingExclusive = false;
        pendingClear = false;
        pressScreen = screen;

        if (target is null)
        {
            // With sticky selection on, empty space never changes anything. With it off, a
            // click out here clears the selection - but only a click: the same press is also
            // how a camera orbit begins, so the decision waits for the release.
            if (!viewModel.StickySelection && !IsShiftDown && !IsControlDown) pendingClear = true;
            return; // unhandled, so the camera gesture takes over
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

    private void OnViewportMove(object sender, MouseEventArgs e)
    {
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

    private HitTestResult? FirstHit(Point screen)
    {
        var hits = View.FindHits(screen);
        if (hits is null) return null;

        foreach (var hit in hits)
            if (hit.IsValid && renderer?.Resolve(hit.ModelHit) is not null)
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

    private static bool IsControlDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
    private static bool IsShiftDown => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    // --- Properties-panel undo coalescing --------------------------------------------

    private void OnFieldGotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TextBox { Tag: "transform" }) return;
        editObjects = viewModel.Scene.Selection.ToList();
        editBefore = editObjects.Select(TransformState.Capture).ToList();
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

    // --- Nudging the numeric fields ---------------------------------------------------

    /// <summary>
    /// How much one nudge moves a value. Shift takes bigger steps and Ctrl finer ones, which is
    /// the convention everywhere else and saves reaching for the keyboard to type an exact figure.
    /// </summary>
    private static double NudgeStep()
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return 10;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return 0.1;
        return 1;
    }

    private void OnFieldKey(object sender, KeyEventArgs e)
    {
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

        if (Math.Abs(plateShown - viewModel.PlateSize) > 0.01f) RebuildPlate();

        PlateGroup.Visibility = viewModel.ShowPlate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildPlate()
    {
        foreach (var element in PlateGroup.Children) element.Dispose();
        PlateGroup.Children.Clear();

        foreach (var element in BuildPlateVisual.Create(viewModel.PlateSize))
            PlateGroup.Children.Add(element);

        plateShown = viewModel.PlateSize;
    }

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

        if (e.PropertyName is nameof(MainViewModel.UniformScale) && gizmo is not null)
            gizmo.UniformScale = viewModel.UniformScale;

        if (e.PropertyName is nameof(MainViewModel.ScaleOneSide) && gizmo is not null)
            gizmo.ScaleOneSide = viewModel.ScaleOneSide;

        if (e.PropertyName is nameof(MainViewModel.SnapStep) && gizmo is not null)
            gizmo.SnapStep = viewModel.SnapStep;

        if (e.PropertyName is nameof(MainViewModel.SnapRotation))
        {
            if (gizmo is not null) gizmo.SnapRotation = viewModel.SnapRotation;
            if (splitGizmo is not null) splitGizmo.SnapRotation = viewModel.SnapRotation;
        }

        if (e.PropertyName is nameof(MainViewModel.IsSplitMode)
            or nameof(MainViewModel.SplitNormal)
            or nameof(MainViewModel.SplitOffset)
            or nameof(MainViewModel.Selected))
        {
            RefreshSplitGizmo();
        }

        if (e.PropertyName is nameof(MainViewModel.IsSplitMode)
            or nameof(MainViewModel.SplitAxis)
            or nameof(MainViewModel.SplitOffset)
            or nameof(MainViewModel.SplitPlaneVisible))
        {
            UpdateSplitPlane();
        }
    }

    private void UpdateSplitPlane()
    {
        if (splitPlaneVisual is not null)
        {
            OverlayGroup.Children.Remove(splitPlaneVisual);
            splitPlaneVisual.Dispose();
            splitPlaneVisual = null;
        }

        if (!viewModel.IsSplitMode) return;

        var selection = viewModel.Scene.Selection;
        if (selection.Count == 0) return;

        // One plane cuts everything selected, so the slab has to span the whole group.
        var bounds = Bounds.Empty;
        foreach (var o in selection) bounds = bounds.Union(o.WorldBounds);
        Vector3 normal = viewModel.SplitNormal;
        float offset = viewModel.SplitOffset;

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
                DiffuseColor = new SharpDX.Color4(1f, 0.55f, 0.15f, 0.45f),
                AmbientColor = new SharpDX.Color4(0.4f, 0.2f, 0.05f, 1f)
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
        LookFrom(back, new Media3D.Vector3D(0, 0, 1));
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

    private void OnZoomExtents(object sender, RoutedEventArgs e) => ZoomExtents();

    private void OnViewTop(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0, 0, 1), new Media3D.Vector3D(0, 1, 0));

    private void OnViewFront(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0, -1, 0), new Media3D.Vector3D(0, 0, 1));

    private void OnViewRight(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(1, 0, 0), new Media3D.Vector3D(0, 0, 1));

    private void OnViewIso(object sender, RoutedEventArgs e) =>
        LookFrom(new Media3D.Vector3D(0.8, -1, 0.7), new Media3D.Vector3D(0, 0, 1));

    private void LookFrom(Media3D.Vector3D direction, Media3D.Vector3D up)
    {
        if (View.Camera is not PerspectiveCamera camera) return;

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
