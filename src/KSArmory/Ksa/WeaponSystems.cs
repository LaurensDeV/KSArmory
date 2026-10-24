using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="WeaponSystem"/> per weapons system in the world, each with its own
/// <see cref="SystemConfig"/>.
///
/// <para>Keyed by <see cref="Vehicle"/> reference and launcher ordinal: each launcher is its own
/// weapon, with its own magazine. Re-pointing one system between launchers would refill the
/// magazine on every switch. Dropped when the craft dies, so no destroyed vehicle stays reachable.
/// CLAUDE.md, "Adding a weapon system", has the roster's rules.</para>
/// </summary>
internal sealed class WeaponSystems(Config config)
{
    internal sealed record Entry(WeaponSystem Weapon, SystemConfig Policy, Vehicle Craft, int Ordinal)
    {
        public string DisplayName => Weapon.Profile.DisplayName;
    }

    private readonly Config _config = config;
    private readonly Dictionary<(Vehicle Craft, int Ordinal), Entry> _entries = [];

    // Per craft, and about which system rather than any one of them, so neither Config nor SystemConfig.
    private readonly Dictionary<Vehicle, int> _selected = [];

    private readonly List<(Part Part, LauncherProfile Profile)> _launcherScratch = [];
    private readonly List<(Vehicle Craft, int Ordinal)> _gone = [];

    // Which save's settings the live systems hold: a load switches the bucket before the craft are
    // rebuilt, and a write would stamp the outgoing session's settings onto the save just opened.
    private string _scope = string.Empty;

    // Settings are written only when the player saves: written continuously, a reload has nothing
    // older to restore.
    private long _savedAt;

    public int Count => _entries.Count;

    /// <summary>In no particular order.</summary>
    public IEnumerable<Entry> All => _entries.Values;

    // Out of _entries, whose key holds the Vehicle: a loose system holds a Celestial and a captured
    // name instead, and is dropped when its last round lands.
    private readonly List<WeaponSystem> _loose = [];

    public IReadOnlyList<WeaponSystem> Loose => _loose;

    /// <summary>Rounds in the air draw as the craft's parts.</summary>
    public bool HasRoundsInFlight(Vehicle craft)
    {
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!ReferenceEquals(kv.Key.Craft, craft)) continue;
            foreach (IProjectile _ in kv.Value.Weapon.Rounds) return true;
        }

        return false;
    }

    /// <summary>For the warp log.</summary>
    public string WhoHasRoundsUp()
    {
        string who = "";
        foreach (Entry e in _entries.Values)
        {
            if (e.Weapon.Rounds.Count > 0) who += $" {e.Weapon.Profile.PartId}:{e.Weapon.Rounds.Count};";
        }

        for (int i = 0; i < _loose.Count; i++)
        {
            if (_loose[i].Rounds.Count > 0) who += $" loose {_loose[i].Profile.PartId}:{_loose[i].Rounds.Count};";
        }

        return who;
    }

    /// <summary>
    /// The longest step any round in the world, loose ones included, can still integrate. It bounds
    /// a clamp that discards time, so tightening it makes rounds fall behind rather than slowing the
    /// world; <see cref="WarpTargetStep"/> slows the world.
    /// </summary>
    public double FaithfulStep(out bool anyInFlight)
    {
        double faithful = double.MaxValue;
        anyInFlight = false;

        foreach (Entry e in _entries.Values)
        {
            foreach (IProjectile round in e.Weapon.Rounds)
            {
                anyInFlight = true;
                faithful = Math.Min(faithful, round.Munition.MaxFaithfulStepSeconds);
            }
        }

        for (int i = 0; i < _loose.Count; i++)
        {
            foreach (IProjectile round in _loose[i].Rounds)
            {
                anyInFlight = true;
                faithful = Math.Min(faithful, round.Munition.MaxFaithfulStepSeconds);
            }
        }

        return anyInFlight ? faithful : Interceptor.MaxFaithfulStep;
    }

    /// <summary>
    /// The step the world should be held to, asked of each round rather than its profile: a warhead
    /// coasting in vacuum takes a third of a second and the same warhead entering the air cannot.
    /// </summary>
    public double WarpTargetStep()
    {
        double target = double.MaxValue;
        bool any = false;

        foreach (Entry e in _entries.Values)
        {
            foreach (IProjectile round in e.Weapon.Rounds)
            {
                any = true;
                target = Math.Min(target, round.FaithfulStepSeconds);
            }
        }

        for (int i = 0; i < _loose.Count; i++)
        {
            foreach (IProjectile round in _loose[i].Rounds)
            {
                any = true;
                target = Math.Min(target, round.FaithfulStepSeconds);
            }
        }

        return any ? target : Interceptor.MaxFaithfulStep;
    }

    /// <summary>
    /// Crewed or loose. Asked by every effect holding a pooled emitter or a channel; without the loose
    /// ones the plume and the sound go on the frame the launcher dies.
    /// </summary>
    public bool Knows(object? owner)
    {
        if (owner is null) return false;

        foreach (Entry e in _entries.Values)
        {
            if (ReferenceEquals(e.Weapon, owner)) return true;
        }

        for (int i = 0; i < _loose.Count; i++)
        {
            if (ReferenceEquals(_loose[i], owner)) return true;
        }

        return false;
    }

    private readonly HashSet<string> _reportedShared = [];

    private void WarnIfNameIsTaken(Vehicle craft)
    {
        string name = KsaWorld.DisplayName(craft);

        foreach ((Vehicle other, int ordinal) in _entries.Keys)
        {
            if (ordinal != 0) continue;
            if (ReferenceEquals(other, craft)) continue;
            if (!string.Equals(KsaWorld.DisplayName(other), name, StringComparison.Ordinal)) continue;
            if (!_reportedShared.Add(name)) return;

            Log.Warn($"two crewed craft are both called '{name}'; they share one settings entry, "
                     + "so each will restore the other's and the last saved wins. Rename one.");
            return;
        }
    }

    /// <summary>The director crewed on a part, which the launcher on it cues.</summary>
    public Func<Part, OpticalHead?>? DirectorFor { get; set; }

    /// <summary>The contact a craft's picture hands out, for a beam with nothing designated.</summary>
    public Func<Vehicle?, object?>? PictureFor { get; set; }

    /// <summary>
    /// What an anti-radiation round homes on: any set aboard that emits and is not silent.
    /// </summary>
    public bool IsEmitting(Vehicle craft)
    {
        foreach (Entry entry in _entries.Values)
        {
            if (!ReferenceEquals(entry.Craft, craft)) continue;
            if (entry.Weapon.Sensor.Emits && !entry.Policy.RadarSilent) return true;
        }

        return false;
    }

    /// <summary>
    /// The <em>selected</em> weapon. What every consumer meaning "the system on this craft" asks.
    /// </summary>
    public Entry? For(Vehicle? craft)
    {
        if (craft is null) return null;

        if (_selected.TryGetValue(craft, out int ordinal)
            && _entries.TryGetValue((craft, ordinal), out Entry? chosen))
        {
            return chosen;
        }

        // Nothing selected, or the selection has gone: the lowest-numbered launcher still fitted.
        Entry? first = null;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!ReferenceEquals(kv.Key.Craft, craft)) continue;
            if (first is null || kv.Key.Ordinal < first.Ordinal) first = kv.Value;
        }

        return first;
    }

    /// <summary>
    /// The lowest-numbered launcher whose part provides guidance; never the selection, which may be
    /// another weapon beside the bus.
    /// </summary>
    public Entry? GuidedFrom(Vehicle? craft)
    {
        if (craft is null) return null;

        Entry? first = null;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!ReferenceEquals(kv.Key.Craft, craft)) continue;
            if (!Catalogue.ProvidesGuidance(kv.Value.Weapon.Profile.PartId)) continue;
            if (first is null || kv.Key.Ordinal < first.Ordinal) first = kv.Value;
        }

        return first;
    }

    /// <summary>In part order. Cleared and refilled.</summary>
    public void AllOn(Vehicle? craft, List<Entry> into)
    {
        into.Clear();
        if (craft is null) return;

        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (ReferenceEquals(kv.Key.Craft, craft)) into.Add(kv.Value);
        }

        into.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
    }

    /// <summary>Every launcher on the craft carrying the same part, in part order. Cleared and refilled.</summary>
    public void StationsOf(Entry entry, List<Entry> into)
    {
        AllOn(entry.Craft, into);

        string partId = entry.Weapon.Profile.PartId;
        for (int i = into.Count - 1; i >= 0; i--)
        {
            if (into[i].Weapon.Profile.PartId != partId) into.RemoveAt(i);
        }
    }

    /// <summary>
    /// Designates for a whole weapon: every station takes the mark, so whichever the trigger reaches
    /// next releases onto it, and a falling store dropped with nothing marked is sent there once.
    /// </summary>
    public void DesignateWeapon(Entry entry, Aimpoint aim, string what)
    {
        ReconcileSteerables();
        StationsOf(entry, _stationScratch);

        for (int i = 0; i < _stationScratch.Count; i++)
        {
            _stationScratch[i].Weapon.Point(aim, what, log: i == 0);
        }

        foreach (Entry station in _stationScratch)
        {
            station.Weapon.SendStoresInTheAir(what, station.Weapon.Steerable);
        }
    }

    /// <summary>
    /// The station of the entry's weapon that released last, or the entry itself when none has:
    /// the trigger steps between stations, so the round just fired is rarely the selected one's.
    /// </summary>
    public Entry LastReleasing(Entry entry)
    {
        StationsOf(entry, _stationScratch);

        Entry found = entry;
        foreach (Entry s in _stationScratch)
        {
            if (s.Weapon.ReleasedAt > found.Weapon.ReleasedAt) found = s;
        }

        return found;
    }

    /// <summary>
    /// Keeps a steerable store only on the station of each weapon that released last: releasing
    /// the next store hands the marks to it, whichever rack it came off.
    /// </summary>
    public void ReconcileSteerables()
    {
        foreach (Entry entry in _entries.Values)
        {
            if (entry.Weapon.Steerable is null) continue;

            StationsOf(entry, _stationScratch);
            foreach (Entry other in _stationScratch)
            {
                bool releasedSince = other.Weapon.ReleasedAt > entry.Weapon.ReleasedAt;
                if (StoreRetarget.Takes(releasedSince)) continue;

                entry.Weapon.LockSteerable();
                break;
            }
        }
    }

    /// <summary>
    /// Copies the shown station's settings, or the first's, onto every station: the panel edits only
    /// one and the trigger steps to the others.
    /// </summary>
    public void ShareStationSettings()
    {
        foreach (Entry entry in _entries.Values)
        {
            StationsOf(entry, _stationScratch);
            if (_stationScratch.Count < 2) continue;

            Entry lead = For(entry.Craft) is { } shown && _stationScratch.Contains(shown)
                ? shown
                : _stationScratch[0];
            if (ReferenceEquals(lead, entry)) continue;

            SystemSettings wanted = SystemSettings.From(lead.Policy);
            if (wanted.Differs(SystemSettings.From(entry.Policy))) wanted.ApplyTo(entry.Policy);

            entry.Policy.MouseFire = lead.Policy.MouseFire;
        }
    }

    private readonly List<Entry> _stationScratch = [];

    public void Select(Vehicle? craft, int ordinal)
    {
        if (craft is null || !_entries.ContainsKey((craft, ordinal))) return;

        _selected[craft] = ordinal;
        Log.Info($"selected {_entries[(craft, ordinal)].DisplayName} "
                 + $"({ordinal + 1}) on {KsaWorld.DisplayName(craft)}");
    }

    /// <summary>Wrapping; <paramref name="by"/> is +1 for the next and -1 for the previous.</summary>
    public void Cycle(Vehicle? craft, int by, List<Entry> scratch)
    {
        AllOn(craft, scratch);
        if (scratch.Count < 2) return;

        int[] ordinals = new int[scratch.Count];
        for (int i = 0; i < scratch.Count; i++) ordinals[i] = scratch[i].Ordinal;

        int at = For(craft) is { } current ? WeaponSelection.IndexOf(ordinals, current.Ordinal) : 0;

        Select(craft, scratch[WeaponSelection.Step(scratch.Count, at, by)].Ordinal);
    }

    /// <summary>
    /// Where a launcher went this frame. One decision, also read by the ballistic computer, so the
    /// two cannot disagree about which craft the shot is on.
    /// </summary>
    public IReadOnlyList<(Vehicle From, Vehicle To)> Handovers => _handovers;

    private readonly List<(Vehicle From, Vehicle To)> _handovers = [];
    private readonly List<HandoverCandidate> _candidates = [];
    private readonly List<Vehicle> _handoverScratch = [];

    /// <summary>Crews anything new, follows what a decoupler carried off, forgets what was destroyed.</summary>
    public void Sync(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems)
    {
        // Before crewing: it re-keys entries onto craft the loop walks, or a launcher gets a second
        // system with a full magazine.
        FollowDecoupledLaunchers();

        for (int i = 0; i < systems.Count; i++)
        {
            Vehicle craft = systems[i].Craft;
            if (!KsaWorld.IsAlive(craft)) continue;

            // A craft with only a director would get a launcher-less system on the first profile.
            if (!systems[i].Inventory.IsWeaponSystem) continue;

            // Off the craft, not the inventory: the ordinal has to be the one LauncherPart resolves.
            LauncherPart.FindAll(craft, _launcherScratch);

            for (int ordinal = 0; ordinal < _launcherScratch.Count; ordinal++)
            {
                if (_entries.ContainsKey((craft, ordinal))) continue;

                // Crewed under another ordinal: a rack renumbers when its neighbour drops.
                if (IsHeld(_launcherScratch[ordinal].Part)) continue;

                // Settings are keyed on the display name, which craft from one blueprint share.
                if (ordinal == 0) WarnIfNameIsTaken(craft);

                SystemConfig policy = new();

                // Before the system exists, so its first frame runs on the restored settings.
                if (SettingsStore.For(SettingsKey(craft, ordinal, _launcherScratch[ordinal].Part)) is { } stored)
                {
                    stored.ApplyTo(policy);

                    // Team names are session-wide and memberships per system: restore both, or
                    // every contact reads Unknown, engageable by default.
                    stored.DeclareTeams(_config.TeamNames);
                    _config.AdoptRules(policy.Iff);
                }

                WeaponSystem system = new(_config, policy, ordinal, IsEmitting) { DirectorFor = DirectorFor, PictureFor = PictureFor };

                // Pinned, or ResolvePlatform elects the craft being flown for every system.
                system.PinPlatform(craft);

                _entries[(craft, ordinal)] = new Entry(system, policy, craft, ordinal);
                Log.Info($"crewed {KsaWorld.DisplayName(craft)} launcher {ordinal + 1} "
                         + $"of {_launcherScratch.Count}");

                // KSA elects no control part on a stack with no command pod, so attitude resolves
                // against the wrong axes. Only a launcher declaring <Control /> can serve.
                KsaWorld.EnsureControlPart(craft, _launcherScratch[ordinal].Part);
            }
        }

        RetireDestroyed();

        ReapLoose();
    }

    public void UpdateLoose(double dt, IReadOnlyList<IContact>? airborne)
    {
        for (int i = 0; i < _loose.Count; i++) _loose[i].UpdateLoose(dt, airborne);

        ReapLoose();
    }

    private void ReapLoose()
    {
        for (int i = _loose.Count - 1; i >= 0; i--)
        {
            if (_loose[i].RoundsInFlight > 0) continue;

            Log.Info($"{_loose[i].LooseName}: last round down, system forgotten");
            Diagnostics.Forget(_loose[i]);
            _loose[i].Reset();
            _loose.RemoveAt(i);
        }
    }

    /// <summary>
    /// Hands a destroyed craft's rounds to the body. Run from the step as well as the sync, which is
    /// in the UI pass: a dead craft's system must not be stepped or sampled.
    /// </summary>
    public void RetireDestroyed()
    {
        _gone.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) _gone.Add(kv.Key);
        }

        foreach ((Vehicle Craft, int Ordinal) key in _gone)
        {
            if (_entries.TryGetValue(key, out Entry? entry) && TryFollowHeldPart(key, entry)) continue;

            Retire(key, "a crewed system was destroyed");
        }
    }

    // A crash can leave the launcher part whole on a fragment, where a fresh system would start with a
    // full magazine. Only the very part, and only onto a craft that is a platform.
    private bool TryFollowHeldPart((Vehicle Craft, int Ordinal) key, Entry entry)
    {
        if (entry.Weapon.HeldPart is not { } held) return false;

        foreach (Vehicle craft in KsaWorld.Vehicles)
        {
            if (!KsaWorld.IsAlive(craft) || ReferenceEquals(craft, key.Craft) || !KsaWorld.HasPlatform(craft)) continue;

            LauncherPart.FindAll(craft, _launcherScratch);
            for (int ordinal = 0; ordinal < _launcherScratch.Count; ordinal++)
            {
                if (ReferenceEquals(_launcherScratch[ordinal].Part, held))
                {
                    return MoveTo(key, entry, craft, ordinal, held, "its own part, off a craft that was destroyed");
                }
            }
        }

        return false;
    }

    // A fired round outlives its launcher, so the system goes loose to fly it. The name is read
    // before detaching: it is the team identity as well as the label.
    private void Retire((Vehicle Craft, int Ordinal) key, string why)
    {
        if (!_entries.TryGetValue(key, out Entry? entry)) return;

        WeaponSystem system = entry.Weapon;

        if (system.GoLoose(KsaWorld.ParentBody(key.Craft), KsaWorld.DisplayName(key.Craft)))
        {
            _loose.Add(system);
        }
        else
        {
            system.Reset();

            // Or an entry keyed on the system keeps a destroyed vehicle reachable.
            Diagnostics.Forget(system);
        }

        _entries.Remove(key);
        _selected.Remove(key.Craft);
        _fruitless.Remove(system);
        Log.Info(why);
    }

    // A tree read mid-rebuild returns nothing, for a frame or two of staging; past this many a part a
    // warhead destroyed is retired rather than searched for every frame. Counted in searches,
    // because the sync runs from the UI pass, which can be off.
    private const int FruitlessSearchesBeforeRetiring = 120;

    // Keyed on the system, so it is dropped with it.
    private readonly Dictionary<WeaponSystem, int> _fruitless =
        new(ReferenceEqualityComparer.Instance);

    // A launcher a decoupler carried onto another craft keeps its system and magazine. The scan runs
    // only when an entry has lost its launcher.
    private void FollowDecoupledLaunchers()
    {
        _handovers.Clear();

        bool anyLost = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (kv.Value.Weapon is { Platform: not null, Launcher: null }) { anyLost = true; break; }
        }

        if (!anyLost) return;

        _handoverScratch.Clear();
        _handoverScratch.AddRange(KsaWorld.Vehicles);
        if (_handoverScratch.Count == 0) return;

        _gone.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (kv.Value.Weapon is not { Platform: not null, Launcher: null }) continue;
            _gone.Add(kv.Key);
        }

        for (int i = 0; i < _gone.Count; i++)
        {
            if (!_entries.TryGetValue(_gone[i], out Entry? entry)) continue;

            if (TryFollow(_gone[i], entry))
            {
                _fruitless.Remove(entry.Weapon);
                continue;
            }

            _fruitless.TryGetValue(entry.Weapon, out int misses);
            _fruitless[entry.Weapon] = ++misses;

            if (misses < FruitlessSearchesBeforeRetiring) continue;

            Retire(_gone[i], $"{KsaWorld.DisplayName(_gone[i].Craft)} launcher "
                             + $"{_gone[i].Ordinal + 1} lost its launcher part and nothing else "
                             + "carries one - retiring the system");
        }

        _gone.Clear();
    }

    private bool IsHeld(Part part)
    {
        foreach (Entry e in _entries.Values)
        {
            if (ReferenceEquals(e.Weapon.HeldPart, part)) return true;
        }

        return false;
    }

    // True when settled: moved, or refused for a reason other than nothing carrying it.
    private bool TryFollow((Vehicle Craft, int Ordinal) key, Entry entry)
    {
        WeaponSystem system = entry.Weapon;
        string wanted = system.Profile.PartId;

        _candidates.Clear();
        (int Craft, int Ordinal)? exact = null;

        for (int i = 0; i < _handoverScratch.Count; i++)
        {
            Vehicle craft = _handoverScratch[i];
            if (!KsaWorld.IsAlive(craft) || ReferenceEquals(craft, key.Craft)) continue;

            // Never onto a craft whose every part rides, such as a rail lying on the ground.
            if (!KsaWorld.HasPlatform(craft)) continue;

            LauncherPart.FindAll(craft, _launcherScratch);

            for (int ordinal = 0; ordinal < _launcherScratch.Count; ordinal++)
            {
                // The very part, which a split carries whole onto the new craft; by its Id only for a
                // system that never held one.
                if (system.HeldPart is { } held && ReferenceEquals(_launcherScratch[ordinal].Part, held))
                {
                    exact = (i, ordinal);
                }

                if (_launcherScratch[ordinal].Profile.PartId != wanted) continue;

                _candidates.Add(new HandoverCandidate(
                    i, ordinal,
                    Vec.Len(KsaWorld.PositionEcl(craft) - system.PlatformEcl),
                    _entries.ContainsKey((craft, ordinal))));
            }
        }

        if (exact is { } found) return MoveTo(key, entry, _handoverScratch[found.Craft], found.Ordinal, system.HeldPart, "its own part");

        Handover choice = PlatformHandover.Choose(_candidates);

        if (choice.Verdict == HandoverVerdict.Ambiguous)
        {
            Log.Warn($"{KsaWorld.DisplayName(key.Craft)} launcher {key.Ordinal + 1} lost its "
                     + $"launcher and {choice.Why} - leaving it where it is");

            // Settled, not moved: retiring would throw a live magazine away for too many candidates.
            return true;
        }

        if (choice.Verdict != HandoverVerdict.Move) return false;

        Vehicle to = _handoverScratch[choice.CraftIndex];

        // Already crewed there.
        if (_entries.ContainsKey((to, choice.Ordinal))) return true;

        return MoveTo(key, entry, to, choice.Ordinal, held: null, choice.Why);
    }

    private bool MoveTo((Vehicle Craft, int Ordinal) key, Entry entry, Vehicle to, int ordinal, Part? held, string why)
    {
        // A key is an identity, not a place: a taken ordinal files the next free one.
        int filed = ordinal;
        while (_entries.ContainsKey((to, filed))) filed++;

        _entries.Remove(key);
        _entries[(to, filed)] = entry with { Craft = to, Ordinal = filed };

        if (_selected.TryGetValue(key.Craft, out int selected) && selected == key.Ordinal)
        {
            _selected.Remove(key.Craft);
            _selected[to] = filed;
        }

        if (filed == 0) WarnIfNameIsTaken(to);

        entry.Weapon.Rehome(to, ordinal, held);
        _handovers.Add((key.Craft, to));

        Log.Info($"{KsaWorld.DisplayName(key.Craft)} launcher {key.Ordinal + 1} followed its "
                 + $"launcher onto {KsaWorld.DisplayName(to)} launcher {ordinal + 1} "
                 + $"({why}); settings now filed under \"{SettingsKey(to, filed, entry.Weapon.HeldPart)}\"");

        return true;
    }

    // The first launcher keeps the bare craft name; later ones are suffixed, so two racks do not share
    // switches. Moved past a switched-off family's launchers (LauncherPart.FiledOrdinal).
    private static string SettingsKey(Vehicle craft, int ordinal, Part? part)
    {
        int filed = part is null ? ordinal : LauncherPart.FiledOrdinal(craft, part, ordinal);
        return filed == 0 ? KsaWorld.DisplayName(craft) : $"{KsaWorld.DisplayName(craft)}#{filed + 1}";
    }

    /// <summary>The craft flown if it carries a weapon, else any. Never null while the roster is not empty.</summary>
    public Vehicle? Default()
    {
        if (For(KsaWorld.ControlledVehicle) is not null) return KsaWorld.ControlledVehicle;

        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries) return kv.Key.Craft;
        return null;
    }

    /// <summary>What <see cref="Remember"/> found, so the ballistic computers follow the same save.</summary>
    public enum SettingsMoment { Nothing, Saved, Reopened }

    /// <summary>
    /// Writes the settings down when the game has just saved, and re-reads them when another save
    /// has been opened. Cheap to call every frame.
    /// </summary>
    public SettingsMoment Remember()
    {
        string scope = SettingsStore.CurrentScope;

        // Seen directly with the hook in, so a save-as is written into rather than adopted.
        bool savedNow = SaveHook.ConsumeSaved();
        if (savedNow)
        {
            _scope = scope;
            _savedAt = KsaWorld.CurrentSaveStamp();
            return WriteDown();
        }

        if (_scope.Length == 0)
        {
            // First pass: Sync has already applied whatever this bucket holds.
            _scope = scope;
        }
        else if (scope != _scope)
        {
            // A different save is open. Adopt what it says instead of writing over it.
            _scope = scope;
            _savedAt = KsaWorld.CurrentSaveStamp();
            Adopt();
            return SettingsMoment.Reopened;
        }

        if (SaveHook.Installed) return SettingsMoment.Nothing;

        // Without the hook, the save's timestamp says when the game saved.
        long stamp = KsaWorld.CurrentSaveStamp();
        if (stamp == 0 || stamp == _savedAt) return SettingsMoment.Nothing;

        bool firstSight = _savedAt == 0;
        _savedAt = stamp;

        // The first stamp seen is the save as it already was, not the player saving.
        if (firstSight) return SettingsMoment.Nothing;

        return WriteDown();
    }

    private SettingsMoment WriteDown()
    {
        Log.Info($"settings: the game saved {SettingsStore.CurrentScope}, writing the systems' settings with it");

        bool changed = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) continue;
            changed |= SettingsStore.Remember(SettingsKey(kv.Key.Craft, kv.Key.Ordinal, kv.Value.Weapon.HeldPart),
                                              kv.Value.Policy);
        }

        if (changed) SettingsStore.Save();
        return SettingsMoment.Saved;
    }

    private void Adopt()
    {
        int applied = 0;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) continue;
            if (SettingsStore.For(SettingsKey(kv.Key.Craft, kv.Key.Ordinal, kv.Value.Weapon.HeldPart)) is not { } stored) continue;

            stored.ApplyTo(kv.Value.Policy);
            applied++;
        }

        if (applied > 0) Log.Info($"settings: re-read {applied} system(s) for the open save");
    }

    /// <summary>Whatever the save has done: for unload, and for what must take effect now.</summary>
    public void WriteNow()
    {
        bool changed = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) continue;
            changed |= SettingsStore.Remember(SettingsKey(kv.Key.Craft, kv.Key.Ordinal, kv.Value.Weapon.HeldPart),
                                              kv.Value.Policy);
        }

        if (changed) SettingsStore.Save();
    }

    public void Clear()
    {
        WriteNow();
        Discard();
    }

    /// <summary>
    /// Drops every system, loose ones too, without writing: after a save load the file belongs to the
    /// save being opened, and nothing a loose system holds exists any more.
    /// </summary>
    public void Discard()
    {
        foreach (Entry e in _entries.Values) e.Weapon.Reset();
        _entries.Clear();

        foreach (WeaponSystem loose in _loose) loose.Reset();
        _loose.Clear();
    }
}
