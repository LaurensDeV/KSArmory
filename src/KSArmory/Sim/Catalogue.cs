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

    private static readonly List<PackResult> _registrations = [];
    private static bool _open = true;

    /// <summary>
    /// Whether the catalogue still accepts registrations. Open until the roster is built, shut
    /// afterwards: a registry that grows while systems are crewed is a magazine resized under a
    /// launcher mid-salvo.
    /// </summary>
    public static bool IsOpen => _open;

    /// <summary>What every pack offered and how much of it stuck, in the order it arrived.</summary>
    public static IReadOnlyList<PackResult> Registrations => _registrations;

    /// <summary>Shut the door. Called once the roster is about to be built.</summary>
    internal static void Freeze() => _open = false;

    // There is no reopening in the game -- the roster is built once and the catalogue is shut for
    // the session. This exists so the freeze itself is testable without ending the test run.
    internal static void Reopen() => _open = true;

    /// <summary>
    /// Take what a pack read, minus anything that collides with what is already here.
    ///
    /// <para>Rounds and sensors go in before launchers, because a launcher whose round lost a
    /// collision is a launcher naming something the catalogue cannot answer — and that resolves
    /// to element zero at the first shot rather than to an error.</para>
    /// </summary>
    internal static PackResult Register(PackContents pack)
    {
        List<PackFault> faults = [.. pack.Faults];

        if (!_open)
        {
            faults.Add(new PackFault(pack.Source, "WeaponPack", "",
                                     "registered after the roster was built; nothing can be added now"));
            return Record(new PackResult(pack.Source, 0, faults));
        }

        HashSet<string> refused = [];
        int registered = 0;
        registered += Merge(pack.Munitions, _munitions, m => m.Name, "Munition", pack.Source, faults, refused);
        registered += Merge(pack.Sensors, _sensors, s => s.Name, "Sensor", pack.Source, faults, refused);
        registered += Merge(pack.Optics, _optics, o => o.PartId, "Optic", pack.Source, faults, refused);

        foreach (LauncherProfile launcher in pack.Launchers)
        {
            if (Taken(launcher, refused) is { } lost)
            {
                faults.Add(new PackFault(pack.Source, "Launcher", launcher.PartId,
                                         $"names '{lost}', which something else already claims"));
                continue;
            }

            if (Merge([launcher], _launchers, l => l.PartId, "Launcher", pack.Source, faults, refused) == 0)
            {
                continue;
            }

            registered++;
            foreach (ComponentProfile component in pack.Components)
            {
                if (component.PartId == launcher.PartId) _components.Add(component);
            }
        }

        return Record(new PackResult(pack.Source, registered, faults));
    }

    private static PackResult Record(PackResult result)
    {
        _registrations.Add(result);
        return result;
    }

    // The key this launcher names that somebody else got to first, or null when it is whole.
    //
    // Checking that the name *resolves* is not enough and is the dangerous version: a refused
    // round leaves the name in the catalogue carrying somebody else's profile, so the launcher
    // loads, flies, and throws a weapon its author never shipped.
    private static string? Taken(LauncherProfile launcher, HashSet<string> refused)
    {
        if (refused.Contains(launcher.Munition)) return launcher.Munition;
        if (refused.Contains(launcher.Sensor)) return launcher.Sensor;
        if (launcher.GunMunition is { } shell && refused.Contains(shell)) return shell;

        return null;
    }

    private static int Merge<T>(IReadOnlyList<T> from, List<T> into, Func<T, string> keyOf,
                                string kind, string source, List<PackFault> faults,
                                HashSet<string> refused)
    {
        int taken = 0;

        foreach (T candidate in from)
        {
            string key = keyOf(candidate);
            bool clash = false;

            for (int i = 0; i < into.Count && !clash; i++) clash = keyOf(into[i]) == key;

            if (clash)
            {
                refused.Add(key);
                faults.Add(new PackFault(source, kind, key, "something already registered claims this"));
                continue;
            }

            into.Add(candidate);
            taken++;
        }

        return taken;
    }

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
