using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="OpticalHead"/> per director fitted, each with its own <see cref="OpticConfig"/>.
///
/// <para>Keyed on the craft <em>and</em> which director on it, unlike <see cref="WeaponSystems"/>,
/// which crews one battery per craft. A director is a small part that a craft can sensibly carry
/// several of — one forward, one aft — and each is an instrument in its own right pointed
/// somewhere different. Sharing one head between them would make the second one scenery.</para>
///
/// <para>Heads are crewed independently of weapons: a craft with a director and no armament gets
/// one, and a craft with a launcher and no director gets none.</para>
///
/// <para>Which is also why this roster runs its own handover rather than reading the weapon
/// roster's. A director is a separate part, so a split can leave it and a launcher on opposite
/// halves — and a craft carrying nothing but a director publishes no launcher handover at all. The
/// <em>decision</em> is shared: <see cref="PlatformHandover"/> draws the line for both.</para>
/// </summary>
internal sealed class OpticalHeads(Config config)
{
    internal sealed record Entry(OpticalHead Head, OpticConfig Policy);

    private readonly Config _config = config;

    // Craft plus ordinal, because one craft can carry several and each is its own instrument.
    private readonly Dictionary<(Vehicle Craft, int Ordinal), Entry> _entries = [];

    private readonly List<(Vehicle, int)> _stale = [];
    private readonly List<(Part Part, OpticProfile Profile)> _scratch = [];
    private readonly List<(Vehicle Craft, int Ordinal)> _lost = [];
    private readonly List<HandoverCandidate> _candidates = [];

    public int Count => _entries.Count;

    /// <summary>Every crewed head, in no particular order.</summary>
    public IEnumerable<Entry> All => _entries.Values;

    /// <summary>The heads on one craft, appended in part order.</summary>
    public void On(Vehicle? craft, List<Entry> into)
    {
        into.Clear();
        if (craft is null) return;

        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (ReferenceEquals(kv.Key.Craft, craft)) into.Add(kv.Value);
        }

        into.Sort((a, b) => a.Head.Ordinal.CompareTo(b.Head.Ordinal));
    }

    /// <summary>
    /// The head whose picture the player is being shown, or the first if none has claimed a view.
    ///
    /// <para>The frame hook drives exactly one head, so asking for the first made every control
    /// under a second director inert — settable, saved, and with nothing reading them. Asking
    /// which one holds a viewport makes the row the player is looking at the row that works.</para>
    /// </summary>
    public Entry? Driving(Vehicle? craft)
    {
        Entry? claimed = null;

        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!ReferenceEquals(kv.Key.Craft, craft)) continue;
            if (kv.Value.Policy.Viewport < 0) continue;

            if (claimed is null || kv.Value.Head.Ordinal < claimed.Head.Ordinal) claimed = kv.Value;
        }

        return claimed ?? FirstOn(craft);
    }

    /// <summary>The first head on a craft, or null if it carries none.</summary>
    public Entry? FirstOn(Vehicle? craft)
    {
        Entry? best = null;

        if (craft is null) return null;

        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!ReferenceEquals(kv.Key.Craft, craft)) continue;
            if (best is null || kv.Value.Head.Ordinal < best.Head.Ordinal) best = kv.Value;
        }

        return best;
    }

    /// <summary>
    /// Brings the roster in line with the world: crew every director now fitted, follow every one
    /// a decoupler carried onto another craft, forget every one that has gone with its craft.
    /// </summary>
    public void Sync(IReadOnlyList<Vehicle> craft)
    {
        // Before the crewing loop, and it has to be: it re-keys entries onto craft the loop below
        // is about to walk, and crewing first would put a second head on the same director —
        // parked, at default zoom, watching nothing — with the operator's settings left behind.
        FollowDecoupledDirectors(craft);

        for (int i = 0; i < craft.Count; i++)
        {
            Vehicle v = craft[i];
            if (!KsaWorld.IsAlive(v)) continue;

            OpticParts.FindAll(v, _scratch);

            for (int ordinal = 0; ordinal < _scratch.Count; ordinal++)
            {
                if (_entries.ContainsKey((v, ordinal))) continue;

                OpticConfig policy = new();
                OpticalHead head = new(_config, policy, v, ordinal);

                _entries[(v, ordinal)] = new Entry(head, policy);
                Log.Info($"crewed an optical director on {KsaWorld.DisplayName(v)}");
            }
        }

        _stale.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft)) _stale.Add(kv.Key);
        }

        foreach ((Vehicle, int) key in _stale)
        {
            _entries.Remove(key);
            Log.Info("an optical director was destroyed");
        }
    }

    // A director that is no longer on the craft its head was crewed on, because a decoupler carried
    // it onto another. Retirement is the wrong answer: the director is alive on a live craft, and
    // dropping the entry throws away everything the operator set on it.
    //
    // Costs nothing when nothing has separated: an entry only loses its director if a part tree
    // says so, so the scan below runs on the frame after a split and on no other.
    private void FollowDecoupledDirectors(IReadOnlyList<Vehicle> craft)
    {
        bool anyLost = false;
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (HasLostItsDirector(kv.Key.Craft, kv.Value)) { anyLost = true; break; }
        }

        if (!anyLost || craft.Count == 0) return;

        _lost.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (HasLostItsDirector(kv.Key.Craft, kv.Value)) _lost.Add(kv.Key);
        }

        for (int i = 0; i < _lost.Count; i++)
        {
            if (!_entries.TryGetValue(_lost[i], out Entry? entry)) continue;

            if (TryFollow(craft, _lost[i], entry))
            {
                _fruitless.Remove(entry.Head);
                continue;
            }

            _fruitless.TryGetValue(entry.Head, out int misses);
            _fruitless[entry.Head] = ++misses;

            if (misses < FruitlessSearchesBeforeRetiring) continue;

            _entries.Remove(_lost[i]);
            _fruitless.Remove(entry.Head);
            Log.Info($"the director {_lost[i].Ordinal + 1} on "
                     + $"{KsaWorld.DisplayName(_lost[i].Craft)} has been destroyed and nothing "
                     + "else carries one - dropping its head");
        }

        _lost.Clear();
    }

    // How many consecutive searches may come back empty before a director is taken to be gone
    // rather than merely unreadable.
    //
    // Same bound and same reason as the weapons roster: a part tree read mid-rebuild returns
    // nothing, which is indistinguishable from a part a warhead has destroyed, so only a long
    // run of empty searches separates the two. Without it a destroyed director leaves its head
    // searching the whole world every simulated frame for ever.
    //
    // A head has nothing in the air, so it is simply dropped rather than loosed.
    private const int FruitlessSearchesBeforeRetiring = 120;

    // Consecutive searches that found no craft carrying a head's director.
    private readonly Dictionary<OpticalHead, int> _fruitless =
        new(ReferenceEqualityComparer.Instance);

    // Only while the craft it was crewed on is still alive. A destroyed one's head is dropped by
    // the stale sweep in Sync, and searching from it would hand the entry to whichever neighbour
    // carries the same part — a head left running for a craft that has gone.
    private static bool HasLostItsDirector(Vehicle craft, Entry entry)
        => entry.Head is { Platform: not null, Director: null } && KsaWorld.IsAlive(craft);

    // Whether the entry is settled: either its director was found somewhere and the head moved,
    // or the search was refused for a reason that is not "nothing carries it".
    private bool TryFollow(IReadOnlyList<Vehicle> craft, (Vehicle Craft, int Ordinal) key, Entry entry)
    {
        string wanted = entry.Head.Profile.PartId;

        _candidates.Clear();

        for (int i = 0; i < craft.Count; i++)
        {
            Vehicle other = craft[i];
            if (!KsaWorld.IsAlive(other) || ReferenceEquals(other, key.Craft)) continue;

            OpticParts.FindAll(other, _scratch);

            for (int ordinal = 0; ordinal < _scratch.Count; ordinal++)
            {
                // On the part Id, not on the Part reference: KSA rebuilds the tree during staging,
                // so a reference does not survive the very event this is reacting to.
                if (_scratch[ordinal].Profile.PartId != wanted) continue;

                _candidates.Add(new HandoverCandidate(
                    i, ordinal,
                    Vec.Len(KsaWorld.PositionEcl(other) - entry.Head.PlatformEcl),
                    _entries.ContainsKey((other, ordinal))));
            }
        }

        Handover choice = PlatformHandover.Choose(_candidates);

        if (choice.Verdict == HandoverVerdict.Ambiguous)
        {
            Log.Warn($"the director {key.Ordinal + 1} on {KsaWorld.DisplayName(key.Craft)} has gone "
                     + $"and {choice.Why} - leaving its head where it is");

            // Settled, though not moved: two craft carry it and neither was chosen. Dropping the
            // head over a refusal would throw the operator's settings away for an excess of
            // candidates.
            return true;
        }

        if (choice.Verdict != HandoverVerdict.Move) return false;

        Vehicle to = craft[choice.CraftIndex];
        (Vehicle, int) newKey = (to, choice.Ordinal);

        // The craft carrying it already has a head on that ordinal, so the director is not missing
        // from the world - it is simply already crewed.
        if (_entries.ContainsKey(newKey)) return true;

        _entries.Remove(key);
        _entries[newKey] = entry;

        entry.Head.Rehome(to, choice.Ordinal);

        Log.Info($"director {key.Ordinal + 1} on {KsaWorld.DisplayName(key.Craft)} followed its "
                 + $"part onto {KsaWorld.DisplayName(to)} as director {choice.Ordinal + 1} "
                 + $"({choice.Why})");

        return true;
    }

    /// <summary>Reads the world once for every head, before anything is drawn against it.</summary>
    public void SampleWorld()
    {
        foreach (Entry e in _entries.Values) e.Head.SampleWorld();
    }

    /// <summary>One simulated step for every head.</summary>
    public void Update(double dt, IReadOnlyList<IContact>? airborne = null)
    {
        foreach (Entry e in _entries.Values) e.Head.Update(dt, airborne);
    }

    public void Clear() => _entries.Clear();
}
