namespace KSArmory;

/// <summary>
/// The places one bus is aimed at, and how many of its warheads each one gets.
///
/// <para>Data only: nothing here flies a bus between targets, and a set of one behaves exactly as a
/// lone designation does. <c>docs/ICBM-GUIDANCE.md</c>, "Several targets".</para>
///
/// <para>Spare warheads stay aboard. Warheads sharing a target are released together, because a bus's
/// own warheads cannot destroy one another (<c>WeaponSystem.FillIncoming</c>) and a half-second
/// stagger costs 8.33 m/s of divert at 6,179 km.</para>
/// </summary>
internal sealed class TargetSet
{
    /// <summary>
    /// Seven because the Heavy MIRV Bus carries seven warheads. Not a budget: divert and the coast bound
    /// a set long before this does, and a target past a smaller rack simply gets nothing.
    /// </summary>
    public const int MaxTargets = 7;

    private readonly List<Entry> _entries = [];

    internal readonly record struct Entry(AimSite Site, int Warheads);

    /// <summary>The targets in the order they were chosen, which is not the order they are flown.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    public int Count => _entries.Count;

    public int Assigned
    {
        get
        {
            int total = 0;
            foreach (Entry e in _entries) total += e.Warheads;
            return total;
        }
    }

    /// <summary>
    /// Which entry the flight is flown to: the booster's target, and the first warheads away.
    ///
    /// <para>Not necessarily the first chosen: the release schedule flies the farthest reach first.
    /// <see cref="ElectFarthestLead"/> moves it, only while <see cref="TargetEdit.LeadMayMove"/>. For a
    /// set of one it is entry zero, so a single-target flight is unchanged.</para>
    /// </summary>
    public int LeadIndex { get; private set; }

    /// <summary>The place the whole flight is aimed at.</summary>
    public AimSite Primary => _entries.Count > 0 ? _entries[LeadIndex].Site : AimSite.None;

    public bool SetLead(int index)
    {
        if (index < 0 || index >= _entries.Count) return false;

        LeadIndex = index;
        return true;
    }

    /// <summary>
    /// Aim the booster at whichever entry is farthest downrange, which is what the release schedule
    /// needs of it.
    /// </summary>
    /// <remarks>
    /// <para>Farthest, because the bus walks inward from where the booster puts it and the reach a hop
    /// buys falls as it descends. Aimed anywhere but an end of the set, the walk goes out and back,
    /// which <c>ReleaseWalkHold.NotWhereTheBusIsAimed</c> refuses.</para>
    ///
    /// <para>Ties keep the lead where it is, and an unresolvable range ranks nearest, so the aim never
    /// moves on a reading nobody can make.</para>
    /// </remarks>
    /// <param name="rangeMetres">
    /// How far each entry is from the launch site, in the order of <see cref="Entries"/>. A list
    /// that does not match the set is refused rather than partly applied.
    /// </param>
    public bool ElectFarthestLead(IReadOnlyList<double> rangeMetres)
    {
        if (_entries.Count == 0 || rangeMetres.Count != _entries.Count) return false;

        int best = LeadIndex;
        double farthest = Ranged(rangeMetres[best]);

        for (int i = 0; i < _entries.Count; i++)
        {
            if (Ranged(rangeMetres[i]) > farthest)
            {
                farthest = Ranged(rangeMetres[i]);
                best = i;
            }
        }

        if (best == LeadIndex) return false;

        LeadIndex = best;
        return true;
    }

    private static double Ranged(double metres)
        => double.IsFinite(metres) ? metres : double.NegativeInfinity;

    /// <summary>Take a designation as the whole set.</summary>
    public void SetOnly(AimSite site, int warheads)
    {
        Clear();
        if (site.IsSet) _entries.Add(new Entry(site, Math.Max(0, warheads)));
    }

    /// <remarks>
    /// Restores counts and lead without <see cref="SetWarheads"/>, which bounds a count by the warheads aboard: a computer
    /// crewed from a save has not yet seen its magazine, and would read every count as zero.
    /// </remarks>
    public void Restore(IReadOnlyList<Entry> entries, int lead)
    {
        Clear();

        foreach (Entry e in entries)
        {
            if (_entries.Count == MaxTargets) break;
            if (e.Site.IsSet) _entries.Add(e with { Warheads = Math.Max(0, e.Warheads) });
        }

        LeadIndex = lead >= 0 && lead < _entries.Count ? lead : 0;
    }

    public void Clear()
    {
        _entries.Clear();
        LeadIndex = 0;
    }

    /// <summary>
    /// Add a place, refusing one the world could not resolve and one past <see cref="MaxTargets"/>.
    /// </summary>
    /// <remarks>
    /// The new target starts with no warheads: giving it some would silently take them off a target
    /// the player already aimed at. The split is asked for (<see cref="Balance"/>).
    /// </remarks>
    public bool TryAdd(AimSite site)
    {
        if (!site.IsSet || _entries.Count >= MaxTargets) return false;

        _entries.Add(new Entry(site, 0));
        return true;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count) return false;

        _entries.RemoveAt(index);

        // A lead that has just been taken away falls back to the first chosen, never to whatever
        // slid into its place: that would aim the flight at a place the removal did not name.
        if (index == LeadIndex) LeadIndex = 0;
        else if (index < LeadIndex) LeadIndex--;

        if (LeadIndex >= _entries.Count) LeadIndex = 0;

        return true;
    }

    /// <returns>What it actually got, bounded by what the others have not taken.</returns>
    public int SetWarheads(int index, int warheads, int available)
    {
        if (index < 0 || index >= _entries.Count) return 0;

        int elsewhere = Assigned - _entries[index].Warheads;
        int got = Math.Clamp(warheads, 0, Math.Max(0, available - elsewhere));

        _entries[index] = _entries[index] with { Warheads = got };
        return got;
    }

    /// <summary>
    /// Move warheads onto one target, by one or by several — what a click on a map does.
    /// </summary>
    /// <remarks>
    /// <para>It comes from whichever target holds the most, after any unassigned remainder is spent.
    /// Not from the lead: adding a target re-elects the lead to the farthest, which is often the empty
    /// new one.</para>
    ///
    /// <para><b>Giving one back does not name a destination.</b> It simply stops being assigned,
    /// and an unassigned warhead already rides to the lead — crediting it explicitly would be the
    /// same arithmetic written twice, and the two could then disagree.</para>
    ///
    /// <para>Refused rather than clamped when there are not enough: half a gesture leaves an
    /// operator believing a click did something it did not.</para>
    /// </remarks>
    public bool Shift(int index, int delta, int available)
    {
        if (index < 0 || index >= _entries.Count || delta == 0) return false;

        int want = _entries[index].Warheads + delta;
        if (want < 0) return false;

        if (delta < 0)
        {
            _entries[index] = _entries[index] with { Warheads = want };
            return true;
        }

        int spare = Math.Max(0, available - Assigned);
        int owed = delta - Math.Min(spare, delta);

        if (owed > 0)
        {
            int from = Fullest(index);
            if (from < 0 || _entries[from].Warheads < owed) return false;

            _entries[from] = _entries[from] with { Warheads = _entries[from].Warheads - owed };
        }

        _entries[index] = _entries[index] with { Warheads = want };
        return true;
    }

    // Which other target has the most to give. Ties keep the earliest, which is the order the
    // player stated them in.
    private int Fullest(int besides)
    {
        int found = -1;
        int most = 0;

        for (int i = 0; i < _entries.Count; i++)
        {
            if (i == besides || _entries[i].Warheads <= most) continue;

            most = _entries[i].Warheads;
            found = i;
        }

        return found;
    }

    /// <summary>
    /// Spread every warhead over the chosen targets: evenly, and the remainder to the earliest chosen.
    /// </summary>
    /// <remarks>
    /// The earliest rather than the nearest, because the order targets were picked in is the only one
    /// the player stated. The cheapest order to <em>fly</em> them is a different question and belongs to
    /// the release loop (<see cref="ReleaseItinerary"/>), which flies them farthest reach first.
    /// </remarks>
    public void Balance(int available)
    {
        if (_entries.Count == 0) return;

        int each = Math.Max(0, available) / _entries.Count;
        int spare = Math.Max(0, available) - (each * _entries.Count);

        for (int i = 0; i < _entries.Count; i++)
        {
            _entries[i] = _entries[i] with { Warheads = each + (i < spare ? 1 : 0) };
        }
    }

    /// <summary>
    /// Which target each warhead is meant for, in release order, or <c>-1</c> for one kept aboard.
    /// </summary>
    public int[] ReleasePlan(int available)
    {
        int[] plan = new int[Math.Max(0, available)];
        int next = 0;

        for (int target = 0; target < _entries.Count && next < plan.Length; target++)
        {
            for (int n = 0; n < _entries[target].Warheads && next < plan.Length; n++)
            {
                plan[next++] = target;
            }
        }

        for (; next < plan.Length; next++) plan[next] = -1;

        return plan;
    }

    /// <summary>What the panel says about the set, in one line.</summary>
    public string Describe(int available)
    {
        if (_entries.Count == 0) return "no targets";

        int spare = Math.Max(0, available - Assigned);
        string targets = _entries.Count == 1 ? "1 target" : $"{_entries.Count} targets";

        return spare == 0
                   ? $"{targets}, every warhead assigned"
                   : $"{targets}, {spare} warhead{(spare == 1 ? "" : "s")} kept aboard";
    }
}
