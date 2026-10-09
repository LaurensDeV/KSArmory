using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    // Coasting on purpose. Nothing here commands an engine; the whole job is deciding when to.
    private IcbmCommand Hold(in IcbmState state)
    {
        if (_sinceWindow >= WindowIntervalSeconds || !double.IsFinite(_windowWait))
        {
            _sinceWindow = 0.0;

            if (BurnWindow.TryFind(state.Body, state.PositionCci, state.VelocityCci, state.AimNowCci,
                                   out BurnWindow.Window window, Config.Loft, FloorDeg))
            {
                // Waiting is a fallback, not an optimisation. A weapon whose whole point is
                // arriving is not worth holding in orbit for ninety metres a second — but leaving
                // now can also be flatly impossible, or cost the entire orbital velocity, and that
                // is what this is for.
                bool worthWaiting = !double.IsFinite(window.CostIfLeavingNow)
                                 || window.Saving >= WaitMustSaveMetresPerSecond;

                _windowWait = worthWaiting ? window.WaitSeconds : 0.0;
                _windowCost = window.Cost;
                _windowDirection = window.BurnDirectionCci;
                _closestOffPlane = window.ClosestOffPlaneRadians;
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

        if (_windowWait <= lead)
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
                               SecondsToBurn: Math.Max(_windowWait, 0.0),
                               ShortfallMetresPerSecond: _shortfall);
    }
}
