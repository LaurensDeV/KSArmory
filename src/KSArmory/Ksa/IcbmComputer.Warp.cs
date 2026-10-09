using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // How much of the remaining wait each warp leaves for the next one, and the most it will leave.
    // A fraction rather than a constant because the span is what decides how fast KSA warps: a
    // fixed margin off a ninety-minute hold is still approached at thousands of times speed.
    private const double MarginFraction = 0.2;

    private const double MaxMarginSeconds = 900.0;

    /// <summary>
    /// Hand the wait to KSA's own warp-to-a-time. Pressed, never automatic.
    ///
    /// <para>Warping is an action rather than a setting, and taking the player's time control
    /// because a target happened to be designated is not a thing a weapon gets to do. They may have
    /// set a tenth speed to watch something.</para>
    ///
    /// <para>One press covers the whole wait, in hops. That is not tidiness — it is the only way
    /// the handover can work. KSA scales its warp rate to the <em>span</em> it is asked to cover, so
    /// a single jump to the end of a ninety-minute hold arrives doing thousands of times normal
    /// speed, where the last minute of it passes in under two frames and there is nowhere to hand
    /// over. Each hop leaves a margin, and the next one covers a shorter span and so runs gentler,
    /// until the approach is slow enough to be caught.</para>
    /// </summary>
    public bool TryWarpToWindow() => TryWarpAhead("the burn window");

    /// <summary>
    /// The same, for the long fall after the engines stop.
    ///
    /// <para>Stops a settling margin short of the release rather than at it — see
    /// <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>, which is where the number and the
    /// measurement behind it live.</para>
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

    // What else in the world was still being integrated when this warp started. An auto-warp is
    // world-wide and WarpPolicy cannot rein one in, so every name here is a flight that is about to
    // be stepped at whatever rate KSA picked for somebody else's coast.
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

    /// <summary>Whether the window is far enough away for warping to it to be worth offering.</summary>
    public bool CanWarpToWindow => Program.Phase == IcbmPhase.Holding && CanWarpAhead;

    /// <summary>Whether the coast has enough left in it to be worth warping.</summary>
    public bool CanWarpTheCoast => Program.Phase == IcbmPhase.Coast && CanWarpAhead;

    // Only for the craft being flown, only out to a margin short of what is coming, and never while
    // ANY flight in the world is being integrated -- NeedsShortSteps covers the burn and the trim,
    // and WarpPolicy cannot slow the world at all while an auto-warp is running, so a warp started
    // over the top of one is a warp nothing can rein in.
    //
    // Every flight, not this one. There is one world and one clock: a computer that checks only
    // itself hands the clock away while seven other rockets are still burning, and their longest
    // burn step goes 33 ms to 205 ms with their one-frame velocity quantum 0.081 to 1.675 m/s.
    // Same rule and same reason as WorldSpeed.Slowest, which the speed path already obeys.
    private bool CanWarpAhead
        => !KsaWorld.IsAutoWarpActive
        && !NeedsShortSteps
        && !AnythingElseNeedsShortSteps
        && ReferenceEquals(Craft, KsaWorld.ControlledVehicle)
        && double.IsFinite(SecondsToTheNextThingThatMatters)
        && SecondsToTheNextThingThatMatters > IcbmProgram.WarpHoldLeadSeconds * 2.0;

    // How far off the next thing this computer has to be awake for is, or NaN when there is nothing
    // to wait for. Two waits, and they are one problem: a departure window in orbit, and the release
    // point at the end of a ballistic coast. Both are a known instant minutes or hours away with
    // nothing to do until then, which is what KSA's warp-to-a-time is for.
    private double SecondsToTheNextThingThatMatters
        => Program.Phase switch
        {
            IcbmPhase.Holding => Command.SecondsToBurn,
            IcbmPhase.Coast => SecondsToReleaseApproach,
            _ => double.NaN,
        };

    /// <summary>
    /// How long until the warheads are due to leave, or NaN when nothing is waiting for that.
    ///
    /// <para>What a coast is actually counting down to. The arrival is minutes later and is a
    /// different question — and a shot that fell short holds its warheads for ever, which is why
    /// this is absent rather than large there.</para>
    ///
    /// <para>The <em>time</em> gate only. A release also waits for the deploy altitude on the way
    /// up, and where that is the binding one the hold line says so.</para>
    /// </summary>
    public double SecondsToRelease
    {
        get
        {
            if (Command.ShortfallMetresPerSecond > 0.0) return double.NaN;

            double toArrival = Program.CommittedArrivalFromNow;
            if (!double.IsFinite(toArrival)) return double.NaN;

            return toArrival - Program.ReleaseGate;
        }
    }

    /// <summary>
    /// When the world has to be back at normal speed, which is earlier than the release itself.
    ///
    /// <para>The aim correction is still converging out here and at a hundred times its steps are
    /// seconds long — see <see cref="IcbmProgram.SteadyBeforeReleaseSeconds"/>.</para>
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

        // Still running: leave it alone. It stops itself at the margin, which is the whole reason
        // for asking it to stop short rather than braking the world by hand.
        if (KsaWorld.IsAutoWarpActive) return;

        // A hop finished. Close the remaining gap with another, shorter and therefore slower one.
        if (CanWarpAhead && TryWarpAhead("what is next")) return;

        _warpIsOurs = false;
    }

    // Hand the wait to KSA's own warp-to-a-time. Only while holding, only for the craft being
    // flown, and only out to a margin short of the burn - the last minute belongs to WarpPolicy,
    // which cannot slow the world down at all while an auto-warp is running.
}
