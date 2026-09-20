namespace KSArmory;

/// <summary>
/// The places one bus is aimed at, and how many of its warheads each one gets.
///
/// <para>A salvo has always gone to a single designation, which is one site and one count. This is
/// that generalised to at most <see cref="MaxTargets"/>, and it is deliberately only the
/// <em>data</em>: nothing here flies a bus between targets, and a set of one behaves exactly as a
/// lone designation does. <c>docs/MIRV-TARGETS.md</c> is the whole plan.</para>
///
/// <para><b>Spare warheads stay aboard rather than being spread by default.</b> They can be given to
/// a target by raising its count, and warheads sharing a target are released <em>together</em> —
/// phase 0 measured that a bus's own warheads cannot destroy one another
/// (<c>WeaponSystem.FillIncoming</c> takes a system's own rounds out of the list the blast sweep
/// walks), so the arrival stagger this plan was first written around buys nothing and costs 8.33 m/s
/// of divert for half a second of separation, against 2.91 m/s for 2 km of ground.</para>
/// </summary>
internal sealed class TargetSet
{
    /// <summary>
    /// How many places one bus may be sent to.
    ///
    /// <para>Six because the bus carries six warheads, so past that a target gets nothing. It is not a
    /// budget: reaching six targets is bounded by divert and by the coast's length long before this
    /// is, and at 2,000 km only about three fit.</para>
    /// </summary>
    public const int MaxTargets = 6;

    private readonly List<Entry> _entries = [];

    /// <summary>One place, and how many warheads are meant for it.</summary>
    internal readonly record struct Entry(AimSite Site, int Warheads);

    /// <summary>The targets in the order they were chosen, which is not the order they are flown.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    public int Count => _entries.Count;

    /// <summary>How many warheads the set has spoken for.</summary>
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
    /// <para><b>Not necessarily the first chosen.</b> The order places were picked in is the
    /// player's; the order they are flown in belongs to the release schedule, which puts the
    /// farthest reach first because the kick's leverage decays as the bus descends.
    /// <see cref="ElectFarthestLead"/> is what moves it, and only while the aim is still free —
    /// <see cref="TargetEdit.LeadMayMove"/>. For a set of one it is entry zero whatever happens,
    /// which is what leaves a single-target flight exactly the shot it has always been.</para>
    /// </summary>
    public int LeadIndex { get; private set; }

    /// <summary>The lead's place, which is the one the whole flight is aimed at.</summary>
    public AimSite Primary => _entries.Count > 0 ? _entries[LeadIndex].Site : AimSite.None;

    /// <summary>Fly to this one first, refusing an entry that is not there.</summary>
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
    /// <para><b>Farthest, because the bus walks inward from wherever the booster puts it.</b> The
    /// reach a hop can buy falls as the bus descends — 1,076 down to 886 m of ground per m/s over a
    /// six-stop schedule — so the dearest hop belongs in the earliest slot, and the first stop is
    /// the one the bus arrives on for free. Aimed anywhere but an end of the set, the walk goes out
    /// and back and costs about twice what the itinerary charges, which is what
    /// <c>ReleaseWalkHold.NotWhereTheBusIsAimed</c> refuses.</para>
    ///
    /// <para><b>Ties keep the lead where it is</b>, and an unresolvable range ranks nearest rather
    /// than throwing the comparison: the aim is the one thing in the set that cannot be allowed to
    /// move on a reading nobody can make.</para>
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

    /// <summary>
    /// Take a designation as the whole set, which is what clicking the world has always done.
    /// </summary>
    public void SetOnly(AimSite site, int warheads)
    {
        Clear();
        if (site.IsSet) _entries.Add(new Entry(site, Math.Max(0, warheads)));
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
    /// The new target starts with <b>no warheads</b>. Giving it some would take them off a target the
    /// player has already aimed at, silently, which is the one thing a click on the map must not do —
    /// so the split is asked for rather than assumed (<see cref="Balance"/>).
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

    /// <summary>
    /// Give one target a number of warheads, bounded by what the others have not taken.
    /// </summary>
    /// <returns>What it actually got, which is less than asked when the rest are spoken for.</returns>
    public int SetWarheads(int index, int warheads, int available)
    {
        if (index < 0 || index >= _entries.Count) return 0;

        int elsewhere = Assigned - _entries[index].Warheads;
        int got = Math.Clamp(warheads, 0, Math.Max(0, available - elsewhere));

        _entries[index] = _entries[index] with { Warheads = got };
        return got;
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
    /// <remarks>
    /// The flight reads this and nothing else about the set, so a set of one target holding every
    /// warhead produces exactly the salvo that flies today.
    /// </remarks>
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
