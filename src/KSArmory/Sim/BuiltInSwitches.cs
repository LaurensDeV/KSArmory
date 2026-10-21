using System.Text.Json;

namespace KSArmory;

/// <summary>
/// Which of KSArmory's own weapon families the player switched off, as the file it is kept in. Text in,
/// text out: the game reads it before any save is open, because the catalogue is built then and frozen.
/// </summary>
public static class BuiltInSwitches
{
    private sealed record Stored(string[]? Disabled);

    /// <summary>The families switched off. Anything unreadable is nothing switched off.</summary>
    public static HashSet<string> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<Stored>(json)?.Disabled is { } names
                       ? [.. names.Where(n => !string.IsNullOrWhiteSpace(n))]
                       : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Write(IEnumerable<string> disabled)
        => JsonSerializer.Serialize(new Stored([.. disabled.Order(StringComparer.Ordinal)]),
                                    new JsonSerializerOptions { WriteIndented = true });
}
