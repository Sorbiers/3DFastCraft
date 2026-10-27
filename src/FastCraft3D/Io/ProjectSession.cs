using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FastCraft3D.Geometry;
using FastCraft3D.Model;

namespace FastCraft3D.Io;

/// <summary>One step of the session kept in a .3dfp: what it was, when, and how many objects were on the plate after it.</summary>
public sealed record SessionStep(int Number, string Label, DateTime Utc, int Objects);

/// <summary>Work a run left in a .3dfp unsaved: when, how much, and which run, to tell a crash from a copy still open.</summary>
public sealed record SessionLeftover(DateTime Utc, int Objects, int ProcessId, long StartedUtcTicks);

/// <summary>
/// The session kept in a project's .3dfp: the plate after every step, written as the work goes
/// rather than exported at the end, and the work not yet saved, for a run that never gets to close.
///
/// Each mesh is stored once, by a hash of its content, and a step names the meshes it uses. Most
/// steps change no mesh - a move, a colour, a rename - and cost a few hundred bytes; a boolean
/// costs its one new mesh. Steps are appended to the zip in place: .NET rewrites a zip opened for
/// update only from its first changed entry, so adding a step to a large file takes milliseconds,
/// where copying the file every time would grow with the session.
///
/// Steps are recorded as they happen and held here until the window is still for a moment, then
/// written together, as the crash file always was. The meshes they name are held by reference
/// meanwhile: nothing changes a mesh in place once it is on the plate - an edit makes a new one,
/// which is what lets undo keep the old - so the reference is as good as a copy.
///
/// Like the crash file, nothing here may interrupt the work: a write that fails is dropped.
/// </summary>
public sealed class ProjectSession
{
    /// <summary>Past either of these the oldest steps go, a tenth at a time so it is not paid at every step.</summary>
    private const int MaxSteps = 2000;
    private const long MaxBytes = 256L << 20;

    /// <summary>Steps held for a project with nowhere to write them yet - one never saved.</summary>
    private const int MaxPending = 500;

    private const string IndexPath = "3DFastCraft/session.json";
    private const string StepsFolder = "3DFastCraft/steps/";
    private const string MeshesFolder = "3DFastCraft/meshes/";
    private const string LeftoverPath = "3DFastCraft/unsaved.json";

    private readonly List<Pending> pending = [];
    private readonly Dictionary<string, Mesh> meshes = [];
    private readonly ConditionalWeakTable<Mesh, string> keys = new();

    /// <summary>The .3dfp this run last left unsaved work in, so it can be taken out again.</summary>
    private string? leftIn;

    private sealed record Pending(string Label, DateTime Utc, byte[] Json, string[] Meshes, int Objects, string Hash);

    public bool HasPending => pending.Count > 0;

    /// <summary>
    /// Takes down the plate as it is after a step. Nothing if it is the same as the step before -
    /// the last one held, or the last one in <paramref name="file"/> when nothing is held.
    /// </summary>
    public void Record(Scene scene, string label, string? file = null)
    {
        try
        {
            var used = new List<string>();
            byte[] json = SceneSerializer.WriteStep(scene, null, mesh => Key(mesh, used));
            string hash = Convert.ToHexString(SHA256.HashData(json), 0, 8);

            string? last = pending.Count > 0 ? pending[^1].Hash : file is not null ? LastHash(file) : null;
            if (hash == last) return;

            pending.Add(new Pending(label, DateTime.UtcNow, json, used.Distinct().ToArray(), scene.Objects.Count, hash));
            if (pending.Count > MaxPending) pending.RemoveAt(0);
        }
        catch
        {
        }
    }

    /// <summary>Lets go of whatever is held unwritten - a project that keeps no .3dfp after all.</summary>
    public void Drop()
    {
        pending.Clear();
        meshes.Clear();
    }

    /// <summary>
    /// Writes the steps held into the .3dfp at <paramref name="path"/>, and the work not yet saved -
    /// <paramref name="unsaved"/>, or null when there is none and any left there before can go.
    /// Returns whether it was written; a file that is not there, or is another project's, is not
    /// written to, and what is held waits.
    /// </summary>
    /// <param name="view">
    /// The camera, kept here as well as in the part a save writes, so a project put down unsaved -
    /// or saved, and then looked round before closing - opens as it was left.
    /// </param>
    public bool Flush(string path, string id, Scene? unsaved, ProjectSettings? settings, ViewState? view = null)
    {
        if (pending.Count == 0 && unsaved is null && leftIn != path && view is null) return true;

        try
        {
            if (!File.Exists(path)) return false;

            IndexDto index;
            var stored = new Dictionary<string, long>();
            bool hadLeftover;

            // Read on its own first: an entry opened for reading in update mode counts as changed,
            // and the manifest is near the front - the whole file would be rewritten every time.
            using (var read = ZipFile.OpenRead(path))
            {
                if (ProjectFile.IdIn(read) != id) return false;
                index = ReadIndex(read) ?? new IndexDto { Format = 1, Next = 1 };
                foreach (var entry in read.Entries)
                    if (entry.FullName.StartsWith(MeshesFolder, StringComparison.Ordinal))
                        stored[entry.FullName[MeshesFolder.Length..]] = entry.Length;
                hadLeftover = read.GetEntry(LeftoverPath) is not null;
            }

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                foreach (var step in pending)
                {
                    foreach (string key in step.Meshes) Store(zip, stored, key);

                    int number = index.Next++;

                    // Only there if a write before this one failed partway, after the step and before
                    // the index: a second entry of the same name would never be read.
                    zip.GetEntry(StepPath(number))?.Delete();
                    Put(zip, StepPath(number), step.Json);
                    (index.Steps ??= []).Add(new StepDto
                    {
                        N = number, Label = step.Label, Utc = step.Utc, Objects = step.Objects, Meshes = step.Meshes, Hash = step.Hash,
                        Bytes = step.Json.Length
                    });
                }

                if (hadLeftover) zip.GetEntry(LeftoverPath)?.Delete();

                if (unsaved is not null)
                {
                    var used = new List<string>();
                    byte[] json = SceneSerializer.WriteStep(unsaved, settings, mesh => Key(mesh, used));
                    foreach (string key in used.Distinct()) Store(zip, stored, key);
                    Put(zip, LeftoverPath, json);

                    var (process, started) = Recovery.ThisRun();
                    index.Unsaved = new LeftoverDto
                    {
                        Utc = DateTime.UtcNow, Objects = unsaved.Objects.Count, Meshes = used.Distinct().ToArray(),
                        ProcessId = process, Started = started
                    };
                    leftIn = path;
                }
                else
                {
                    index.Unsaved = null;
                    if (leftIn == path) leftIn = null;
                }

                Trim(zip, index, stored);

                if (view is not null) index.View = view;
                zip.GetEntry(IndexPath)?.Delete();
                Put(zip, IndexPath, JsonSerializer.SerializeToUtf8Bytes(index));
            }

            Drop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The unsaved work this run left is no longer anything to recover: saved, or let go of.</summary>
    public void ForgetLeftover()
    {
        if (leftIn is not { } path) return;
        leftIn = null;
        ClearLeftover(path);
    }

    // --- Reading back -----------------------------------------------------------------

    /// <summary>The steps kept in a .3dfp, oldest first. None for a file without a session.</summary>
    public static List<SessionStep> ReadSteps(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadIndex(zip)?.Steps?.Select(s => new SessionStep(s.N, s.Label ?? "Step", s.Utc, s.Objects)).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The view as the project was last put down, or null.</summary>
    public static ViewState? ReadView(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadIndex(zip)?.View;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The plate as it was after one step.</summary>
    public static List<SceneObject> LoadStep(string path, int number)
    {
        using var zip = ZipFile.OpenRead(path);
        return Load(zip, StepPath(number), out _);
    }

    /// <summary>Unsaved work left in a .3dfp, or null for none.</summary>
    public static SessionLeftover? ReadLeftover(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.GetEntry(LeftoverPath) is null || ReadIndex(zip)?.Unsaved is not { } left) return null;
            return new SessionLeftover(left.Utc, left.Objects, left.ProcessId, left.Started);
        }
        catch
        {
            return null;
        }
    }

    public static List<SceneObject> LoadLeftover(string path, out ProjectSettings? settings)
    {
        using var zip = ZipFile.OpenRead(path);
        return Load(zip, LeftoverPath, out settings);
    }

    /// <summary>Takes the unsaved work out of a .3dfp: recovered, or turned down. The steps stay.</summary>
    public static void ClearLeftover(string path)
    {
        try
        {
            IndexDto? index;
            using (var read = ZipFile.OpenRead(path))
            {
                index = ReadIndex(read);
                if (read.GetEntry(LeftoverPath) is null && index?.Unsaved is null) return;
            }

            using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
            zip.GetEntry(LeftoverPath)?.Delete();
            if (index is null) return;

            index.Unsaved = null;
            zip.GetEntry(IndexPath)?.Delete();
            Put(zip, IndexPath, JsonSerializer.SerializeToUtf8Bytes(index));
        }
        catch
        {
        }
    }

    /// <summary>
    /// Forgets every step before <paramref name="number"/>, and the meshes only they used. Rewrites
    /// most of the file, since what goes is at the front of it.
    /// </summary>
    public static void ForgetBefore(string path, int number)
    {
        IndexDto? index;
        using (var read = ZipFile.OpenRead(path)) index = ReadIndex(read);
        if (index?.Steps is not { } steps) return;

        var gone = steps.Where(s => s.N < number).ToList();
        if (gone.Count == 0) return;

        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        Forget(zip, index, gone);
        zip.GetEntry(IndexPath)?.Delete();
        Put(zip, IndexPath, JsonSerializer.SerializeToUtf8Bytes(index));
    }

    // --- Plumbing ---------------------------------------------------------------------

    /// <summary>
    /// A mesh's key: a hash of its content, so the same mesh is stored once however many steps and
    /// objects use it. Worked out once per mesh - a scene of fifty parts would otherwise hash all
    /// fifty at every step.
    /// </summary>
    private string Key(Mesh mesh, List<string> used)
    {
        if (!keys.TryGetValue(mesh, out var key))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> counts = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(counts, mesh.Positions.Count);
            BinaryPrimitives.WriteInt32LittleEndian(counts[4..], mesh.Indices.Count);
            hash.AppendData(counts);
            hash.AppendData(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(mesh.Positions)));
            hash.AppendData(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(mesh.Indices)));
            key = Convert.ToHexString(hash.GetHashAndReset(), 0, 16).ToLowerInvariant();
            keys.AddOrUpdate(mesh, key);
        }

        meshes[key] = mesh;
        used.Add(key);
        return key;
    }

    private void Store(ZipArchive zip, Dictionary<string, long> stored, string key)
    {
        if (stored.ContainsKey(key) || !meshes.TryGetValue(key, out var mesh)) return;

        byte[] data = Encode(mesh);
        Put(zip, MeshesFolder + key, data);
        stored[key] = data.Length;
    }

    /// <summary>Counts, then the positions and the indices as they lie in memory.</summary>
    private static byte[] Encode(Mesh mesh)
    {
        var positions = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(mesh.Positions));
        var indices = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(mesh.Indices));
        var data = new byte[8 + positions.Length + indices.Length];
        BinaryPrimitives.WriteInt32LittleEndian(data, mesh.Positions.Count);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), mesh.Indices.Count);
        positions.CopyTo(data.AsSpan(8));
        indices.CopyTo(data.AsSpan(8 + positions.Length));
        return data;
    }

    private static Mesh Decode(byte[] data)
    {
        int vertices = BinaryPrimitives.ReadInt32LittleEndian(data);
        int indices = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        var positions = MemoryMarshal.Cast<byte, System.Numerics.Vector3>(data.AsSpan(8, vertices * 12));
        var triangles = MemoryMarshal.Cast<byte, int>(data.AsSpan(8 + vertices * 12, indices * 4));
        return new Mesh(positions.ToArray(), triangles.ToArray());
    }

    private static List<SceneObject> Load(ZipArchive zip, string entry, out ProjectSettings? settings)
    {
        var part = zip.GetEntry(entry) ?? throw new InvalidDataException("That step is no longer in the project file.");

        // A fresh mesh for every object, even two naming the same one: nothing expects to share one.
        var read = new Dictionary<string, byte[]>();
        Mesh Fetch(string key)
        {
            if (!read.TryGetValue(key, out var data))
            {
                var stored = zip.GetEntry(MeshesFolder + key) ?? throw new InvalidDataException("A mesh this step uses is missing from the project file.");
                read[key] = data = Bytes(stored);
            }
            return Decode(data);
        }

        using var stream = part.Open();
        return SceneSerializer.ReadStep(stream, Fetch, out settings);
    }

    private static byte[] Bytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream((int)entry.Length);
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string? LastHash(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadIndex(zip)?.Steps is { Count: > 0 } steps ? steps[^1].Hash : null;
        }
        catch
        {
            return null;
        }
    }

    private static string StepPath(int number) => $"{StepsFolder}{number:D6}.json";

    /// <summary>
    /// Fastest, not the best: this is written on the UI thread while the user works, and a mesh is
    /// mostly floats, which squeeze little better for the extra time.
    /// </summary>
    private static void Put(ZipArchive zip, string name, byte[] data)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        stream.Write(data);
    }

    private static IndexDto? ReadIndex(ZipArchive zip)
    {
        if (zip.GetEntry(IndexPath) is not { } entry) return null;
        using var stream = entry.Open();
        var index = JsonSerializer.Deserialize<IndexDto>(stream);
        return index is { Format: <= 1 } ? index : null;
    }

    /// <summary>
    /// Lets the oldest steps go once the session is past its count or its size, down to nine tenths
    /// of both. The size counts the steps as well as the meshes: a plate of two thousand bricks is a
    /// large step even when it is only a move.
    /// </summary>
    private static void Trim(ZipArchive zip, IndexDto index, Dictionary<string, long> stored)
    {
        if (index.Steps is not { } steps) return;

        long total = stored.Values.Sum() + steps.Sum(s => (long)s.Bytes);
        if (steps.Count <= MaxSteps && total <= MaxBytes) return;

        var uses = Uses(index);
        int drop = 0;
        while (drop < steps.Count - 1 && (steps.Count - drop > MaxSteps * 9 / 10 || total > MaxBytes * 9 / 10))
        {
            total -= steps[drop].Bytes;
            foreach (string key in steps[drop].Meshes ?? [])
                if (uses.TryGetValue(key, out int n) && (uses[key] = n - 1) == 0)
                    total -= stored.GetValueOrDefault(key);
            drop++;
        }

        Forget(zip, index, steps.GetRange(0, drop));
    }

    /// <summary>Takes steps out, and every mesh nothing left uses.</summary>
    private static void Forget(ZipArchive zip, IndexDto index, List<StepDto> gone)
    {
        var steps = index.Steps!;
        foreach (var step in gone)
        {
            zip.GetEntry(StepPath(step.N))?.Delete();
            steps.Remove(step);
        }

        var uses = Uses(index);
        foreach (var entry in zip.Entries.ToList())
            if (entry.FullName.StartsWith(MeshesFolder, StringComparison.Ordinal) && !uses.ContainsKey(entry.FullName[MeshesFolder.Length..]))
                entry.Delete();
    }

    /// <summary>How many steps - and the unsaved work - use each mesh.</summary>
    private static Dictionary<string, int> Uses(IndexDto index)
    {
        var uses = new Dictionary<string, int>();
        foreach (var keys in (index.Steps ?? []).Select(s => s.Meshes).Append(index.Unsaved?.Meshes))
            foreach (string key in keys ?? [])
                uses[key] = uses.GetValueOrDefault(key) + 1;
        return uses;
    }

    private sealed class IndexDto
    {
        public int Format { get; set; }

        /// <summary>The number the next step gets. Numbers are never given out again, so a step forgotten leaves a gap.</summary>
        public int Next { get; set; }

        public List<StepDto>? Steps { get; set; }
        public LeftoverDto? Unsaved { get; set; }
        public ViewState? View { get; set; }
    }

    private sealed class StepDto
    {
        public int N { get; set; }
        public string? Label { get; set; }
        public DateTime Utc { get; set; }
        public int Objects { get; set; }
        public string[]? Meshes { get; set; }

        /// <summary>Of the step as written, to tell a step that changed nothing from one that did.</summary>
        public string? Hash { get; set; }

        /// <summary>How big the step is, unpacked, for the cap on the session's size.</summary>
        public int Bytes { get; set; }
    }

    private sealed class LeftoverDto
    {
        public DateTime Utc { get; set; }
        public int Objects { get; set; }
        public string[]? Meshes { get; set; }
        public int ProcessId { get; set; }
        public long Started { get; set; }
    }
}
