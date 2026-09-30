using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FastCraft3D.Geometry;

namespace FastCraft3D.Generators;

/// <summary>
/// A generator's settings written into a <see cref="Recipe"/> and read back out of one.
///
/// By name, not by position, and forgiving on the way in: a setting the file has no value for
/// takes its default, and one the generator no longer has is ignored. That is what lets a
/// generator grow a setting without every part made before it becoming unreadable.
/// </summary>
public static class Recipes
{
    public static Recipe For(Generator generator, object settings, string? role = null, string? set = null) =>
        new(generator.Id, generator.Version, Write(generator, settings), role, set, Vector3.Zero);

    public static string Write(Generator generator, object settings)
    {
        var values = generator.Shape.Values(settings);
        var json = new JsonObject();

        for (int i = 0; i < values.Length; i++)
        {
            var p = generator.Parameters[i];
            json[p.Name] = p.Kind switch
            {
                ParameterKind.Choice => JsonValue.Create(values[i].ToString()),
                ParameterKind.Toggle => JsonValue.Create((bool)values[i]),
                ParameterKind.Count => JsonValue.Create((int)values[i]),
                _ => JsonValue.Create((float)values[i])
            };
        }

        return json.ToJsonString();
    }

    /// <summary>The settings a recipe holds, with anything missing or unreadable at its default.</summary>
    public static object Read(Generator generator, string settings)
    {
        var values = generator.Shape.Values(generator.Defaults());

        JsonObject? json;
        try
        {
            json = JsonNode.Parse(settings) as JsonObject;
        }
        catch (JsonException)
        {
            json = null;
        }

        if (json is null) return generator.Shape.From(values);

        for (int i = 0; i < values.Length; i++)
        {
            var p = generator.Parameters[i];
            if (json[p.Name] is not JsonValue value) continue;

            try
            {
                values[i] = p.Kind switch
                {
                    ParameterKind.Choice => Enum.TryParse(p.Type, value.GetValue<string>(), out var chosen) && Enum.IsDefined(p.Type, chosen!) ? chosen! : values[i],
                    ParameterKind.Toggle => value.GetValue<bool>(),
                    ParameterKind.Count => (int)value.GetValue<double>(),
                    _ => (float)value.GetValue<double>()
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                // A value of the wrong kind - a word where a number was - keeps the default.
            }
        }

        return generator.Shape.Sane(generator.Shape.From(values));
    }
}

/// <summary>The printers kept, by name, and the one in use.</summary>
public sealed record PrinterProfiles(IReadOnlyList<Printer> All, string Current)
{
    public Printer InUse => All.FirstOrDefault(p => p.Name == Current) ?? All.FirstOrDefault() ?? Printer.Default;

    /// <summary>These with <paramref name="printer"/> in place of the one of the same name, or added, and in use.</summary>
    public PrinterProfiles With(Printer printer)
    {
        printer = printer.Saned();
        var all = All.Where(p => p.Name != printer.Name).Append(printer).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        return new PrinterProfiles(all, printer.Name);
    }

    public PrinterProfiles Without(string name)
    {
        var all = All.Where(p => p.Name != name).ToList();
        if (all.Count == 0) all.Add(Printer.Default);
        return new PrinterProfiles(all, all.Any(p => p.Name == Current) ? Current : all[0].Name);
    }
}

/// <summary>
/// The printers, remembered between sessions in a file of their own beside the app's other
/// settings. Several, by name, because the fits change with the filament as much as the machine:
/// PLA and PETG on one printer want different clearances.
/// </summary>
public static class PrinterProfile
{
    private sealed record Stored(float Nozzle, float Layer, float XyClearance)
    {
        public string? Name { get; init; }
        public float? HoleClearance { get; init; }
        public float? PressFit { get; init; }
        public float? BrickFit { get; init; }
        public float? ThreadClearance { get; init; }

        public Printer Printer() => new Printer(Nozzle, Layer, XyClearance)
        {
            Name = Name ?? Generators.Printer.Default.Name,
            HoleClearance = HoleClearance ?? Generators.Printer.Default.HoleClearance,
            PressFit = PressFit ?? Generators.Printer.Default.PressFit,
            BrickFit = BrickFit ?? Generators.Printer.Default.BrickFit,
            ThreadClearance = ThreadClearance ?? Generators.Printer.Default.ThreadClearance
        }.Saned();

        public static Stored Of(Printer p) => new(p.Nozzle, p.Layer, p.XyClearance)
        {
            Name = p.Name, HoleClearance = p.HoleClearance, PressFit = p.PressFit, BrickFit = p.BrickFit, ThreadClearance = p.ThreadClearance
        };
    }

    private sealed record StoredProfiles(List<Stored> Printers, string Current);

    private static string Folder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3DFastCraft");

    /// <summary>The file the profiles are kept in; before there were several, the one printer was kept in printer.json.</summary>
    private static string DefaultPath() => Path.Combine(Folder(), "printers.json");

    private static string OldPath(string path) => Path.Combine(Path.GetDirectoryName(path)!, "printer.json");

    /// <summary>The printer in use.</summary>
    public static Printer Load(string? path = null) => LoadAll(path).InUse;

    public static PrinterProfiles LoadAll(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<StoredProfiles>(File.ReadAllText(path)) is { Printers.Count: > 0 } stored)
            {
                var all = stored.Printers.Select(p => p.Printer()).GroupBy(p => p.Name).Select(g => g.First()).ToList();
                return new PrinterProfiles(all, stored.Current);
            }

            // The one printer kept before there were profiles becomes the first of them.
            if (File.Exists(OldPath(path)) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(OldPath(path))) is { } old)
                return new PrinterProfiles([old.Printer()], old.Printer().Name);
        }
        catch
        {
            // A damaged file is not worth a word; the defaults are a fine place to start.
        }

        return new PrinterProfiles([Printer.Default], Printer.Default.Name);
    }

    /// <summary>Keeps the printer as the one in use, in place of its namesake.</summary>
    public static void Save(Printer printer, string? path = null) => SaveAll(LoadAll(path).With(printer), path);

    public static void SaveAll(PrinterProfiles profiles, string? path = null)
    {
        try
        {
            path ??= DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var stored = new StoredProfiles(profiles.All.Select(Stored.Of).ToList(), profiles.Current);
            File.WriteAllText(path, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Read-only or roaming profiles must not get in the way of the work itself.
        }
    }

    /// <summary>"0.4 mm nozzle, 0.2 mm layers, 0.2 mm clearance".</summary>
    public static string Describe(Printer printer) => string.Format(CultureInfo.CurrentCulture,
        "{0:0.##} mm nozzle, {1:0.##} mm layers, {2:0.##} mm clearance", printer.Nozzle, printer.Layer, printer.XyClearance);
}
