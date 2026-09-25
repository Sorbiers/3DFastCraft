using System.Numerics;
using FastCraft3D.Generators;
using FastCraft3D.Geometry;
using FastCraft3D.Geometry.Csg;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;

namespace FastCraft3D.ViewModels;

/// <summary>
/// The app's whole side of the generator library: list what is there, open one in the side
/// panel, put what it makes on the plate or cut it into the part selected, and open it again on a
/// part it made. Everything else - the settings, the panel, the preview's timing, the printer, the
/// parts themselves - lives in FastCraft3D.Generators, so the library can grow without this file
/// growing with it.
/// </summary>
public sealed partial class MainViewModel
{
    private System.Windows.Input.ICommand? openLibrary;
    private System.Windows.Input.ICommand? insertGenerated;
    private System.Windows.Input.ICommand? editGenerated;
    private Printer? printer;

    /// <summary>What each generator was last left at this session, so the next starts from it.</summary>
    private readonly Dictionary<string, object> lastGenerated = [];

    public IReadOnlyList<Generator> Generators => GeneratorRegistry.All;

    /// <summary>Opens the catalogue in the side panel, and the generator chosen from it.</summary>
    public System.Windows.Input.ICommand OpenLibraryCommand =>
        openLibrary ??= Track(RelayCommand.Simple(OpenLibrary));

    /// <summary>Opens the generator given as the parameter.</summary>
    public System.Windows.Input.ICommand InsertGeneratedCommand =>
        insertGenerated ??= Track(new RelayCommand(p =>
        {
            if (p is Generator generator) InsertGenerated(generator);
        }));

    /// <summary>Opens the generator that made the selected part, filled in as it was made.</summary>
    public System.Windows.Input.ICommand EditGeneratedCommand =>
        editGenerated ??= RelayCommand.Simple(EditGenerated, () => Selected?.Recipe is not null);

    private Printer CurrentPrinter => printer ??= PrinterProfile.Load();

    private void OpenLibrary()
    {
        var view = new LibraryView(GeneratorRegistry.All, LibraryMemory.Shared, LibraryPictures.Shared);
        var panel = new ToolPanel { Title = "Library", IsBeta = true, Content = view };

        Generator? chosen = null;
        view.Chosen += generator =>
        {
            chosen = generator;
            panel.DialogResult = true;
        };
        view.Closed += () => panel.DialogResult = false;

        panel.ShowDialog();

        // Through the command, so the repeat key opens this generator again rather than the catalogue.
        if (chosen is not null) InsertGeneratedCommand.Execute(chosen);
    }

    /// <summary>For a ribbon button that is a generator: Stair, Thread.</summary>
    private void InsertGenerated(string id)
    {
        if (GeneratorRegistry.Find(id) is { } generator) InsertGenerated(generator);
    }

    /// <summary>
    /// Opens a generator and puts what it makes on the plate. A single part is shown where it will
    /// go and can be moved while the numbers are chosen, as the stair and the thread always could;
    /// a set is shown on its own, as it goes together, and put down side by side as it prints. A
    /// cutter, with one part selected, starts sunk into that part's top and can be Cut straight in.
    /// </summary>
    private void InsertGenerated(Generator generator)
    {
        if (IsBusy) return;

        var selection = Scene.Selection.ToList();
        var target = selection.Count == 1 ? selection[0] : null;

        // What is already on the plate, for the new part to go clear of.
        var taken = Scene.Objects.Where(o => !o.IsHidden).Select(o => o.WorldBounds).ToList();
        Vector2 ClearOf(Bounds footprint) =>
            BedPlacement.Clear(new Vector2(footprint.Size.X, footprint.Size.Y), taken, PlateWidth, PlateDepth);

        var colours = new List<Vector3>();
        Vector3 ColourOf(int part)
        {
            while (colours.Count <= part) colours.Add(NextAutomaticColour());
            return colours[part];
        }

        // The single part being shown: the same object throughout, so wherever it is moved to is
        // where it stays as it is rebuilt - it grows about its own origin - and where it is put down.
        SceneObject? held = null;
        bool heldCuts = false;
        var heldPivot = Vector3.Zero;
        Vector3 OriginOf(SceneObject o) => Vector3.Transform(-heldPivot, o.Transform);

        void Put(SceneObject o, bool cutter)
        {
            o.Rotation = Vector3.Zero;
            if (cutter && target is not null)
            {
                // Upright in the middle of the part's top, its mouth a little proud of the surface
                // so the hole opens cleanly rather than leaving a skin.
                var part = target.WorldBounds;
                o.Position = new Vector3(part.Center.X, part.Center.Y, part.Max.Z + HoleCutter.Overshoot);
            }
            else
            {
                o.Position = Vector3.Zero;
                Stand(o, ClearOf(o.WorldBounds));
            }
        }

        lastGenerated.TryGetValue(generator.Id, out var start);

        var (inserted, view) = OpenGenerator(generator, start, "Insert", null, target?.Name, made =>
        {
            if (made.Parts.Count != 1)
            {
                var objects = Objects(made, assembled: true, ColourOf);
                BedPlacement.Fit(objects, 1f);
                return (objects, true, null);
            }

            var part = made.Parts[0];
            if (held is null)
            {
                held = new SceneObject(part.Name, part.Mesh) { Colour = ColourOf(0), Anchors = part.Anchors?.ToList() ?? [] }
                    .CentredOn(part.Pivot);
                heldPivot = part.Pivot;
                Put(held, part.Cutter);
            }
            else
            {
                var origin = OriginOf(held);
                held.Mesh = part.Mesh;
                held.Anchors = part.Anchors?.ToList() ?? [];
                held.CentredOn(part.Pivot);
                heldPivot = part.Pivot;
                held.Name = part.Name;
                Keep(held, -heldPivot, origin);

                // A cutter goes into the part it is for, and anything else comes back out of it.
                if (part.Cutter != heldCuts && target is not null) Put(held, part.Cutter);
            }

            heldCuts = part.Cutter;

            // A cutter is shown with the part it is going into; anything else on its own.
            return ([held], false, part.Cutter ? target : null);
        });

        if (!inserted || view.Result is not { } settings || view.Made is not { Parts.Count: > 0 } result)
        {
            Scene.SelectOnly(target);
            RefreshSelection();
            return;
        }

        lastGenerated[generator.Id] = settings;
        LibraryMemory.Shared.Used(generator.Id);

        string? set = result.Parts.Count > 1 ? Guid.NewGuid().ToString("N") : null;
        var parts = Objects(result, assembled: false, ColourOf, part => Recipes.For(generator, settings, part.Role, set));
        foreach (var o in parts) o.Name = Scene.UniqueName(o.Name);

        if (parts.Count == 1 && held is not null)
        {
            parts[0].Rotation = held.Rotation;
            parts[0].Scale = held.Scale;
            Keep(parts[0], parts[0].Recipe!.Origin, OriginOf(held));
        }
        else
        {
            // As the generator laid the set out to print, the lot moved together onto the plate,
            // clear of what is there already.
            BedPlacement.Fit(parts, 1f);
            var reach = BedPlacement.Reach(parts);
            var clear = ClearOf(reach);
            foreach (var o in parts) o.Position += new Vector3(clear.X - reach.Center.X, clear.Y - reach.Center.Y, 0);
        }

        if (view.Cuts && target is not null && parts.Count == 1)
        {
            _ = CutInto(target, parts[0]);
            return;
        }

        Undo.Execute(new AddObjectsCommand($"Insert {generator.Title.ToLowerInvariant()}", parts));
        RefreshSelection();
        Status = parts.Count == 1 ? $"Inserted {parts[0].Name}" : $"Inserted {parts.Count} parts: {string.Join(", ", parts.Select(o => o.Name))}";
    }

    /// <summary>
    /// Takes a cutter out of the part it was sunk into, as one undo step. Refused rather than kept
    /// if the part comes back with holes in its surface.
    /// </summary>
    private async Task CutInto(SceneObject target, SceneObject cutter)
    {
        Scene.SelectOnly(target);
        RefreshSelection();

        var token = StartWork($"Cutting the {cutter.Name}");
        try
        {
            var world = target.ToWorldMesh();
            var cutterWorld = cutter.ToWorldMesh();
            var result = await Task.Run(() => MeshHealer.Heal(LocalCsg.Subtract(world, cutterWorld, token), token: token).Mesh);

            if (result.TriangleCount == 0 || !result.CheckHealth().IsWatertight)
            {
                Status = $"The {cutter.Name} would not cut cleanly into {target.Name} - nothing was changed";
                return;
            }

            var cut = new SceneObject(target.Name, result) { Colour = target.Colour }.Centred();
            Undo.Execute(new ReplaceObjectsCommand($"Cut {cutter.Name}", [target], [cut]));
            RefreshSelection();
            Status = $"Cut the {cutter.Name} into {target.Name}";
        }
        catch (Exception abort) when (WasAborted(abort))
        {
            Status = $"{busyTitle} aborted - nothing was changed";
        }
        catch (Exception ex)
        {
            Status = $"The {cutter.Name} could not be cut: {ex.Message}";
        }
        finally
        {
            EndWork();
        }
    }

    /// <summary>
    /// Makes the selected part again with other numbers: its generator reopens filled in as it
    /// was made, and Apply puts the new part where the old one stood, as one undo step. For a set,
    /// every part of it is made again, since one part's size is usually another's fit.
    /// </summary>
    private void EditGenerated()
    {
        if (Selected is not { Recipe: { } recipe } picked) return;

        if (GeneratorRegistry.Find(recipe.Generator) is not { } generator)
        {
            Status = $"{picked.Name} was made by a generator this version does not have ({recipe.Generator})";
            return;
        }

        var members = recipe.Set is null
            ? [picked]
            : Scene.Objects.Where(o => o.Recipe?.Set == recipe.Set && o.Recipe.Generator == recipe.Generator).ToList();

        string? notice = recipe.Version == generator.Version ? null
            : recipe.Version < generator.Version
                ? $"Made with an earlier version of {generator.Title}. Applying makes it again with this one, which may come out a little different."
                : $"Made with a later version of {generator.Title} than this app has. Applying makes it again with this one.";

        var start = Recipes.Read(generator, recipe.Settings);
        var (applied, view) = OpenGenerator(generator, start, "Apply", notice, null,
            made => (Remade(members, generator, settings: null, made, NextAutomaticColour), true, null));

        if (!applied || view.Result is not { } settings || view.Made is not { Parts.Count: > 0 } result) return;

        var produced = Remade(members, generator, settings, result, NextAutomaticColour);
        foreach (var o in produced.Where(o => members.All(m => m.Name != o.Name))) o.Name = Scene.UniqueName(o.Name);

        Undo.Execute(new ReplaceObjectsCommand($"Edit {generator.Title.ToLowerInvariant()}", members, produced));
        RefreshSelection();
        Status = produced.Count == 1 ? $"Made {produced[0].Name} again with the new settings" : $"Made the {produced.Count} parts of the set again";
    }

    /// <summary>
    /// The generator's panel in the side panel, with its preview on the plate while it is open.
    ///
    /// Only the preview is drawn while the panel is open, and whatever it is being cut into:
    /// everything else on the plate stands aside, as it does for the gear. A single part among
    /// the rest was tried first, as the stair and the thread always were, and a new part previewed
    /// in the middle of the plate sat inside whatever was already there. A single part is still
    /// held, selected, so it can be moved to where it is going; a set, or a part being edited, is
    /// shown as it goes together. Returns whether it was finished rather than cancelled, and the
    /// panel for its result.
    /// </summary>
    private (bool Finished, GeneratorView View) OpenGenerator(
        Generator generator, object? start, string action, string? notice, string? target,
        Func<Generated, (List<SceneObject> Objects, bool Alone, SceneObject? Beside)> preview)
    {
        var shown = new List<SceneObject>();
        GeneratorView? panelView = null;
        var turning = new Turning();

        void Clear()
        {
            // The objects are about to go, so nothing is put back; the button stops offering to stop.
            if (turning.Playing) panelView?.Turning(false);
            turning.Stop(restore: false);
            PreviewOnly = null;
            foreach (var o in shown) Scene.Objects.Remove(o);
            shown.Clear();
        }

        var context = new GeneratorContext(
            CurrentPrinter, new DisplayUnit(Unit.Label, Unit.Millimetres), PlateWidth, PlateDepth,
            LibraryMemory.Shared, modelScale, target);

        var view = new GeneratorView(generator, start, context, (_, made) =>
        {
            Clear();
            if (made is null) return;

            var (objects, alone, beside) = preview(made);
            foreach (var o in objects)
            {
                Scene.Objects.Add(o);
                shown.Add(o);
            }

            PreviewOnly = beside is null ? shown.ToList() : [.. shown, beside];
            if (!alone && shown.Count == 1) HoldPreview(shown[0]);
        }, action, notice);
        panelView = view;

        // Only while inserting: then the preview is the set put together, which is what turns.
        view.OffersTurning = action == "Insert";
        view.TurnPressed += made =>
        {
            if (turning.Playing)
            {
                turning.Stop(restore: true);
                view.Turning(false);
                view.SayMotion(null, false);
                return;
            }

            turning.Play(made, shown, view);
        };

        var panel = new ToolPanel { Title = generator.Title, IsBeta = generator.IsBeta, Content = view };
        view.Finished += finished =>
        {
            // Record's "before" shot, taken while the panel and its preview are still on screen;
            // the step the insert makes pairs it with its "after".
            if (finished) Applying?.Invoke();
            panel.DialogResult = finished;
        };
        panel.Closed += (_, _) => view.Stop();

        bool done = panel.ShowDialog() == true;
        turning.Stop(restore: true);
        view.Stop();
        Clear();
        ReleasePreview();

        if (view.Printer != CurrentPrinter)
        {
            printer = view.Printer;
            PrinterProfile.Save(view.Printer);
        }

        return (done, view);
    }

    /// <summary>
    /// A set's preview turning on the plate: the motion check's run played pose by pose as it is
    /// worked out, on a background thread, so it starts at once however long the whole turn
    /// takes. A jam stops it where it happened, with the two parts that met shown red and the
    /// reason said in the panel. Everything is put back as it was when it is stopped, or when the
    /// settings change and the preview is made again.
    /// </summary>
    private sealed class Turning
    {
        private static readonly Vector3 Jammed = new(0.86f, 0.18f, 0.14f);

        private System.Windows.Threading.DispatcherTimer? timer;
        private CancellationTokenSource? cancel;
        private readonly List<(SceneObject Object, Vector3 Position, Vector3 Rotation, Vector3 Colour)> home = [];

        public bool Playing => timer is not null;

        public void Play(Generated made, IReadOnlyList<SceneObject> shown, GeneratorView view)
        {
            Stop(restore: true);
            if (made.Motion is not { } mechanism || shown.Count != made.Parts.Count) return;

            // Where the set was moved to on the plate: each object stands on its part's pivot, as
            // the set goes together, and the lot was then moved as one.
            var first = made.Parts[0];
            var pivot = Vector3.Transform(first.Pivot, first.Assembled ?? Matrix4x4.Identity);
            var moved = shown[0].Position - pivot;
            var shift = new Vector2(moved.X, moved.Y);

            foreach (var o in shown) home.Add((o, o.Position, o.Rotation, o.Colour));

            var steps = new System.Collections.Concurrent.ConcurrentQueue<FilmStep>();
            bool finished = false;
            cancel = new CancellationTokenSource();
            var token = cancel.Token;
            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var step in Films.Roll(made, token)) steps.Enqueue(step);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    finished = true;
                }
            }, CancellationToken.None);

            view.Turning(true);
            view.SayMotion("Turning...", false);

            // Two steps a frame: a turn of the driver in about three seconds.
            timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += (_, _) =>
            {
                FilmStep? latest = null;
                for (int i = 0; i < 2 && steps.TryDequeue(out var step); i++) latest = step;

                if (latest is { } pose) Pose(mechanism, pose.Pose, shift);

                if (latest is { Jammed: var (a, b) })
                {
                    shown[a].Colour = Jammed;
                    shown[b].Colour = Jammed;
                    timer?.Stop();
                    timer = null;
                    view.Turning(false);
                    view.SayMotion(latest.Jam, true);
                    return;
                }

                if (latest is null && finished && steps.IsEmpty)
                {
                    timer?.Stop();
                    timer = null;
                    view.Turning(false);
                    view.SayMotion("Turned a whole turn without a jam.", false);
                }
            };
            timer.Start();
        }

        private void Pose(Mechanism mechanism, double[] pose, Vector2 shift)
        {
            foreach (var m in mechanism.Moving)
            {
                var (o, position, rotation, _) = home[m.Part];
                double at = pose[m.Part];

                if (m.Joint == Geometry.Motion.Joint.Revolute)
                {
                    var centre = m.Centre + shift;
                    float cos = (float)Math.Cos(at), sin = (float)Math.Sin(at);
                    var from = new Vector2(position.X, position.Y) - centre;
                    var to = centre + new Vector2(cos * from.X - sin * from.Y, sin * from.X + cos * from.Y);
                    o.Rotation = rotation + new Vector3(0, 0, (float)(at * 180.0 / Math.PI));
                    o.Position = new Vector3(to, position.Z);
                }
                else
                {
                    o.Position = position + new Vector3(Vector2.Normalize(m.Direction) * (float)at, 0);
                }
            }
        }

        /// <summary>Stops turning, and with <paramref name="restore"/> puts everything back where it started.</summary>
        public void Stop(bool restore)
        {
            timer?.Stop();
            timer = null;
            cancel?.Cancel();
            cancel = null;

            if (restore)
                foreach (var (o, position, rotation, colour) in home)
                {
                    o.Position = position;
                    o.Rotation = rotation;
                    o.Colour = colour;
                }

            home.Clear();
        }
    }

    /// <summary>
    /// The parts as objects, each with its pivot where the part says - the point it turns and grows
    /// about - and its recipe, if given, remembering where the generator's own origin is. As the set
    /// goes together where a part says so, for the preview; as it prints otherwise.
    /// </summary>
    private static List<SceneObject> Objects(
        Generated made, bool assembled, Func<int, Vector3> colour, Func<GeneratedPart, Recipe>? recipe = null) =>
        made.Parts.Select((part, i) =>
        {
            var at = assembled ? part.Assembled : null;
            var o = new SceneObject(part.Name, at is { } m ? MeshTransform.Transformed(part.Mesh, m) : part.Mesh)
            {
                Colour = part.Colour ?? colour(i),
                Filament = part.Filament > 0 ? part.Filament : 1,
                Anchors = part.Anchors is not { Count: > 0 } marked ? []
                    : at is { } moved ? marked.Select(a => a.Through(moved)).ToList() : marked
            };

            // Before the pivot is moved, which carries the recipe's origin with it.
            if (recipe is not null) o.Recipe = recipe(part);
            return o.CentredOn(at is { } shown ? Vector3.Transform(part.Pivot, shown) : part.Pivot);
        }).ToList();

    /// <summary>
    /// Moves an object, without turning it, so the point <paramref name="inMesh"/> of its mesh -
    /// the generator's origin - lands at <paramref name="origin"/> on the plate.
    /// </summary>
    private static void Keep(SceneObject o, Vector3 inMesh, Vector3 origin) =>
        o.Position += origin - Vector3.Transform(inMesh, o.Transform);

    /// <summary>
    /// A set made again: each new part where the part with the same role stood, with its name,
    /// colour, filament, turn and scale, and its origin on the old one's. A part the set did not
    /// have before goes beside the rest.
    ///
    /// Placed by the generator's origin rather than by the bounding box, because the box moves
    /// when the part changes size: a box made 5 mm wider, centred where the old one was centred,
    /// would have slid half of that sideways off whatever it was lined up with.
    /// </summary>
    /// <param name="settings">What goes in the new recipes; null for a preview, which keeps none.</param>
    public static List<SceneObject> Remade(
        IReadOnlyList<SceneObject> members, Generator generator, object? settings, Generated made, Func<Vector3> nextColour)
    {
        var unused = members.ToList();
        var produced = new List<SceneObject>();
        var loose = new List<SceneObject>();
        string? set = members.Select(m => m.Recipe?.Set).FirstOrDefault(s => s is not null);

        for (int i = 0; i < made.Parts.Count; i++)
        {
            var part = made.Parts[i];
            var old = unused.FirstOrDefault(m => m.Recipe?.Role == part.Role)
                      ?? (part.Role is null && unused.Count > 0 ? unused[0] : null);

            var o = new SceneObject(old?.Name ?? part.Name, part.Mesh)
            {
                Colour = old?.Colour ?? part.Colour ?? nextColour(),
                Filament = part.Filament > 0 ? part.Filament : 1,
                Anchors = part.Anchors?.ToList() ?? [],

                // Always, so the generator's origin is known through the pivot's move; a preview's
                // is thrown away with it.
                Recipe = Recipes.For(generator, settings ?? generator.Defaults(), part.Role, set)
            }.CentredOn(part.Pivot);

            if (old is not null)
            {
                unused.Remove(old);
                o.Filament = old.Filament;
                o.IsHidden = old.IsHidden;
                o.IsLocked = old.IsLocked;
                o.Rotation = old.Rotation;
                o.Scale = old.Scale;
                Keep(o, o.Recipe!.Origin, Vector3.Transform(old.Recipe?.Origin ?? Vector3.Zero, old.Transform));
            }
            else
            {
                loose.Add(o);
            }

            produced.Add(o);
        }

        // Beside everything else in the set, standing on the plate.
        float right = produced.Except(loose).Select(o => o.WorldBounds.Max.X).DefaultIfEmpty(0f).Max();
        foreach (var o in loose)
        {
            Stand(o, new Vector2(right + 5f + o.WorldBounds.Size.X / 2f, 0f));
            right = o.WorldBounds.Max.X;
        }

        return produced;
    }

    /// <summary>Moves an object so the middle of its footprint is at <paramref name="centre"/> and it stands on the plate.</summary>
    private static void Stand(SceneObject o, Vector2 centre)
    {
        var bounds = o.WorldBounds;
        o.Position += new Vector3(centre - new Vector2(bounds.Center.X, bounds.Center.Y), -bounds.Min.Z);
    }
}
