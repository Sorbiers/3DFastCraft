using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.View;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Assemblies: parts kept together under one name, picked as one, lined up and cut with as one,
/// and put back where they were assembled.
/// </summary>
public class AssemblyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-asm-" + Guid.NewGuid().ToString("N"));

    public AssemblyTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
    }

    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>A box that is not a cube, so a wrong turn shows up in where its corners end up.</summary>
    private static SceneObject Part(string name, Vector3 at, Vector3 turn = default) =>
        new(name, Primitives.Box(10, 20, 30)) { Position = at, Rotation = turn };

    private static (Scene Scene, UndoStack Undo, Assembly Assembly, SceneObject A, SceneObject B) Assembled()
    {
        var scene = new Scene();
        var undo = new UndoStack(scene);
        var a = Part("A", new Vector3(0, 0, 15), new Vector3(10, 20, 30));
        var b = Part("B", new Vector3(40, 0, 15));
        scene.Objects.Add(a);
        scene.Objects.Add(b);

        undo.Execute(AssemblyTools.Assemble(scene, [a, b], out var made));
        return (scene, undo, made, a, b);
    }

    /// <summary>Where a part's corners would be with its pivot at home and its own size.</summary>
    private static List<Vector3> AtHome(SceneObject o) =>
        MeshTransform.Transformed(o.Mesh, MeshTransform.Compose(o.HomePosition, o.HomeRotation, o.Scale)).Positions;

    private static void SamePlaces(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.True(Vector3.Distance(expected[i], actual[i]) < 1e-3f, $"corner {i}: {actual[i]} where {expected[i]} was expected");
    }

    // --- Making one, and putting it back --------------------------------------------------

    [Fact]
    public void ReassemblePutsMovedAndTurnedPartsBackAndLeavesTheirSizeAlone()
    {
        var (scene, undo, assembly, a, b) = Assembled();

        a.Position += new Vector3(50, -20, 5);
        a.Rotation = new Vector3(0, 0, 90);
        b.SizeX = 25;

        undo.Execute(AssemblyTools.Reassemble(scene, assembly)!);

        Assert.Equal(new Vector3(0, 0, 15), a.Position);
        Assert.Equal(new Vector3(10, 20, 30), a.Rotation);
        Assert.Equal(new Vector3(40, 0, 15), b.Position);
        Assert.Equal(25, b.SizeX, 3);

        undo.Undo();
        Assert.Equal(new Vector3(50, -20, 20), a.Position);
    }

    [Fact]
    public void ReassembleLeavesALockedPartWhereItIs()
    {
        var (scene, _, assembly, a, b) = Assembled();
        a.Position += new Vector3(30, 0, 0);
        b.Position += new Vector3(30, 0, 0);
        b.IsLocked = true;

        AssemblyTools.Reassemble(scene, assembly);

        Assert.Equal(new Vector3(0, 0, 15), a.Position);
        Assert.Equal(new Vector3(70, 0, 15), b.Position);
    }

    [Fact]
    public void ReassembleWithEverythingHomeIsNotAStep()
    {
        var (scene, _, assembly, _, _) = Assembled();
        Assert.Null(AssemblyTools.Reassemble(scene, assembly));
    }

    [Fact]
    public void UpdateMakesWhereThePartsAreNowHome()
    {
        var (scene, undo, assembly, a, _) = Assembled();
        a.Position = new Vector3(-30, 10, 15);

        undo.Execute(AssemblyTools.Update(scene, assembly));
        a.Position = Vector3.Zero;
        AssemblyTools.Reassemble(scene, assembly);

        Assert.Equal(new Vector3(-30, 10, 15), a.Position);
    }

    [Fact]
    public void UngroupLeavesThePartsWhereTheyAreAndUndoBringsTheAssemblyBack()
    {
        var (scene, undo, assembly, a, b) = Assembled();
        a.Position += new Vector3(20, 0, 0);

        undo.Execute(AssemblyTools.Ungroup(scene, assembly));

        Assert.Empty(scene.Assemblies);
        Assert.Null(a.Assembly);
        Assert.Equal(new Vector3(20, 0, 15), a.Position);

        undo.Undo();
        Assert.Same(assembly, Assert.Single(scene.Assemblies));
        Assert.Equal([a, b], scene.MembersOf(assembly));
    }

    [Fact]
    public void AssemblingPartsOfAnotherAssemblyTakesThemOutOfIt()
    {
        var (scene, undo, first, a, b) = Assembled();
        var c = Part("C", new Vector3(80, 0, 15));
        scene.Objects.Add(c);

        undo.Execute(AssemblyTools.Assemble(scene, [b, c], out var second));

        Assert.Equal([a], scene.MembersOf(first));
        Assert.Equal([b, c], scene.MembersOf(second));
        Assert.NotEqual(first.Name, second.Name);
    }

    [Fact]
    public void UndoingADeletePutsThePartBackInItsAssembly()
    {
        var (scene, undo, assembly, a, b) = Assembled();

        undo.Execute(new DeleteObjectsCommand([a, b]));
        Assert.Empty(scene.Assemblies);

        undo.Undo();
        Assert.Equal([a, b], scene.MembersOf(assembly));
    }

    // --- Parts that a tool remakes ---------------------------------------------------------

    /// <summary>
    /// The way Subtract, Smooth, Repair and the rest remake a part: its shape baked where it stands,
    /// turn and all, and its pivot put at the middle of the new box. Its numbers then read nothing
    /// like the old part's, and the old home copied across would turn it a second time.
    /// </summary>
    private static SceneObject Remade(SceneObject o) => new SceneObject(o.Name, o.ToWorldMesh()).Centred();

    [Fact]
    public void APartATurnedToolRemakesReassemblesToWhereTheOriginalWouldHave()
    {
        var (scene, undo, assembly, a, _) = Assembled();
        a.Scale = new Vector3(1, 2, 1);
        a.Position += new Vector3(30, 40, 0);
        a.Rotation = new Vector3(0, 45, 60);

        var expected = AtHome(a);
        var remade = Remade(a);
        undo.Execute(new ReplaceObjectsCommand("Smooth", [a], [remade]));

        Assert.Same(assembly, remade.Assembly);

        AssemblyTools.Reassemble(scene, assembly);
        SamePlaces(expected, remade.ToWorldMesh().Positions);
    }

    [Fact]
    public void ACutPartReassemblesToWhereItWasEvenWhenTheCutterIsInAnotherAssembly()
    {
        var (scene, undo, assembly, a, _) = Assembled();
        var pin = Part("Pin", new Vector3(0, 0, 15));
        scene.Objects.Add(pin);
        undo.Execute(AssemblyTools.Assemble(scene, [pin], out _));
        a.Position += new Vector3(0, 60, 0);

        var expected = AtHome(a);
        var cut = Remade(a);
        undo.Execute(new ReplaceObjectsCommand("Subtract", [a, pin], [cut], [(a, cut)]));

        Assert.Same(assembly, cut.Assembly);
        AssemblyTools.Reassemble(scene, assembly);
        SamePlaces(expected, cut.ToWorldMesh().Positions);
    }

    [Fact]
    public void SettingAPivotOnAPartKeepsWhereReassemblePutsIt()
    {
        var (scene, undo, assembly, a, _) = Assembled();
        a.Position += new Vector3(-25, 15, 0);
        a.Rotation = new Vector3(30, 0, 0);

        var expected = AtHome(a);
        undo.Execute(new PivotCommand("Set pivot", a, new Vector3(5, 10, -15), own: true, wasOwn: false));

        AssemblyTools.Reassemble(scene, assembly);
        SamePlaces(expected, a.ToWorldMesh().Positions);
    }

    [Fact]
    public void BothHalvesOfASplitPartStayInItsAssembly()
    {
        var (scene, undo, assembly, a, _) = Assembled();
        var front = new SceneObject("A front", a.ToWorldMesh()).Centred();
        var back = new SceneObject("A back", a.ToWorldMesh()).Centred();

        undo.Execute(new ReplaceObjectsCommand("Split", [a], [front, back]));

        Assert.Same(assembly, front.Assembly);
        Assert.Same(assembly, back.Assembly);
    }

    [Fact]
    public void WhatIsMadeFromPartsOfTwoAssembliesWithNoPairingGoesInNeither()
    {
        var (scene, undo, _, a, _) = Assembled();
        var c = Part("C", new Vector3(80, 0, 15));
        scene.Objects.Add(c);
        undo.Execute(AssemblyTools.Assemble(scene, [c], out _));

        var union = new SceneObject("Union", Mesh.Combine([a.ToWorldMesh(), c.ToWorldMesh()])).Centred();
        undo.Execute(new ReplaceObjectsCommand("Union", [a, c], [union]));

        Assert.Null(union.Assembly);
    }

    [Fact]
    public void ACopyIsAPartOfItsOwn()
    {
        var (_, _, _, a, _) = Assembled();

        Assert.Null(a.Clone().Assembly);
        Assert.Null(Assert.Single(SceneSerializer.FromBytes(SceneSerializer.ToBytes([a]))).Assembly);
    }

    // --- Picked as one ---------------------------------------------------------------------

    [Fact]
    public void AnAssemblyPickedByItsNameIsOnePickAndCutsAsTheLast()
    {
        var (scene, _, assembly, a, b) = Assembled();
        var target = Part("Target", new Vector3(0, 60, 15));
        scene.Objects.Add(target);

        target.IsSelected = true;
        scene.SelectAssembly(assembly);

        var picks = scene.SelectionInPicks;
        Assert.Equal(2, picks.Count);
        Assert.Equal([target], picks[0]);
        Assert.Equal([a, b], picks[1]);

        var (earlier, last) = scene.SplitLastPick();
        Assert.Equal([target], earlier);
        Assert.Equal([a, b], last);
        Assert.True(assembly.IsSelected);
    }

    [Fact]
    public void AnObjectPickedAfterTheAssemblyIsTheCutter()
    {
        var (scene, _, assembly, a, b) = Assembled();
        var pin = Part("Pin", new Vector3(0, 60, 15));
        scene.Objects.Add(pin);

        scene.SelectAssembly(assembly);
        pin.IsSelected = true;

        var (earlier, last) = scene.SplitLastPick();
        Assert.Equal([a, b], earlier);
        Assert.Equal([pin], last);
    }

    [Fact]
    public void PartsThatLeftTheAssemblyCountOneByOneAgain()
    {
        var (scene, _, assembly, a, b) = Assembled();
        scene.SelectAssembly(assembly);

        a.Assembly = null;
        b.Assembly = null;

        Assert.Equal(2, scene.SelectionInPicks.Count);
    }

    [Fact]
    public void ObjectsLineUpWithTheBoxOfAnAssemblyPickedLastAndItsPartsStayPut()
    {
        var (scene, _, assembly, a, b) = Assembled();
        var loose = Part("Loose", new Vector3(10, 80, 15));
        scene.Objects.Add(loose);

        loose.IsSelected = true;
        scene.SelectAssembly(assembly);

        var box = a.WorldBounds.Union(b.WorldBounds);
        var offsets = AlignTools.BlockOffsets(scene.SelectionInPicks, Axis.X, AlignMode.Maximum);

        Assert.Equal(box.Max.X - loose.WorldBounds.Max.X, offsets[0].X, 3);
        Assert.Equal(Vector3.Zero, offsets[1]);
    }

    [Fact]
    public void AnAssemblyLinedUpAgainstAnObjectMovesAsOneAndKeepsItsShape()
    {
        var (scene, _, assembly, a, b) = Assembled();
        var anchor = Part("Anchor", new Vector3(-60, 80, 15));
        scene.Objects.Add(anchor);

        scene.SelectAssembly(assembly);
        anchor.IsSelected = true;

        var box = a.WorldBounds.Union(b.WorldBounds);
        var offsets = AlignTools.BlockOffsets(scene.SelectionInPicks, Axis.X, AlignMode.Minimum);

        Assert.Equal(2, offsets.Count);
        Assert.Equal(anchor.WorldBounds.Min.X - box.Min.X, offsets[0].X, 3);
        Assert.Equal(Vector3.Zero, offsets[1]);
    }

    [Fact]
    public void AnAssemblyOnItsOwnLinesUpOnTheBedAsOneBlock()
    {
        var (scene, _, assembly, a, b) = Assembled();
        scene.SelectAssembly(assembly);

        var bed = new Bounds(new Vector3(-100, -100, 0), new Vector3(100, 100, 200));
        var offsets = AlignTools.BlockOffsets(scene.SelectionInPicks, Axis.X, AlignMode.Minimum, bed);

        var box = a.WorldBounds.Union(b.WorldBounds);
        Assert.Equal(-100 - box.Min.X, Assert.Single(offsets).X, 3);
    }

    [Fact]
    public void SinglePartsStillLineUpOneByOne()
    {
        var a = Part("A", new Vector3(0, 0, 15));
        var b = Part("B", new Vector3(40, 0, 15));

        Assert.Equal(AlignTools.Offsets([a, b], Axis.X, AlignMode.Centre),
                     AlignTools.BlockOffsets([[a], [b]], Axis.X, AlignMode.Centre));
    }

    // --- Saving --------------------------------------------------------------------------

    [Fact]
    public void SavingAndOpeningKeepsTheAssembliesAndWhereTheirPartsGoBack()
    {
        var (scene, _, assembly, a, _) = Assembled();
        assembly.Name = "Gearbox";
        assembly.Colour = new Vector3(1, 0.5f, 0);
        assembly.IsExpanded = false;
        a.Position += new Vector3(30, 0, 0);
        scene.Objects.Add(Part("Loose", new Vector3(0, 80, 15)));

        string path = Path.Combine(directory, "assembly.3mf");
        SceneSerializer.Save(path, scene);
        var loaded = SceneSerializer.Load(path);

        Assert.Null(loaded[2].Assembly);
        var opened = loaded[0].Assembly;
        Assert.NotNull(opened);
        Assert.Same(opened, loaded[1].Assembly);
        Assert.Equal("Gearbox", opened!.Name);
        Assert.Equal(new Vector3(1, 0.5f, 0), opened.Colour);
        Assert.False(opened.IsExpanded);
        Assert.Equal(new Vector3(0, 0, 15), loaded[0].HomePosition);
        Assert.Equal(new Vector3(10, 20, 30), loaded[0].HomeRotation);
        Assert.Equal(new Vector3(30, 0, 15), loaded[0].Position);
    }

    [Fact]
    public void AKeptVersionKeepsItsAssemblies()
    {
        var (scene, _, assembly, _, _) = Assembled();
        assembly.Name = "Before";

        string path = Path.Combine(directory, "versions.3mf");
        SceneSerializer.SaveVersion(path, scene, "one");
        var restored = SceneSerializer.LoadVersion(path, 0);

        Assert.Equal("Before", restored[0].Assembly?.Name);
        Assert.Same(restored[0].Assembly, restored[1].Assembly);
    }

    // --- On screen and in the list -----------------------------------------------------------

    [Fact]
    public void PickingAnAssemblyDrawsItsPartsInItsColourAndLeavesTheirOwnAlone()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var a = Part("A", new Vector3(0, 0, 15));
            var b = Part("B", new Vector3(40, 0, 15));
            a.Colour = new Vector3(0.2f, 0.2f, 0.2f);
            model.Scene.Objects.Add(a);
            model.Scene.Objects.Add(b);
            a.IsSelected = b.IsSelected = true;
            model.RefreshSelection();

            model.AssembleCommand.Execute(null);
            var assembly = Assert.IsType<Assembly>(model.ListRows[0]);
            Assert.Same(assembly, model.SelectedAssembly);
            Assert.Equal([assembly, a, b], model.ListRows);

            var orange = new Vector3(1, 0.5f, 0);
            model.SetAssemblyColourCommand.Execute(orange);

            Assert.Equal(orange, a.ShownColour);
            Assert.Equal(new Vector3(0.2f, 0.2f, 0.2f), a.Colour);
            Assert.True(a.InPickedAssembly && b.InPickedAssembly);

            // Letting go of one part lets go of the assembly, and its colour with it.
            b.IsSelected = false;
            model.RefreshSelection();
            Assert.False(assembly.IsSelected);
            Assert.Equal(a.Colour, a.ShownColour);
            Assert.False(a.InPickedAssembly);
        });
    }

    [Fact]
    public void OneObjectOnItsOwnCanBeAnAssembly()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var a = Part("A", new Vector3(0, 0, 15));
            model.Scene.Objects.Add(a);
            a.IsSelected = true;
            model.RefreshSelection();

            Assert.True(model.AssembleCommand.CanExecute(null));
            model.AssembleCommand.Execute(null);

            Assert.Same(model.SelectedAssembly, a.Assembly);
            Assert.Equal([a.Assembly!, a], model.ListRows);
        });
    }

    [Fact]
    public void ObjectsSelectedBesideAPickedAssemblyAreAddedToIt()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var a = Part("A", new Vector3(0, 0, 15));
            var b = Part("B", new Vector3(40, 0, 15));
            var c = Part("C", new Vector3(80, 0, 15));
            model.Scene.Objects.Add(a);
            model.Scene.Objects.Add(b);
            model.Scene.Objects.Add(c);
            a.IsSelected = true;
            model.RefreshSelection();
            model.AssembleCommand.Execute(null);

            var assembly = model.SelectedAssembly!;
            assembly.Name = "Frame";
            assembly.Colour = new Vector3(1, 0, 0);

            // The heading picked, then two more objects alongside it.
            b.IsSelected = c.IsSelected = true;
            model.RefreshSelection();
            Assert.Same(assembly, model.SelectedAssembly);

            model.AssembleCommand.Execute(null);

            Assert.Same(assembly, Assert.Single(model.Scene.Assemblies));
            Assert.Equal([a, b, c], model.Scene.MembersOf(assembly));
            Assert.Equal("Frame", assembly.Name);
            Assert.Equal(new Vector3(80, 0, 15), c.HomePosition);
            Assert.True(assembly.IsSelected);

            model.UndoCommand.Execute(null);
            Assert.Equal([a], model.Scene.MembersOf(assembly));
            Assert.Null(b.Assembly);
        });
    }

    [Fact]
    public void AFoldedAssemblyListsOnlyItsHeading()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var a = Part("A", new Vector3(0, 0, 15));
            var loose = Part("Loose", new Vector3(0, 80, 15));
            var b = Part("B", new Vector3(40, 0, 15));
            model.Scene.Objects.Add(a);
            model.Scene.Objects.Add(loose);
            model.Scene.Objects.Add(b);
            a.IsSelected = b.IsSelected = true;
            model.RefreshSelection();

            model.AssembleCommand.Execute(null);
            var assembly = model.SelectedAssembly!;
            Assert.Equal([assembly, a, b, loose], model.ListRows);

            model.FoldAssemblyCommand.Execute(assembly);
            Assert.Equal([assembly, loose], model.ListRows);
            Assert.Equal(2, assembly.MemberCount);

            model.FoldAllAssembliesCommand.Execute("Open");
            Assert.Equal([assembly, a, b, loose], model.ListRows);
            model.FoldAllAssembliesCommand.Execute("Close");
            Assert.Equal([assembly, loose], model.ListRows);
        });
    }

    [Fact]
    public void UngroupTakesAPickedAssemblyApartAndLeavesItsPartsWhole()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var pair = new SceneObject("Pair", Mesh.Combine([Primitives.Box(10, 10, 10),
                MeshTransform.Transformed(Primitives.Box(10, 10, 10), Matrix4x4.CreateTranslation(30, 0, 0))]))
            {
                Position = new Vector3(0, 0, 5)
            };
            var b = Part("B", new Vector3(0, 60, 15));
            model.Scene.Objects.Add(pair);
            model.Scene.Objects.Add(b);
            pair.IsSelected = b.IsSelected = true;
            model.RefreshSelection();
            model.AssembleCommand.Execute(null);

            model.UngroupCommand.Execute(null);

            Assert.Empty(model.Scene.Assemblies);
            Assert.Equal([pair, b], model.Scene.Objects);

            // Nothing picked by its name now, so it is the mesh's turn.
            model.UngroupCommand.Execute(null);
            Assert.Equal(3, model.Scene.Objects.Count);

            model.UndoCommand.Execute(null);
            model.UndoCommand.Execute(null);
            Assert.Single(model.Scene.Assemblies);
        });
    }

    [Fact]
    public void TheEyeAndLockOnAHeadingDoEveryPartAndSaySo()
    {
        RunSta(() =>
        {
            var model = new MainViewModel();
            var a = Part("A", new Vector3(0, 0, 15));
            var b = Part("B", new Vector3(40, 0, 15));
            model.Scene.Objects.Add(a);
            model.Scene.Objects.Add(b);
            a.IsSelected = b.IsSelected = true;
            model.RefreshSelection();
            model.AssembleCommand.Execute(null);
            var assembly = model.SelectedAssembly!;

            // One hidden by hand: the heading says some are, and a click hides the rest.
            a.IsHidden = true;
            model.RefreshSelection();
            Assert.True(assembly.AnyHidden);
            Assert.False(assembly.AllHidden);

            model.ToggleAssemblyHiddenCommand.Execute(assembly);
            Assert.True(a.IsHidden && b.IsHidden && assembly.AllHidden);

            model.ToggleAssemblyHiddenCommand.Execute(assembly);
            Assert.False(a.IsHidden || b.IsHidden || assembly.AnyHidden);

            model.ToggleAssemblyLockedCommand.Execute(assembly);
            Assert.True(a.IsLocked && b.IsLocked && assembly.AllLocked);
            Assert.False(assembly.IsSelected);
        });
    }

    private static (Window Window, ListBox List, SelectionListSync Sync) ListOf(Scene scene, ObservableCollection<object> rows)
    {
        var list = new ListBox { ItemsSource = rows, SelectionMode = SelectionMode.Extended };
        var window = new Window { Width = 300, Height = 300, Content = list, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        return (window, list, new SelectionListSync(list, scene));
    }

    [Fact]
    public void ClickingTheHeadingSelectsEveryPartAndLettingGoOfOnePartLetsGoOfTheHeading()
    {
        RunSta(() =>
        {
            var (scene, _, assembly, a, b) = Assembled();
            var rows = new ObservableCollection<object> { assembly, a, b };
            var (window, list, sync) = ListOf(scene, rows);

            // What the window wires up: a refresh lets go of an assembly not wholly selected.
            sync.ChangedFromList += () =>
            {
                if (!scene.IsWhollySelected(assembly)) assembly.IsSelected = false;
            };

            try
            {
                list.SelectedItems.Add(assembly);

                Assert.True(a.IsSelected && b.IsSelected && assembly.IsSelected);
                Assert.Equal(3, list.SelectedItems.Count);

                list.SelectedItems.Remove(a);

                Assert.False(a.IsSelected);
                Assert.True(b.IsSelected);
                Assert.False(assembly.IsSelected);
                Assert.Equal([b], list.SelectedItems.Cast<object>());
            }
            finally
            {
                sync.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void LettingGoOfTheHeadingAloneLetsGoOfEveryPart()
    {
        RunSta(() =>
        {
            var (scene, _, assembly, a, b) = Assembled();
            var rows = new ObservableCollection<object> { assembly, a, b };
            var (window, list, sync) = ListOf(scene, rows);

            try
            {
                list.SelectedItems.Add(assembly);
                list.SelectedItems.Remove(assembly);

                Assert.False(a.IsSelected || b.IsSelected || assembly.IsSelected);
                Assert.Empty(list.SelectedItems);
            }
            finally
            {
                sync.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void FoldingTheListDoesNotLetGoOfTheParts()
    {
        RunSta(() =>
        {
            var (scene, _, assembly, a, b) = Assembled();
            var rows = new ObservableCollection<object> { assembly, a, b };
            var (window, list, sync) = ListOf(scene, rows);

            try
            {
                list.SelectedItems.Add(assembly);

                sync.Hold();
                rows.Remove(a);
                rows.Remove(b);
                sync.Release();

                Assert.True(a.IsSelected && b.IsSelected && assembly.IsSelected);
                Assert.Equal([assembly], list.SelectedItems.Cast<object>());
            }
            finally
            {
                sync.Dispose();
                window.Close();
            }
        });
    }
}
