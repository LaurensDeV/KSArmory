using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    // What the operator may ask for, refreshed rarely. The stack's whole delta-v where the engine
    // reports it, because that is the figure that accounts for throwing dry mass away -- the running
    // stage's alone understates a staged rocket badly enough to cap the control at a few degrees on
    // a vehicle that could fly thirty.
    private void RefreshArrivalBudget(in IcbmState state)
    {
        if (_sinceArrivalBudget < ArrivalBudgetIntervalSeconds) return;

        _sinceArrivalBudget = 0.0;

        double available = state.StackDeltaV > 0.0 && double.IsFinite(state.StackDeltaV)
                               ? state.StackDeltaV
                               : state.Booster.DeltaVRemaining;

        SteepestAffordableArrivalDeg = ArrivalBudget.SteepestAffordableDeg(
            state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci, available, Config.Loft);

        LatchArrivalFloor();
    }

    // Once, the first time the budget answers with an angle. Preference zero leaves the operator's
    // own number alone, which is what ships; above zero the floor is that fraction of what the tanks
    // can pay for, and never below what was asked for outright.
    //
    // A budget of zero is not an angle. It is ArrivalBudget saying no arc at all is affordable from
    // here, which is the ordinary state of a vehicle on the pad with the whole burn still to fly --
    // so latching a fraction of it pins the floor at zero for the flight and the preference never
    // applies. Flown: three of four rockets latched 0.0 off the pad and arrived wherever the
    // unfloored arc took them.
    private void LatchArrivalFloor()
    {
        if (double.IsFinite(ArrivalFloorDeg)) return;

        if (!(Config.ArrivalPreference > 0.0) || !(SteepestAffordableArrivalDeg > 0.0))
        {
            return;
        }

        double wanted = Config.ArrivalPreference * SteepestAffordableArrivalDeg;

        ArrivalFloorFromDeg = SteepestAffordableArrivalDeg;
        ArrivalFloorDeg = Math.Max(Config.MinArrivalAngleDeg, wanted);
    }

    /// <summary>Whether the aim correction sits out this flight's burn, <see cref="IcbmConfig.AimWaitsForCutoffUnderAFloor"/>.</summary>
    public bool AimSitsOutTheBurn
        => Config.AimWaitsForCutoffUnderAFloor && (Config.MinArrivalAngleDeg > 0.0 || Config.ArrivalPreference > 0.0);

    // What the search is bounded by: the latched floor, or the operator's own number.
    private double FloorDeg => double.IsFinite(ArrivalFloorDeg)
                                   ? ArrivalFloorDeg
                                   : Config.MinArrivalAngleDeg;

    // Whether the tanks can pay for the shot the solver found.
    //
    // Prefers the engine's own per-stage total, which is the only figure that accounts for staging
    // throwing dry mass away. Falling back to the running stage's exhaust velocity over the whole
    // vehicle's propellant understates a staged rocket badly -- enough to call an ordinary ICBM
    // unreachable while it sits on the pad with the range to spare. Understating is at least the
    // right way round to be wrong, which is why it remains the fallback.
    private void AssessReach(in IcbmState state, double required)
    {
        bool wholeStack = state.StackDeltaV > 0.0 && double.IsFinite(state.StackDeltaV);
        double available = wholeStack ? state.StackDeltaV : state.Booster.DeltaVRemaining;

        NeedMetresPerSecond = required;
        HaveMetresPerSecond = available > 0.0 ? available : double.NaN;
        HaveIsTheWholeStack = wholeStack;

        if (!(available > 0.0))
        {
            Reach = IcbmReach.Unknown;
            _shortfall = 0.0;
            return;
        }

        _shortfall = Math.Max(0.0, required - available);
        Reach = _shortfall > 0.0 ? IcbmReach.ShortOfPropellant : IcbmReach.Reachable;
    }

    // Why there is no arc, as the banner to show and the line to print under it. A target beyond
    // any trajectory needs a different target; one no arc arrives steeply enough at needs the floor
    // lowered; one this stack cannot reach needs a bigger rocket.
    private (IcbmReach Reach, string Why) WhyNot(in IcbmState state)
    {
        if (!BallisticArc.TryCheapest(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                      out BallisticArc.Solution reachable, Config.Loft, LongWay,
                                      double.NaN, FloorDeg))
        {
            // Asked again with the floor off, because "there is no trajectory" and "there is no
            // trajectory that arrives that steeply" are the same silence from outside and only one
            // of them is about a setting the operator can move.
            if (FloorDeg > 0.0
                && BallisticArc.TryCheapest(state.Body, state.PositionCci, state.VelocityCci,
                                            state.AimNowCci, out BallisticArc.Solution shallow,
                                            Config.Loft, LongWay))
            {
                return (IcbmReach.TooShallow,
                        $"nothing arrives at {FloorDeg:F0} deg or steeper from here; "
                        + $"the cheapest arc arrives at {shallow.ArrivalAngleDeg:F0} deg");
            }

            return (IcbmReach.NoTrajectory, "no trajectory reaches that target");
        }

        // Everything past here keeps NoTrajectory, which is not literally true of either — the
        // detail is in the line beside it, and the reach is what the red banner reads. The floor
        // above is the one exception because it is the only one a control on this panel fixes.
        if (!state.Booster.CanThrust) return (IcbmReach.NoTrajectory, "no engine running");

        double needed = Vec.Len(reachable.VelocityToGain(state.VelocityCci));
        double have = state.Booster.DeltaVRemaining;
        return (IcbmReach.NoTrajectory,
                $"not enough in the tanks: needs {needed / 1000.0:F1} km/s, has {have / 1000.0:F1} km/s");
    }

    // The same question for the orbital case, where the search is over departures as well as
    // flight times: a floor that no window satisfies is not the same as a target the orbit cannot
    // reach, and the unconstrained search already measures the steepest arrival it saw.
    private (IcbmReach Reach, string Why) NoWindow(in IcbmState state)
    {
        if (FloorDeg > 0.0
            && BurnWindow.TryFind(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                  out BurnWindow.Window any, Config.Loft))
        {
            return (IcbmReach.TooShallow,
                    $"no window arrives at {FloorDeg:F0} deg or steeper; "
                    + $"the steepest one found arrives at {any.SteepestArrivalDeg:F0} deg");
        }

        return (IcbmReach.NoTrajectory, "no trajectory reaches that target from this orbit");
    }
}
