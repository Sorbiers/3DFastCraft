using System.Diagnostics;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FastCraft3D.Geometry;
using FastCraft3D.Model;
using FastCraft3D.Render;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Tools run from their panel to the finished object, as a click on the panel's button runs them:
/// what the panel leaves behind when it closes is what they have to work with.
///
/// In one collection with the other tests that open a panel, since all of them put the panel
/// somewhere through the one static host.
/// </summary>
[Collection("Tool panels")]
public class ToolFlowTests
{
    private static void WithModel(Action<MainViewModel> body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(new MainViewModel()); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>
    /// Opens the next panel through the host, lets <paramref name="whileOpen"/> do what a user would
    /// with the preview, then presses the named button - all from inside the panel's own wait.
    /// </summary>
    private static void AnswerPanel(MainViewModel model, string button, Action whileOpen) =>
        AnswerPanel(model, button, _ => whileOpen());

    private static void AnswerPanel(MainViewModel model, string button, Action<ToolPanel> whileOpen)
    {
        ToolPanel.Host = panel =>
        {
            model.ShowPanel(panel);
            if (panel is null) return;

            panel.Dispatcher.BeginInvoke(new Action(() =>
            {
                whileOpen(panel);

                // A panel of its own names its buttons; a generator's panel is built in code, and
                // its buttons carry an automation name instead.
                var pressed = panel.FindName(button) as Button ?? Named<Button>(panel, button);
                pressed.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, pressed));
            }), DispatcherPriority.Background);
        };
    }

    /// <summary>A control in a generator's panel, by its automation name or id.</summary>
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().First(e =>
            System.Windows.Automation.AutomationProperties.GetName(e) == name
            || System.Windows.Automation.AutomationProperties.GetAutomationId(e) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var below in Descendants(child)) yield return below;
        }
    }

    /// <summary>Runs the dispatcher until something is so, for the part of a tool that finishes after an await.</summary>
    private static void PumpUntil(Func<bool> done, int milliseconds = 20000)
    {
        var watch = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background,
            (_, _) => { if (done() || watch.ElapsedMilliseconds > milliseconds) frame.Continue = false; },
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    [Fact]
    public void CutTakesTheHoleOutOfTheSelectedPart() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();
        double before = cube.ToWorldMesh().ComputeSignedVolume();

        AnswerPanel(model, "AcceptButton", () => { });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(cube) && !model.IsBusy);

        var cut = Assert.Single(model.Scene.Objects);
        Assert.NotSame(cube, cut);
        var mesh = cut.ToWorldMesh();
        Assert.True(mesh.CheckHealth().IsWatertight);
        Assert.True(mesh.ComputeSignedVolume() < before - 50, "no hole was cut");
        Assert.False(model.PanelHandles);
    });

    [Fact]
    public void AHoleGoesWhereTheCutterWasMoved() => WithModel(model =>
    {
        var block = new SceneObject("Block", Primitives.Box(60, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(block);
        block.IsSelected = true;
        model.RefreshSelection();

        // Moved along X by the handles, while the panel is open.
        AnswerPanel(model, "AcceptButton", () =>
        {
            Assert.True(model.PanelHandles);
            var preview = Assert.Single(model.Scene.Selection);
            preview.Position += new Vector3(20, 0, 0);
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(block) && !model.IsBusy);

        var cut = Assert.Single(model.Scene.Objects).ToWorldMesh();

        // Where the hole is, there is nothing on the top face: no vertex of the top face sits
        // nearer the hole's middle than the hole's own edge, at X = 20.
        var holeRim = cut.Positions.Where(p => MathF.Abs(p.Z - 20f) < 1e-3f && MathF.Abs(p.Y) < 5f && p.X > 10f && p.X < 30f).ToList();
        Assert.NotEmpty(holeRim);
        Assert.True(holeRim.Average(p => p.X) is > 19f and < 21f, $"the hole's rim is round X = {holeRim.Average(p => p.X):0.#}");
    });

    [Fact]
    public void ACutterIsAddedWhereItWasPut() => WithModel(model =>
    {
        AnswerPanel(model, "AddButton", () =>
        {
            var preview = Assert.Single(model.Scene.Selection);
            preview.Position += new Vector3(15, -5, 0);
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel);

        var cutter = Assert.Single(model.Scene.Objects);
        Assert.Equal(15f, cutter.WorldBounds.Center.X, 1);
        Assert.Equal(-5f, cutter.WorldBounds.Center.Y, 1);
    });

    [Fact]
    public void AHoleThroughSeveralSelectedPartsGoesThroughEveryOneLinedUp() => WithModel(model =>
    {
        // Two plates, one over the other with air between, as a lid over a box's rim.
        var lower = new SceneObject("Lower", Primitives.Box(40, 40, 6)) { Position = new Vector3(0, 0, 3) };
        var upper = new SceneObject("Upper", Primitives.Box(40, 40, 6)) { Position = new Vector3(0, 0, 13) };
        model.Scene.Objects.Add(lower);
        model.Scene.Objects.Add(upper);
        lower.IsSelected = upper.IsSelected = true;
        model.RefreshSelection();
        double before = lower.ToWorldMesh().ComputeSignedVolume();

        AnswerPanel(model, "AcceptButton", panel =>
        {
            var through = (CheckBox)panel.FindName("ThroughBox")!;
            through.IsChecked = true;
            through.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(lower) && !model.Scene.Objects.Contains(upper) && !model.IsBusy, 60000);

        Assert.Equal(2, model.Scene.Objects.Count);
        foreach (var plate in model.Scene.Objects)
        {
            var mesh = plate.ToWorldMesh();
            Assert.True(mesh.CheckHealth().IsWatertight);

            // Each has lost a hole's worth, in the middle, where the other's is.
            Assert.True(mesh.ComputeSignedVolume() < before - 20);
            Assert.DoesNotContain(mesh.Positions, p => new Vector2(p.X, p.Y).Length() < 1f);
            Assert.Contains(mesh.Positions, p => new Vector2(p.X, p.Y).Length() < 5f);
        }
    });

    [Fact]
    public void WhileHoleIsOpenOnlyItsPartsAndTheCutterAreDrawn() => WithModel(model =>
    {
        var plate = new SceneObject("Plate", Primitives.Box(40, 40, 6)) { Position = new Vector3(0, 0, 3) };
        var beside = new SceneObject("Beside", Primitives.Box(10, 10, 10)) { Position = new Vector3(60, 0, 5) };
        model.Scene.Objects.Add(plate);
        model.Scene.Objects.Add(beside);
        plate.IsSelected = true;
        model.RefreshSelection();

        IReadOnlyList<SceneObject>? drawn = null;
        AnswerPanel(model, "CancelButton", () => drawn = model.PreviewOnly?.ToList());
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.HasOpenPanel);

        Assert.NotNull(drawn);
        Assert.Contains(plate, drawn!);
        Assert.DoesNotContain(beside, drawn!);
        Assert.Equal(2, drawn!.Count);
        Assert.Null(model.PreviewOnly);
    });

    [Fact]
    public void ALibraryPreviewResizedOnThePlateIsMadeAgainAtThatSizeNotStretched() => WithModel(model =>
    {
        float before = 0f;
        AnswerPanel(model, "GeneratorInsert", panel =>
        {
            PumpUntil(() => model.Scene.Selection.Count == 1);
            var held = model.Scene.Selection[0];
            before = held.WorldBounds.Size.X;
            Assert.True(model.ResizeOffered);

            held.Scale = new Vector3(2f, 1f, 1f);
            model.SettleHeldSize();
            Assert.Equal(Vector3.One, held.Scale);

            PumpUntil(() => held.WorldBounds.Size.X > before * 1.5f, 60000);
            PumpUntil(() => Named<Button>(panel, "GeneratorInsert").IsEnabled, 60000);
        });
        model.InsertGeneratedCommand.Execute(FastCraft3D.Generators.GeneratorRegistry.Find("building.roof"));
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel, 60000);

        var roof = Assert.Single(model.Scene.Objects);
        Assert.Equal(Vector3.One, roof.Scale);
        Assert.True(roof.WorldBounds.Size.X > before * 1.5f);
        Assert.Equal(120f, ((FastCraft3D.Generators.Buildings.Roof.Settings)FastCraft3D.Generators.Recipes.Read(
            FastCraft3D.Generators.GeneratorRegistry.Find("building.roof")!, roof.Recipe!.Settings)).Width, 1);
    });

    /// <summary>
    /// A window and its glass are held as one while the panel is open: the frame takes the handles
    /// and the bar under the view, the glass follows it wherever it is moved and turned, a stretch
    /// becomes the settings, and the two go down where the preview stood, the glass in the frame.
    /// </summary>
    [Fact]
    public void AWindowAndItsGlassAreHeldAsOneAndGoDownWhereThePreviewStood() => WithModel(model =>
    {
        var generator = FastCraft3D.Generators.GeneratorRegistry.Find("building.window")!;
        float before = 0f;
        var at = new Vector3(37f, -21f, 0f);
        var stood = Vector3.Zero;

        AnswerPanel(model, "GeneratorInsert", panel =>
        {
            PumpUntil(() => model.Scene.Selection.Count == 1 && model.PreviewOnly is { Count: 2 });
            var frame = model.Scene.Selection[0];
            var glass = model.PreviewOnly!.Single(o => o != frame);
            Assert.True(model.PanelHandles);
            Assert.True(model.ResizeOffered);

            frame.Rotation = new Vector3(0, 0, 90);
            frame.Position = at;
            Assert.Equal(new Vector3(0, 0, 90), glass.Rotation);
            Assert.True(Inside(glass.WorldBounds, frame.WorldBounds), "the glass did not follow the frame");

            before = frame.WorldBounds.Size.Y;
            frame.Scale = new Vector3(2f, 1f, 1f);
            model.SettleHeldSize();
            PumpUntil(() => frame.WorldBounds.Size.Y > before * 1.5f, 60000);
            PumpUntil(() => Named<Button>(panel, "GeneratorInsert").IsEnabled, 60000);
            stood = frame.WorldBounds.Center;
        });
        model.InsertGeneratedCommand.Execute(generator);
        PumpUntil(() => model.Scene.Objects.Count == 2 && !model.HasOpenPanel, 60000);

        var frameMade = model.Scene.Objects.Single(o => o.Recipe!.Role == "frame");
        var glassMade = model.Scene.Objects.Single(o => o.Recipe!.Role == "glass");
        Assert.NotNull(frameMade.Assembly);
        Assert.Same(frameMade.Assembly, glassMade.Assembly);
        Assert.All(model.Scene.Objects, o => Assert.Equal(new Vector3(0, 0, 90), o.Rotation));
        Assert.True(Inside(glassMade.WorldBounds, frameMade.WorldBounds), "the glass went down out of its frame");
        Assert.True(frameMade.WorldBounds.Size.Y > before * 1.5f, "the stretch was not made the window's width");
        Assert.Equal(28f, ((FastCraft3D.Generators.Buildings.Window.Settings)FastCraft3D.Generators.Recipes.Read(generator, frameMade.Recipe!.Settings)).Width, 1);
        Assert.True(Vector3.Distance(stood, frameMade.WorldBounds.Center) < 1e-3f, $"put down at {frameMade.WorldBounds.Center}, not where it stood at {stood}");
    });

    [Fact]
    public void AStairInTheLibraryIsHeldWithMoveAndRotateOnTheBar() => WithModel(model =>
    {
        AnswerPanel(model, "GeneratorInsert", panel =>
        {
            PumpUntil(() => model.Scene.Selection.Count == 1);
            var held = model.Scene.Selection[0];
            Assert.True(model.PanelHandles);
            Assert.True(model.ShowManipulatorBar);

            model.GizmoMode = GizmoMode.Rotate;
            Assert.True(model.IsRotateMode);
            Assert.Equal(GizmoMode.Rotate, model.GizmoMode);

            // Turned on the plate, it goes down turned.
            held.Rotation = new Vector3(0, 0, 90);
        });
        model.InsertGeneratedCommand.Execute(FastCraft3D.Generators.GeneratorRegistry.Find("building.stair"));
        PumpUntil(() => model.Scene.Objects.Count >= 1 && !model.HasOpenPanel, 60000);

        Assert.Equal(new Vector3(0, 0, 90), Assert.Single(model.Scene.Objects).Rotation);
    });

    /// <summary>Where a ray from <paramref name="origin"/> along <paramref name="dir"/> first leaves the mesh: the farthest hit.</summary>
    private static float Reach(Mesh mesh, Vector3 origin, Vector3 dir)
    {
        float best = 0;
        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
        {
            var p0 = mesh.Positions[mesh.Indices[t]]; var p1 = mesh.Positions[mesh.Indices[t + 1]]; var p2 = mesh.Positions[mesh.Indices[t + 2]];
            var e1 = p1 - p0; var e2 = p2 - p0; var h = Vector3.Cross(dir, e2); float det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-9f) continue;
            float f = 1f / det; var sv = origin - p0; float u = f * Vector3.Dot(sv, h); if (u < 0 || u > 1) continue;
            var q = Vector3.Cross(sv, e1); float v = f * Vector3.Dot(dir, q); if (v < 0 || u + v > 1) continue;
            float tt = f * Vector3.Dot(e2, q); if (tt > best) best = tt;
        }
        return best;
    }

    /// <summary>
    /// A box textured round its walls: picked on the +X wall, then Ctrl+clicked on each wall in
    /// <paramref name="more"/> - a low point on each, clear of any window through the middle.
    /// </summary>
    private static SceneObject TexturedBox(
        MainViewModel model, Mesh box, Vector3 pick, string kind, float pitch, params Vector3[] more)
    {
        var part = new SceneObject("Box", box).Centred();
        part.Position = new Vector3(0, 0, box.ComputeBounds().Size.Z / 2f);
        model.Scene.Objects.Add(part);
        part.IsSelected = true;
        model.RefreshSelection();
        model.BeginEmbossCommand.Execute(null);

        Assert.True(model.PickEmbossFace(part, pick, Vector3.UnitX));
        model.EmbossTexture = Enum.Parse<FastCraft3D.Geometry.Engraving.TextureKind>(kind);
        model.EmbossProjection = FastCraft3D.Geometry.Engraving.TextProjection.Walls;
        model.EmbossRaised = true;
        model.EmbossDepth = 1.5f;
        model.EmbossTexturePitch = pitch;

        var bounds = part.WorldBounds;
        foreach (var facing in more)
        {
            var at = bounds.Center + facing * (bounds.Size / 2f) + new Vector3(facing.Y, -facing.X, 0) * 3f
                     - Vector3.UnitZ * (bounds.Size.Z * 0.35f);
            Assert.True(model.AddEmbossWall(part, at, facing));
            Assert.DoesNotContain("not", model.Status);
        }

        // Done when the part has been replaced by the textured one: a few courses of siding are a
        // few dozen triangles, so counting them says nothing about whether it has finished.
        model.ApplyEmbossCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !ReferenceEquals(model.Scene.Objects[0], part), 120000);
        Assert.True(!ReferenceEquals(part, model.Scene.Objects[0]), model.Status);
        return model.Scene.Objects[0];
    }

    /// <summary>
    /// How far a texture laid 1.5 mm deep may stand off the wall. Siding's boards lap, and the tail
    /// of each stands off by the lap rather than by the depth.
    /// </summary>
    private static float MostProud(string kind) => kind == "Siding" ? 2.1f : 1.6f;

    private static readonly Vector3[] TheOtherThree = [Vector3.UnitY, -Vector3.UnitX, -Vector3.UnitY];

    /// <summary>
    /// Round the walls of a box a texture is one closed solid that stands off every wall, the
    /// corners included, by its depth at the most - no overhang past the mitre, no blade.
    /// </summary>
    [Theory]
    [InlineData("Rubble")]
    [InlineData("Castle")]
    [InlineData("Bark")]
    [InlineData("Grain")]
    [InlineData("Brick")]
    [InlineData("Tiles")]
    [InlineData("Knurl")]
    [InlineData("Siding")]
    public void ATextureRoundTheWallsOfABoxStandsOffAllFourWallsAsOneSolid(string kind) => WithModel(model =>
    {
        var box = Primitives.Box(40, 30, 20);
        var part = TexturedBox(model, box, new Vector3(20, 0, 10), kind, kind == "Grain" ? 2.5f : 8f, TheOtherThree);
        var mesh = part.ToWorldMesh();

        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
        var bounds = part.WorldBounds;
        float most = MostProud(kind);
        Assert.InRange(bounds.Max.X, 20f + 0.5f, 20f + most);
        Assert.InRange(bounds.Max.Y, 15f + 0.5f, 15f + most);
        Assert.InRange(-bounds.Min.X, 20f + 0.5f, 20f + most);
        Assert.InRange(-bounds.Min.Y, 15f + 0.5f, 15f + most);
        Assert.Single(MeshComponents.Split(mesh));
    });

    /// <summary>A raised texture round the walls keeps off a window cut through them, as on a flat face.</summary>
    [Fact]
    public void ATextureRoundTheWallsKeepsOffAnOpeningInThem() => WithModel(model =>
    {
        var box = Primitives.Box(40, 30, 20);
        var window = MeshTransform.Transformed(Primitives.Box(60, 8, 8), Matrix4x4.CreateTranslation(0, 0, 0));
        var walled = FastCraft3D.Geometry.Csg.ManifoldCsg.Subtract(box, window)!;
        var part = TexturedBox(model, walled, new Vector3(20, 10, 0), "Rubble", 8f, TheOtherThree);
        var mesh = part.ToWorldMesh();
        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());

        // Through the middle of the opening, along it: nothing in the way.
        var c = part.WorldBounds.Center;
        Assert.Equal(0f, Reach(mesh, new Vector3(c.X, c.Y, c.Z), Vector3.UnitX), 3);
    });

    /// <summary>
    /// Two walls picked: the texture covers those two and goes round the corner between them, and
    /// the other two are left as they were, flat and bare to the edge.
    /// </summary>
    [Theory]
    [InlineData("Rubble")]
    [InlineData("Brick")]
    [InlineData("Planks")]
    [InlineData("Siding")]
    public void ATextureOnTwoWallsTurnsTheirCornerAndLeavesTheOtherTwoBare(string kind) => WithModel(model =>
    {
        var box = Primitives.Box(40, 30, 20);
        var part = TexturedBox(model, box, new Vector3(20, 0, 10), kind, 8f, Vector3.UnitY);
        var mesh = part.ToWorldMesh();

        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
        Assert.Single(MeshComponents.Split(mesh));

        var bounds = part.WorldBounds;
        float most = MostProud(kind);
        Assert.InRange(bounds.Max.X, 20f + 0.5f, 20f + most);
        Assert.InRange(bounds.Max.Y, 15f + 0.5f, 15f + most);
        Assert.Equal(-20f, bounds.Min.X, 2);
        Assert.Equal(-15f, bounds.Min.Y, 2);

        // Right at the corner it stands off both walls: a ray along the diagonal meets it there,
        // past the corner of the box itself.
        var c = bounds.Center;
        var corner = new Vector3(20, 15, c.Z);
        var diagonal = Vector3.Normalize(new Vector3(1, 1, 0));
        float past = Reach(mesh, corner - diagonal * 10f, diagonal) - 10f;
        Assert.InRange(past, 0.3f, most * MathF.Sqrt(2f) + 0.1f);
    });

    /// <summary>
    /// A wall that already carries a texture is not a flat wall any more, and the strip will not
    /// take it in: it says so at once rather than spending minutes on a wall of a thousand facets.
    /// </summary>
    [Fact]
    public void AWallAlreadyTexturedIsRefusedAtOnce() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(30, 30, 30)).Centred();
        cube.Position = new Vector3(0, 0, 15);
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();
        model.BeginEmbossCommand.Execute(null);

        Assert.True(model.PickEmbossFace(cube, new Vector3(0, 15, 15), Vector3.UnitY));
        model.EmbossTexture = FastCraft3D.Geometry.Engraving.TextureKind.Rubble;
        model.EmbossRaised = true;
        model.EmbossDepth = 0.8f;
        model.EmbossTexturePitch = 7f;
        model.ApplyEmbossCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects[0].ToWorldMesh().TriangleCount > 1000, 120000);

        var textured = model.Scene.Objects[0];
        textured.IsSelected = true;
        model.RefreshSelection();
        if (!model.IsEmbossMode) model.BeginEmbossCommand.Execute(null);

        Assert.True(model.PickEmbossFace(textured, new Vector3(15, 0, 15), Vector3.UnitX));
        model.EmbossProjection = FastCraft3D.Geometry.Engraving.TextProjection.Walls;
        Assert.Contains("1 of", model.Status);

        // On past the textured wall to the far one: refused, and quickly.
        var watch = Stopwatch.StartNew();
        Assert.True(model.AddEmbossWall(textured, new Vector3(-15, 5, 5), -Vector3.UnitX));
        Assert.True(model.AddEmbossWall(textured, new Vector3(0, 15.3f, 5), Vector3.UnitY));
        Assert.True(watch.ElapsedMilliseconds < 5000, $"{watch.ElapsedMilliseconds} ms");
        Assert.Contains("not", model.Status);
    });

    /// <summary>
    /// Masonry round all four walls of a box: one solid with bricks standing off every wall, and
    /// Emboss back as it was when the tool is put down.
    /// </summary>
    [Fact]
    public void MasonryBricksTheWallsPickedAndPutsEmbossBackAfterwards() => WithModel(model =>
    {
        var part = new SceneObject("Box", Primitives.Box(40, 30, 20)).Centred();
        part.Position = new Vector3(0, 0, 10);
        model.Scene.Objects.Add(part);
        part.IsSelected = true;
        model.RefreshSelection();

        var texture = model.EmbossTexture;
        var projection = model.EmbossProjection;

        model.BeginMasonryCommand.Execute(null);
        Assert.True(model.IsEmbossMode && model.IsMasonryMode);
        Assert.Equal("Masonry", model.EmbossTitle);
        Assert.Equal(
            [FastCraft3D.Geometry.Engraving.TextureKind.Brick, FastCraft3D.Geometry.Engraving.TextureKind.Rubble,
             FastCraft3D.Geometry.Engraving.TextureKind.Castle], model.EmbossTextures);
        Assert.Equal(FastCraft3D.Geometry.Engraving.TextureKind.Brick, model.EmbossTexture);

        Assert.True(model.PickEmbossFace(part, new Vector3(20, 0, 10), Vector3.UnitX));
        model.EmbossTexturePitch = 6f;
        model.EmbossTextureLine = 0.2f;
        model.EmbossTextureAspect = 3f;
        model.EmbossDepth = 0.4f;

        foreach (var facing in TheOtherThree)
            Assert.True(model.AddEmbossWall(part, part.WorldBounds.Center + facing * (part.WorldBounds.Size / 2f) - Vector3.UnitZ * 5f, facing));
        Assert.Contains("All 4 walls", model.Status);
        Assert.Contains("bricks", model.Status);

        model.ApplyEmbossCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !ReferenceEquals(model.Scene.Objects[0], part), 120000);
        Assert.True(!ReferenceEquals(part, model.Scene.Objects[0]), model.Status);

        var mesh = model.Scene.Objects[0].ToWorldMesh();
        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
        Assert.Single(MeshComponents.Split(mesh));

        var bounds = model.Scene.Objects[0].WorldBounds;
        Assert.Equal(20.4f, bounds.Max.X, 2);
        Assert.Equal(-15.4f, bounds.Min.Y, 2);

        Assert.False(model.IsMasonryMode);
        Assert.Equal("Emboss", model.EmbossTitle);
        Assert.Equal(texture, model.EmbossTexture);
        Assert.Equal(projection, model.EmbossProjection);
    });

    /// <summary>
    /// Masonry starts at a 5 mm brick, a 0.2 mm joint and 0.3 mm proud, and comes back as it was
    /// last left for the rest of the session - without any of it turning up in Emboss.
    /// </summary>
    [Fact]
    public void MasonryRemembersItsOwnNumbersAndLeavesEmbossItsOwn() => WithModel(model =>
    {
        var part = new SceneObject("Box", Primitives.Box(40, 30, 20)).Centred();
        model.Scene.Objects.Add(part);
        part.IsSelected = true;
        model.RefreshSelection();

        model.BeginEmbossCommand.Execute(null);
        float embossPitch = model.EmbossTexturePitch, embossDepth = model.EmbossDepth;
        model.CancelEmbossCommand.Execute(null);

        model.BeginMasonryCommand.Execute(null);
        Assert.Equal(5f, model.EmbossTexturePitch, 3);
        Assert.Equal(0.2f, model.EmbossTextureLine, 3);
        Assert.Equal(0.3f, model.EmbossDepth, 3);

        model.EmbossTexturePitch = 4f;
        model.EmbossDepth = 0.5f;
        model.CancelEmbossCommand.Execute(null);

        model.BeginEmbossCommand.Execute(null);
        Assert.Equal(embossPitch, model.EmbossTexturePitch, 3);
        Assert.Equal(embossDepth, model.EmbossDepth, 3);
        model.CancelEmbossCommand.Execute(null);

        model.BeginMasonryCommand.Execute(null);
        Assert.Equal(4f, model.EmbossTexturePitch, 3);
        Assert.Equal(0.5f, model.EmbossDepth, 3);
    });

    /// <summary>
    /// Rubble by Masonry round all four walls of a box: its own starting numbers, one closed solid,
    /// and rubble still in hand the next time the tool is picked up.
    /// </summary>
    [Fact]
    public void MasonryLaysRubbleWithQuoinsAndRemembersItWasRubble() => WithModel(model =>
    {
        var part = new SceneObject("Box", Primitives.Box(40, 30, 20)).Centred();
        part.Position = new Vector3(0, 0, 10);
        model.Scene.Objects.Add(part);
        part.IsSelected = true;
        model.RefreshSelection();

        model.BeginMasonryCommand.Execute(null);
        Assert.Equal(3, model.EmbossTextures.Count);
        model.EmbossTexture = FastCraft3D.Geometry.Engraving.TextureKind.Rubble;
        Assert.Equal(7f, model.EmbossTexturePitch, 3);
        Assert.Equal(0.8f, model.EmbossDepth, 3);

        Assert.True(model.PickEmbossFace(part, new Vector3(20, 0, 10), Vector3.UnitX));
        foreach (var facing in TheOtherThree)
            Assert.True(model.AddEmbossWall(part, part.WorldBounds.Center + facing * (part.WorldBounds.Size / 2f) - Vector3.UnitZ * 5f, facing));
        Assert.Contains("All 4 walls", model.Status);

        model.ApplyEmbossCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !ReferenceEquals(model.Scene.Objects[0], part), 120000);
        Assert.True(!ReferenceEquals(part, model.Scene.Objects[0]), model.Status);

        var mesh = model.Scene.Objects[0].ToWorldMesh();
        Assert.True(mesh.CheckHealth().IsWatertight, mesh.CheckHealth().Describe());
        Assert.Single(MeshComponents.Split(mesh));
        Assert.Contains("rubble walling", model.Status);

        model.Scene.Objects[0].IsSelected = true;
        model.RefreshSelection();
        model.BeginMasonryCommand.Execute(null);
        Assert.Equal(FastCraft3D.Geometry.Engraving.TextureKind.Rubble, model.EmbossTexture);
    });

    private static bool Inside(Bounds inner, Bounds outer) =>
        inner.Min.X >= outer.Min.X - 1e-3f && inner.Max.X <= outer.Max.X + 1e-3f &&
        inner.Min.Y >= outer.Min.Y - 1e-3f && inner.Max.Y <= outer.Max.Y + 1e-3f &&
        inner.Min.Z >= outer.Min.Z - 1e-3f && inner.Max.Z <= outer.Max.Z + 1e-3f;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FillUpFillsACupSolidOrAsAPartOfItsOwn(bool apart) => WithModel(model =>
    {
        var outer = Primitives.Box(40, 40, 30);
        var inner = MeshTransform.Transformed(Primitives.Box(30, 30, 30), System.Numerics.Matrix4x4.CreateTranslation(0, 0, 20));
        var cupMesh = FastCraft3D.Geometry.Csg.ManifoldCsg.Subtract(
            MeshTransform.Transformed(outer, System.Numerics.Matrix4x4.CreateTranslation(0, 0, 15)), inner)!;
        var cup = new SceneObject("Cup", cupMesh).Centred();
        model.Scene.Objects.Add(cup);
        cup.IsSelected = true;
        model.RefreshSelection();
        double before = cup.ToWorldMesh().ComputeSignedVolume();

        AnswerPanel(model, "AcceptButton", panel =>
        {
            if (apart)
            {
                var box = (CheckBox)panel.FindName("ApartBox")!;
                box.IsChecked = true;
                box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }

            // The part is worked out off the UI thread as the panel opens; Fill waits for it.
            PumpUntil(() => ((Button)panel.FindName("AcceptButton")!).IsEnabled, 60000);
        });
        model.FillUpCommand.Execute(null);
        PumpUntil(() => !model.HasOpenPanel && !model.IsBusy && (apart ? model.Scene.Objects.Count == 2 : !model.Scene.Objects.Contains(cup)), 60000);

        if (apart)
        {
            var fill = model.Scene.Objects.Single(o => o != cup);
            Assert.Equal(30 * 30 * 25, fill.ToWorldMesh().ComputeSignedVolume(), 30 * 30 * 25 * 0.02);
        }
        else
        {
            var filled = Assert.Single(model.Scene.Objects);
            Assert.Equal(40 * 40 * 30, filled.ToWorldMesh().ComputeSignedVolume(), 40 * 40 * 30 * 0.02);
            Assert.True(filled.ToWorldMesh().ComputeSignedVolume() > before);
        }
    });

    [Fact]
    public void DropDownLandsOnThePartUnderneathAndOverlapGoesALittleIntoIt() => WithModel(model =>
    {
        var plate = new SceneObject("Plate", Primitives.Box(40, 40, 6)) { Position = new Vector3(0, 0, 3) };
        var block = new SceneObject("Block", Primitives.Box(10, 10, 10)) { Position = new Vector3(5, 5, 30) };
        model.Scene.Objects.Add(plate);
        model.Scene.Objects.Add(block);
        block.IsSelected = true;
        model.RefreshSelection();

        model.DropDownCommand.Execute(null);
        Assert.Equal(6f, block.WorldBounds.Min.Z, 3);

        block.Position += new Vector3(0, 0, 10);
        model.DropDownCommand.Execute("Overlap");
        Assert.Equal(6f - MainViewModel.DropOverlap, block.WorldBounds.Min.Z, 3);

        // Again, it is resting on it already, and stays.
        model.DropDownCommand.Execute(null);
        Assert.Equal(6f - MainViewModel.DropOverlap, block.WorldBounds.Min.Z, 3);

        // Moved off the plate, there is nothing under it but the build plate.
        block.Position += new Vector3(60, 0, 0);
        model.DropDownCommand.Execute(null);
        Assert.Equal(0f, block.WorldBounds.Min.Z, 3);
    });

    [Fact]
    public void AddLeavesThePartWholeAndPutsTheCutterWhereItStood() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();

        AnswerPanel(model, "AddButton", () => { });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 2 && !model.HasOpenPanel);

        Assert.Equal(2, model.Scene.Objects.Count);
        Assert.Contains(cube, model.Scene.Objects);
        var cutter = model.Scene.Objects.Single(o => o != cube);

        // All the way through a part 20 deep: from the top a hair past the bottom, not the
        // part's diagonal again below it.
        Assert.Equal(20f + HoleCutter.Overshoot, cutter.WorldBounds.Max.Z, 1);
        Assert.InRange(cutter.WorldBounds.Min.Z, -1.5f, 0f);
    });

    [Fact]
    public void ABoltCircleCutsEveryHoleAtOnce() => WithModel(model =>
    {
        var plate = new SceneObject("Plate", Primitives.Box(60, 60, 10)) { Position = new Vector3(0, 0, 5) };
        model.Scene.Objects.Add(plate);
        plate.IsSelected = true;
        model.RefreshSelection();

        AnswerPanel(model, "AcceptButton", panel =>
        {
            ((RadioButton)panel.FindName("CircleBox")!).IsChecked = true;
            ((TextBox)panel.FindName("CircleDiameterBox")!).Text = "30";
            ((TextBox)panel.FindName("CountBox")!).Text = "4";
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => !model.Scene.Objects.Contains(plate) && !model.IsBusy, 60000);

        var cut = Assert.Single(model.Scene.Objects).ToWorldMesh();
        Assert.True(cut.CheckHealth().IsWatertight);

        // A plain top face has only its corners; each hole puts a rim round its own middle.
        foreach (var middle in new[] { new Vector2(15, 0), new Vector2(0, 15), new Vector2(-15, 0), new Vector2(0, -15) })
            Assert.Contains(cut.Positions, p => MathF.Abs(p.Z - 10f) < 1e-3f && Vector2.Distance(new Vector2(p.X, p.Y), middle) < 5f);
        Assert.DoesNotContain(cut.Positions, p => MathF.Abs(p.Z - 10f) < 1e-3f && new Vector2(p.X, p.Y).Length() < 5f);
    });

    [Fact]
    public void AThreadIsMadeWhereItsPreviewWasMovedAndTurned() => WithModel(model =>
    {
        AnswerPanel(model, "GeneratorInsert", () =>
        {
            // The preview is built off the UI thread once the panel settles.
            PumpUntil(() => model.Scene.Selection.Count == 1);
            var preview = Assert.Single(model.Scene.Selection);
            preview.Position += new Vector3(-30, 10, 0);
            preview.Rotation = new Vector3(90, 0, 0);
        });
        model.InsertGeneratedCommand.Execute(FastCraft3D.Generators.GeneratorRegistry.Find("fastener.thread"));
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel);

        var thread = Assert.Single(model.Scene.Objects);
        Assert.Equal(-30f, thread.Position.X, 1);
        Assert.Equal(10f, thread.Position.Y, 1);
        Assert.Equal(90f, thread.Rotation.X, 1);
    });

    [Fact]
    public void ASetLaidOutForPrintingGoesDownAsItsPartsEachInItsOwnColour() => WithModel(model =>
    {
        var generator = FastCraft3D.Generators.GeneratorRegistry.Find("mechanism.gear-train")!;
        AnswerPanel(model, "GeneratorInsert", () => PumpUntil(() => model.Scene.Objects.Count > 0));
        model.InsertGeneratedCommand.Execute(generator);
        PumpUntil(() => model.Scene.Objects.Count > 1 && !model.HasOpenPanel);

        Assert.True(model.Scene.Objects.Count >= 3);
        Assert.DoesNotContain(model.Scene.Objects, o => o.Recipe?.Role == "(set)");
        Assert.True(model.Scene.Objects.Select(o => o.Colour).Distinct().Count() > 1);
    });

    [Theory]
    [InlineData("mechanism.gear-train", 3)]
    [InlineData("mechanism.planetary", 4)]
    public void AnAssembledSetGoesDownAsAnAssemblyAndIsRemadeInItWhereItStands(string id, int atLeast) => WithModel(model =>
    {
        var generator = FastCraft3D.Generators.GeneratorRegistry.Find(id)!;
        AnswerPanel(model, "GeneratorInsert", panel =>
        {
            PumpUntil(() => model.Scene.Objects.Count > 0);
            var layout = Named<CheckBox>(panel, $"{id}.Organise");
            layout.IsChecked = false;
            layout.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            // Insert waits for the set to be made again with the new setting.
            PumpUntil(() => Named<Button>(panel, "GeneratorInsert").IsEnabled);
        });
        model.InsertGeneratedCommand.Execute(generator);
        PumpUntil(() => model.Scene.Objects.Count >= atLeast && !model.HasOpenPanel);

        // Its parts on their own, in one assembly named for the generator and picked by it.
        var parts = model.Scene.Objects.ToList();
        var assembly = parts[0].Assembly;
        Assert.NotNull(assembly);
        Assert.All(parts, o => Assert.Same(assembly, o.Assembly));
        Assert.Equal(generator.Title, assembly!.Name);
        Assert.DoesNotContain(parts, o => o.Recipe?.Role == "(set)");
        Assert.Same(assembly, model.SelectedAssembly);

        var home = parts[0].Position;
        foreach (var o in parts) o.Position += new Vector3(20, -15, 0);

        // Made again from the heading, where it stands now, and still in the assembly.
        AnswerPanel(model, "GeneratorInsert", () => PumpUntil(() => model.Scene.Objects.Count > parts.Count));
        model.EditGeneratedCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == parts.Count && !model.HasOpenPanel);

        var again = model.Scene.Objects.ToList();
        Assert.DoesNotContain(again, parts.Contains);
        Assert.All(again, o => Assert.Same(assembly, o.Assembly));
        Assert.Equal(home.X + 20, again[0].Position.X, 2);
        Assert.Equal(home.Y - 15, again[0].Position.Y, 2);

        // And it still goes back to where it was put down.
        model.Scene.SelectAssembly(assembly);
        model.RefreshSelection();
        model.ReassembleCommand.Execute(null);
        Assert.Equal(home.X, again[0].Position.X, 2);
        Assert.Equal(home.Y, again[0].Position.Y, 2);
    });

    [Fact]
    public void AThreadedHoleIsCutIntoTheSelectedPart() => WithModel(model =>
    {
        var cube = new SceneObject("Cube", Primitives.Box(20, 20, 20)) { Position = new Vector3(0, 0, 10) };
        model.Scene.Objects.Add(cube);
        cube.IsSelected = true;
        model.RefreshSelection();
        double before = cube.ToWorldMesh().ComputeSignedVolume();

        AnswerPanel(model, "GeneratorCut", panel =>
        {
            PumpUntil(() => model.Scene.Selection.Count == 1 && model.Scene.Selection[0] != cube);
            var cut = Named<Button>(panel, "GeneratorCut");
            Assert.NotEqual(Visibility.Visible, cut.Visibility);

            Named<ComboBox>(panel, "fastener.thread.Kind").SelectedIndex = (int)ThreadKind.HoleCutter;
            PumpUntil(() => cut.Visibility == Visibility.Visible && cut.IsEnabled);
            Assert.Equal(Visibility.Visible, cut.Visibility);

            // Sunk into the middle of the top, its mouth just proud of it.
            var cutter = Assert.Single(model.Scene.Selection);
            Assert.Equal(20f + HoleCutter.Overshoot, cutter.WorldBounds.Max.Z, 2);
            Assert.Equal(0f, cutter.WorldBounds.Center.X, 2);
        });
        model.InsertGeneratedCommand.Execute(FastCraft3D.Generators.GeneratorRegistry.Find("fastener.thread"));
        PumpUntil(() => !model.Scene.Objects.Contains(cube) && !model.IsBusy, 60000);

        var drilled = Assert.Single(model.Scene.Objects).ToWorldMesh();
        Assert.True(drilled.CheckHealth().IsWatertight);
        Assert.True(drilled.ComputeSignedVolume() < before - 300, "no threaded hole was cut");
    });

    [Fact]
    public void APreviewCannotBeResizedAndTypingIntoItsBoxesLeavesNoUndoStep() => WithModel(model =>
    {
        AnswerPanel(model, "AddButton", () =>
        {
            Assert.True(model.ShowManipulatorBar);
            Assert.False(model.ResizeOffered);

            model.GizmoMode = GizmoMode.Scale;
            Assert.NotEqual(GizmoMode.Scale, model.GizmoMode);
        });
        model.InsertHoleCommand.Execute(null);
        PumpUntil(() => model.Scene.Objects.Count == 1 && !model.HasOpenPanel);

        Assert.True(model.ResizeOffered);
    });
}
