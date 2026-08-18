using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="IcbmComputer"/> per craft this mod recognises a weapon on, crewed and forgotten
/// with the craft.
///
/// <para>Per <em>craft</em>, not per launcher, unlike <see cref="WeaponSystems"/> — and that is the
/// whole difference between the two rosters. A craft can sensibly carry two rails and fire them at
/// different things, but it has exactly one trajectory, so a second computer aboard would be a
/// second autopilot fighting the first for the same engines.</para>
///
/// <para>Fitting a KSArmory weapon is what confers it. That is the mod's usual rule — a part gives
/// a craft a capability — and here it also draws the line the player expects: strap the bus onto a
/// rocket and the rocket knows how to deliver it, leave it off and the mod does not reach for
/// somebody's launch vehicle.</para>
/// </summary>
internal sealed class IcbmComputers
{
    private readonly Dictionary<Vehicle, IcbmComputer> _computers = [];
    private readonly List<Vehicle> _stale = [];

    public int Count => _computers.Count;

    public IEnumerable<IcbmComputer> All => _computers.Values;

    public IcbmComputer? For(Vehicle? craft)
        => craft is not null && _computers.TryGetValue(craft, out IcbmComputer? c) ? c : null;

    /// <summary>
    /// The longest step any burning computer can be flown across, and whether one is burning.
    ///
    /// <para>The same question <see cref="WeaponSystems.FaithfulStep"/> answers for rounds, and it
    /// feeds the same policy. A powered guided burn is not something that degrades gracefully under
    /// timewarp: the cutoff lands on a frame boundary, so a long step is velocity left ungained,
    /// and at the steps high warp hands out that is thousands of metres a second.</para>
    /// </summary>
    public double FaithfulStep(out bool anyBurning)
    {
        anyBurning = false;

        foreach (IcbmComputer computer in _computers.Values)
        {
            if (!computer.Program.NeedsShortSteps) continue;

            // Not while KSA is running its own warp to a time. That mechanism lands the world where
            // it was asked to and stops; racing it down is the fight WarpPolicy stands down from
            // anyway, and from a thousand times speed the first slowdown it computes is nearly
            // zero — which pauses the game. The computer stops the warp itself when the window is
            // close, and the hold takes over from a speed it can work with.
            if (computer.Program.Phase == IcbmPhase.Holding && KsaWorld.IsAutoWarpActive) continue;

            anyBurning = true;
        }

        return anyBurning ? IcbmProgram.MaxFaithfulStep : double.MaxValue;
    }

    /// <summary>Stand every burning computer down, for a world that outran what it can fly.</summary>
    public void AbandonBurns(string why)
    {
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (computer.Program.IsBurning) computer.Abort(why);
        }
    }

    public void Sync(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems)
    {
        for (int i = 0; i < systems.Count; i++)
        {
            Vehicle craft = systems[i].Craft;
            if (!KsaWorld.IsAlive(craft)) continue;
            if (!systems[i].Inventory.IsWeaponSystem) continue;
            if (_computers.ContainsKey(craft)) continue;

            _computers[craft] = new IcbmComputer(craft, new IcbmConfig());
            Log.Debug($"ICBM computer crewed on {KsaWorld.DisplayName(craft)}");
        }

        Retire();
    }

    /// <summary>
    /// Step every computer, handing each the weapon aboard its own craft.
    ///
    /// <para>The weapon arrives as <see cref="IManualFire"/> rather than as a system, because
    /// letting a warhead go at a place is the whole of what a ballistic computer wants from one.
    /// It is resolved here rather than held, so a craft that loses its launcher stops being able
    /// to release without the computer having to notice.</para>
    /// </summary>
    public void Update(double simStep, double playerStep, WeaponSystems weapons)
    {
        foreach (IcbmComputer computer in _computers.Values)
        {
            computer.Update(simStep, playerStep, weapons.For(computer.Craft)?.Battery);
        }
    }

    /// <summary>Stand every computer down and forget them. What a scene change does.</summary>
    public void Clear()
    {
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (KsaWorld.IsAlive(computer.Craft)) computer.Abort("scene ended");
        }

        _computers.Clear();
    }

    // A destroyed craft's computer goes with it. Nothing is handed back, because there is nothing
    // left to hand it to - the same rule the rest of the mod follows about not keeping a dead
    // vehicle reachable.
    private void Retire()
    {
        _stale.Clear();

        foreach (KeyValuePair<Vehicle, IcbmComputer> kv in _computers)
        {
            if (!KsaWorld.IsAlive(kv.Key)) _stale.Add(kv.Key);
        }

        for (int i = 0; i < _stale.Count; i++) _computers.Remove(_stale[i]);
    }
}
