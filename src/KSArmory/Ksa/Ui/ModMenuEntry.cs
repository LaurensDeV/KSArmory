namespace KSArmory;

/// <summary>
/// A copy of MrJeranimo's <c>ModMenuEntryAttribute</c>, not a dependency: ModMenu matches it by
/// <c>GetType().Name</c> alone, so it is found when ModMenu is present and inert when it is not.
/// Wanted gone; <c>docs/BLOCKED-ON-KSA.md</c> carries it on the recheck list.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class ModMenuEntryAttribute(string menuName, string? isModMenuActivePropertyName = null)
    : Attribute
{
    public string MenuName { get; } = menuName;

    public string? IsModMenuActivePropertyName { get; } = isModMenuActivePropertyName;
}

/// <summary>Whether ModMenu is loaded, so this mod does not draw its own menu as well.</summary>
internal static class ModMenuPresence
{
    private static bool? _present;

    /// <summary>Resolved once: assemblies do not come and go, and this is asked every frame.</summary>
    public static bool Installed => _present ??= Detect();

    private static bool Detect()
    {
        try
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "ModMenu") return true;
            }
        }
        catch
        {
            // A mod that cannot tell either way should draw its own menu rather than none.
        }

        return false;
    }
}
