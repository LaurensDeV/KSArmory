using KSA;

namespace KSArmory;

/// <summary>
/// One picture per craft: every searching set aboard it — search parts and launcher radars — merged
/// by <see cref="PictureChoice"/>, and the contact to hand a director or a beam with nothing more
/// specific. Directors do not feed it: they hold only what they were handed.
///
/// <para>Derived every frame and never pinned, as <see cref="Armaments"/> is: it is rebuilt from the
/// sets' own tracks, so a split needs no handover. Only the last handle handed out is carried, for
/// <see cref="WatchChoice"/>'s hysteresis, and losing it costs one frame of it.</para>
///
/// <para>What it hands out is a handle, never a position: the taker's own set scans it fresh and
/// judges it on its own IFF, so a picture a step old pairs no epochs.</para>
/// </summary>
internal sealed class CraftPicture
{
    private sealed class Picture
    {
        public readonly List<IReadOnlyList<Track>> Sets = [];
        public readonly List<Track> Merged = [];
        public object? Best;
        public bool Seen;
    }

    private readonly Dictionary<Vehicle, Picture> _byCraft = new(ReferenceEqualityComparer.Instance);
    private readonly List<Vehicle> _stale = [];

    /// <summary>Whether a set searches for itself and shows what it found.</summary>
    public static bool Searches(SensorProfile sensor) => !sensor.CuedOnly && sensor.Scope == ScopePresentation.Search;

    public void Refresh(IEnumerable<WeaponSystems.Entry> systems, IEnumerable<SearchSet> sets)
    {
        foreach (Picture p in _byCraft.Values)
        {
            p.Seen = false;
            p.Sets.Clear();
        }

        foreach (WeaponSystems.Entry e in systems)
        {
            if (e.Weapon.Platform is { } craft && Searches(e.Weapon.Sensor)) For(craft).Sets.Add(e.Weapon.Radar.Tracks);
        }

        foreach (SearchSet s in sets)
        {
            if (s.Craft is { } craft && KsaWorld.IsAlive(craft)) For(craft).Sets.Add(s.Radar.Tracks);
        }

        _stale.Clear();
        foreach (KeyValuePair<Vehicle, Picture> kv in _byCraft)
        {
            Picture p = kv.Value;
            if (!p.Seen || !KsaWorld.IsAlive(kv.Key))
            {
                _stale.Add(kv.Key);
                continue;
            }

            PictureChoice.Merge(p.Sets, static t => t.Contact.Handle, p.Merged);
            int best = PictureChoice.Choose(p.Merged, static t => t.Contact.Handle, p.Best);
            p.Best = best >= 0 ? p.Merged[best].Contact.Handle : null;
        }

        for (int i = 0; i < _stale.Count; i++) _byCraft.Remove(_stale[i]);
    }

    /// <summary>The handle to hand out on this craft, or null.</summary>
    public object? BestFor(Vehicle? craft)
        => craft is not null && _byCraft.TryGetValue(craft, out Picture? p) ? p.Best : null;

    /// <summary>Every contact on this craft's picture, most urgent first.</summary>
    public IReadOnlyList<Track> TracksFor(Vehicle? craft)
        => craft is not null && _byCraft.TryGetValue(craft, out Picture? p) ? p.Merged : [];

    public void Clear() => _byCraft.Clear();

    private Picture For(Vehicle craft)
    {
        if (!_byCraft.TryGetValue(craft, out Picture? p))
        {
            p = new Picture();
            _byCraft[craft] = p;
        }

        p.Seen = true;
        return p;
    }
}
