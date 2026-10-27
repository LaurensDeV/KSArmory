using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    // Later than the window held by more than a refinement could move it, or earlier but dearer than good enough.
    private bool JumpedAway(in BurnWindow.Window fresh, in IcbmState state)
    {
        double period = Kepler.PeriodSeconds(state.Body.Mu, state.PositionCci, state.VelocityCci);
        double span = (double.IsFinite(period) && period > 0.0 ? period : BurnWindow.UnboundHorizonSeconds) / BurnWindow.NearSamples;

        if (fresh.WaitSeconds > _windowAnchor + span) return true;
        return fresh.WaitSeconds < _windowAnchor - span && fresh.Cost > _windowCost * (1.0 + BurnWindow.GoodEnoughFraction);
    }

    // Coasting on purpose. Nothing here commands an engine; the whole job is deciding when to.
    private IcbmCommand Hold(in IcbmState state)
    {
        if (_sinceWindow >= WindowIntervalSeconds || !double.IsFinite(_windowWait))
        {
            _sinceWindow = 0.0;

            // A fresh search is taken when it agrees with the window held, or finds an earlier one as good; only when it has
            // jumped later or dearer is the held window refined instead -- about the instant first chosen, never about the
            // last refinement, which walks a window that is cheaper a little later away for ever.
            BurnWindow.Window window = default;
            bool have = BurnWindow.TryFind(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                           out window, Config.Loft, FloorDeg);
            bool holding = Config.HoldKeepsItsWindow && double.IsFinite(_windowAnchor) && _windowAnchor > 0.0;
            bool kept = false;

            if (holding && (!have || JumpedAway(window, state)))
            {
                if (BurnWindow.TryKeep(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci, _windowAnchor,
                                       out BurnWindow.Window held, Config.Loft, FloorDeg))
                {
                    window = held;
                    kept = true;
                    have = true;
                }
            }

            if (have)
            {
                // Waiting is a fallback, not an optimisation. A weapon whose whole point is
                // arriving is not worth holding in orbit for ninety metres a second — but leaving
                // now can also be flatly impossible, or cost the entire orbital velocity, and that
                // is what this is for.
                bool worthWaiting = !double.IsFinite(window.CostIfLeavingNow)
                                 || window.Saving >= WaitMustSaveMetresPerSecond
                                 || (Config.HoldKeepsItsWindow && _windowWait > 0.0);

                _windowWait = worthWaiting ? window.WaitSeconds : 0.0;
                if (!kept) _windowAnchor = _windowWait;
                _windowCost = window.Cost;
                _windowDirection = window.BurnDirectionCci;
                if (!kept) _closestOffPlane = window.ClosestOffPlaneRadians;
                Arc = window.Arc;
                _flightSeed = window.Arc.CheapestFlightSeconds;
                AssessReach(state, window.Cost);
            }
            else
            {
                Phase = IcbmPhase.NoSolution;
                (Reach, string why) = NoWindow(state);
                return Idle(state, why);
            }
        }

        double lead = state.Booster.CanThrust
                          ? 0.5 * state.Booster.SecondsToGain(_windowCost)
                          : AssumedBurnLeadSeconds;

        if (!double.IsFinite(lead)) lead = AssumedBurnLeadSeconds;

        _burnLead = lead;

        // A burn lit on a warped step flies its first frame for minutes; NeedsShortSteps is already slowing the world.
        // One lit mid-turn pushes off its line, so after a hold long enough to have drifted a late turn is waited for.
        if (_windowWait > HoldPointing.MaxLeadSeconds) _heldToDrift = true;

        bool turned = !Config.HoldDriftsUntilIgnition || !_heldToDrift || state.ThrustAxisCci.Equals(Vec.Zero)
                      || _windowDirection.Equals(Vec.Zero)
                      || Vec.AngleBetween(state.ThrustAxisCci, _windowDirection) <= double.DegreesToRadians(LightWithinDeg)
                      || _waitedToTurn >= WaitToTurnSeconds;

        if (_windowWait <= lead && _lastStep <= LightOnStepsUnderSeconds && !turned) _waitedToTurn += _lastStep;

        if (_windowWait <= lead && _lastStep <= LightOnStepsUnderSeconds && turned)
        {
            Phase = IcbmPhase.ClosedLoop;
            _lineCarriedOver = true;

            // Solve before steering, not after. The closed loop opens by asking whether the burn is
            // already finished, and the velocity still to gain is zero until something has worked
            // it out — so handing straight over cuts the engines off before they light.
            _sinceSolve = double.PositiveInfinity;
            Resolve(state);

            return Arc is null ? Idle(state, _reachHold) : ClosedLoop(state);
        }

        // Pointed where the burn will be, so the vehicle is already settled when the window opens.
        double3 facing = _windowDirection.Equals(Vec.Zero) ? Vec.Unit(state.VelocityCci) : _windowDirection;

        return new IcbmCommand(IcbmPhase.Holding, facing, 0.0, EngineOn: false, RequestStage: false,
                               VelocityToGain: _windowCost, SecondsToCutoff: double.NaN,
                               ReadyToDeploy: false,
                               Hold: $"holding for the burn window, {Clock(_windowWait)} away",
                               Reach: Reach, SecondsToArrival: SecondsToArrival,
                               SecondsToBurn: SecondsToIgnition,
                               ShortfallMetresPerSecond: _shortfall);
    }
}
