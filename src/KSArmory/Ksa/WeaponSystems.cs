using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="WeaponSystem"/> per weapons system in the world, each with its own
/// <see cref="SystemConfig"/>.
///
/// <para>Every craft carrying a recognised part is crewed, permanently and independently: a
/// battery is pinned to the craft it was created for and never moves, so arming one site, sending
/// it a target or putting it on a team says nothing about any other.</para>
///
/// <para>Keyed by <see cref="Vehicle"/> reference <em>and launcher ordinal</em>, because a craft
/// can carry several launchers and each is its own weapon: its own magazine, drives and rounds in
/// the air. Re-pointing one system at a different launcher instead would refill the magazine on
/// every switch, so a player could drop, switch, drop, switch back and find the bomb returned.</para>
///
/// <para>Entries appear when a system is surveyed and are dropped when the craft dies — a battery
/// outliving its platform would keep a destroyed vehicle alive in this dictionary for the
/// session.</para>
/// </summary>
internal sealed class WeaponSystems(Config config)
{
    internal sealed record Entry(WeaponSystem Battery, SystemConfig Policy, Vehicle Craft, int Ordinal)
    {
        /// <summary>What the selector calls this weapon.</summary>
        public string DisplayName => Battery.Profile.DisplayName;
    }

    private readonly Config _config = config;
    private readonly Dictionary<(Vehicle Craft, int Ordinal), Entry> _entries = [];

    // Which launcher the player has selected on each craft. Held here rather than on Config
    // because it is per craft, and rather than on SystemConfig because it is about *which* system
    // rather than about any one of them.
    private readonly Dictionary<Vehicle, int> _selected = [];

    private readonly List<(Part Part, LauncherProfile Profile)> _launcherScratch = [];
    private readonly List<(Vehicle Craft, int Ordinal)> _gone = [];

    // Which save's settings the live batteries are holding. Loading a save switches the bucket
    // before the craft are rebuilt, so without this the next periodic write stamps the outgoing
    // session's settings onto the save just opened -- which reads as a save that will not keep
    // what it was given.
    private string _scope = string.Empty;

    // When the open save was last written. Settings are written to disk on the frame this
    // advances -- i.e. when the player saves -- and at no other time. Writing continuously stops
    // a reload restoring anything: the file is then always already up to date with the session,
    // so there is nothing older to come back to.
    private long _savedAt;


    public int Count => _entries.Count;

    /// <summary>Every crewed system, in no particular order.</summary>
    public IEnumerable<Entry> All => _entries.Values;

    // Systems whose craft has been destroyed with rounds still in the air. Held here rather than
    // left in _entries because the key carries the Vehicle, and a destroyed craft reachable from a
    // dictionary is exactly what this roster's own note says must not happen. A loose system holds
    // a Celestial and a captured name instead, and is dropped the moment its last round lands --
    // so the lifetime is one flight, not the session.
    private readonly List<WeaponSystem> _loose = [];

    /// <summary>Systems still flying rounds for a craft that no longer exists.</summary>
    public IReadOnlyList<WeaponSystem> Loose => _loose;

    /// <summary>
    /// The shortest step any round in the world needs, and whether there is anything up at all.
    ///
    /// <para>Every round, not every crewed system's: a round whose launcher has been destroyed is
    /// still being integrated, so letting the world run away from it steps it over its own fuse
    /// radius exactly as it would any other. One walk, because the warp policy and the integration
    /// clamp are two readings of the same question and must not disagree.</para>
    /// </summary>
    /// <remarks>
    /// The <em>integration</em> limit: how long a step the sub-stepping inside a round can still
    /// resolve. It bounds a clamp that discards time, so tightening it does not slow anything down
    /// — it makes the round fall behind the world. What a round would <em>prefer</em> is
    /// <see cref="WarpTargetStep"/>, which slows the world instead.
    /// </remarks>
    public double FaithfulStep(out bool anyInFlight)
    {
        double faithful = double.MaxValue;
        anyInFlight = false;

        foreach (Entry e in _entries.Values)
        {
            foreach (IProjectile round in e.Battery.Rounds)
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
    /// The step the world should be held to, which is not the same as the one a round can survive.
    ///
    /// <para>Asked of each round rather than of its profile, because what a round needs depends on
    /// where it is: a warhead coasting in vacuum can take a third of a second and the same warhead
    /// entering the atmosphere cannot. Driving <see cref="WarpPolicy"/> with this is what lets the
    /// world run fast through a six-minute coast and slow itself for the minute of entry that
    /// decides where the round lands — <b>without</b> shortening the round's own step, which would
    /// simply drop the difference on the floor.</para>
    /// </summary>
    public double WarpTargetStep()
    {
        double target = double.MaxValue;
        bool any = false;

        foreach (Entry e in _entries.Values)
        {
            foreach (IProjectile round in e.Battery.Rounds)
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
    /// Whether this roster is still running a system — crewed on a live craft, or loose and seeing
    /// its last rounds down.
    ///
    /// <para>Asked by every effect that holds a pooled emitter or an audio channel, to decide
    /// whether the thing it is drawing for still exists. <b>Loose systems count.</b> Testing only
    /// the crewed ones takes the plume and the motor sound off every round on the frame its
    /// launcher dies, which is the one frame anybody is watching.</para>
    /// </summary>
    public bool Knows(object? owner)
    {
        if (owner is null) return false;

        foreach (Entry e in _entries.Values)
        {
            if (ReferenceEquals(e.Battery, owner)) return true;
        }

        for (int i = 0; i < _loose.Count; i++)
        {
            if (ReferenceEquals(_loose[i], owner)) return true;
        }

        return false;
    }

    // Names already reported as shared, so it is said once each rather than every survey.
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

    /// <summary>
    /// Whether anything on this craft is transmitting — the only thing an anti-radiation round can
    /// home on.
    ///
    /// <para>Asked of the roster because only the roster knows every system in the world, and a
    /// craft is a target for such a round because of what it is <em>doing</em> rather than what it
    /// carries: a site whose set is silent is not one, and neither is a craft whose only sensor is
    /// an infrared seeker or an optical head. Several launchers on one craft mean one is enough.</para>
    /// </summary>
    public bool IsEmitting(Vehicle craft)
    {
        foreach (Entry entry in _entries.Values)
        {
            if (!ReferenceEquals(entry.Craft, craft)) continue;
            if (entry.Battery.Sensor.Emits && !entry.Policy.RadarSilent) return true;
        }

        return false;
    }

    /// <summary>
    /// The <em>selected</em> weapon on a craft, or null if it carries no weapons system.
    ///
    /// <para>Every consumer that used to mean "the system on this craft" still gets one, which is
    /// what let a craft grow several launchers without any of them changing: the panel, the sight,
    /// the chase camera and the manual trigger all ask this and all follow the selection.</para>
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

    /// <summary>Every weapon on a craft, in part order. Cleared and refilled.</summary>
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

    /// <summary>Selects a weapon on a craft by its launcher ordinal.</summary>
    public void Select(Vehicle? craft, int ordinal)
    {
        if (craft is null || !_entries.ContainsKey((craft, ordinal))) return;

        _selected[craft] = ordinal;
        Log.Info($"selected {_entries[(craft, ordinal)].DisplayName} "
                 + $"({ordinal + 1}) on {KsaWorld.DisplayName(craft)}");
    }

    /// <summary>
    /// Steps the selection round a craft's weapons, wrapping. <paramref name="by"/> is +1 for the
    /// next and -1 for the previous.
    /// </summary>
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
    /// Where a launcher went this frame, for anything else that has to follow it.
    ///
    /// <para>One decision, consulted twice: the ballistic computer keys on the craft too, and a
    /// disagreement about which craft the shot is on breaks the release in either direction.</para>
    /// </summary>
    public IReadOnlyList<(Vehicle From, Vehicle To)> Handovers => _handovers;

    private readonly List<(Vehicle From, Vehicle To)> _handovers = [];
    private readonly List<HandoverCandidate> _candidates = [];
    private readonly List<Vehicle> _handoverScratch = [];

    /// <summary>
    /// Brings the roster in line with what has been surveyed: crew anything new, follow anything
    /// a decoupler carried onto another craft, forget anything destroyed.
    /// </summary>
    public void Sync(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems)
    {
        // Before the crewing loop below, and it has to be: it re-keys entries onto craft the loop
        // is about to walk, and crewing first would put a second battery on the same launcher with
        // a full magazine and default settings.
        FollowDecoupledLaunchers();

        for (int i = 0; i < systems.Count; i++)
        {
            Vehicle craft = systems[i].Craft;
            if (!KsaWorld.IsAlive(craft)) continue;

            // The panel lists everything this mod recognises, including a craft carrying only an
            // optical director. Only the ones that shoot get a battery: crewing the rest gives
            // them a launcher-less system running on whichever profile is first in the registry.
            if (!systems[i].Inventory.IsWeaponSystem) continue;

            // One per launcher *part*, read off the craft rather than off the inventory: the
            // ordinal a system is crewed at has to be the one LauncherPart.FindNth will resolve,
            // and the survey counts components rather than walking that same list.
            LauncherPart.FindAll(craft, _launcherScratch);

            for (int ordinal = 0; ordinal < _launcherScratch.Count; ordinal++)
            {
                if (_entries.ContainsKey((craft, ordinal))) continue;

                // Settings are keyed on the display name, which craft from one blueprint share.
                // They will restore each other's and overwrite each other on save, and nothing
                // else would ever say so.
                if (ordinal == 0) WarnIfNameIsTaken(craft);

                SystemConfig policy = new();

                // Whatever this weapon was last set to. Applied before the battery exists so its
                // first frame runs on the restored settings rather than on defaults it then
                // overwrites.
                if (SettingsStore.For(SettingsKey(craft, ordinal)) is { } stored)
                {
                    stored.ApplyTo(policy);

                    // And put its teams back on the session's roster. Memberships are stored per
                    // system and the names are session-wide, so restoring only the first half
                    // leaves every system sure of its allegiance in a world that has forgotten the
                    // teams exist. Every contact is then Unknown, which is engageable by default.
                    stored.DeclareTeams(_config.TeamNames);
                }

                WeaponSystem battery = new(_config, policy, ordinal, IsEmitting);

                // Pinned on creation, so ResolvePlatform leaves it alone. Without this every
                // battery would independently elect the craft being flown and they would all pile
                // onto it.
                battery.PinPlatform(craft);

                _entries[(craft, ordinal)] = new Entry(battery, policy, craft, ordinal);
                Log.Info($"crewed {KsaWorld.DisplayName(craft)} launcher {ordinal + 1} "
                         + $"of {_launcherScratch.Count}");
            }
        }

        _gone.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) _gone.Add(kv.Key);
        }

        foreach ((Vehicle Craft, int Ordinal) key in _gone)
        {
            WeaponSystem battery = _entries[key].Battery;

            // A fired round does not belong to the launcher any more, so losing the launcher is
            // not a reason to un-fire it: a seeker homes on its own and an anti-radiation round
            // already carries the emission it remembers. The system stays alive to fly them, with
            // the body they are over as their anchor, and is dropped when the last one lands.
            //
            // Read the name before detaching -- it is the only thing that still knows what fired
            // them, and it is the team identity as well as the label.
            if (battery.GoLoose(KsaWorld.ParentBody(key.Craft), KsaWorld.DisplayName(key.Craft)))
            {
                _loose.Add(battery);
            }
            else
            {
                battery.Reset();

                // Anything keyed on the system rather than on the craft has to be told, or its
                // entry outlives the craft and keeps a destroyed vehicle reachable for the session.
                Diagnostics.Forget(battery);
            }

            _entries.Remove(key);
            _selected.Remove(key.Craft);
            Log.Info("a crewed system was destroyed");
        }

        ReapLoose();
    }

    /// <summary>
    /// Flies the rounds of every system whose craft has gone, and drops each one as it empties.
    /// </summary>
    public void UpdateLoose(double dt, IReadOnlyList<IContact>? airborne)
    {
        for (int i = 0; i < _loose.Count; i++) _loose[i].UpdateLoose(dt, airborne);

        ReapLoose();
    }

    // A loose system exists only for what it still has up.
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

    // What a weapon's settings are filed under.
    //
    // The first launcher keeps the bare craft name, so a save written before a craft could carry
    // several still restores. Anything beyond it is suffixed, because two racks on one craft
    // sharing one entry would share an arm switch -- and arming one to drop a bomb would arm the
    // other.
    // A launcher that is no longer on the craft it was crewed on, because a decoupler carried it
    // onto another. Retirement is the wrong answer - that throws the magazine away and stops fire
    // control for good, and this launcher is alive with rounds still in its tubes.
    //
    // Costs nothing when nothing has separated: an entry only loses its launcher if a part tree
    // says so, so the vehicle scan below runs on the frame after a split and on no other.
    private void FollowDecoupledLaunchers()
    {
        _handovers.Clear();

        bool anyLost = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (kv.Value.Battery is { Platform: not null, Launcher: null }) { anyLost = true; break; }
        }

        if (!anyLost) return;

        KsaWorld.CollectVehicles(_handoverScratch);
        if (_handoverScratch.Count == 0) return;

        _gone.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (kv.Value.Battery is not { Platform: not null, Launcher: null }) continue;
            _gone.Add(kv.Key);
        }

        for (int i = 0; i < _gone.Count; i++)
        {
            if (!_entries.TryGetValue(_gone[i], out Entry entry)) continue;
            TryFollow(_gone[i], entry);
        }

        _gone.Clear();
    }

    private void TryFollow((Vehicle Craft, int Ordinal) key, Entry entry)
    {
        WeaponSystem battery = entry.Battery;
        string wanted = battery.Profile.PartId;

        _candidates.Clear();

        for (int i = 0; i < _handoverScratch.Count; i++)
        {
            Vehicle craft = _handoverScratch[i];
            if (!KsaWorld.IsAlive(craft) || ReferenceEquals(craft, key.Craft)) continue;

            LauncherPart.FindAll(craft, _launcherScratch);

            for (int ordinal = 0; ordinal < _launcherScratch.Count; ordinal++)
            {
                // On the part Id, not on the Part reference: KSA rebuilds the tree during staging,
                // so a reference does not survive the very event this is reacting to.
                if (_launcherScratch[ordinal].Profile.PartId != wanted) continue;

                _candidates.Add(new HandoverCandidate(
                    i, ordinal,
                    Vec.Len(KsaWorld.PositionEcl(craft) - battery.PlatformEcl),
                    _entries.ContainsKey((craft, ordinal))));
            }
        }

        Handover choice = PlatformHandover.Choose(_candidates);

        if (choice.Verdict == HandoverVerdict.Ambiguous)
        {
            Log.Warn($"{KsaWorld.DisplayName(key.Craft)} launcher {key.Ordinal + 1} lost its "
                     + $"launcher and {choice.Why} - leaving it where it is");
            return;
        }

        if (choice.Verdict != HandoverVerdict.Move) return;

        Vehicle to = _handoverScratch[choice.CraftIndex];
        (Vehicle, int) newKey = (to, choice.Ordinal);

        if (_entries.ContainsKey(newKey)) return;

        _entries.Remove(key);
        _entries[newKey] = entry with { Craft = to, Ordinal = choice.Ordinal };

        if (_selected.TryGetValue(key.Craft, out int selected) && selected == key.Ordinal)
        {
            _selected.Remove(key.Craft);
            _selected[to] = choice.Ordinal;
        }

        if (choice.Ordinal == 0) WarnIfNameIsTaken(to);

        battery.Rehome(to, choice.Ordinal);
        _handovers.Add((key.Craft, to));

        Log.Info($"{KsaWorld.DisplayName(key.Craft)} launcher {key.Ordinal + 1} followed its "
                 + $"launcher onto {KsaWorld.DisplayName(to)} launcher {choice.Ordinal + 1} "
                 + $"({choice.Why}); settings now filed under \"{SettingsKey(to, choice.Ordinal)}\"");
    }

    private static string SettingsKey(Vehicle craft, int ordinal)
        => ordinal == 0 ? KsaWorld.DisplayName(craft) : $"{KsaWorld.DisplayName(craft)}#{ordinal + 1}";

    /// <summary>
    /// The system to show when nothing has been chosen — the one being flown if it is armed,
    /// otherwise any of them. Never null while the roster is not empty.
    /// </summary>
    public Vehicle? Default()
    {
        if (For(KsaWorld.ControlledVehicle) is not null) return KsaWorld.ControlledVehicle;

        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries) return kv.Key.Craft;
        return null;
    }

    /// <summary>
    /// Writes down anything that has changed. Cheap to call often: the store compares against what
    /// it already holds and only reports a change when there is one.
    /// </summary>
    public void Remember()
    {
        string scope = SettingsStore.CurrentScope;
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
            return;
        }

        // Only when the game saves. A load then finds the settings as they were at that moment,
        // because nothing has been written over them since.
        long stamp = KsaWorld.CurrentSaveStamp();
        if (stamp == 0 || stamp == _savedAt) return;

        bool firstSight = _savedAt == 0;
        _savedAt = stamp;

        // The first stamp seen is the save as it already was, not the player saving.
        if (firstSight) return;

        Log.Info("settings: the game saved, writing the systems' settings with it");

        bool changed = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) continue;
            changed |= SettingsStore.Remember(SettingsKey(kv.Key.Craft, kv.Key.Ordinal),
                                              kv.Value.Policy);
        }

        if (changed) SettingsStore.Save();
    }

    // Re-reads every live battery's settings from the store. Used when the save changes under a
    // roster that is still holding the previous one's.
    private void Adopt()
    {
        int applied = 0;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) continue;
            if (SettingsStore.For(SettingsKey(kv.Key.Craft, kv.Key.Ordinal)) is not { } stored) continue;

            stored.ApplyTo(kv.Value.Policy);
            applied++;
        }

        if (applied > 0) Log.Info($"settings: re-read {applied} system(s) for the open save");
    }

    /// <summary>
    /// Writes now, whatever the save has done. For unload, and for anything that has to take
    /// effect immediately rather than at the next save.
    /// </summary>
    public void WriteNow()
    {
        bool changed = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) continue;
            changed |= SettingsStore.Remember(SettingsKey(kv.Key.Craft, kv.Key.Ordinal),
                                              kv.Value.Policy);
        }

        if (changed) SettingsStore.Save();
    }

    public void Clear()
    {
        // Last chance: a battery about to be forgotten still holds settings someone chose.
        WriteNow();

        foreach (Entry e in _entries.Values) e.Battery.Reset();
        _entries.Clear();

        foreach (WeaponSystem loose in _loose) loose.Reset();
        _loose.Clear();
    }
}
