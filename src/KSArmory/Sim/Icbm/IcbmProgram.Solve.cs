using Brutal.Numerics;

namespace KSArmory;

internal sealed partial class IcbmProgram
{
    private void Resolve(in IcbmState state)
    {
        bool burning = IsBurning;
        if (burning && _lastStep > 0.0 && !_pulsedOff) _countdown -= _lastStep * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0);

        bool due = _sinceSolve >= SolveIntervalSeconds
                || _countdown <= SolveEveryStepWithin
                || Arc is null;

        if (!due) return;

        double sinceLastSolve = double.IsFinite(_sinceSolve) ? _sinceSolve : SolveIntervalSeconds;
        _sinceSolve = 0.0;

        double arrivalFromNow = double.IsFinite(_arrivalFromLaunch)
                              ? _arrivalFromLaunch - _sinceLaunch
                              : double.NaN;

        double wasUnavoidable = _unavoidable;
        _unavoidable = Unavoidable(state);
        if (Config.LatchWaitsOutTheSolids && Phase == IcbmPhase.ClosedLoop && wasUnavoidable > 0.0 && !(_unavoidable > 0.0))
        {
            _sinceSolidsOut = 0.0;
        }

        // Under the drag solve the aim is moved and the floor is judged on the vacuum arc to the moved
        // point, which is shallower than the warhead's real one: a floor applied there unlatches the
        // arrival a short shot pins, and the latch is what stops it chasing a lower arc.
        bool dragGate = SolvesWithDrag(state);
        double floorDeg = dragGate && _dragEngaged ? 0.0 : FloorDeg;
        double3 aimUsed = dragGate ? Offset(state.AimNowCci, _dragEast, _dragNorth, state.Body) : state.AimNowCci;

        bool steered = BurnoutGuidance.TrySteer(
            state.Body, state.PositionCci, state.VelocityCci, aimUsed, state.Booster,
            out BurnoutGuidance.Command command, Config.Loft, LongWay, _cutoffSeed, _flightSeed,
            arrivalFromNow, floorDeg, _unavoidable);

        // A floor is what to aim for, not a reason to fly nowhere. A stack that cannot afford the
        // arrival asked for still has a target, and the shallow arc it can afford is worth far more
        // than a refusal: the same rule as a shot short of the propellant, which is flown and
        // reported. What the operator loses is precision, and the readout says which angle it got.
        if (steered)
        {
            _arrivalFloorUnaffordable = false;
        }
        else if (floorDeg > 0.0)
        {
            steered = BurnoutGuidance.TrySteer(
                state.Body, state.PositionCci, state.VelocityCci, aimUsed, state.Booster,
                out command, Config.Loft, LongWay, _cutoffSeed, _flightSeed, arrivalFromNow);

            if (steered) _arrivalFloorUnaffordable = true;
        }

        if (!steered)
        {
            // A flight already under way keeps flying its schedule: the geometry a solve needs can
            // be momentarily out of reach on the way up, and abandoning a shot for it would throw
            // away a launch that is going perfectly well.
            if (Arc is null) (_reachIfNoArc, _reachHold) = WhyNot(state);
            return;
        }

        if (dragGate && command.SecondsToCutoff <= DragSolveWithinSeconds)
        {
            command = SolveWithDrag(state, command, arrivalFromNow);
        }

        Arc = command.Arc;
        // Not re-read on a dry frame: the cutoff at a solid's burnout asks what the arc was matched to
        // while it burned.
        if (state.Booster.CanThrust)
        {
            _absorbing = (_unavoidable > 0.0 || (Config.FlyAnyRange && !state.RunningStageCanStop))
                         && command.VelocityToGain <= _unavoidable * 1.01 + BusTrim.MaxMetresPerSecond;
            _shortShot |= _absorbing;
        }
        CutoffPositionCci = command.CutoffPositionCci;
        ReferencePositionCci = command.CutoffPositionCci;
        SecondsSinceReference = 0.0;
        _cutoffSeed = command.SecondsToCutoff;
        _flightSeed = command.Arc.CheapestFlightSeconds;
        if (Config.ShortShotPushesThroughAStall && _shortShot && Phase == IcbmPhase.ClosedLoop && sinceLastSolve > 0.0
            && !_lastToGainCci.Equals(Vec.Zero) && !_thrustDirCci.Equals(Vec.Zero))
        {
            // What moved the velocity to gain other than the thrust: the stack's drag, which the vacuum arc does not see,
            // and the drag solve walking the aim.
            double3 axis = state.ThrustAxisCci.Equals(Vec.Zero) ? _thrustDirCci : state.ThrustAxisCci;
            double3 push = (command.ToGainVectorCci - _lastToGainCci) / sinceLastSolve
                         + axis * (state.Booster.AccelerationNow * Math.Clamp(state.ThrottleAchieved, 0.0, 1.0));
            _pushCci += (push - _pushCci) * Math.Clamp(sinceLastSolve / PushFilterSeconds, 0.0, 1.0);
            LatchPushOnDragShare(state);
        }
        _lastToGainCci = command.ToGainVectorCci;
        _fullToGainVectorCci = default;
        // Only a ramp being pushed through a stall: one that closes on its own leaves almost nothing, and flown on Real
        // SRB4 at 25 km, projected, it cut off with 1.03 m/s off the line and landed 167 m out against 0.9 mm.
        if (Config.ShortShotCutsOffOnTheMiss && dragGate && PushesThrough(state) && command.VelocityToGain <= CutsOffOnTheMissBelow
            && TryMissFreeLine(state, command, out double3 free))
        {
            // Only on a long fall, and only where the air lets the stack point along the line. Ending the burn early moves
            // the cutoff off the solved one, which a pinned 60 s fall from 25 km pays 20 m for and a 200 s one does not;
            // and flown at 9 km the airflow limit held the stack off the line and the burn ended 8.8 m/s short.
            BurnoutGuidance.Command off = OffTheLine(state, command, free);
            bool pointable = off.ToGainVectorCci.Equals(Vec.Zero)
                             || Vec.AngleBetween(Limit(off.ThrustDirectionCci, state), off.ThrustDirectionCci)
                                <= double.DegreesToRadians(MissFreeLineAllowanceDeg);
            if (pointable && command.Arc.FlightSeconds >= MissFreeMinFlightSeconds)
            {
                _fullToGainVectorCci = command.ToGainVectorCci;
                command = off;
            }
        }
        if (_pulsing && sinceLastSolve <= _lastStep * 1.5) _pulseQuantum = Math.Max(_pulseQuantum, _toGain - command.VelocityToGain);
        _toGain = command.VelocityToGain;
        _toGainVectorCci = command.ToGainVectorCci;
        _lowestToGain = Math.Min(_lowestToGain, _toGain);
        if (double.IsNaN(ToGainAtIgnition) && IsBurning) ToGainAtIgnition = _toGain;

        bool pushing = PushesThrough(state);
        double holdBelow = pushing ? Math.Min(HoldDirectionThreshold(state), PushesThroughHoldBelow) : HoldDirectionThreshold(state);
        HoldDirectionBelowNow = holdBelow;

        // Within this much of the thrust being made, what is left to gain turns faster than the stack
        // can follow it, and steering straight at it chases. So the line is turned toward it at a
        // bounded rate instead: fast enough for what drag and gravity do to it, too slow to run away.
        bool slewed = SlowsTheLine(state) && _toGain > holdBelow && !_thrustDirCci.Equals(Vec.Zero) && !_lineCarriedOver
                      && _toGain < state.Booster.AccelerationNow
                                   * Math.Clamp(state.ThrottleAchieved, state.MinThrottle, 1.0)
                                   * Config.ShortShotSlowsLineSeconds;

        LineSlowed = slewed;

        if (slewed)
        {
            double turn = double.DegreesToRadians(SlowLineDegPerSec) * sinceLastSolve;
            _thrustDirCci = Vec.TurnToward(_thrustDirCci, command.ThrustDirectionCci, turn);

            double along = Vec.Dot(command.ToGainVectorCci, _thrustDirCci);
            double seconds = state.Booster.SecondsToGain(Math.Max(along, 0.0));
            _countdown = double.IsFinite(seconds) ? seconds : 0.0;
        }
        else if (_toGain > holdBelow || _thrustDirCci.Equals(Vec.Zero) || (_lineCarriedOver && SlowsTheLine(state)))
        {
            _thrustDirCci = pushing ? LeadAcrossThePush(state, command) : command.ThrustDirectionCci;
            _countdown = command.SecondsToCutoff;
            if (Phase == IcbmPhase.ClosedLoop) _lineCarriedOver = false;
        }
        else
        {
            // Steering is frozen, so only the component along the line being thrust can still be
            // removed; counting down the whole length overshoots and grows the residual again.
            double along = Vec.Dot(command.ToGainVectorCci, _thrustDirCci);
            double seconds = state.Booster.SecondsToGain(Math.Max(along, 0.0));
            _countdown = double.IsFinite(seconds) ? seconds : 0.0;
        }
        AssessReach(state, command.VelocityToGain);

        if (Phase is IcbmPhase.Rising or IcbmPhase.PitchProgram)
        {
            DownrangeCci = AscentProfile.Downrange(state.UpCci, command.Arc.RequiredVelocityCci,
                                                   state.Body.GroundVelocityCci(state.PositionCci));
        }

        // The closed loop latches the arrival (LatchMayCommit); docs/ICBM-GUIDANCE.md, "Flight time is the parameter".
        // It waits for a steady aim because a pinned arrival on a shallow arc moves the impact several times further
        // than the aim, which puts the aim correction past its stability limit; the window bounds the wait. A short
        // shot pins at once: left free, the cheapest arc from a lofted one is lower and the velocity to gain swings
        // backwards. Re-pinned every solve while the solids are matched, so the next stage finishes their arc.
        if (Config.SolidsLeaveMetresPerSecond > 0.0 && _absorbing && _unavoidable > 0.0
            && double.IsFinite(command.CarrySeconds))
        {
            // From where the arc departs, the solids' burnout, never the uncapped time to gain: that
            // counts the margin at the solids' own tail-off thrust, seconds after they are spent.
            _arrivalFromLaunch = _sinceLaunch + command.CarrySeconds + command.Arc.FlightSeconds;
        }
        else if (Phase == IcbmPhase.ClosedLoop && !double.IsFinite(_arrivalFromLaunch) && !(_unavoidable > 0.0)
            && LatchMayCommit(state))
        {
            _arrivalFromLaunch = _sinceLaunch + command.SecondsToCutoff + command.Arc.FlightSeconds;
        }
        else if (double.IsFinite(_arrivalFromLaunch) && !command.HeldTheArrival)
        {
            // A pinned transfer angle can walk onto the one geometry Lambert cannot answer: give the
            // arrival up and follow the cheapest arc, rather than taking the fallback every cycle.
            _arrivalFromLaunch = double.NaN;
        }
    }

    // The closed loop's latch: a steady aim, an aim that sits out the burn, the window, or a short shot. Once solids
    // have burned out inside the loop, the window and the steady aim count from their burnout instead.
    private bool LatchMayCommit(in IcbmState state)
    {
        if (double.IsFinite(_sinceSolidsOut))
        {
            return (state.AimIsSteady && _sinceSolidsOut >= SteadyAfterTheSolidsSeconds) || AimSitsOutTheBurn
                || _sinceSolidsOut >= LatchArrivalWithinSeconds || (_shortShot && !OutranTheStack);
        }

        return state.AimIsSteady || AimSitsOutTheBurn || _sinceClosedLoop >= LatchArrivalWithinSeconds
            || (_shortShot && !OutranTheStack);
    }
}
