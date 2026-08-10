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
/// </summary>
internal sealed class OpticalHeads(Config config)
{
    internal sealed record Entry(OpticalHead Head, OpticConfig Policy);

    private readonly Config _config = config;

    // Craft plus ordinal, because one craft can carry several and each is its own instrument.
    private readonly Dictionary<(Vehicle Craft, int Ordinal), Entry> _entries = [];

    private readonly List<(Vehicle, int)> _stale = [];
    private readonly List<(Part, OpticProfile)> _scratch = [];

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
    /// Brings the roster in line with the world: crew every director now fitted, forget every one
    /// that has gone with its craft or been staged away.
    /// </summary>
    public void Sync(IReadOnlyList<Vehicle> craft)
    {
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
