using System.Reflection;

namespace KSArmory;

/// <summary>
/// What build this is, read off the assembly: semantic-release sets the version through
/// <c>tools/set-version.sh</c>, so a constant here would be a second copy that goes stale.
/// </summary>
internal static class Build
{
    /// <summary>
    /// A <c>developer</c> file beside the assembly, which <c>tools/deploy.sh</c> writes and the release
    /// archive never carries. It shows the bridge, the scenario runner and the research switches.
    /// </summary>
    public static bool Developer { get; } = ResolveDeveloper();

    private static bool ResolveDeveloper()
    {
        try
        {
            string? dir = Path.GetDirectoryName(typeof(Build).Assembly.Location);
            return dir is not null && File.Exists(Path.Combine(dir, "developer"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Version string for the panel, e.g. <c>0.9.8</c>: a prerelease suffix is kept, a <c>+commit</c> is not.</summary>
    public static string Version { get; } = Resolve();

    /// <summary>The KSA build compiled against, stamped from <c>ksa-assemblies.lock</c>; null if the stamp is missing.</summary>
    public static string? KsaBuild { get; } = ResolveKsaBuild();

    /// <summary>The KSA build running: the game's own version is its assembly version.</summary>
    public static string? KsaRunning { get; } = ResolveKsaRunning();

    private static string? ResolveKsaRunning()
    {
        try
        {
            return typeof(KSA.Vehicle).Assembly.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveKsaBuild()
    {
        try
        {
            foreach (AssemblyMetadataAttribute meta in typeof(Build).Assembly
                         .GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (meta.Key == "KsaBuild") return meta.Value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

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
                // The SDK appends "+<commit>" when SourceLink is on.
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
