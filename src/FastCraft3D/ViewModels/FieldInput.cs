using System.Globalization;

namespace FastCraft3D.ViewModels;

/// <summary>
/// What a transform box was given: a value to set, or an amount to change by.
///
/// "+=5" and "-=5" change by five, and "+5" does too, since nobody types a plus sign to mean a
/// positive number. A plain number sets the value, negative ones included - that is the one
/// form it cannot take, because positions below zero are normal on a plate centred on the origin.
/// </summary>
public static class FieldInput
{
    public static bool TryParseRelative(string? text, out float delta)
    {
        delta = 0f;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string t = text.Trim();
        float sign;
        string rest;

        if (t.StartsWith("+=", StringComparison.Ordinal)) { sign = 1f; rest = t[2..]; }
        else if (t.StartsWith("-=", StringComparison.Ordinal)) { sign = -1f; rest = t[2..]; }
        else if (t.StartsWith('+')) { sign = 1f; rest = t[1..]; }
        else return false;

        rest = rest.Trim();
        if (!float.TryParse(rest, NumberStyles.Float, CultureInfo.CurrentCulture, out float amount)
            && !float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
            return false;

        if (!float.IsFinite(amount)) return false;

        delta = sign * amount;
        return true;
    }

    /// <summary>
    /// "*=1.5", "*1.5", "/=2" or "/2": a factor to multiply by, and "*=150%" is the same as 1.5.
    /// </summary>
    public static bool TryParseFactor(string? text, out float factor)
    {
        factor = 1f;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string t = text.Trim();
        bool divide;
        string rest;

        if (t.StartsWith("*=", StringComparison.Ordinal) || t.StartsWith("x=", StringComparison.OrdinalIgnoreCase)) { divide = false; rest = t[2..]; }
        else if (t.StartsWith("/=", StringComparison.Ordinal)) { divide = true; rest = t[2..]; }
        else if (t.StartsWith('*')) { divide = false; rest = t[1..]; }
        else if (t.StartsWith('/')) { divide = true; rest = t[1..]; }
        else return false;

        rest = rest.Trim();
        bool percent = rest.EndsWith('%');
        if (percent) rest = rest[..^1].Trim();

        if (!float.TryParse(rest, NumberStyles.Float, CultureInfo.CurrentCulture, out float amount)
            && !float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
            return false;

        if (percent) amount /= 100f;
        if (!float.IsFinite(amount) || amount <= 0f) return false;

        factor = divide ? 1f / amount : amount;
        return true;
    }
}
