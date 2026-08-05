using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="DefenceBattery"/> per weapons system in the world, each with its own
/// <see cref="BatteryConfig"/>.
///
/// <para>Every craft carrying a recognised part is crewed, permanently and independently: a
/// battery is pinned to the craft it was created for and never moves, so arming one site, sending
/// it a target or putting it on a team says nothing about any other. Before this there was a
/// single battery that elected one launcher, and every other system in the world was listed but
/// dead.</para>
///
/// <para>Keyed by <see cref="Vehicle"/> reference, which is what a craft compares by. Entries
/// appear when a system is surveyed and are dropped when the craft dies — a battery outliving its
/// platform would keep a destroyed vehicle alive in this dictionary for the session.</para>
/// </summary>
internal sealed class BatteryRoster(Config config)
{
    internal sealed record Entry(DefenceBattery Battery, BatteryConfig Policy);

    private readonly Config _config = config;
    private readonly Dictionary<Vehicle, Entry> _entries = [];
    private readonly List<Vehicle> _scratch = [];

    public int Count => _entries.Count;

    /// <summary>Every crewed system, in no particular order.</summary>
    public IEnumerable<Entry> All => _entries.Values;

    /// <summary>The battery running on a craft, or null if it carries no weapons system.</summary>
    public Entry? For(Vehicle? craft)
        => craft is not null && _entries.TryGetValue(craft, out Entry? e) ? e : null;

    /// <summary>
    /// Brings the roster in line with what has been surveyed: crew anything new, forget anything
    /// destroyed.
    /// </summary>
    public void Sync(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems)
    {
        for (int i = 0; i < systems.Count; i++)
        {
            Vehicle craft = systems[i].Craft;
            if (!KsaWorld.IsAlive(craft) || _entries.ContainsKey(craft)) continue;

            BatteryConfig policy = new();
            DefenceBattery battery = new(_config, policy);

            // Pinned on creation, so ResolvePlatform leaves it alone. Without this every battery
            // would independently elect the craft being flown and they would all pile onto it.
            battery.PinPlatform(craft);

            _entries[craft] = new Entry(battery, policy);
            Log.Info($"crewed {KsaWorld.DisplayName(craft)}");
        }

        _scratch.Clear();
        foreach (KeyValuePair<Vehicle, Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key)) _scratch.Add(kv.Key);
        }

        foreach (Vehicle craft in _scratch)
        {
            _entries[craft].Battery.Reset();
            _entries.Remove(craft);
            Log.Info("a crewed system was destroyed");
        }
    }

    /// <summary>
    /// The system to show when nothing has been chosen — the one being flown if it is armed,
    /// otherwise any of them. Never null while the roster is not empty.
    /// </summary>
    public Vehicle? Default()
    {
        if (For(KsaWorld.ControlledVehicle) is not null) return KsaWorld.ControlledVehicle;

        foreach (KeyValuePair<Vehicle, Entry> kv in _entries) return kv.Key;
        return null;
    }

    public void Clear()
    {
        foreach (Entry e in _entries.Values) e.Battery.Reset();
        _entries.Clear();
    }
}
