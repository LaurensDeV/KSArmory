using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // A fraction of the wait, because KSA's warp rate scales with the span: a fixed margin off a
    // ninety-minute hold is still approached at thousands of times speed.
    private const double MarginFraction = 0.2;

    private const double MaxMarginSeconds = 900.0;

    /// <summary>
    /// Hand the wait to KSA's own warp-to-a-time, on a press. One press covers the wait in hops,
    /// each leaving a margin, because KSA's rate scales with the span: one jump would arrive at
    /// thousands of times speed with nowhere to hand over.
    /// </summary>
    public bool TryWarpToWindow() => TryWarpAhead("the burn window");

    /// <summary>
    /// The same for the coast, stopping <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/> short
    /// of the release.
    /// </summary>
    public bool TryWarpTheCoast() => TryWarpAhead("the release point");

    private bool TryWarpAhead(string what)
    {
        if (!CanWarpAhead) return false;

        double wait = SecondsToTheNextThingThatMatters;
        double margin = Math.Clamp(wait * MarginFraction, IcbmProgram.WarpHoldLeadSeconds, MaxMarginSeconds);

        if (!KsaWorld.TryAutoWarpTo(wait, margin)) return false;

        _warpIsOurs = true;
        Log.Info($"warping to within {IcbmProgram.Clock(margin)} of {what} on "
                 + $"{KsaWorld.DisplayName(Craft)}, {IcbmProgram.Clock(wait)} to go{OverTheTopOf()}");
        return true;
    }

    // Whether any OTHER flight is mid-burn or mid-trim. The list is empty when nobody handed one
    // in, so a computer driven outside IcbmComputers asks about itself alone.
    private bool AnythingElseNeedsShortSteps
    {
        get
        {
            for (int i = 0; i < _busyElsewhere.Count; i++)
            {
                if (!ReferenceEquals(_busyElsewhere[i], this)) return true;
            }

            return false;
        }
    }

    // Flights still being integrated when this warp started: WarpPolicy cannot rein in an auto-warp.
    private string OverTheTopOf()
    {
        int others = 0;

        for (int i = 0; i < _busyElsewhere.Count; i++)
        {
            if (!ReferenceEquals(_busyElsewhere[i], this)) others++;
        }

        if (others == 0) return " -- nothing else needs short steps";

        List<string> names = [];

        for (int i = 0; i < _busyElsewhere.Count; i++)
        {
            if (!ReferenceEquals(_busyElsewhere[i], this))
            {
                names.Add(KsaWorld.DisplayName(_busyElsewhere[i].Craft));
            }
        }

        return $" -- OVER THE TOP OF {others} still needing short steps: {string.Join(", ", names)}";
    }

    public bool CanWarpToWindow => Program.Phase == IcbmPhase.Holding && CanWarpAhead;

    public bool CanWarpTheCoast => Program.Phase == IcbmPhase.Coast && CanWarpAhead;

    // Never while any flight in the world is integrated, as WorldSpeed.Slowest: checking only itself
    // took seven burning rockets' longest step from 33 ms to 205 ms, quantum 0.081 to 1.675 m/s.
    private bool CanWarpAhead
        => !KsaWorld.IsAutoWarpActive
        && !NeedsShortSteps
        && !AnythingElseNeedsShortSteps
        && !_roundsInFlight
        && ReferenceEquals(Craft, KsaWorld.ControlledVehicle)
        && double.IsFinite(SecondsToTheNextThingThatMatters)
        && SecondsToTheNextThingThatMatters > IcbmProgram.WarpHoldLeadSeconds * 2.0;

    // The burn window while holding, the release point while coasting; NaN otherwise.
    private double SecondsToTheNextThingThatMatters
        => Program.Phase switch
        {
            IcbmPhase.Holding => Config.HoldDriftsUntilIgnition ? Command.SecondsToBurn - _pointLeadSeconds : Command.SecondsToBurn,
            IcbmPhase.Coast => SecondsToReleaseApproach - UnplannedWalkSeconds,
            _ => double.NaN,
        };

    // A walk planned after the split moves the gate a hop earlier per extra stop, so a warp to the
    // plain gate would leave it no time.
    private double UnplannedWalkSeconds
        => _targets.Count > 1 && !_walker.Walking && !_walker.Committed
               ? (_targets.Count - 1) * ReleaseItinerary.MedianHopSeconds
               : 0.0;

    /// <summary>
    /// How long until the warheads are due to leave on the time gate alone, or NaN, as for a shot
    /// that fell short and holds its warheads for ever. The deploy altitude is not counted.
    /// </summary>
    public double SecondsToRelease
    {
        get
        {
            if (Command.ShortfallMetresPerSecond > 0.0) return double.NaN;

            double toArrival = Program.CommittedArrivalFromNow;
            if (!double.IsFinite(toArrival)) return double.NaN;

            return toArrival - Program.SplitGate;
        }
    }

    /// <summary>
    /// When the world has to be back at normal speed, <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>
    /// before the release.
    /// </summary>
    public double SecondsToReleaseApproach
    {
        get
        {
            double toRelease = SecondsToRelease;

            return double.IsFinite(toRelease)
                       ? toRelease - IcbmProgram.SteadyBeforeReleaseSeconds
                       : double.NaN;
        }
    }

    // Carries a warp this computer started through to whatever it was aimed at, and ends it if the
    // shot stops wanting one. Only ever a warp it started: one the player started is theirs.
    private void CarryOurWarp()
    {
        if (Config.WarpTheCoast && !_warpIsOurs && CanWarpTheCoast) TryWarpTheCoast();

        if (!_warpIsOurs) return;

        // A round launched under the warp, its own or anyone's, is stepped at whatever rate KSA picked.
        if (_roundsInFlight)
        {
            if (KsaWorld.IsAutoWarpActive)
            {
                Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, rounds are in the air");
                KsaWorld.StopAutoWarp();
            }

            _warpIsOurs = false;
            return;
        }

        if (!double.IsFinite(SecondsToTheNextThingThatMatters))
        {
            if (KsaWorld.IsAutoWarpActive)
            {
                Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, "
                         + "there is nothing left to wait for");
                KsaWorld.StopAutoWarp();
            }

            _warpIsOurs = false;
            return;
        }

        // A hold's ignition can come inside the margin when the window moves under a warp.
        if (KsaWorld.IsAutoWarpActive && SecondsToTheNextThingThatMatters <= IcbmProgram.WarpHoldLeadSeconds)
        {
            Log.Info($"stopping the warp on {KsaWorld.DisplayName(Craft)}, "
                     + $"{IcbmProgram.Clock(SecondsToTheNextThingThatMatters)} to go");
            KsaWorld.StopAutoWarp();
            _warpIsOurs = false;
            return;
        }

        // It stops itself at the margin.
        if (KsaWorld.IsAutoWarpActive) return;

        // A hop finished. Close the remaining gap with another, shorter and therefore slower one.
        if (CanWarpAhead && TryWarpAhead("what is next")) return;

        _warpIsOurs = false;
    }
}
