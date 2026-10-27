using System.IO;
using System.Runtime.ExceptionServices;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.Model.Commands;
using FastCraft3D.ViewModels;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// Which plate prints a part, set on a whole selection as the filament is, and the object list
/// narrowed to one plate or one filament.
/// </summary>
public class PlateTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "3dfc-plate-" + Guid.NewGuid().ToString("N"));

    public PlateTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch { /* the temp folder can wait */ }
        GC.SuppressFinalize(this);
    }

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

    private static SceneObject Part(string name, int plate = 1, int filament = 1) =>
        new(name, Primitives.Box(10, 10, 10)) { Plate = plate, Filament = filament };

    [Fact]
    public void APartKeepsItsPlateThroughACopyAndTheProjectFile()
    {
        var scene = new Scene();
        scene.Objects.Add(Part("Wall"));
        scene.Objects.Add(Part("Roof", plate: 3));
        Assert.Equal(3, scene.Objects[1].Clone().Plate);

        string path = Path.Combine(folder, "house.3mf");
        SceneSerializer.Save(path, scene);

        Assert.Equal([1, 3], SceneSerializer.Load(path).Select(o => o.Plate));
    }

    [Fact]
    public void SettingThePlateOnSeveralPartsIsOneStepUndoneTogether()
    {
        var scene = new Scene();
        var parts = new[] { Part("A", plate: 1), Part("B", plate: 2) };
        foreach (var p in parts) scene.Objects.Add(p);
        var undo = new UndoStack(scene);

        undo.Execute(PlateCommand.CreateIfChanged("Plate", parts, 4)!);
        Assert.All(parts, p => Assert.Equal(4, p.Plate));

        undo.Undo();
        Assert.Equal([1, 2], parts.Select(p => p.Plate));
        Assert.Null(PlateCommand.CreateIfChanged("Plate", [parts[0]], 1));
    }

    [Fact]
    public void ASelectionThatDisagreesShowsNothingAndWhatIsTypedGoesToAllOfIt()
    {
        WithModel(model =>
        {
            model.Scene.Objects.Add(Part("A", plate: 1, filament: 2));
            model.Scene.Objects.Add(Part("B", plate: 2, filament: 2));
            model.SelectAllCommand.Execute(null);

            Assert.Null(model.ObjectPlate);
            Assert.Equal(2, model.ObjectFilament);
            Assert.Contains("differ", model.PlateHint);

            model.ObjectPlate = 1;

            Assert.All(model.Scene.Objects, o => Assert.Equal(1, o.Plate));
            Assert.Equal(1, model.ObjectPlate);
        });
    }

    [Fact]
    public void TheListNarrowedToAPlateSaysSoAndForgetsItWhenThePlateIsCleared()
    {
        WithModel(model =>
        {
            model.Scene.Objects.Add(Part("Wall", plate: 1));
            model.Scene.Objects.Add(Part("Roof", plate: 2, filament: 3));
            model.Scene.Objects.Add(Part("Chimney", plate: 2));

            Assert.Equal([(1, 1), (2, 2)], model.PlatesInUse);

            model.FilterPlateCommand.Execute(2);
            Assert.True(model.IsListFiltered);
            Assert.Equal(["Roof", "Chimney"], model.ListRows.Cast<SceneObject>().Select(o => o.Name));
            Assert.Equal("Plate 2 - 2 of 3 objects", model.ListFilterSummary);

            model.FilterFilamentCommand.Execute(3);
            Assert.Equal("Roof", Assert.Single(model.ListRows.Cast<SceneObject>()).Name);

            // A part moved off the plate leaves the list.
            model.SelectAllCommand.Execute(null);
            model.FilterFilamentCommand.Execute(null);
            model.Scene.Objects[2].IsSelected = false;
            model.Scene.Objects[0].IsSelected = false;
            model.ObjectPlate = 5;
            Assert.Equal("Chimney", Assert.Single(model.ListRows.Cast<SceneObject>()).Name);

            model.Scene.Objects.Clear();
            Assert.False(model.IsListFiltered);
        });
    }
}
