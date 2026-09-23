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
/// load on, and the part is dented once, at the last of them, with their real pressures added
/// (<see cref="BlastDamage.CombinedDentRatio"/>).</para>
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
        public double Due;

        // Its load has gone on to another front at the same part, which dents for both.
        public bool DentHandedOn;

        // What earlier fronts at this part handed on: ratio, real and breaking pressure.
        public List<(double Ratio, double RealPascals, double BreakingPascals)>? Carried;
    }

    private static readonly List<Load> _pending = [];
    private static readonly List<Load> _due = [];
    private static readonly List<(double Ratio, double RealPascals, double BreakingPascals)> _together = [];

    // What the fronts that arrived this step did, told once per craft rather than once per part.
    private static readonly Dictionary<Vehicle, (int Struck, int Dented, int Together)> _told =
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
    /// </summary>
    public static void Queue(Vehicle craft, Part part, double3 burstAsmb, double pressureRatio,
                             double dueSeconds, double airRatio, double chargeKg, double gapMetres,
                             double crashTolerancePascals)
    {
        (double real, double breaking) = BlastDamage.RealLoad(chargeKg, crashTolerancePascals, gapMetres);

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
            Due = Math.Max(dueSeconds, 0.0),
        });
    }

    /// <summary>Counts the fronts down and strikes what they have reached.</summary>
    public static void Update(double dt)
    {
        if (_pending.Count == 0 || !double.IsFinite(dt) || dt < 0.0) return;

        _told.Clear();
        _pushed.Clear();
        _due.Clear();

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

        foreach ((Vehicle craft, (int struck, int dented, int together)) in _told)
        {
            string with = together > 0 ? $", {together} of them loaded by more than one front at once" : "";
            Log.Info($"blast front reached {struck} part(s) of {KsaWorld.DisplayName(craft)}; "
                     + $"the engine took {dented} as dents{with}");
        }

        foreach ((Vehicle craft, (double3 linear, double3 angular)) in _pushed)
        {
            AttitudeHook.Shove(craft, linear, angular);
        }
    }

    // Moves a load's pressure, and everything it was already carrying, onto another front's.
    private static void HandOn(Load from, Load to)
    {
        from.DentHandedOn = true;
        to.Carried ??= [];
        to.Carried.Add((from.PressureRatio, from.RealPascals, from.BreakingPascals));
        if (from.Carried is not null) to.Carried.AddRange(from.Carried);
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

            _told.TryGetValue(load.Craft, out (int Struck, int Dented, int Together) so);

            // Another front will reach this part while this one is still pushing: the dent waits for
            // it, carrying this load along.
            if (!load.DentHandedOn && StillToCome(load) is { } later) HandOn(load, later);

            if (load.DentHandedOn)
            {
                _told[load.Craft] = (so.Struck + 1, so.Dented, so.Together);
                return;
            }

            _together.Clear();
            _together.Add((load.PressureRatio, load.RealPascals, load.BreakingPascals));
            if (load.Carried is not null) _together.AddRange(load.Carried);

            double ratio = BlastDamage.CombinedDentRatio(CollectionsMarshal.AsSpan(_together));
            bool dented = KsaWorld.ReportBlastDent(load.Part, face, push, across, ratio);

            _told[load.Craft] = (so.Struck + 1, so.Dented + (dented ? 1 : 0),
                                 so.Together + (_together.Count > 1 ? 1 : 0));
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
    }
}
