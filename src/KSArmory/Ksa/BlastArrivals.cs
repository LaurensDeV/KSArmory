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
        public double Due;
    }

    private static readonly List<Load> _pending = [];

    // What the fronts that arrived this step did, told once per craft rather than once per part.
    private static readonly Dictionary<Vehicle, (int Struck, int Dented)> _told = new(ReferenceEqualityComparer.Instance);

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
    /// time from now. <paramref name="airRatio"/> is the air at the burst against sea level, zero
    /// where there is none to throw dust into or to blow.
    /// </summary>
    public static void Queue(Vehicle craft, Part part, double3 burstAsmb, double pressureRatio,
                             double dueSeconds, double airRatio, double chargeKg)
    {
        _pending.Add(new Load
        {
            Craft = craft,
            Part = part,
            BurstAsmb = burstAsmb,
            PressureRatio = pressureRatio,
            AirRatio = airRatio,
            ChargeKg = chargeKg,
            Due = Math.Max(dueSeconds, 0.0),
        });
    }

    /// <summary>Counts the fronts down and strikes what they have reached.</summary>
    public static void Update(double dt)
    {
        if (_pending.Count == 0 || !double.IsFinite(dt) || dt < 0.0) return;

        _told.Clear();
        _pushed.Clear();

        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            Load load = _pending[i];
            load.Due -= dt;
            if (load.Due > 0.0) continue;

            _pending.RemoveAt(i);
            Strike(load);
        }

        foreach ((Vehicle craft, (int struck, int dented)) in _told)
        {
            Log.Info($"blast front reached {struck} part(s) of {KsaWorld.DisplayName(craft)}; "
                     + $"the engine took {dented} as dents");
        }

        foreach ((Vehicle craft, (double3 linear, double3 angular)) in _pushed)
        {
            AttitudeHook.Shove(craft, linear, angular);
        }
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

            bool dented = KsaWorld.ReportBlastDent(load.Part, face, push, across, load.PressureRatio);

            if (load.AirRatio > 0.0)
            {
                BlastPuff.Throw(load.Craft, face, push, across, load.PressureRatio);
                Push(load, face, push, centre, facing);
            }

            _told.TryGetValue(load.Craft, out (int Struck, int Dented) so);
            _told[load.Craft] = (so.Struck + 1, so.Dented + (dented ? 1 : 0));
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
