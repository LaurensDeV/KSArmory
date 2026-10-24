using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="OpticalHead"/> per director fitted, each with its own <see cref="OpticConfig"/>.
///
/// <para>Keyed on the craft <em>and</em> which director on it, since a craft can carry several.</para>
///
/// <para>Crewed independently of weapons, with its own handover: a split can leave a director and a
/// launcher on opposite halves. The decision is shared, <see cref="PlatformHandover"/>.</para>
/// </summary>
internal sealed class OpticalHeads(Config config)
{
    internal sealed record Entry(OpticalHead Head, OpticConfig Policy);

    private readonly Config _config = config;

    private readonly Dictionary<(Vehicle Craft, int Ordinal), Entry> _entries = [];

    private readonly List<(Vehicle, int)> _stale = [];
    private readonly List<(Part Part, OpticProfile Profile)> _scratch = [];
    private readonly List<(Vehicle Craft, int Ordinal)> _lost = [];
    private readonly List<HandoverCandidate> _candidates = [];

    public int Count => _entries.Count;

    /// <summary>Every crewed head, in no particular order.</summary>
    public IEnumerable<Entry> All => _entries.Values;

    /// <summary>The head crewed on a part, or null.</summary>
    public OpticalHead? OnPart(Part part)
    {
        foreach (Entry e in _entries.Values)
        {
            if (ReferenceEquals(e.Head.Director, part)) return e.Head;
        }

        return null;
    }

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
    /// <para>The frame hook drives exactly one head, so this is the one holding a viewport.</para>
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
        // Before the crewing loop: it re-keys entries onto craft the loop walks, and crewing first
        // would put a second, default head on the same director.
        FollowDecoupledDirectors(craft);

        for (int i = 0; i < craft.Count; i++)
        {
            Vehicle v = craft[i];
            if (!KsaWorld.IsAlive(v)) continue;

            // A pod that is the whole craft, left by a breakup, has nothing to look out from
            // (KsaWorld.PartRides); a hull with a director on it is still an observation post.
            if (!KsaWorld.HasPlatform(v)) continue;

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

    // A director a decoupler carried onto another craft keeps its head and settings. The scan runs
    // only on the frame after a split.
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

    // How many consecutive searches may come back empty before a director is taken to be gone: a
    // tree read mid-rebuild returns nothing too. Same bound as the weapons roster.
    private const int FruitlessSearchesBeforeRetiring = 120;

    // Consecutive searches that found no craft carrying a head's director.
    private readonly Dictionary<OpticalHead, int> _fruitless =
        new(ReferenceEqualityComparer.Instance);

    // Only while the craft it was crewed on is alive; a destroyed one's head is dropped in Sync.
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

            // Settled, though not moved: two craft carry it and neither was chosen.
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
        LaserDesignators.BeginFrame();
        foreach (Entry e in _entries.Values)
        {
            e.Head.Pictured = PictureFor?.Invoke(e.Head.Platform);
            e.Head.Update(dt, airborne);
        }
    }

    /// <summary>The contact the craft's picture hands out, which a head takes under its tracking switch.</summary>
    public Func<Vehicle?, object?>? PictureFor { get; set; }

    public void Clear() => _entries.Clear();
}
