using System.IO;
using FastCraft3D.Io;
using FastCraft3D.Render;
using Xunit;

namespace FastCraft3D.Tests;

public class RecentFilesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "3dfc-recent-" + Guid.NewGuid().ToString("N"));

    public RecentFilesTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private RecentFiles New() => new(Path.Combine(directory, "recent.json"));
    private string Path_(string name) => Path.Combine(directory, name);

    [Fact]
    public void TheNewestFileComesFirst()
    {
        var recent = New();

        recent.Add(Path_("a.3dfc"));
        recent.Add(Path_("b.3dfc"));

        Assert.Equal(Path_("b.3dfc"), recent.Paths[0]);
        Assert.Equal(Path_("a.3dfc"), recent.Paths[1]);
    }

    /// <summary>Reopening a file moves it up rather than listing it twice.</summary>
    [Fact]
    public void ReopeningAFileMovesItToTheTop()
    {
        var recent = New();
        recent.Add(Path_("a.3dfc"));
        recent.Add(Path_("b.3dfc"));

        recent.Add(Path_("a.3dfc"));

        Assert.Equal(2, recent.Paths.Count);
        Assert.Equal(Path_("a.3dfc"), recent.Paths[0]);
    }

    [Fact]
    public void TheListIsCappedAndDropsTheOldest()
    {
        var recent = New();
        for (int i = 0; i < 12; i++) recent.Add(Path_($"file{i}.3dfc"));

        Assert.Equal(8, recent.Paths.Count);
        Assert.Equal(Path_("file11.3dfc"), recent.Paths[0]);
        Assert.DoesNotContain(Path_("file0.3dfc"), recent.Paths);
    }

    [Fact]
    public void TheListSurvivesBetweenSessions()
    {
        New().Add(Path_("kept.3dfc"));

        var reopened = New();

        Assert.Single(reopened.Paths);
        Assert.Equal(Path_("kept.3dfc"), reopened.Paths[0]);
    }

    [Fact]
    public void AFileCanBeDroppedWhenItHasGone()
    {
        var recent = New();
        recent.Add(Path_("a.3dfc"));
        recent.Add(Path_("b.3dfc"));

        recent.Remove(Path_("a.3dfc"));

        Assert.Single(recent.Paths);
        Assert.Equal(Path_("b.3dfc"), recent.Paths[0]);
    }

    /// <summary>A damaged list must not stop the app starting.</summary>
    [Fact]
    public void ADamagedListSimplyStartsEmpty()
    {
        string store = Path.Combine(directory, "recent.json");
        File.WriteAllText(store, "this is not json at all");

        var recent = new RecentFiles(store);

        Assert.Empty(recent.Paths);
        recent.Add(Path_("a.3dfc")); // and still works afterwards
        Assert.Single(recent.Paths);
    }
}

/// <summary>Snapping a drag so parts land on a shared grid.</summary>
public class SnapTests
{
    [Fact]
    public void TheDestinationIsSnappedNotTheDistance()
    {
        // Starting off-grid at 3.7 and dragging 5.1: the object should land on 9, not on 8.8.
        double travel = GizmoMath.SnapTravel(start: 3.7, travel: 5.1, step: 1);

        Assert.Equal(9.0, 3.7 + travel, 6);
    }

    /// <summary>
    /// Even the smallest drag puts an off-grid object onto the grid, at whichever grid point the
    /// destination is nearest - so nudging one way or the other decides which.
    /// </summary>
    [Fact]
    public void AnObjectAlreadyOffGridIsTidiedUpByTheFirstDrag()
    {
        // 4.37 + 0.2 is 4.57, nearest 5.
        Assert.Equal(5.0, 4.37 + GizmoMath.SnapTravel(4.37, 0.2, 1), 6);

        // Nudged the other way it settles on 4 instead.
        Assert.Equal(4.0, 4.37 + GizmoMath.SnapTravel(4.37, -0.2, 1), 6);
    }

    [Fact]
    public void AFiveMillimetreStepLandsOnMultiplesOfFive()
    {
        double travel = GizmoMath.SnapTravel(start: 0, travel: 13, step: 5);

        Assert.Equal(15.0, travel, 6);
    }

    [Fact]
    public void ZeroOrNegativeStepsLeaveTheDragAlone()
    {
        Assert.Equal(5.1, GizmoMath.SnapTravel(3.7, 5.1, 0), 6);
        Assert.Equal(5.1, GizmoMath.SnapTravel(3.7, 5.1, -1), 6);
    }

    /// <summary>
    /// Two parts dragged onto the same grid meet exactly, which is the whole point of snapping.
    /// </summary>
    [Fact]
    public void TwoPartsSnappedToTheSameGridMeetExactly()
    {
        double a = 2.31 + GizmoMath.SnapTravel(2.31, 7.8, 1);
        double b = 19.62 + GizmoMath.SnapTravel(19.62, -9.4, 1);

        Assert.Equal(10.0, a, 6);
        Assert.Equal(10.0, b, 6);
        Assert.Equal(a, b, 9);
    }
}
