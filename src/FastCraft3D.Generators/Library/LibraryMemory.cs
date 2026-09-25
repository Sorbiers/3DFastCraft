using System.IO;
using System.Numerics;
using System.Text.Json;
using FastCraft3D.Geometry;
using FastCraft3D.Io;

namespace FastCraft3D.Generators;

/// <summary>
/// What the library remembers of the person using it: the generators they starred, the ones they
/// used last, and the settings they saved under a name of their own. Kept in one small file of
/// the library's own, as the printer is, so the app does not grow a field for each.
/// </summary>
public sealed class LibraryMemory
{
    /// <summary>How many recently used generators are listed.</summary>
    public const int RecentKept = 6;

    private sealed class Stored
    {
        public List<string> Favourites { get; set; } = [];
        public List<string> Recent { get; set; } = [];

        /// <summary>Generator id, then preset name, then its settings as a recipe writes them.</summary>
        public Dictionary<string, Dictionary<string, string>> Presets { get; set; } = [];
    }

    private readonly string? path;
    private readonly Stored stored;

    private LibraryMemory(string? path, Stored stored)
    {
        this.path = path;
        this.stored = stored;
    }

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3DFastCraft", "library.json");

    private static readonly Lazy<LibraryMemory> shared = new(() => Load());

    /// <summary>The one the app uses, read once.</summary>
    public static LibraryMemory Shared => shared.Value;

    /// <summary>Remembers nothing beyond itself: for tests, and for a panel opened without the app.</summary>
    public static LibraryMemory Temporary() => new(null, new Stored());

    public static LibraryMemory Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) is { } stored)
                return new LibraryMemory(path, stored);
        }
        catch
        {
            // A damaged file is not worth a word; starting with nothing remembered is fine.
        }

        return new LibraryMemory(path, new Stored());
    }

    public IReadOnlyList<string> Favourites => stored.Favourites;

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<string> Recent => stored.Recent;

    public bool IsFavourite(string id) => stored.Favourites.Contains(id);

    public void SetFavourite(string id, bool favourite)
    {
        stored.Favourites.Remove(id);
        if (favourite) stored.Favourites.Add(id);
        Save();
    }

    public void Used(string id)
    {
        stored.Recent.Remove(id);
        stored.Recent.Insert(0, id);
        if (stored.Recent.Count > RecentKept) stored.Recent.RemoveRange(RecentKept, stored.Recent.Count - RecentKept);
        Save();
    }

    public IReadOnlyList<string> PresetNames(string id) =>
        stored.Presets.TryGetValue(id, out var mine) ? mine.Keys.OrderBy(n => n, StringComparer.CurrentCulture).ToList() : [];

    /// <summary>A saved preset's settings, read as forgivingly as a recipe is.</summary>
    public object? Preset(Generator generator, string name) =>
        stored.Presets.TryGetValue(generator.Id, out var mine) && mine.TryGetValue(name, out var json)
            ? Recipes.Read(generator, json)
            : null;

    public void SavePreset(Generator generator, string name, object settings)
    {
        if (!stored.Presets.TryGetValue(generator.Id, out var mine)) stored.Presets[generator.Id] = mine = [];
        mine[name] = Recipes.Write(generator, settings);
        Save();
    }

    public void ForgetPreset(string id, string name)
    {
        if (stored.Presets.TryGetValue(id, out var mine) && mine.Remove(name) && mine.Count == 0) stored.Presets.Remove(id);
        Save();
    }

    private void Save()
    {
        if (path is null) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(stored));
        }
        catch
        {
            // Read-only or roaming profiles must not get in the way of the work itself.
        }
    }
}

/// <summary>
/// The catalogue's pictures: each generator's defaults, drawn once and kept on disk by its Id and
/// Version, so they are made the first time the catalogue is opened and read after that. A new
/// Version draws a new picture, since the same settings may now make something else.
/// </summary>
public sealed class LibraryPictures(string folder)
{
    /// <summary>The colours the parts of a set are drawn in, the app's first automatic colours.</summary>
    private static readonly Vector3[] Colours =
    [
        new(0.30f, 0.55f, 0.85f), new(0.93f, 0.55f, 0.24f), new(0.40f, 0.72f, 0.45f), new(0.80f, 0.36f, 0.42f)
    ];

    public const int Size = 176;

    public static LibraryPictures Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3DFastCraft", "Library"));

    public string PathOf(Generator generator) => Path.Combine(folder, $"{generator.Id}.v{generator.Version}.png");

    /// <summary>The picture as a PNG, drawn now if it has not been before; null when nothing could be drawn.</summary>
    public byte[]? Get(Generator generator)
    {
        string file = PathOf(generator);

        try
        {
            if (File.Exists(file)) return File.ReadAllBytes(file);
        }
        catch (IOException)
        {
            // Drawn again below.
        }

        byte[]? png;
        try
        {
            var made = generator.Make(generator.Defaults(Printer.Default), Printer.Default);
            png = MeshThumbnail.Png(made.Parts
                .Select((p, i) => (p.Assembled is { } m ? MeshTransform.Transformed(p.Mesh, m) : p.Mesh, Colours[i % Colours.Length]))
                .ToList(), Size);
        }
        catch (Exception)
        {
            // A generator that cannot draw itself is still listed, without a picture.
            return null;
        }

        if (png is null) return null;

        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(file, png);
        }
        catch (IOException)
        {
            // Drawn again next time rather than not shown now.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return png;
    }
}
