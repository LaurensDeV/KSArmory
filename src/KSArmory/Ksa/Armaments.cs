using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="TargetAllocation"/> per craft: the rounds its weapons have in the air against each target.
/// <para>Derived every frame and never pinned: the tally is rebuilt from the rounds in the air, so a
/// split needs no handover. CLAUDE.md, "Ksa/Armaments.cs is derived every frame", has why.</para>
/// </summary>
internal sealed class Armaments
{
    private readonly Dictionary<Vehicle, TargetAllocation> _byCraft =
        new(ReferenceEqualityComparer.Instance);

    // So a craft that has gone is dropped rather than holding a destroyed vehicle reachable.
    private readonly List<Vehicle> _stale = [];

    /// <summary>
    /// Called before any system decides to fire: a half-built tally reports spent capacity, and which
    /// systems it under-counts would follow the roster's iteration order.
    /// </summary>
    public void Refresh(IEnumerable<WeaponSystems.Entry> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);

        foreach (TargetAllocation a in _byCraft.Values) a.Clear();

        foreach (WeaponSystems.Entry entry in systems)
        {
            if (entry.Weapon.Platform is not { } craft) continue;

            TargetAllocation tally = For(craft);

            // Handed to the system here rather than by the caller, so it cannot be stepped with a
            // tally from a craft it has since been rehomed off.
            entry.Weapon.CraftRounds = tally;

            foreach (IProjectile round in entry.Weapon.Rounds)
            {
                if (round.State == RoundState.Flying) tally.Commit(round.TargetRef);
            }
        }

        Prune(systems);
    }

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
                if (ReferenceEquals(entry.Weapon.Platform, craft)) { stillArmed = true; break; }
            }

            if (!stillArmed) _stale.Add(craft);
        }

        for (int i = 0; i < _stale.Count; i++) _byCraft.Remove(_stale[i]);
        _stale.Clear();
    }
}
