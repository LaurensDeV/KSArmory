using System.Runtime.InteropServices;
using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// What a burst loads, held until the blast front gets there: the dent, the dust it throws off and
/// the push of the wind behind it land as the front passes the part, not at the flash.
///
/// <para>The part breaking is still decided at the burst, by the kill path; this is what a part that
/// survives is left with, and the pause between the flash and the hit is most of what makes it read
/// as a blast. On the simulated step, like the bang, so it waits through a pause and hurries under
/// timewarp.</para>
///
/// <para><b>The front is followed, not timed.</b> The burst is anchored to the ground it went off
/// on, and each step every part still waiting is measured against the front as it is now: struck
/// when <see cref="MushroomCloud.ShockRadius"/> reaches it, and pushed along the line from the burst
/// to where it is then. A craft in flight moves and turns between the flash and the hit, and one
/// climbing faster than the front is never caught; an arrival time and a direction fixed at the
/// flash get both wrong.</para>
///
/// <para><b>Fronts reaching one part together load it together.</b> Each throws its own dust and
/// pushes on its own as it arrives, because both of those simply add. A dent does not: two loads
/// each short of the engine's threshold leave nothing asked about one at a time. So a front that
/// arrives while another is still due at the same part inside this one's positive phase hands its
/// load on, and the part is loaded once, at the last of them, with what is left of each and the
/// most head-on pair meeting as at a wall (<see cref="BlastDamage.Combine"/>). Loaded past what
/// breaks it, the part breaks, unless the craft is one the burst may only dent.</para>
/// </summary>
internal static class BlastArrivals
{
    private sealed class Load
    {
        public required Vehicle Craft;
        public required Part Part;
        public required object? Body;
        public required double3 Anchor;
        public required double AirRatio;
        public required double ChargeKg;
        public required double TolerancePascals;

        // What the ground adds to the front at this part, over free air: GroundReflection.GainAt.
        public required double Reflection;

        // The ground under the burst, for a front that stands on it as a Mach stem.
        public required GroundReflection Ground;
        public required bool MayBreak;

        // Seconds since the burst, and the front's arrival at this part as things stand now.
        public double Age;
        public double Due;

        // Its load has gone on to another front at the same part, which loads it for both.
        public bool DentHandedOn;

        // What earlier fronts at this part handed on, each with when it arrived on _clock.
        public List<(FrontLoad Front, double ArrivedAt)>? Carried;

        // Where the burst is in the craft's frame, and how far the front has to go to the part's
        // skin, as measured this step.
        public double3 BurstAsmb;
        public double Gap;

        // Where the front pushes from, in the same frame: the burst, or for a part in an air burst's
        // Mach stem the point over ground zero level with it, since the stem is a wall standing on the
        // ground and pushes along it.
        public double3 PushFromAsmb;
    }

    // The least distance a front is taken to have left to go, so a part at the burst is still one
    // the front reaches rather than one it is already past.
    private const double LeastGapMetres = 0.5;

    private static readonly List<Load> _pending = [];
    private static readonly List<Load> _due = [];
    private static readonly List<FrontLoad> _together = [];

    // Simulated seconds since the scene started counting fronts, which is what an arrival is timed on.
    private static double _clock;

    // Parts fronts broke between them this step, by craft, handed to the engine together.
    private static readonly Dictionary<Vehicle, List<Part>> _broken = new(ReferenceEqualityComparer.Instance);

    // What the fronts that arrived this step did, told once per craft rather than once per part.
    private static readonly Dictionary<Vehicle, (int Struck, int Dented, int Together, int Broken)> _told =
        new(ReferenceEqualityComparer.Instance);

    // The wind's push on each craft this step, summed over the parts the front reached, about the
    // craft's centre of mass and in its assembly frame, with the fastest wind that did it.
    private static readonly Dictionary<Vehicle, (double3 Linear, double3 Angular, double Wind, double3 AwayEcl)> _pushed =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Forgets every front still on its way, for a scene that no longer contains them.</summary>
    public static void Clear() => _pending.Clear();

    /// <summary>How many parts are still waiting for a front.</summary>
    public static int Pending => _pending.Count;

    /// <summary>
    /// Waits for the front of a burst at <paramref name="burstEcl"/>, <paramref name="sinceBurst"/>
    /// seconds ago, to reach one part. <paramref name="airRatio"/> is the air at the burst against
    /// sea level, zero where there is none, which strikes at once: with no air there is no front.
    /// <paramref name="mayBreak"/> is false for a craft the burst only dents, which fronts together
    /// cannot break either.
    /// </summary>
    public static void Queue(Vehicle craft, Part part, double3 burstEcl, double sinceBurst, double airRatio,
                             double chargeKg, double crashTolerancePascals, bool mayBreak,
                             double reflection = BlastWave.SurfaceReflection, GroundReflection? ground = null)
    {
        if (!KsaWorld.TryAnchorToGround(burstEcl, out object? body, out double3 anchor)) return;

        _pending.Add(new Load
        {
            Craft = craft,
            Part = part,
            Body = body,
            Anchor = anchor,
            AirRatio = airRatio,
            ChargeKg = chargeKg,
            TolerancePascals = crashTolerancePascals,
            Reflection = reflection,
            Ground = ground ?? GroundReflection.FreeAir,
            MayBreak = mayBreak,
            Age = Math.Max(sinceBurst, 0.0),
        });
    }

    /// <summary>Follows the fronts and strikes what they have reached.</summary>
    public static void Update(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0.0) return;

        _clock += dt;
        if (_pending.Count == 0) return;

        _told.Clear();
        _pushed.Clear();
        _due.Clear();
        _broken.Clear();

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Load load = _pending[i];
            load.Age += dt;

            if (!Measure(load))
            {
                _pending.RemoveAt(i);
                continue;
            }

            if (load.Due > 0.0) continue;

            _pending.RemoveAt(i);
            _due.Add(load);
        }

        // Several fronts at one part in the same step load it once, through whichever is hardest.
        for (int i = 0; i < _due.Count; i++)
        {
            Load load = _due[i];
            if (load.DentHandedOn) continue;

            for (int j = i + 1; j < _due.Count; j++)
            {
                Load other = _due[j];
                if (other.DentHandedOn || !ReferenceEquals(other.Part, load.Part)) continue;

                Load keep = RealPascals(other) > RealPascals(load) ? other : load;
                Load give = ReferenceEquals(keep, other) ? load : other;
                HandOn(give, keep);
                if (ReferenceEquals(give, load)) break;
            }
        }

        foreach (Load load in _due) Strike(load);

        if (_broken.Count > 0) Break();

        foreach ((Vehicle craft, (int struck, int dented, int together, int broken)) in _told)
        {
            string with = together > 0 ? $", {together} of them loaded by more than one front at once" : "";
            string broke = broken > 0 ? $"; {broken} broke under fronts together" : "";
            Log.Info($"blast front reached {struck} part(s) of {KsaWorld.DisplayName(craft)}; "
                     + $"the engine took {dented} as dents{with}{broke}");
        }

        foreach ((Vehicle craft, (double3 linear, double3 angular, double wind, double3 away)) in _pushed)
        {
            AttitudeHook.Shove(craft, linear, angular, wind, away);
        }
    }

    // Where the burst and the part are now, how far the front has to go, and so when it arrives.
    // False drops the load: the craft, the part or the ground it was anchored to is gone.
    private static bool Measure(Load load)
    {
        if (!KsaWorld.IsAlive(load.Craft)) return false;

        // A part the burst broke off, or a decoupler took away, is on another craft by now.
        if (!ReferenceEquals(load.Part.Tree, load.Craft.Parts)) return false;

        if (!KsaWorld.TryGroundAnchorEcl(load.Body, load.Anchor, out double3 burstEcl, out _)) return false;
        if (!KsaWorld.TryPartBox(load.Part, out double3 centreAsmb, out double3 half)) return false;

        double3 centreEcl = KsaWorld.VehicleAsmbToEcl(load.Craft, centreAsmb);
        load.Gap = Math.Max(Vec.Len(centreEcl - burstEcl) - Vec.Len(half), LeastGapMetres);
        load.BurstAsmb = KsaWorld.EclToVehicleAsmb(load.Craft, burstEcl);
        load.PushFromAsmb = KsaWorld.EclToVehicleAsmb(load.Craft, load.Ground.PushFrom(burstEcl, centreEcl));

        if (!(load.AirRatio > 0.0))
        {
            load.Due = 0.0;
            return true;
        }

        double kt = MushroomCloud.KilotonsFor(load.ChargeKg);
        load.Due = MushroomCloud.ShockRadius(kt, load.Age) >= load.Gap
            ? 0.0
            : Math.Max(MushroomCloud.ShockArrivalSeconds(kt, load.Gap) - load.Age, 1.0e-6);

        return true;
    }

    private static double RealPascals(Load load)
        => BlastWave.PeakOverpressurePascals(load.ChargeKg, load.Gap, reflection: load.Reflection);

    // The charge the damage law, calibrated in free air, sees at this part.
    private static double LoadingCharge(Load load) => load.ChargeKg * load.Reflection;

    private static FrontLoad Front(Load load, double sinceArrival)
    {
        double ratio = BlastDamage.DentRatio(LoadingCharge(load), load.TolerancePascals, load.Gap);
        (double real, double breaking) = BlastDamage.RealLoad(LoadingCharge(load), load.TolerancePascals, load.Gap);

        double3 push = KsaWorld.TryPartBox(load.Part, out double3 centre, out _)
            ? Vec.Unit(centre - load.PushFromAsmb)
            : Vec.Unit(Vec.Zero - load.PushFromAsmb);

        return new FrontLoad(ratio, real, breaking, push, sinceArrival,
                             BlastWave.PositivePhaseSeconds(load.ChargeKg, load.Gap, load.Reflection));
    }

    // Moves a load, arriving now, and everything it was already carrying, onto another front's.
    private static void HandOn(Load from, Load to)
    {
        from.DentHandedOn = true;
        to.Carried ??= [];
        to.Carried.Add((Front(from, 0.0), _clock));
        if (from.Carried is not null) to.Carried.AddRange(from.Carried);
    }

    // The next front still due at this part inside this one's positive phase, if there is one.
    private static Load? StillToCome(Load load)
    {
        double phase = BlastWave.PositivePhaseSeconds(load.ChargeKg, load.Gap, load.Reflection);

        Load? next = null;
        foreach (Load other in _pending)
        {
            if (!ReferenceEquals(other.Part, load.Part) || other.Due > phase) continue;
            if (next is null || other.Due < next.Due) next = other;
        }

        return next;
    }

    // Everything fronts broke between them this step: one join of the engine's workers for the lot,
    // as the kill path takes it, and the engine's own rule on whether that many parts is the craft.
    private static void Break()
    {
        KsaWorld.WaitForVehicleSolvers();

        foreach ((Vehicle craft, List<Part> parts) in _broken)
        {
            if (!KsaWorld.IsAlive(craft)) continue;

            if (KsaWorld.LosingThatManyPartsIsFatal(parts.Count, KsaWorld.PartCount(craft)))
            {
                Log.Info($"fronts together destroyed {KsaWorld.DisplayName(craft)}");
                KsaWorld.Destroy(craft, blastSeverity: 50f);
                continue;
            }

            KsaWorld.TryQueuePartFailure(craft, parts);
        }
    }

    private static void Strike(Load load)
    {
        try
        {
            if (!KsaWorld.TryBlastFace(load.Part, load.PushFromAsmb, out double3 face, out double3 push,
                                       out double across, out double3 centre, out double facing)) return;

            FrontLoad own = Front(load, 0.0);

            if (load.AirRatio > 0.0)
            {
                BlastPuff.Throw(load.Craft, face, push, across, own.Ratio);
                Push(load, push, centre, facing);
            }

            _told.TryGetValue(load.Craft, out (int Struck, int Dented, int Together, int Broken) so);

            // Another front will reach this part while this one is still pushing: the load waits for
            // it, carried along.
            if (!load.DentHandedOn && StillToCome(load) is { } later) HandOn(load, later);

            if (load.DentHandedOn)
            {
                _told[load.Craft] = (so.Struck + 1, so.Dented, so.Together, so.Broken);
                return;
            }

            _together.Clear();
            _together.Add(own);
            if (load.Carried is not null)
            {
                foreach ((FrontLoad front, double arrivedAt) in load.Carried)
                {
                    _together.Add(front with { SinceArrival = _clock - arrivedAt });
                }
            }

            (double ratio, double share) = BlastDamage.Combine(CollectionsMarshal.AsSpan(_together));
            int together = _together.Count > 1 ? 1 : 0;

            // One front alone was judged at the flash; only fronts together are new here.
            if (load.MayBreak && together > 0 && share >= 1.0)
            {
                if (!_broken.TryGetValue(load.Craft, out List<Part>? parts)) _broken[load.Craft] = parts = [];
                parts.Add(load.Part);
                _told[load.Craft] = (so.Struck + 1, so.Dented, so.Together + together, so.Broken + 1);
                return;
            }

            bool dented = ratio > 0.0 && KsaWorld.ReportBlastDent(load.Part, face, push, across, ratio);

            _told[load.Craft] = (so.Struck + 1, so.Dented + (dented ? 1 : 0), so.Together + together, so.Broken);
        }
        catch
        {
            // A front that finds nothing it can strike leaves nothing.
        }
    }

    private static void Push(Load load, double3 pushAsmb, double3 centreAsmb, double facingM2)
    {
        if (!KsaWorld.TryCentreOfMassAsmb(load.Craft, out double3 com)) return;

        double ambient = BlastWave.SeaLevelPascals * load.AirRatio;
        double wind = BlastWave.WindImpulse(load.ChargeKg, load.Gap, ambient, load.Reflection);
        double speed = BlastWave.WindSpeed(BlastWave.PeakOverpressurePascals(load.ChargeKg, load.Gap, ambient,
                                                                             load.Reflection), ambient);
        (double3 linear, double3 angular) = BlastShove.OnPart(centreAsmb, pushAsmb, facingM2, wind, com);

        double3 away = KsaWorld.VehicleAsmbDirectionToEcl(load.Craft, com - load.PushFromAsmb);

        _pushed.TryGetValue(load.Craft, out (double3 Linear, double3 Angular, double Wind, double3 AwayEcl) so);
        _pushed[load.Craft] = (so.Linear + linear, so.Angular + angular, Math.Max(so.Wind, speed), away);
    }
}
