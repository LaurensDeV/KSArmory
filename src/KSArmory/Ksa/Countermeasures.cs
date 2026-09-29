using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// Every dispenser fitted and every flare and chaff cloud in the air.
///
/// <para>One instance for the world, because a decoy is not anybody's: a seeker sees every flare in
/// its field, whoever threw it. Decoys are stepped once a frame, after the airborne sample and before
/// any system flies a round, so a seeker reads them as it reads a craft — at the end of the step it is
/// about to integrate.</para>
/// </summary>
internal sealed class Countermeasures
{
    internal sealed record Entry(Vehicle Craft, int Ordinal, Dispenser Dispenser);

    // Auto-dispense is the craft's, not a part's: one warning receiver, answered from whichever
    // dispensers hold the right load.
    private sealed class CraftState
    {
        public readonly AutoDispense Auto = new();
        public bool AutoOn;
    }

    private readonly Dictionary<Vehicle, CraftState> _crafts = [];
    private readonly List<Dispenser> _onCraft = [];

    private static readonly List<Decoy> _live = [];

    private readonly Dictionary<(Vehicle Craft, int Ordinal), Entry> _entries = [];
    private readonly List<(Part Part, DispenserProfile Profile)> _scratch = [];
    private readonly List<(Vehicle, int)> _stale = [];
    private readonly List<IncomingMissile> _incoming = [];

    /// <summary>Every decoy still burning or blooming, for the seekers and the effects to read.</summary>
    public static IReadOnlyList<Decoy> Live => _live;

    public IEnumerable<Entry> All => _entries.Values;

    /// <summary>The dispensers on one craft, appended in part order.</summary>
    public void On(Vehicle? craft, List<Entry> into)
    {
        into.Clear();
        if (craft is null) return;

        foreach (Entry e in _entries.Values)
        {
            if (ReferenceEquals(e.Craft, craft)) into.Add(e);
        }

        into.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
    }

    /// <summary>Crews every dispenser now fitted and forgets every one gone with its craft.</summary>
    public void Sync(IReadOnlyList<Vehicle> craft)
    {
        for (int i = 0; i < craft.Count; i++)
        {
            Vehicle v = craft[i];
            if (!KsaWorld.IsAlive(v) || !KsaWorld.HasPlatform(v)) continue;

            FindAll(v, _scratch);
            for (int ordinal = 0; ordinal < _scratch.Count; ordinal++)
            {
                if (_entries.ContainsKey((v, ordinal))) continue;

                _entries[(v, ordinal)] = new Entry(v, ordinal, new Dispenser(_scratch[ordinal].Profile));
                Log.Info($"fitted a countermeasures dispenser on {KsaWorld.DisplayName(v)}");
            }
        }

        _stale.Clear();
        foreach (KeyValuePair<(Vehicle Craft, int Ordinal), Entry> kv in _entries)
        {
            if (!KsaWorld.IsAlive(kv.Key.Craft) || FindNth(kv.Key.Craft, kv.Key.Ordinal) is null) _stale.Add(kv.Key);
        }

        foreach ((Vehicle, int) key in _stale) _entries.Remove(key);

        foreach (Vehicle v in _crafts.Keys.ToList())
        {
            if (!KsaWorld.IsAlive(v)) _crafts.Remove(v);
        }
    }

    /// <summary>One salvo of <paramref name="kind"/> from the dispensers on a craft.</summary>
    public void Dispense(Vehicle craft, DecoyKind kind)
    {
        DispensersOn(craft);
        int queued = DispenseOrder.Request(_onCraft, kind);
        (int left, _) = DispenseOrder.Load(_onCraft, kind);

        if (queued > 0) Log.Info($"{KsaWorld.DisplayName(craft)} dispensing {queued} {kind}, {left - queued} left after");
        else Log.Debug(() => $"{KsaWorld.DisplayName(craft)} asked for {kind} and has none aboard");
    }

    /// <summary>What a craft carries of one kind, across its dispensers.</summary>
    public (int Remaining, int Capacity) Load(Vehicle craft, DecoyKind kind)
    {
        DispensersOn(craft);
        return DispenseOrder.Load(_onCraft, kind);
    }

    /// <summary>Whether any dispenser on the craft is mid-salvo.</summary>
    public bool Busy(Vehicle craft)
    {
        DispensersOn(craft);
        return _onCraft.Exists(d => d.Busy);
    }

    public bool AutoOn(Vehicle craft) => _crafts.TryGetValue(craft, out CraftState? c) && c.AutoOn;

    public void SetAuto(Vehicle craft, bool on)
    {
        if (!_crafts.TryGetValue(craft, out CraftState? c)) _crafts[craft] = c = new CraftState();
        c.AutoOn = on;
    }

    private void DispensersOn(Vehicle craft)
    {
        _onCraft.Clear();
        foreach (Entry e in _entries.Values)
        {
            if (ReferenceEquals(e.Craft, craft)) _onCraft.Add(e.Dispenser);
        }
    }

    /// <summary>
    /// Steps the decoys in the air, then lets every dispenser throw what it has queued or what its
    /// warning receiver asks for.
    /// </summary>
    public void Update(double step, IReadOnlyList<IContact> airborne)
    {
        StepDecoys(step);

        foreach ((Vehicle craft, CraftState state) in _crafts)
        {
            if (!state.AutoOn || !KsaWorld.IsAlive(craft)) continue;

            CollectIncoming(craft, airborne);
            (bool flares, bool chaff) = state.Auto.Decide(step, KsaWorld.PositionEcl(craft),
                                                          KsaWorld.VelocityEcl(craft), _incoming);
            if (flares) Dispense(craft, DecoyKind.Flare);
            if (chaff) Dispense(craft, DecoyKind.Chaff);
        }

        foreach (Entry e in _entries.Values)
        {
            if (!KsaWorld.IsAlive(e.Craft)) continue;

            int thrown = e.Dispenser.Update(step);
            for (int i = 0; i < thrown; i++) Eject(e);
        }
    }

    /// <summary>Throws the world's decoys away, for a save being loaded or the mod unloading.</summary>
    public void Discard()
    {
        _entries.Clear();
        _crafts.Clear();
        _live.Clear();
    }

    private static void StepDecoys(double step)
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Decoy d = _live[i];
            if (d.Spent || d.Body is not Celestial body)
            {
                if (!d.Spent) Log.Warn($"a {d.Profile.DisplayName} had no body to fall through and was dropped");
                _live.RemoveAt(i);
                continue;
            }

            try
            {
                // Read at the decoy's start-of-step position against a body sampled at the step's end,
                // so the body is put back half a step for the pull and a whole one for the air: the
                // same pairing WeaponSystem gives its rounds.
                double3 bodyVel = body.GetVelocityEcl();
                double3 gravity = KsaWorld.PullOnRound(body, d.PositionEcl, bodyVel, -0.5 * step);
                double3 airAt = d.PositionEcl + (bodyVel * step);

                double3 air = KsaWorld.GroundVelocityAt(body, airAt);
                d.Step(step, gravity, air, KsaWorld.AirDensityRatioAt(body, airAt));
                if (!d.Landed) LandIfUnderground(d, body, air);
            }
            catch (Exception ex)
            {
                Log.Warn($"a {d.Profile.DisplayName} could not be stepped and was dropped: {ex.Message}");
                _live.RemoveAt(i);
            }
        }
    }

    // The ground stops a decoy, which then lies where it fell. Sampled only near the ground, because a
    // height lookup per decoy per frame is the cost of the whole feature otherwise.
    private static void LandIfUnderground(Decoy d, Celestial body, double3 groundVelocity)
    {
        double3 fromCentre = d.PositionEcl - body.GetPositionEcl();
        double nearGround = body.MeanRadius + Math.Max(0.0, body.MaxTerrainHeightApprox) + 500.0;
        if (Vec.Len(fromCentre) > nearGround) return;

        if (!KsaWorld.TrySnapToGround(d.PositionEcl, out double3 ground, out double3 centre)) return;
        if (Vec.Len(d.PositionEcl - centre) <= Vec.Len(ground - centre)) d.Land(ground, groundVelocity);
    }

    // A missile whose seeker is on this craft, closing. That is what a warning receiver answers; one
    // aimed at somebody else is not a threat to this craft whatever its geometry.
    private void CollectIncoming(Vehicle craft, IReadOnlyList<IContact> airborne)
    {
        _incoming.Clear();
        for (int i = 0; i < airborne.Count; i++)
        {
            if (airborne[i] is not RoundContact { Round: { } round } contact) continue;
            if (!ReferenceEquals(round.TargetRef, craft) || !AutoDispense.Warns(round)) continue;

            SeekerBand? band = round.Munition.Seducible ? round.Munition.Band : null;
            _incoming.Add(new IncomingMissile(contact.PositionEcl, contact.VelocityEcl, band));
        }
    }

    // Out of the part's face at the craft's own velocity plus the kick, placed at the step's end as a
    // craft is sampled, so it is not stepped until the next frame.
    private void Eject(Entry e)
    {
        if (FindNth(e.Craft, e.Ordinal) is not { } found) return;

        try
        {
            Part part = found.Part;
            DispenserProfile profile = found.Profile;

            double3 faceAsmb = part.PositionVehicleAsmb + (part.Asmb2VehicleAsmb * (profile.EjectDirection * 0.2));
            double3 at = KsaWorld.VehicleAsmbToEcl(e.Craft, faceAsmb);
            double3 outward = Vec.Unit(KsaWorld.VehicleAsmbDirectionToEcl(e.Craft,
                                                                            part.Asmb2VehicleAsmb * profile.EjectDirection));
            double3 velocity = KsaWorld.VelocityEcl(e.Craft) + (outward * profile.EjectSpeed);

            DecoyProfile decoy = Catalogue.DecoyNamed(profile.Decoy);
            _live.Add(new Decoy(decoy, at, velocity, e.Craft) { Body = KsaWorld.ParentBody(e.Craft) });
            Log.Debug(() => $"  {decoy.DisplayName} out of {profile.DisplayName} at {profile.EjectSpeed:F0} m/s, "
                            + $"{_live.Count} decoy(s) in the air");
        }
        catch (Exception ex)
        {
            Log.Warn($"a {e.Dispenser.Kind} could not be dispensed: {ex.Message}");
        }
    }

    private (Part Part, DispenserProfile Profile)? FindNth(Vehicle craft, int ordinal)
    {
        FindAll(craft, _scratch);
        return ordinal < _scratch.Count ? _scratch[ordinal] : null;
    }

    private static void FindAll(Vehicle vehicle, List<(Part Part, DispenserProfile Profile)> into)
    {
        into.Clear();
        try
        {
            ReadOnlySpan<Part> parts = vehicle.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is { } part && Catalogue.DispenserForPart(part.Id) is { } profile) into.Add((part, profile));
            }
        }
        catch
        {
            // Part tree can be mid-rebuild during staging or docking.
        }
    }
}
