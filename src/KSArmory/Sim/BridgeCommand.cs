using System.Globalization;
using System.Text.Json;

namespace KSArmory;

/// <summary>
/// One command dropped into the bridge's folder, read. Text in, so every refusal is testable here;
/// <c>Ksa/Bridge.cs</c> runs what this reads.
///
/// <para>A JSON object with an <c>id</c> the reply is filed under, a <c>cmd</c>, and the command's
/// own arguments beside them.</para>
/// </summary>
public sealed class BridgeCommand
{
    /// <summary>What the reply is filed under.</summary>
    public required string Id { get; init; }

    /// <summary>The command, lower case.</summary>
    public required string Name { get; init; }

    private readonly Dictionary<string, JsonElement> _args;

    private BridgeCommand(Dictionary<string, JsonElement> args) => _args = args;

    /// <summary>
    /// Reads a command, or says why it cannot be. An id is required, because a reply nobody can
    /// find is the same as no reply.
    /// </summary>
    public static bool TryParse(string text, out BridgeCommand? command, out string trouble)
    {
        command = null;
        trouble = string.Empty;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                trouble = "a command is a JSON object";
                return false;
            }

            Dictionary<string, JsonElement> args = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                args[property.Name] = property.Value.Clone();
            }

            string id = Text(args, "id");
            string name = Text(args, "cmd").ToLowerInvariant();

            if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                trouble = "a command needs an id that can be a file name";
                return false;
            }

            if (name.Length == 0)
            {
                trouble = "a command needs a cmd";
                return false;
            }

            command = new BridgeCommand(args) { Id = id, Name = name };
            return true;
        }
        catch (JsonException e)
        {
            trouble = $"not JSON: {e.Message}";
            return false;
        }
    }

    /// <summary>Whether an argument was given at all.</summary>
    public bool Has(string name) => _args.ContainsKey(name);

    /// <summary>A number argument, or <paramref name="fallback"/> when absent or not a number.</summary>
    public double Number(string name, double fallback)
    {
        if (!_args.TryGetValue(name, out JsonElement value)) return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float,
                                                      CultureInfo.InvariantCulture, out double d) => d,
            _ => fallback,
        };
    }

    /// <summary>A text argument, or empty.</summary>
    public string String(string name) => Text(_args, name);

    /// <summary>A true-or-false argument, or <paramref name="fallback"/>.</summary>
    public bool Flag(string name, bool fallback)
    {
        if (!_args.TryGetValue(name, out JsonElement value)) return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0.0,
            JsonValueKind.String => bool.TryParse(value.GetString(), out bool b) ? b : fallback,
            _ => fallback,
        };
    }

    /// <summary>An argument as it arrived, for a command that sets a field of whatever type.</summary>
    public bool TryRaw(string name, out JsonElement value) => _args.TryGetValue(name, out value);

    private static string Text(Dictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out JsonElement value)) return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Sets a public field on <paramref name="target"/> by name from an argument, for the settings
    /// the panel would set. Only plain fields of the types a setting has; anything else is refused
    /// with a reason rather than guessed at.
    /// </summary>
    public static bool TrySetField(object target, string field, JsonElement value, out string trouble)
    {
        trouble = string.Empty;

        System.Reflection.FieldInfo? info = target.GetType().GetField(
            field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                   | System.Reflection.BindingFlags.IgnoreCase);

        if (info is null || info.IsInitOnly)
        {
            trouble = $"no settable field '{field}' on {target.GetType().Name}";
            return false;
        }

        try
        {
            Type type = info.FieldType;
            object? converted =
                type == typeof(bool) ? value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => value.GetDouble() != 0.0,
                    _ => bool.Parse(value.GetString() ?? string.Empty),
                }
                : type == typeof(float) ? (float)ToDouble(value)
                : type == typeof(double) ? ToDouble(value)
                : type == typeof(int) ? (int)Math.Round(ToDouble(value))
                : type == typeof(string) ? value.GetString()
                : type.IsEnum ? Enum.Parse(type, value.GetString() ?? string.Empty, ignoreCase: true)
                : null;

            if (converted is null)
            {
                trouble = $"'{field}' is a {type.Name}, which cannot be set from here";
                return false;
            }

            info.SetValue(target, converted);
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
        {
            trouble = $"'{field}' could not take {value.GetRawText()}: {e.Message}";
            return false;
        }
    }

    /// <summary>A public field's value as text, or null when there is no such field.</summary>
    public static string? FieldText(object target, string field)
    {
        System.Reflection.FieldInfo? info = target.GetType().GetField(
            field, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                   | System.Reflection.BindingFlags.IgnoreCase);

        return info is null
                   ? null
                   : Convert.ToString(info.GetValue(target), CultureInfo.InvariantCulture);
    }

    private static double ToDouble(JsonElement value)
        => value.ValueKind == JsonValueKind.Number
               ? value.GetDouble()
               : double.Parse(value.GetString() ?? string.Empty, CultureInfo.InvariantCulture);
}
