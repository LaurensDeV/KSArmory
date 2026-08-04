using System.Reflection;

namespace KSArmory;

/// <summary>
/// What build this is, read off the assembly rather than written down.
///
/// <para>The version is set by semantic-release through <c>tools/set-version.sh</c>, so a
/// constant here would be a second copy that only ever goes stale — and the one place it matters
/// is a bug report, where a stale number is worse than none.</para>
/// </summary>
internal static class Build
{
    /// <summary>Version string for the panel, e.g. <c>0.8.1</c> or <c>0.8.1+dev</c>.</summary>
    public static string Version { get; } = Resolve();

    private static string Resolve()
    {
        try
        {
            Assembly assembly = typeof(Build).Assembly;

            // InformationalVersion carries any suffix; AssemblyVersion drops it and always
            // reports four components.
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // The SDK appends "+<commit>" when SourceLink is on. The hash is noise in a panel.
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
