using System.Diagnostics;
using System.IO;
using System.Numerics;
using FastCraft3D.Geometry;
using FastCraft3D.Io;
using FastCraft3D.Model;
using Xunit;

namespace FastCraft3D.Tests;

/// <summary>
/// The copy of the scene kept for a run that never gets to close. These drive the store
/// directly: the offer itself is a message box, and what is worth pinning down is which files
/// are left behind and which are taken as somebody else's live work.
/// </summary>
public class RecoveryTests : IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(), "fc3d-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static Scene SceneOf(int objects)
    {
        var scene = new Scene();
        for (int i = 0; i < objects; i++)
            scene.Objects.Add(new SceneObject($"Part {i + 1}", Primitives.Box(10, 10, 10))
            {
                Position = new Vector3(i * 20f, 0f, 5f)
            });

        return scene;
    }

    /// <summary>Nothing is running, so everything in the folder is a leftover.</summary>
    private static bool NothingIsRunning(int id, long started) => false;

    /// <summary>Everything is running, as it is while the app that wrote the file is still up.</summary>
    private static bool EverythingIsRunning(int id, long started) => true;

    [Fact]
    public void WhatWasKeptComesBackWithTheProjectItBelongsTo()
    {
        Recovery.Keep(SceneOf(3), @"F:\models\clock.3dfc", null, folder);

        var abandoned = Recovery.Abandoned(folder, NothingIsRunning);

        Assert.Single(abandoned);
        Assert.Equal(@"F:\models\clock.3dfc", abandoned[0].ProjectPath);
        Assert.Equal(3, abandoned[0].Objects);
        Assert.Equal(3, SceneSerializer.Load(abandoned[0].ScenePath).Count);
    }

    [Fact]
    public void AModelThatWasNeverSavedIsKeptWithNoProjectAtAll()
    {
        Recovery.Keep(SceneOf(1), null, null, folder);

        var abandoned = Recovery.Abandoned(folder, NothingIsRunning);

        Assert.Single(abandoned);
        Assert.Null(abandoned[0].ProjectPath);
    }

    /// <summary>
    /// The file this very process wrote a moment ago is its own live work. Told apart by the
    /// process id and its start time, which is what the real check reads - so this one runs it
    /// rather than a stub.
    /// </summary>
    [Fact]
    public void WhatAStillRunningCopyOfTheAppHoldsIsNotOfferedBack()
    {
        Recovery.Keep(SceneOf(2), null, null, folder);

        Assert.Empty(Recovery.Abandoned(folder));
        Assert.Single(Recovery.Abandoned(folder, NothingIsRunning));
    }

    [Fact]
    public void ClearingTakesAwayWhatThisRunKept()
    {
        Recovery.Keep(SceneOf(2), null, null, folder);
        Recovery.Clear(folder);

        Assert.Empty(Recovery.Abandoned(folder, NothingIsRunning));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void KeepingAgainReplacesWhatWasThereRatherThanAddingToIt()
    {
        Recovery.Keep(SceneOf(1), null, null, folder);
        Recovery.Keep(SceneOf(4), null, null, folder);

        var abandoned = Recovery.Abandoned(folder, NothingIsRunning);

        Assert.Single(abandoned);
        Assert.Equal(4, abandoned[0].Objects);
    }

    [Fact]
    public void OneThatWasRecoveredOrRefusedIsGoneNextTime()
    {
        Recovery.Keep(SceneOf(1), null, null, folder);

        Recovery.Discard(Recovery.Abandoned(folder, NothingIsRunning)[0]);

        Assert.Empty(Recovery.Abandoned(folder, NothingIsRunning));
    }

    /// <summary>A scene with no note beside it says nothing about whose it is, so it is swept up.</summary>
    [Fact]
    public void ASceneLeftWithoutItsNoteIsClearedOutRatherThanOffered()
    {
        Directory.CreateDirectory(folder);
        SceneSerializer.SaveSnapshot(Path.Combine(folder, "999999.3dfc"), SceneOf(1));

        Assert.Empty(Recovery.Abandoned(folder, NothingIsRunning));
    }

    [Fact]
    public void TheNewestIsOfferedFirst()
    {
        Directory.CreateDirectory(folder);

        // Written by hand, because one process can only own one of these at a time.
        foreach (var (id, when, objects) in new[]
        {
            (11, DateTime.UtcNow.AddHours(-3), 1),
            (22, DateTime.UtcNow.AddMinutes(-5), 2),
            (33, DateTime.UtcNow.AddDays(-1), 3)
        })
        {
            SceneSerializer.SaveSnapshot(Path.Combine(folder, $"{id}.3dfc"), SceneOf(objects));
            File.WriteAllText(Path.Combine(folder, $"{id}.json"),
                $"{{\"Project\":null,\"SavedUtc\":\"{when:O}\",\"Objects\":{objects}," +
                $"\"ProcessId\":{id},\"StartedUtcTicks\":0}}");
        }

        var abandoned = Recovery.Abandoned(folder, NothingIsRunning);

        Assert.Equal([2, 1, 3], abandoned.Select(a => a.Objects));
    }

    [Fact]
    public void AFileLeftBehindAMonthAgoIsNobodysWorkAnyMore()
    {
        Directory.CreateDirectory(folder);
        SceneSerializer.SaveSnapshot(Path.Combine(folder, "44.3dfc"), SceneOf(1));
        File.WriteAllText(Path.Combine(folder, "44.json"),
            $"{{\"Project\":null,\"SavedUtc\":\"{DateTime.UtcNow.AddDays(-40):O}\",\"Objects\":1," +
            "\"ProcessId\":44,\"StartedUtcTicks\":0}");

        Assert.Empty(Recovery.Abandoned(folder, NothingIsRunning));
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>A folder that has never been written to is not an error, just nothing to offer.</summary>
    [Fact]
    public void AFolderWithNothingInItOffersNothing()
    {
        Assert.Empty(Recovery.Abandoned(folder, EverythingIsRunning));
    }

    /// <summary>
    /// The crash file is the scene alone. The kept versions live in the project file, which a
    /// recovery does not touch - so rewriting this every half minute must not drag them along.
    /// </summary>
    [Fact]
    public void ASnapshotHoldsTheSceneAndNoKeptVersions()
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "snapshot.3dfc");

        SceneSerializer.SaveVersion(path, SceneOf(1), "before");
        SceneSerializer.SaveSnapshot(path, SceneOf(2));

        Assert.Empty(SceneSerializer.ReadVersions(path));
        Assert.Equal(2, SceneSerializer.Load(path).Count);
    }

    /// <summary>The settings travel with it, so a recovered model comes back on the right bed.</summary>
    [Fact]
    public void TheBedAndTheUnitComeBackWithTheScene()
    {
        Recovery.Keep(SceneOf(1), null, new ProjectSettings(350f, 320f, 400f, "in", 87f), folder);

        var point = Recovery.Abandoned(folder, NothingIsRunning)[0];
        SceneSerializer.Load(point.ScenePath, out var settings);

        Assert.NotNull(settings);
        Assert.Equal(350f, settings.Value.PlateWidth);
        Assert.Equal("in", settings.Value.Unit);
        Assert.Equal(87f, settings.Value.ModelScale);
    }
}
