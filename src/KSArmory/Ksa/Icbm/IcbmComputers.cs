using KSA;

namespace KSArmory;

/// <summary>
/// One <see cref="IcbmComputer"/> per craft carrying a part that provides
/// <see cref="WeaponRole.Guidance"/>, crewed and forgotten with the craft.
///
/// <para>Per craft, not per launcher as <see cref="WeaponSystems"/> is: a craft has one trajectory,
/// and a second computer would be a second autopilot fighting the first for the same engines.</para>
/// </summary>
internal sealed class IcbmComputers(Config session)
{
    private readonly Config _session = session;

    private readonly Dictionary<Vehicle, IcbmComputer> _computers = [];
    private readonly List<Vehicle> _stale = [];
    private readonly List<SurveyedPart> _surveyed = [];

    // Who is mid-burn or mid-trim this frame, worked out once and handed to every computer.
    private readonly List<IcbmComputer> _busy = [];

    public int Count => _computers.Count;

    public IEnumerable<IcbmComputer> All => _computers.Values;

    public IcbmComputer? For(Vehicle? craft)
        => craft is not null && _computers.TryGetValue(craft, out IcbmComputer? c) ? c : null;

    /// <summary>
    /// The longest step any burning computer can be flown across, for the policy
    /// <see cref="WeaponSystems.FaithfulStep"/> feeds: the cutoff lands on a frame boundary.
    /// </summary>
    public double FaithfulStep(out bool anyBurning)
    {
        anyBurning = false;

        foreach (IcbmComputer computer in _computers.Values)
        {
            if (!computer.NeedsShortSteps) continue;

            // Not under KSA's own warp to a time: from a thousand times speed WarpPolicy's first
            // slowdown is nearly zero, which pauses the game. The computer stops the warp itself.
            if (computer.Program.Phase == IcbmPhase.Holding && KsaWorld.IsAutoWarpActive) continue;

            anyBurning = true;
        }

        // Not BusTrim.MaxFaithfulStep: asking 66 ms where the burn asks 300 made WarpPolicy abandon
        // burns, which fell 182 to 316 km short against 5 to 15 km.

        return anyBurning ? IcbmProgram.MaxFaithfulStep : double.MaxValue;
    }

    public string WhoNeedsShortSteps()
    {
        string who = "";
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (computer.NeedsShortSteps) who += $" {KsaWorld.DisplayName(computer.Craft)}:{computer.ShortStepsBecause};";
        }

        return who;
    }

    public void AbandonBurns(string why)
    {
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (computer.Program.IsBurning) computer.Abort(why);
        }
    }

    public void Sync(IReadOnlyList<(Vehicle Craft, WeaponInventory Inventory)> systems,
                     IReadOnlyList<(Vehicle From, Vehicle To)> handovers)
    {
        Follow(handovers);

        for (int i = 0; i < systems.Count; i++)
        {
            Vehicle craft = systems[i].Craft;
            if (!KsaWorld.IsAlive(craft)) continue;
            if (!systems[i].Inventory.HasGuidance) continue;
            if (_computers.ContainsKey(craft)) continue;

            // Re-surveyed: the panel's survey refreshes every 60 draws, and stale it crews a computer
            // on every spent stage.
            KsaWorld.SurveyParts(craft, _surveyed);
            if (!WeaponSurvey.Survey(_surveyed, Catalogue.Components).HasGuidance) continue;

            IcbmComputer computer = new(craft, new IcbmConfig(), _session);
            _computers[craft] = computer;
            Log.Debug($"ICBM computer crewed on {KsaWorld.DisplayName(craft)}");

            if (SettingsStore.BallisticFor(KsaWorld.DisplayName(craft)) is { } stored) computer.Restore(stored);
        }

        Retire();
    }

    /// <summary>For the game having just saved.</summary>
    public void WriteDown()
    {
        bool changed = false;
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;
            changed |= SettingsStore.RememberBallistic(KsaWorld.DisplayName(computer.Craft), computer.Setup);
        }

        if (changed) SettingsStore.Save();
    }

    /// <summary>For a different save opened under them.</summary>
    public void Adopt()
    {
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (!KsaWorld.IsAlive(computer.Craft)) continue;
            if (SettingsStore.BallisticFor(KsaWorld.DisplayName(computer.Craft)) is { } stored) computer.Restore(stored);
        }
    }

    // A weapon a decoupler carried onto another craft takes its computer, unless that craft already
    // has one. Before the crewing loop, or it would crew a second, targetless one there.
    private void Follow(IReadOnlyList<(Vehicle From, Vehicle To)> handovers)
    {
        for (int i = 0; i < handovers.Count; i++)
        {
            (Vehicle from, Vehicle to) = handovers[i];

            if (!_computers.TryGetValue(from, out IcbmComputer? computer)) continue;
            if (_computers.ContainsKey(to)) continue;

            _computers.Remove(from);
            _computers[to] = computer;
            computer.Rehome(to);
        }
    }

    /// <summary>
    /// Step every computer, handing each the weapon aboard its own craft — resolved each frame
    /// rather than held, so a craft that loses its launcher simply cannot release.
    /// </summary>
    public void Update(double simStep, double playerStep, WeaponSystems weapons,
                       bool traceWarhead = false)
    {
        // Before any steps: whether the clock may go to KSA's warp is a question about every flight.
        _busy.Clear();

        foreach (IcbmComputer computer in _computers.Values)
        {
            if (computer.NeedsShortSteps) _busy.Add(computer);
        }

        weapons.FaithfulStep(out bool roundsInFlight);

        foreach (IcbmComputer computer in _computers.Values)
        {
            computer.Update(simStep, playerStep, weapons.GuidedFrom(computer.Craft)?.Weapon, traceWarhead,
                            _busy, roundsInFlight);
        }
    }

    /// <summary>For a scene change.</summary>
    public void Clear()
    {
        foreach (IcbmComputer computer in _computers.Values)
        {
            if (KsaWorld.IsAlive(computer.Craft)) computer.Abort("scene ended");
        }

        _computers.Clear();
    }

    // A destroyed craft's computer goes with it, unless it is still following a warhead down.
    private void Retire()
    {
        _stale.Clear();

        foreach (KeyValuePair<Vehicle, IcbmComputer> kv in _computers)
        {
            if (KsaWorld.IsAlive(kv.Key)) continue;

            // A bus breaks up on reentry before its warheads land, so it is held while a trace is
            // outstanding or the salvo still arriving (ACCURACY-PLAN.md 3ch). It flies nothing meanwhile.
            if (kv.Value.TraceOutstanding || kv.Value.SalvoStillArriving)
            {
                AttitudeHook.Release(kv.Key);
                continue;
            }

            _stale.Add(kv.Key);
        }

        for (int i = 0; i < _stale.Count; i++)
        {
            AttitudeHook.Release(_stale[i]);
            _computers.Remove(_stale[i]);
        }
    }
}
