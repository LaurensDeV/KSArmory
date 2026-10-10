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

        _unavoidable = Unavoidable(state);

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
        }
        _lastToGainCci = command.ToGainVectorCci;
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
            // Steering is frozen, so thrust is no longer parallel to what is left to gain, and the
            // solver's countdown - the time to gain the whole *length* of it - overshoots. Only the
            // component along the line actually being thrust can still be removed; burning past it
            // grows the residual again, which is what the backstop was catching a whole metre a
            // second late.
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

        // Once closed-loop guidance has the vehicle, the arrival is nailed down: when the aim has
        // stopped moving, or once the window runs out. Before that the cheapest shot is the right
        // thing to follow, because the state is changing far too much for any arrival time chosen
        // on the pad to still be the cheapest one.
        //
        // Both loops are solving the same shot. Latching the arrival first makes the aim correction
        // solve against a pinned parameter: moving the aim then forces a different trajectory to
        // arrive at the same *instant*, which on a shallow arrival moves the impact several times
        // further than the aim moved and puts the correction above its stability limit. Left free,
        // the arc simply follows the aim and the same loop converges in a handful of cycles.
        //
        // Bounded, because the reason for latching at all is real: the cheapest arc from the
        // vehicle's current state converges on the arc it is already flying, so a loft above one
        // walks the answer outward every cycle and the shot chases a trajectory running away from
        // it — 162 km, measured. The window is what stops that being unbounded.
        // A short shot pins it the moment the loop has it. Left free, the cheapest arc from a point on
        // a lofted one is a lower one, so the velocity to gain swings round to point backwards and down
        // -- flown at 200 km, through a pause that coasted out of the air, into a relight a hundred
        // degrees off that burned the core dry turning.
        // Re-pinned every solve while the solids are matched, so the stage after them finishes the arc they
        // were steered onto: left free at burnout, the cheapest arc from a lofted one swung what was left
        // from 30 m/s along the nose to 6 m/s 75 deg off it.
        if (Config.SolidsLeaveMetresPerSecond > 0.0 && _absorbing && _unavoidable > 0.0
            && double.IsFinite(command.CarrySeconds))
        {
            // From where the arc departs, the solids' burnout, never the uncapped time to gain: that
            // counts the margin at the solids' own tail-off thrust, seconds after they are spent.
            _arrivalFromLaunch = _sinceLaunch + command.CarrySeconds + command.Arc.FlightSeconds;
        }
        else if (Phase == IcbmPhase.ClosedLoop && !double.IsFinite(_arrivalFromLaunch) && !(_unavoidable > 0.0)
            && (state.AimIsSteady || AimSitsOutTheBurn || _sinceClosedLoop >= LatchArrivalWithinSeconds || _shortShot))
        {
            _arrivalFromLaunch = _sinceLaunch + command.SecondsToCutoff + command.Arc.FlightSeconds;
        }
        else if (double.IsFinite(_arrivalFromLaunch) && !command.HeldTheArrival)
        {
            // The arrival that was latched turned out not to be solvable — a pinned transfer angle
            // can walk onto the one geometry Lambert cannot answer. Give it up rather than asking
            // for it again every cycle and taking the fallback every time; following the cheapest
            // arc is what this did before commitment and it works.
            _arrivalFromLaunch = double.NaN;
        }
    }
}
