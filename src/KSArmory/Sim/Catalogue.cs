namespace KSArmory;

/// <summary>
/// Every weapon the mod knows about right now: the built-ins from <see cref="Arsenal"/>, plus
/// whatever else was registered before the roster was built.
///
/// <para><b>This is what the mod reads.</b> <see cref="Arsenal"/> is the hand-written catalogue of
/// what ships in this archive and stays that way; a lookup taken against it directly sees only the
/// built-ins, which is a weapon somebody registered going silently unrecognised. The two are kept
/// apart rather than merged because the split is what lets the registry grow at runtime while
/// <c>tools/validate-parts.py</c> goes on reading the built-ins out of one file.</para>
/// </summary>
public static class Catalogue
{
    private static readonly List<LauncherProfile> _launchers = [.. Arsenal.Launchers];
    private static readonly List<MunitionProfile> _munitions = [.. Arsenal.Munitions];
    private static readonly List<SensorProfile> _sensors = [.. Arsenal.Sensors];
    private static readonly List<OpticProfile> _optics = [.. Arsenal.Optics];
    private static readonly List<ComponentProfile> _components = [.. Arsenal.Components];

    public static IReadOnlyList<LauncherProfile> Launchers => _launchers;
    public static IReadOnlyList<MunitionProfile> Munitions => _munitions;
    public static IReadOnlyList<SensorProfile> Sensors => _sensors;
    public static IReadOnlyList<OpticProfile> Optics => _optics;

    /// <summary>
    /// The parts recognised on a craft the mod did not design. Separate from
    /// <see cref="Launchers"/> for the reason <see cref="Arsenal.Components"/> gives.
    /// </summary>
    public static IReadOnlyList<ComponentProfile> Components => _components;

    /// <summary>The launcher matching a part Id, or null if that part is not one of ours.</summary>
    public static LauncherProfile? LauncherForPart(string? partId)
        => Arsenal.LauncherForPart(_launchers, partId);

    /// <summary>The optical head a part Id names, or null if it names none.</summary>
    public static OpticProfile? OpticForPart(string? partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;

        for (int i = 0; i < _optics.Count; i++)
        {
            if (_optics[i].PartId == partId) return _optics[i];
        }

        return null;
    }

    /// <summary>
    /// The named round, falling back to the first registered rather than throwing — see
    /// <see cref="Arsenal.Named{T}"/> for why, and for what that costs.
    /// </summary>
    public static MunitionProfile MunitionNamed(string name)
        => Arsenal.Named(_munitions, name, m => m.Name);

    /// <inheritdoc cref="MunitionNamed"/>
    public static SensorProfile SensorNamed(string name) => Arsenal.Named(_sensors, name, s => s.Name);

    /// <summary>
    /// The named round, or null when nothing carries that name.
    ///
    /// <para>What <see cref="MunitionNamed"/> cannot say. That one answers the first registered
    /// round rather than nothing, which keeps a typo in a shipped profile playable and is
    /// indistinguishable from a hit — so anything deciding whether a name is <em>good</em>, rather
    /// than merely wanting a round to fly, has to ask this instead.</para>
    /// </summary>
    public static MunitionProfile? TryMunitionNamed(string name) => Find(_munitions, name, m => m.Name);

    /// <inheritdoc cref="TryMunitionNamed"/>
    public static SensorProfile? TrySensorNamed(string name) => Find(_sensors, name, s => s.Name);

    private static T? Find<T>(IReadOnlyList<T> from, string name, Func<T, string> key) where T : class
    {
        for (int i = 0; i < from.Count; i++)
        {
            if (key(from[i]) == name) return from[i];
        }

        return null;
    }

    /// <summary>
    /// The round and sensor a launcher names, resolved together — see
    /// <see cref="Arsenal.LoadoutFor"/> for why the pairing is made in one place.
    /// </summary>
    public static (MunitionProfile Munition, SensorProfile Sensor) LoadoutFor(LauncherProfile launcher)
        => Arsenal.LoadoutFor(launcher, _munitions, _sensors);
}
