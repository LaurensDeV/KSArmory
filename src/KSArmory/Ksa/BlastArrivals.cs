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
/// <para>The burst is held in the craft's own assembly frame, which is where the engine keeps its
/// dents and where a craft standing still stays still; a bare ecliptic point is left behind by the
/// planet long before a front arrives.</para>
///
/// <para><b>Fronts reaching one part together dent it together.</b> Each throws its own dust and
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
        public required double3 BurstAsmb;
        public required double PressureRatio;
        public required double AirRatio;
        public required double ChargeKg;
        public required double RealPascals;
        public required double BreakingPascals;
        public required double PhaseSeconds;
        public required double3 PushAsmb;
        public required bool MayBreak;
        public double Due;

        // Its load has gone on to another front at the same part, which dents for both.
        public bool DentHandedOn;

        // What earlier fronts at this part handed on, each with when it arrived on _clock.
        public List<(FrontLoad Front, double ArrivedAt)>? Carried;
    }

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
    // craft's centre of mass and in its assembly frame.
    private static readonly Dictionary<Vehicle, (double3 Linear, double3 Angular)> _pushed =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Forgets every front still on its way, for a scene that no longer contains them.</summary>
    public static void Clear() => _pending.Clear();

    /// <summary>How many parts are still waiting for a front.</summary>
    public static int Pending => _pending.Count;

    /// <summary>
    /// Holds one part's load until the front arrives, <paramref name="dueSeconds"/> of simulated
    /// time from now, <paramref name="gapMetres"/> from the burst. <paramref name="airRatio"/> is the
    /// air at the burst against sea level, zero where there is none to throw dust into or to blow.
    /// <paramref name="mayBreak"/> is false for a craft the burst only dents, which fronts together
    /// cannot break either.
    /// </summary>
    public static void Queue(Vehicle craft, Part part, double3 burstAsmb, double pressureRatio,
                             double dueSeconds, double airRatio, double chargeKg, double gapMetres,
                             double crashTolerancePascals, bool mayBreak)
    {
        (double real, double breaking) = BlastDamage.RealLoad(chargeKg, crashTolerancePascals, gapMetres);
        if (!KsaWorld.TryBlastFace(part, burstAsmb, out _, out double3 push, out _, out _, out _)) return;

        _pending.Add(new Load
        {
            Craft = craft,
            Part = part,
            BurstAsmb = burstAsmb,
            PressureRatio = pressureRatio,
            AirRatio = airRatio,
            ChargeKg = chargeKg,
            RealPascals = real,
            BreakingPascals = breaking,
            PhaseSeconds = BlastWave.PositivePhaseSeconds(chargeKg, gapMetres),
            PushAsmb = push,
            MayBreak = mayBreak,
            Due = Math.Max(dueSeconds, 0.0),
        });
    }

    /// <summary>Counts the fronts down and strikes what they have reached.</summary>
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
            load.Due -= dt;
            if (load.Due > 0.0) continue;

            _pending.RemoveAt(i);
            _due.Add(load);
        }

        // Several fronts at one part in the same step dent it once, through whichever is strongest.
        for (int i = 0; i < _due.Count; i++)
        {
            Load load = _due[i];
            if (load.DentHandedOn) continue;

            for (int j = i + 1; j < _due.Count; j++)
            {
                Load other = _due[j];
                if (other.DentHandedOn || !ReferenceEquals(other.Part, load.Part)) continue;

                Load keep = other.PressureRatio > load.PressureRatio ? other : load;
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

        foreach ((Vehicle craft, (double3 linear, double3 angular)) in _pushed)
        {
            AttitudeHook.Shove(craft, linear, angular);
        }
    }

    // Moves a load, arriving now, and everything it was already carrying, onto another front's.
    private static void HandOn(Load from, Load to)
    {
        from.DentHandedOn = true;
        to.Carried ??= [];
        to.Carried.Add((Front(from, 0.0), _clock));
        if (from.Carried is not null) to.Carried.AddRange(from.Carried);
    }

    private static FrontLoad Front(Load load, double sinceArrival)
    {
        return new FrontLoad(load.PressureRatio, load.RealPascals, load.BreakingPascals, load.PushAsmb,
                             sinceArrival, load.PhaseSeconds);
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

    // The next front still due at this part inside this one's positive phase, if there is one.
    private static Load? StillToCome(Load load)
    {
        Load? next = null;
        foreach (Load other in _pending)
        {
            if (!ReferenceEquals(other.Part, load.Part) || other.Due > load.PhaseSeconds) continue;
            if (next is null || other.Due < next.Due) next = other;
        }

        return next;
    }

    private static void Strike(Load load)
    {
        try
        {
            if (!KsaWorld.IsAlive(load.Craft)) return;

            // A part the burst broke off, or a decoupler took away, is on another craft by now.
            if (!ReferenceEquals(load.Part.Tree, load.Craft.Parts)) return;

            if (!KsaWorld.TryBlastFace(load.Part, load.BurstAsmb, out double3 face, out double3 push,
                                       out double across, out double3 centre, out double facing)) return;

            if (load.AirRatio > 0.0)
            {
                BlastPuff.Throw(load.Craft, face, push, across, load.PressureRatio);
                Push(load, face, push, centre, facing);
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
            _together.Add(Front(load, 0.0));
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

            bool dented = KsaWorld.ReportBlastDent(load.Part, face, push, across, ratio);

            _told[load.Craft] = (so.Struck + 1, so.Dented + (dented ? 1 : 0), so.Together + together, so.Broken);
        }
        catch
        {
            // A front that finds nothing it can strike leaves nothing.
        }
    }

    private static void Push(Load load, double3 faceAsmb, double3 pushAsmb, double3 centreAsmb, double facingM2)
    {
        if (!KsaWorld.TryCentreOfMassAsmb(load.Craft, out double3 com)) return;

        double wind = BlastWave.WindImpulse(load.ChargeKg, Vec.Len(faceAsmb - load.BurstAsmb),
                                            BlastWave.SeaLevelPascals * load.AirRatio);
        (double3 linear, double3 angular) = BlastShove.OnPart(centreAsmb, pushAsmb, facingM2, wind, com);

        _pushed.TryGetValue(load.Craft, out (double3 Linear, double3 Angular) so);
        _pushed[load.Craft] = (so.Linear + linear, so.Angular + angular);

        // A craft the burst may not break may not be broken by the ground it is knocked onto either.
        if (!load.MayBreak) CrashGuard.Hold(load.Craft);
    }
}
