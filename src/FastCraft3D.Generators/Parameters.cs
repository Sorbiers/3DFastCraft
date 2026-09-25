using System.Globalization;
using System.Reflection;
using System.Text;

namespace FastCraft3D.Generators;

/// <summary>What sort of value a setting is, which decides how it is shown and checked.</summary>
public enum ParameterKind
{
    /// <summary>A distance in millimetres, shown in the unit the transform boxes use.</summary>
    Length,

    /// <summary>A length that is a wall's thickness. Its default is to come from the printer profile.</summary>
    Wall,

    /// <summary>A length that is a gap for a fit. Its default is to come from the printer profile.</summary>
    Clearance,

    /// <summary>Degrees.</summary>
    Angle,

    /// <summary>Any other number, with a unit of its own or none.</summary>
    Number,

    /// <summary>A whole number.</summary>
    Count,

    /// <summary>One of an enum's values.</summary>
    Choice,

    /// <summary>On or off.</summary>
    Toggle
}

/// <summary>
/// How a generator's setting is shown: what it is called, what the tooltip says, and which heading
/// it goes under. Every parameter of a settings record carries exactly one.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public abstract class FieldAttribute(string label) : Attribute
{
    public string Label { get; } = label;

    /// <summary>The tooltip on the label.</summary>
    public string? Hint { get; set; }

    /// <summary>The heading the field goes under. Fields with none go at the top.</summary>
    public string? Group { get; set; }

    internal abstract ParameterKind Kind { get; }
    internal virtual double Min => 0;
    internal virtual double Max => 0;
    internal virtual string? Unit => null;
}

public class LengthAttribute(string label, double min, double max) : FieldAttribute(label)
{
    internal override ParameterKind Kind => ParameterKind.Length;
    internal override double Min => min;
    internal override double Max => max;
    internal override string? Unit => "mm";
}

public sealed class WallAttribute(string label, double min, double max) : LengthAttribute(label, min, max)
{
    internal override ParameterKind Kind => ParameterKind.Wall;
}

public sealed class ClearanceAttribute(string label, double min, double max) : LengthAttribute(label, min, max)
{
    internal override ParameterKind Kind => ParameterKind.Clearance;
}

public sealed class AngleAttribute(string label, double min, double max) : FieldAttribute(label)
{
    internal override ParameterKind Kind => ParameterKind.Angle;
    internal override double Min => min;
    internal override double Max => max;
    internal override string? Unit => "degrees";
}

public sealed class NumberAttribute(string label, double min, double max) : FieldAttribute(label)
{
    /// <summary>Said after the box, as "mm" is after a length.</summary>
    public string? UnitText { get; set; }

    internal override ParameterKind Kind => ParameterKind.Number;
    internal override double Min => min;
    internal override double Max => max;
    internal override string? Unit => UnitText;
}

public sealed class CountAttribute(string label, int min, int max) : FieldAttribute(label)
{
    /// <summary>Said after the box: "slots", "teeth".</summary>
    public string? UnitText { get; set; }

    internal override ParameterKind Kind => ParameterKind.Count;
    internal override double Min => min;
    internal override double Max => max;
    internal override string? Unit => UnitText;
}

public sealed class ChoiceAttribute(string label) : FieldAttribute(label)
{
    internal override ParameterKind Kind => ParameterKind.Choice;
}

public sealed class ToggleAttribute(string label) : FieldAttribute(label)
{
    internal override ParameterKind Kind => ParameterKind.Toggle;
}

/// <summary>Shows the field only while another setting has one of the values given.</summary>
/// <param name="parameter">The other setting's name - use nameof.</param>
/// <param name="values">The values that make this field matter.</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ShowWhenAttribute(string parameter, params object[] values) : Attribute
{
    public string Parameter { get; } = parameter;
    public IReadOnlyList<object> Values { get; } = values;
}

/// <summary>What an enum value is called in a list, where its own name will not do.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ShownAsAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>One setting of a generator, read off its settings record.</summary>
/// <param name="Name">The record's own name for it.</param>
/// <param name="Min">The least it may be; unused for a choice or a toggle.</param>
/// <param name="Max">The most it may be.</param>
/// <param name="ShownWhen">The setting that decides whether this one is shown, if any.</param>
/// <param name="ShownFor">The values of that setting it is shown for.</param>
public sealed record GeneratorParameter(
    string Name,
    Type Type,
    ParameterKind Kind,
    string Label,
    string? Hint,
    string? Group,
    double Min,
    double Max,
    string? Unit,
    object Default,
    string? ShownWhen,
    IReadOnlyList<object> ShownFor)
{
    public bool IsNumber => Kind is not (ParameterKind.Choice or ParameterKind.Toggle);

    public bool IsLength => Kind is ParameterKind.Length or ParameterKind.Wall or ParameterKind.Clearance;

    /// <summary>For a choice, each value with what it is called.</summary>
    public IReadOnlyList<(object Value, string Label)> Choices =>
        Kind != ParameterKind.Choice ? []
        : Enum.GetValues(Type).Cast<object>()
            .Select(v => (v, Type.GetField(v.ToString()!)?.GetCustomAttribute<ShownAsAttribute>()?.Text ?? Words(v.ToString()!)))
            .ToList();

    /// <summary>The value brought inside its range, and to a whole number for a count.</summary>
    public object Clamp(object value) => Kind switch
    {
        ParameterKind.Count => (int)Math.Clamp(Convert.ToDouble(value, CultureInfo.InvariantCulture), Min, Max),
        ParameterKind.Choice or ParameterKind.Toggle => value,
        _ => (float)Math.Clamp(Convert.ToDouble(value, CultureInfo.InvariantCulture), Min, Max)
    };

    public bool InRange(double value) => value >= Min && value <= Max;

    /// <summary>Whether its default comes from the printer rather than from the generator.</summary>
    public bool FromPrinter => Kind is ParameterKind.Wall or ParameterKind.Clearance;

    /// <summary>
    /// The default for this printer. A wall is rounded up to whole lines, since a slicer lays a
    /// wall of two and a half lines as two and a gap; a clearance is the printer's own gap.
    /// </summary>
    public object DefaultFor(Printer printer) => Kind switch
    {
        ParameterKind.Clearance => Clamp(printer.XyClearance),
        ParameterKind.Wall => Clamp(MathF.Ceiling((float)Default / printer.Nozzle - 1e-3f) * printer.Nozzle),
        _ => Default
    };

    /// <summary>"SlidingLid" read as "Sliding lid".</summary>
    internal static string Words(string name)
    {
        var text = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c)) text.Append(' ').Append(char.ToLowerInvariant(c));
            else text.Append(c);
        }

        return text.ToString();
    }
}

/// <summary>
/// A settings record read as a list of parameters, and built again from values.
///
/// The record's primary constructor is the description: its parameters carry the attributes and
/// the defaults, and the properties it makes are where the values are read back. Calling that
/// constructor is the only way a changed value goes in, because a record's <c>with</c> is not
/// something reflection can reach. It also means a settings object is always one the record
/// itself could have made.
/// </summary>
public sealed class SettingsShape
{
    private readonly ConstructorInfo constructor;
    private readonly PropertyInfo[] properties;

    public Type Type { get; }
    public IReadOnlyList<GeneratorParameter> Parameters { get; }

    private SettingsShape(Type type, ConstructorInfo constructor, PropertyInfo[] properties, List<GeneratorParameter> parameters)
    {
        Type = type;
        this.constructor = constructor;
        this.properties = properties;
        Parameters = parameters;
    }

    /// <summary>
    /// Reads a settings record, or says exactly what is wrong with it. A mistake here is one a
    /// generator's author made, so it throws rather than being shown to anyone printing.
    /// </summary>
    public static SettingsShape Of(Type type)
    {
        if (type.IsValueType)
            throw new InvalidOperationException(
                $"{type.Name} is a struct. Settings must be a record class: new() on a record struct skips the defaults.");

        // The primary constructor: the one whose every parameter is also a property. A record's
        // copy constructor takes the record itself and is not it.
        var constructor = type.GetConstructors()
            .Where(c => c.GetParameters().Length > 0
                        && c.GetParameters().All(p => type.GetProperty(p.Name!)?.PropertyType == p.ParameterType))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{type.Name} has no primary constructor whose parameters are its properties.");

        var parameters = new List<GeneratorParameter>();
        var properties = new List<PropertyInfo>();

        foreach (var p in constructor.GetParameters())
        {
            string where = $"{type.Name}.{p.Name}";
            var field = p.GetCustomAttribute<FieldAttribute>()
                ?? throw new InvalidOperationException($"{where} says nothing about how it is shown - give it a Length, Count, Choice or the like.");

            if (!p.HasDefaultValue)
                throw new InvalidOperationException($"{where} has no default.");

            bool fits = field.Kind switch
            {
                ParameterKind.Count => p.ParameterType == typeof(int),
                ParameterKind.Choice => p.ParameterType.IsEnum,
                ParameterKind.Toggle => p.ParameterType == typeof(bool),
                _ => p.ParameterType == typeof(float)
            };

            if (!fits)
                throw new InvalidOperationException($"{where} is a {p.ParameterType.Name}, which a {field.Kind} cannot be.");

            // An enum's default comes back from reflection as its underlying number.
            object value = p.ParameterType.IsEnum
                ? Enum.ToObject(p.ParameterType, p.DefaultValue!)
                : Convert.ChangeType(p.DefaultValue!, p.ParameterType, CultureInfo.InvariantCulture);

            var show = p.GetCustomAttribute<ShowWhenAttribute>();

            var parameter = new GeneratorParameter(
                p.Name!, p.ParameterType, field.Kind, field.Label, field.Hint, field.Group,
                field.Min, field.Max, field.Unit, value, show?.Parameter, show?.Values ?? []);

            if (parameter.IsNumber && !parameter.InRange(Convert.ToDouble(value, CultureInfo.InvariantCulture)))
                throw new InvalidOperationException($"{where} defaults to {value}, outside {field.Min} to {field.Max}.");

            if (parameter.IsNumber && field.Min > field.Max)
                throw new InvalidOperationException($"{where} runs from {field.Min} down to {field.Max}.");

            parameters.Add(parameter);
            properties.Add(type.GetProperty(p.Name!)!);
        }

        foreach (var p in parameters.Where(p => p.ShownWhen is not null))
            if (parameters.All(q => q.Name != p.ShownWhen))
                throw new InvalidOperationException($"{type.Name}.{p.Name} is shown when {p.ShownWhen} is set, and there is no {p.ShownWhen}.");

        return new SettingsShape(type, constructor, properties.ToArray(), parameters);
    }

    public object Defaults() => constructor.Invoke(Parameters.Select(p => p.Default).ToArray());

    public object Read(object settings, string name) => properties[IndexOf(name)].GetValue(settings)!;

    public object[] Values(object settings) => properties.Select(p => p.GetValue(settings)!).ToArray();

    public object With(object settings, string name, object value)
    {
        var values = Values(settings);
        values[IndexOf(name)] = value;
        return constructor.Invoke(values);
    }

    public object From(object[] values) => constructor.Invoke(values);

    /// <summary>The same settings with every number brought inside its range.</summary>
    public object Sane(object settings)
    {
        var values = Values(settings);
        for (int i = 0; i < values.Length; i++) values[i] = Parameters[i].Clamp(values[i]);
        return constructor.Invoke(values);
    }

    /// <summary>Whether a field is shown with these settings, going by its ShowWhen.</summary>
    public bool Shows(object settings, GeneratorParameter parameter) =>
        parameter.ShownWhen is not { } other || parameter.ShownFor.Contains(Read(settings, other));

    private int IndexOf(string name)
    {
        for (int i = 0; i < Parameters.Count; i++)
            if (Parameters[i].Name == name) return i;

        throw new ArgumentException($"{Type.Name} has no setting called {name}.", nameof(name));
    }
}
