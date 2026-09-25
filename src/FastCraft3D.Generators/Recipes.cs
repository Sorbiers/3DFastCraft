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

/// <summary>
/// The printer, as last set, remembered between sessions in a file of its own beside the app's
/// other settings. Kept by the generator library rather than the app, since nothing else asks.
/// </summary>
public static class PrinterProfile
{
    private sealed record Stored(float Nozzle, float Layer, float XyClearance);

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "3DFastCraft", "printer.json");

    public static Printer Load(string? path = null)
    {
        try
        {
            path ??= DefaultPath();
            if (!File.Exists(path)) return Printer.Default;

            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            return stored is null ? Printer.Default : Printer.Sane(stored.Nozzle, stored.Layer, stored.XyClearance);
        }
        catch
        {
            // A damaged file is not worth a word; the defaults are a fine place to start.
            return Printer.Default;
        }
    }

    public static void Save(Printer printer, string? path = null)
    {
        try
        {
            path ??= DefaultPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Stored(printer.Nozzle, printer.Layer, printer.XyClearance)));
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
