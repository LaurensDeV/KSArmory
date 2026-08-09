namespace KSArmory;

/// <summary>
/// A copy of MrJeranimo's <c>ModMenuEntryAttribute</c>, so <b>ModMenu</b> lists this mod under
/// its shared <c>Mods</c> menu when a player has it installed.
///
/// <para><b>This is a copy on purpose and it is not a dependency.</b> ModMenu scans every loaded
/// assembly and matches the attribute by <c>GetType().Name</c> alone, never by assembly identity —
/// so declaring it here is enough to be found, and it is inert when ModMenu is absent. Its own
/// README says to copy it rather than reference anything.</para>
///
/// <para><b>Delete this at the first opportunity.</b> It exists only because KSA offers no way to
/// add to its menu bar: the bar is drawn inline in <c>Program</c> with hardcoded menus, and
/// ModMenu reaches it by transpiling the IL of a private method. That is a fragile thing for the
/// ecosystem to be standing on, and this file is a workaround for a missing engine feature rather
/// than something worth keeping. <c>docs/BLOCKED-ON-KSA.md</c> carries it on the recheck list, so
/// it gets looked at every time the game moves.</para>
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

    /// <summary>
    /// True when ModMenu is in the process. Resolved once: assemblies do not come and go, and this
    /// is asked every frame by the menu-bar draw.
    /// </summary>
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
