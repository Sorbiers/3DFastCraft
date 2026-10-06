using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using FastCraft3D.View;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>A version keeps a small picture of the plate as it was, and the Versions list shows it.</summary>
public class VersionPictureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-versionpic-" + Guid.NewGuid().ToString("N"));

    public VersionPictureTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static Scene SceneWith(params (string Name, float X)[] parts)
    {
        var scene = new Scene();
        foreach (var (name, x) in parts)
            scene.Objects.Add(new SceneObject(name, Primitives.Box(20, 20, 20)) { Position = new Vector3(x, 0, 10) });

        return scene;
    }

    private string Saved(Scene scene, params string[] versions)
    {
        string path = Path.Combine(directory, "project.3mf");
        SceneSerializer.Save(path, scene);
        foreach (string label in versions) SceneSerializer.SaveVersion(path, scene, label);
        return path;
    }

    private static BitmapSource Decoded(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var picture = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        picture.Freeze();
        return picture;
    }

    [Fact]
    public void AVersionKeepsAPictureOfTheSceneItWasSavedFrom()
    {
        string path = Saved(SceneWith(("A", -30f), ("B", 30f)), "first");

        var version = Assert.Single(SceneSerializer.ReadVersions(path));

        Assert.NotNull(version.Thumbnail);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], version.Thumbnail![..4]);

        var picture = Decoded(version.Thumbnail);
        Assert.Equal(picture.PixelWidth, picture.PixelHeight);
        Assert.True(picture.PixelWidth >= 96, $"{picture.PixelWidth} px is too small to tell versions apart");
    }

    [Fact]
    public void TwoVersionsOfDifferentScenesHaveDifferentPictures()
    {
        string path = Path.Combine(directory, "project.3mf");
        var scene = SceneWith(("A", 0f));
        SceneSerializer.Save(path, scene);
        SceneSerializer.SaveVersion(path, scene, "one box");

        scene.Objects.Add(new SceneObject("B", Primitives.Box(20, 20, 40)) { Position = new Vector3(40, 0, 20) });
        SceneSerializer.SaveVersion(path, scene, "two boxes");

        var versions = SceneSerializer.ReadVersions(path);

        Assert.Equal(2, versions.Count);
        Assert.NotEqual(versions[0].Thumbnail, versions[1].Thumbnail);
    }

    [Fact]
    public void ForgettingOneVersionKeepsThePicturesOfTheOthers()
    {
        string path = Saved(SceneWith(("A", 0f)), "first", "second", "third");
        var before = SceneSerializer.ReadVersions(path);

        SceneSerializer.DeleteVersion(path, 1);

        var after = SceneSerializer.ReadVersions(path);
        Assert.Equal(["first", "third"], after.Select(v => v.Label));
        Assert.Equal(before[0].Thumbnail, after[0].Thumbnail);
        Assert.Equal(before[2].Thumbnail, after[1].Thumbnail);
    }

    [Fact]
    public void AnEmptyPlateKeepsNoPictureAndStillSavesTheVersion()
    {
        string path = Saved(new Scene(), "nothing yet");

        var version = Assert.Single(SceneSerializer.ReadVersions(path));

        Assert.Equal("nothing yet", version.Label);
        Assert.Null(version.Thumbnail);
    }

    /// <summary>
    /// A version kept before pictures were, read from a file that has none: its fields are not
    /// there, and it reads as a version without a picture rather than not at all.
    /// </summary>
    [Fact]
    public void AVersionFromBeforePicturesReadsWithoutOne()
    {
        string path = Saved(SceneWith(("A", 0f)), "old");

        // Take the picture out of the file, as a copy of the app from before it would have written it.
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("3DFastCraft/project.json")!;
            JsonNode root;
            using (var read = entry.Open()) root = JsonNode.Parse(read)!;

            foreach (var version in root["Versions"]!.AsArray()) version!.AsObject().Remove("Thumbnail");

            entry.Delete();
            var rewritten = zip.CreateEntry("3DFastCraft/project.json");
            using var write = rewritten.Open();
            write.Write(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        var kept = Assert.Single(SceneSerializer.ReadVersions(path));
        Assert.Equal("old", kept.Label);
        Assert.Null(kept.Thumbnail);
        Assert.Single(SceneSerializer.LoadVersion(path, 0));
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

    /// <summary>A property of a row of the list, which is an anonymous type of the app's own.</summary>
    private static System.Collections.IList Rows(VersionsDialog dialog) =>
        ((System.Windows.Controls.ListBox)dialog.FindName("VersionList")).Items;

    private static object? Of(object row, string name) => row.GetType().GetProperty(name)!.GetValue(row);

    [Fact]
    public void TheVersionsListShowsEachPictureAndLeavesAnEmptyOneBlank() => RunSta(() =>
    {
        string path = Saved(SceneWith(("A", 0f)), "with a picture");

        var row = Rows(new VersionsDialog(path))[0]!;

        Assert.Equal("with a picture", Of(row, "Label"));
        Assert.NotNull(Of(row, "Picture"));

        // An empty plate has nothing to show, and the list copes.
        string empty = Path.Combine(directory, "empty.3mf");
        SceneSerializer.Save(empty, new Scene());
        SceneSerializer.SaveVersion(empty, new Scene(), "blank");

        var blank = Rows(new VersionsDialog(empty))[0]!;
        Assert.Null(Of(blank, "Picture"));
    });
}
