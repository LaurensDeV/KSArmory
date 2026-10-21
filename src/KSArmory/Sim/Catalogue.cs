namespace KSArmory;

/// <summary>
/// Every weapon the mod knows about: KSArmory's own definition files and whatever any pack registered, until
/// the roster is built. <b>This is what the mod reads</b>; nothing else holds a registry.
/// </summary>
public static class Catalogue
{
    private static readonly List<LauncherProfile> _launchers = [];
    private static readonly List<MunitionProfile> _munitions = [];
    private static readonly List<SensorProfile> _sensors = [];
    private static readonly List<OpticProfile> _optics = [];
    private static readonly List<SearchSetProfile> _searchSets = [];
    private static readonly List<DecoyProfile> _decoys = [];
    private static readonly List<DispenserProfile> _dispensers = [];
    private static readonly List<ComponentProfile> _components = [];

    public static IReadOnlyList<LauncherProfile> Launchers => _launchers;
    public static IReadOnlyList<MunitionProfile> Munitions => _munitions;
    public static IReadOnlyList<SensorProfile> Sensors => _sensors;
    public static IReadOnlyList<OpticProfile> Optics => _optics;
    public static IReadOnlyList<SearchSetProfile> SearchSets => _searchSets;
    public static IReadOnlyList<DecoyProfile> Decoys => _decoys;
    public static IReadOnlyList<DispenserProfile> Dispensers => _dispensers;

    /// <summary>
    /// The parts recognised on a craft the mod did not design. Separate from
    /// <see cref="Launchers"/>: a launcher describes how a part shoots, a component is what tells the survey a
    /// craft carries a weapons system at all.
    /// </summary>
    public static IReadOnlyList<ComponentProfile> Components => _components;

    private static readonly HashSet<string> _disabledLaunchers = [];
    private static readonly HashSet<string> _disabledParts = [];

    /// <summary>
    /// Every part of a built-in family the player switched off that no pack has claimed since: it loads, and
    /// is nothing. A part a pack defines again is that pack's weapon, which is what switching a family off
    /// for a replacement is for.
    /// </summary>
    public static IReadOnlyCollection<string> DisabledParts => [.. _disabledParts.Where(id => !Claimed(id))];

    /// <summary>
    /// Whether this part is an unclaimed launcher of a switched-off family. Counted beside the registered
    /// launchers where a craft's weapons are numbered, so switching a family off does not renumber the rest;
    /// a claimed one is a registered launcher already, and counting it here too would number it twice.
    /// </summary>
    public static bool IsDisabledLauncher(string? partId)
        => partId is not null && _disabledLaunchers.Contains(partId) && !Claimed(partId);

    private static bool Claimed(string partId)
        => LauncherForPart(partId) is not null || OpticForPart(partId) is not null
        || SearchSetForPart(partId) is not null || DispenserForPart(partId) is not null;

    internal static void Disable(IEnumerable<string> launcherParts, IEnumerable<string> parts)
    {
        _disabledLaunchers.UnionWith(launcherParts);
        _disabledParts.UnionWith(parts);
    }

    private static readonly Dictionary<string, string> _familyOf = [];

    // So a pack refused a built-in's part is told which family to switch off to replace it.
    internal static void Owns(string family, IEnumerable<string> parts)
    {
        foreach (string part in parts) _familyOf.TryAdd(part, family);
    }

    private static readonly List<PackResult> _registrations = [];
    private static readonly Dictionary<string, string> _packOf = [];
    private static bool _open = true;

    /// <summary>
    /// Whether the catalogue still accepts registrations. Open until the roster is built, shut
    /// afterwards: a registry that grows while systems are crewed is a magazine resized under a
    /// launcher mid-salvo.
    /// </summary>
    public static bool IsOpen => _open;

    /// <summary>What every pack offered and how much of it stuck, in the order it arrived.</summary>
    public static IReadOnlyList<PackResult> Registrations => _registrations;

    /// <summary>
    /// Whether every registered part can actually be found, once the world has finished loading.
    ///
    /// <para>Covers the built-ins too, and deliberately: a part of ours whose XML did not load is
    /// the same silence as a pack's, and has never had anything to report it.</para>
    /// </summary>
    internal static IReadOnlyList<PackFault> Audit(IPartCatalogue parts)
        => PackAudit.Run(_launchers, _optics, _searchSets, _dispensers, parts,
                         partId => _packOf.GetValueOrDefault(partId, PackReader.BuiltInSource));

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
        registered += Merge(pack.Decoys, _decoys, d => d.Name, "Decoy", pack.Source, faults, refused);
        registered += Merge(pack.Optics, _optics, o => o.PartId, "Optic", pack.Source, faults, refused);
        foreach (OpticProfile head in pack.Optics) _packOf.TryAdd(head.PartId, pack.Source);
        registered += Merge(pack.SearchSets, _searchSets, x => x.PartId, "SearchSet", pack.Source, faults, refused);
        foreach (SearchSetProfile set in pack.SearchSets) _packOf.TryAdd(set.PartId, pack.Source);

        // A dispenser loaded with a decoy that lost a collision would throw somebody else's.
        foreach (DispenserProfile dispenser in pack.Dispensers)
        {
            if (refused.Contains(dispenser.Decoy))
            {
                faults.Add(new PackFault(pack.Source, "Dispenser", dispenser.PartId,
                                         $"names '{dispenser.Decoy}', which something else already claims"));
                refused.Add(dispenser.PartId);
                continue;
            }

            if (Merge([dispenser], _dispensers, d => d.PartId, "Dispenser", pack.Source, faults, refused) == 0) continue;

            registered++;
            _packOf.TryAdd(dispenser.PartId, pack.Source);
        }

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
            _packOf[launcher.PartId] = pack.Source;
            foreach (ComponentProfile component in pack.Components)
            {
                if (component.PartId == launcher.PartId) _components.Add(component);
            }
        }

        // A standalone director's, search set's or dispenser's row, when that part registered from
        // this pack and nothing has a row for it yet.
        foreach (ComponentProfile component in pack.Components)
        {
            if (component.Role is not (WeaponRole.Camera or WeaponRole.Sensor or WeaponRole.Countermeasures)) continue;
            if (refused.Contains(component.PartId)) continue;
            if (!pack.Optics.Any(o => o.PartId == component.PartId)
                && !pack.SearchSets.Any(x => x.PartId == component.PartId)
                && !pack.Dispensers.Any(d => d.PartId == component.PartId)) continue;
            if (_components.Any(c => c.PartId == component.PartId)) continue;

            _components.Add(component);
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
                faults.Add(new PackFault(source, kind, key,
                                         _familyOf.TryGetValue(key, out string? family)
                                             ? $"claimed by built-in family {family}; switch that family off under "
                                               + "KSArmory > Built-in weapons to replace it"
                                             : "something already registered claims this"));
                continue;
            }

            into.Add(candidate);
            taken++;
        }

        return taken;
    }

    /// <summary>
    /// Whether this part is what flies its craft — a <see cref="WeaponRole.Guidance"/> component, or
    /// one carrying guidance built in. A ballistic computer releases through the launcher that
    /// answers yes, never through whichever weapon the panel has selected.
    /// </summary>
    public static bool ProvidesGuidance(string? partId)
    {
        if (string.IsNullOrEmpty(partId)) return false;

        foreach (ComponentProfile component in _components)
        {
            if (component.PartId != partId) continue;
            if (component.Role == WeaponRole.Guidance) return true;

            foreach (BuiltInComponent built in component.Provides)
            {
                if (built.Role == WeaponRole.Guidance) return true;
            }
        }

        return false;
    }

    /// <summary>The launcher matching a part Id, or null if that part is not one of ours.</summary>
    public static LauncherProfile? LauncherForPart(string? partId)
        => LauncherForPart(_launchers, partId);

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

    /// <summary>The search set a part Id names, or null if it names none.</summary>
    public static SearchSetProfile? SearchSetForPart(string? partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;

        foreach (SearchSetProfile s in _searchSets)
        {
            if (s.PartId == partId) return s;
        }

        return null;
    }

    /// <summary>The dispenser a part Id names, or null if it names none.</summary>
    public static DispenserProfile? DispenserForPart(string? partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;

        foreach (DispenserProfile d in _dispensers)
        {
            if (d.PartId == partId) return d;
        }

        return null;
    }

    /// <summary>
    /// Whether a name that matches nothing throws rather than answering the first entry. Off in the
    /// game, where the fallback keeps a flight going; on in the tests, where it would let a renamed
    /// weapon be silently replaced by whichever registered first.
    /// </summary>
    internal static bool MissingNameThrows { get; set; }

    /// <summary>The named expendable, falling back to the first as <see cref="Named{T}"/> does.</summary>
    public static DecoyProfile DecoyNamed(string name) => Resolve(_decoys, name, d => d.Name, "decoy");

    /// <summary>
    /// The named round, falling back to the first registered rather than throwing — see
    /// <see cref="Named{T}"/> for why, and for what that costs.
    /// </summary>
    public static MunitionProfile MunitionNamed(string name) => Resolve(_munitions, name, m => m.Name, "round");

    /// <inheritdoc cref="MunitionNamed"/>
    public static SensorProfile SensorNamed(string name) => Resolve(_sensors, name, s => s.Name, "sensor");

    private static T Resolve<T>(IReadOnlyList<T> from, string name, Func<T, string> key, string kind)
        where T : class
        => Find(from, name, key)
           ?? (MissingNameThrows
                   ? throw new KeyNotFoundException($"no {kind} is registered as '{name}'")
                   : Named(from, name, key));

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
    /// <see cref="LoadoutFor(LauncherProfile, IReadOnlyList{MunitionProfile}, IReadOnlyList{SensorProfile})"/> for why the pairing is made in one place.
    /// </summary>
    public static (MunitionProfile Munition, SensorProfile Sensor) LoadoutFor(LauncherProfile launcher)
        => (launcher.Munition.Length == 0 ? MunitionProfile.None : MunitionNamed(launcher.Munition),
            SensorNamed(launcher.Sensor));

    /// <summary>
    /// The same lookup against an explicit registry.
    ///
    /// <para>Exists so the selection logic can be tested against a registry of any size. With one
    /// entry every registry assertion is trivially satisfied and cannot distinguish "picks the
    /// right one" from "picks the only one".</para>
    /// </summary>
    internal static LauncherProfile? LauncherForPart(IReadOnlyList<LauncherProfile> from, string? partId)
    {
        if (string.IsNullOrEmpty(partId)) return null;

        for (int i = 0; i < from.Count; i++)
        {
            if (from[i].PartId == partId) return from[i];
        }
        return null;
    }

    /// <summary>
    /// The round and sensor a launcher names, resolved together against explicit registries.
    ///
    /// <para>Together because a launcher left holding another system's round is a wrong-weapon
    /// bug with nothing on screen to show for it. Every caller that adopts a launcher takes all
    /// three, and this is the one place that pairing is made.</para>
    /// </summary>
    internal static (MunitionProfile Munition, SensorProfile Sensor) LoadoutFor(
        LauncherProfile launcher,
        IReadOnlyList<MunitionProfile> munitions,
        IReadOnlyList<SensorProfile> sensors)
        => (launcher.Munition.Length == 0 ? MunitionProfile.None : Named(munitions, launcher.Munition, m => m.Name),
            Named(sensors, launcher.Sensor, s => s.Name));

    /// <summary>
    /// First entry whose key matches, or the first entry as a fallback. Internal rather than
    /// private so the fallback can be tested against a registry with more than one candidate,
    /// where "matched" and "fell back to element zero" are actually distinguishable.
    ///
    /// <para>The fallback keeps a typo in a shipped profile playable and cannot report one.
    /// <see cref="Catalogue.TryMunitionNamed"/> is the form that can, and is what anything
    /// judging a name rather than flying a round asks.</para>
    /// </summary>
    internal static T Named<T>(IReadOnlyList<T> from, string name, Func<T, string> key)
    {
        for (int i = 0; i < from.Count; i++)
        {
            if (key(from[i]) == name) return from[i];
        }
        return from[0];
    }
}
