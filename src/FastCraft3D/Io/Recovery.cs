using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FastCraft3D.Model;

namespace FastCraft3D.Io;

/// <param name="ScenePath">The kept copy of the scene.</param>
/// <param name="ProjectPath">The file it was being edited as, or null for a scene never saved.</param>
/// <param name="SavedUtc">When the copy was written.</param>
/// <param name="Objects">How many objects it holds, so the offer can say what is on the table.</param>
public readonly record struct RecoveryPoint(string ScenePath, string? ProjectPath, DateTime SavedUtc, int Objects);

/// <summary>
/// The copy of the scene kept for a run that never gets to close.
///
/// A project file is written when the user says so, and a model an hour into being made has
/// often never been saved at all. So the scene is also kept quietly beside the settings while it
/// is being worked on, and taken away again the moment it is no longer needed - on a save, and
/// on the way out of a close the user confirmed. What is left in the folder afterwards is
/// therefore exactly the work of a run that stopped without closing, and is offered back.
///
/// Each run writes under its own process id, with a note beside it saying which process that is
/// and when it started. Two copies of the app open at once must not offer each other's live work
/// back, and Windows gives process ids out again, so the start time has to agree as well before
/// a file is taken as being in use.
///
/// Nothing here is allowed to interrupt the work: every path swallows its own failures. A
/// read-only profile, a roaming folder that is not there, an antivirus holding the file open -
/// none of those are worth a dialog in the middle of modelling, and the cost of missing them is
/// only that a crash loses what a crash used to lose anyway.
/// </summary>
public static class Recovery
{
    /// <summary>A file left behind this long ago is nobody's work any more.</summary>
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    private static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "3DFastCraft", "recovery");

    /// <summary>What sits beside the kept scene and says whose it is.</summary>
    private sealed class Note
    {
        public string? Project { get; set; }
        public DateTime SavedUtc { get; set; }
        public int Objects { get; set; }
        public int ProcessId { get; set; }
        public long StartedUtcTicks { get; set; }
    }

    /// <summary>
    /// Writes this run's copy of the scene, replacing the one before it.
    ///
    /// The scene goes down before the note, so a note always means there is a whole scene under
    /// it: interrupted the other way round, the next start would offer back half a file.
    /// </summary>
    public static void Keep(Scene scene, string? projectPath, ProjectSettings? settings, string? folder = null)
    {
        try
        {
            string where = folder ?? DefaultFolder();
            Directory.CreateDirectory(where);

            using var me = Process.GetCurrentProcess();

            SceneSerializer.SaveSnapshot(ScenePath(where, me.Id), scene, settings);
            File.WriteAllText(NotePath(where, me.Id), JsonSerializer.Serialize(new Note
            {
                Project = projectPath,
                SavedUtc = DateTime.UtcNow,
                Objects = scene.Objects.Count,
                ProcessId = me.Id,
                StartedUtcTicks = Started(me)
            }));
        }
        catch
        {
        }
    }

    /// <summary>Takes this run's copy away - there is nothing left to recover.</summary>
    public static void Clear(string? folder = null)
    {
        try
        {
            string where = folder ?? DefaultFolder();
            using var me = Process.GetCurrentProcess();

            Forget(ScenePath(where, me.Id), NotePath(where, me.Id));
        }
        catch
        {
        }
    }

    /// <summary>
    /// What earlier runs left behind, newest first. Anything past its time, orphaned or
    /// unreadable is cleared out on the way past.
    /// </summary>
    /// <param name="alive">
    /// Whether a run is still going, given its process id and when it started. Only supplied by
    /// the tests, which have no dead process of their own to point at.
    /// </param>
    public static List<RecoveryPoint> Abandoned(
        string? folder = null, Func<int, long, bool>? alive = null)
    {
        var found = new List<RecoveryPoint>();

        try
        {
            string where = folder ?? DefaultFolder();
            if (!Directory.Exists(where)) return found;

            alive ??= StillRunning;

            foreach (string note in Directory.EnumerateFiles(where, "*.json"))
            {
                string scene = Path.ChangeExtension(note, SceneSerializer.Extension);
                var kept = Read(note);

                if (kept is null || !File.Exists(scene) || DateTime.UtcNow - kept.SavedUtc > KeepFor)
                {
                    Forget(scene, note);
                    continue;
                }

                // Another copy of the app has this open right now: it is live work, not a leftover.
                if (alive(kept.ProcessId, kept.StartedUtcTicks)) continue;

                found.Add(new RecoveryPoint(scene, kept.Project, kept.SavedUtc, kept.Objects));
            }
        }
        catch
        {
        }

        found.Sort((a, b) => b.SavedUtc.CompareTo(a.SavedUtc));
        return found;
    }

    /// <summary>Throws one away, recovered or refused.</summary>
    public static void Discard(RecoveryPoint point) =>
        Forget(point.ScenePath, Path.ChangeExtension(point.ScenePath, ".json"));

    private static Note? Read(string path)
    {
        try { return JsonSerializer.Deserialize<Note>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private static void Forget(string scene, string note)
    {
        try { File.Delete(note); } catch { }
        try { File.Delete(scene); } catch { }
    }

    private static string ScenePath(string folder, int id) =>
        Path.Combine(folder, id.ToString() + SceneSerializer.Extension);

    private static string NotePath(string folder, int id) =>
        Path.Combine(folder, id.ToString() + ".json");

    /// <summary>
    /// When a process started, as a number that survives being written down. Unreadable for a
    /// process the user has no rights over, which is taken as nought rather than as a failure -
    /// a note whose start time is unknown can only be matched by another unknown one.
    /// </summary>
    private static long Started(Process process)
    {
        try { return process.StartTime.ToUniversalTime().Ticks; }
        catch { return 0L; }
    }

    private static bool StillRunning(int id, long startedUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return Started(process) == startedUtcTicks;
        }
        catch
        {
            // No such process - which is the whole point of asking.
            return false;
        }
    }
}
