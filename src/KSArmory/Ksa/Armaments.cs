using KSA;

namespace KSArmory;

/// <summary>
/// What each craft's weapons are doing between them, as against what each of them is doing alone.
///
/// <para>Today that is one thing: how many rounds the craft has in the air against each target, so
/// two rails on one aircraft stop each firing a full salvo at it. <see cref="TargetAllocation"/>
/// is the tally and the reasoning; this is where one is kept per craft.</para>
///
/// <para><b>Derived every frame, never pinned, and that is deliberate.</b> The rosters that follow
/// a <em>part</em> — <see cref="WeaponSystems"/> and <see cref="OpticalHeads"/> — have to be
/// pinned, because they carry an operator's settings and a magazine that must survive a craft
/// splitting in two. This carries nothing worth keeping: the tally is rebuilt from the rounds
/// actually in the air, so re-deriving it costs the same as carrying it and cannot be wrong.</para>
///
/// <para>That answers the split cases without any handover logic, which is better than sharing
/// one: a craft has an armament because its weapons are crewed on it, so the half of a broken
/// craft that keeps the launcher keeps the coordination, the half that keeps only the cockpit gets
/// none, and a craft that breaks into two armed halves gets one each. Nothing decides where a part
/// went — the weapons roster already decided, and this reads it. A third roster searching for
/// itself could land the coordinator on one fragment and its weapons on the other.</para>
///
/// <para><b>Control has nothing to do with it.</b> A Pantsir on a hillside with nobody aboard
/// defends itself, so whether a fragment carries a command module never enters into whether it has
/// an armament.</para>
/// </summary>
internal sealed class Armaments
{
    private readonly Dictionary<Vehicle, TargetAllocation> _byCraft =
        new(ReferenceEqualityComparer.Instance);

    // Craft seen this pass, so one that has gone is dropped rather than holding a destroyed
    // vehicle reachable for the session -- the same rule the weapons roster follows.
    private readonly List<Vehicle> _stale = [];

    /// <summary>
    /// Rebuilds every craft's tally from what its weapons have in the air.
    ///
    /// <para><b>Before any system decides to fire, not during.</b> A tally half-built is a tally
    /// that reports capacity the craft has already spent, and which systems it under-counts would
    /// be decided by the roster's iteration order. Same rule as the airborne sample.</para>
    /// </summary>
    public void Refresh(IEnumerable<WeaponSystems.Entry> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);

        foreach (TargetAllocation a in _byCraft.Values) a.Clear();

        foreach (WeaponSystems.Entry entry in systems)
        {
            if (entry.Battery.Platform is not { } craft) continue;

            TargetAllocation tally = For(craft);

            // Handed to the system here rather than by the caller, so it cannot be stepped with a
            // tally from a craft it has since been rehomed off.
            entry.Battery.CraftRounds = tally;

            foreach (IProjectile round in entry.Battery.Rounds)
            {
                if (round.State == RoundState.Flying) tally.Commit(round.TargetRef);
            }
        }

        Prune(systems);
    }

    /// <summary>One craft's tally, created on demand.</summary>
    public TargetAllocation For(Vehicle craft)
    {
        if (_byCraft.TryGetValue(craft, out TargetAllocation? tally)) return tally;

        tally = new TargetAllocation();
        _byCraft[craft] = tally;
        return tally;
    }

    public void Clear() => _byCraft.Clear();

    private void Prune(IEnumerable<WeaponSystems.Entry> systems)
    {
        _stale.Clear();

        foreach (Vehicle craft in _byCraft.Keys)
        {
            if (!KsaWorld.IsAlive(craft)) { _stale.Add(craft); continue; }

            bool stillArmed = false;
            foreach (WeaponSystems.Entry entry in systems)
            {
                if (ReferenceEquals(entry.Battery.Platform, craft)) { stillArmed = true; break; }
            }

            if (!stillArmed) _stale.Add(craft);
        }

        for (int i = 0; i < _stale.Count; i++) _byCraft.Remove(_stale[i]);
        _stale.Clear();
    }
}
