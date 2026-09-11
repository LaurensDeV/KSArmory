namespace KSArmory;

/// <summary>
/// Whether a craft's weapons are standing guard: armed, and engaging on their own. The switcher
/// row's one switch for master arm and auto-engage together.
/// </summary>
internal enum Guard
{
    /// <summary>Nothing armed.</summary>
    Safe,

    /// <summary>Armed, but something on the craft fires only when told.</summary>
    Armed,

    /// <summary>Everything armed and engaging on its own.</summary>
    Guarding,
}

/// <summary>The rules for <see cref="Guard"/>, kept apart from the panel so they can be tested.</summary>
internal static class GuardState
{
    /// <summary>
    /// One weapons system. One that cannot engage on its own — a bomb rack releases when told —
    /// is guarding once it is armed, or a craft carrying one could never read as guarding.
    /// </summary>
    public static Guard Of(bool armed, bool autoEngage, bool autoEngages)
        => !armed ? Guard.Safe
         : autoEngage || !autoEngages ? Guard.Guarding
         : Guard.Armed;

    /// <summary>Two systems on one craft: guarding only if both are, safe only if both are.</summary>
    public static Guard Combine(Guard a, Guard b) => a == b ? a : Guard.Armed;

    /// <summary>
    /// Whether a click turns guard on. Anything short of every system guarding turns it on, so a
    /// half-armed craft is one click from guarding rather than one click from safe.
    /// </summary>
    public static bool TurnsOn(Guard now) => now != Guard.Guarding;
}
