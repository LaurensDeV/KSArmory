namespace KSArmory;

/// <summary>
/// Whether a craft's weapons are standing guard: engaging on their own. The switcher row's one
/// switch, which is auto-engage on every weapon aboard that has one.
/// </summary>
internal enum Guard
{
    /// <summary>Nothing engages on its own; every weapon fires only when told.</summary>
    Off,

    /// <summary>Some weapons engage on their own and some fire only when told.</summary>
    Partly,

    /// <summary>Every weapon that can engage on its own is doing so.</summary>
    Guarding,
}

/// <summary>The rules for <see cref="Guard"/>, kept apart from the panel so they can be tested.</summary>
internal static class GuardState
{
    /// <summary>
    /// One weapons system, or null for one that cannot engage on its own. A bomb rack releases when
    /// told and has no auto-engage to turn on, so it has no say in whether the craft guards.
    /// </summary>
    public static Guard? Of(bool autoEngage, bool autoEngages)
        => !autoEngages ? null
         : autoEngage ? Guard.Guarding
         : Guard.Off;

    /// <summary>Two systems on one craft: guarding only if both are, off only if both are.</summary>
    public static Guard Combine(Guard a, Guard b) => a == b ? a : Guard.Partly;

    /// <summary>
    /// Whether a click turns guard on. Anything short of every system guarding turns it on, so a
    /// partly guarding craft is one click from guarding rather than one click from off.
    /// </summary>
    public static bool TurnsOn(Guard now) => now != Guard.Guarding;
}
