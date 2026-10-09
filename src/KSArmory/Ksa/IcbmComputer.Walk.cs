using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // One frame of the walk: finish a stop whose warheads have gone, and either re-aim at the next
    // or end it. Nothing here runs for a set of one -- ReleaseLoop.Plan answers OneStop for every
    // one-stop set and ReleaseWalker.Walking is then false, which is the whole of what leaves a
    // single-target flight the shot every accuracy measurement on this mod was taken against.
    private void StepTheWalk(IManualFire? weapon)
    {
        if (!_walker.Walking) return;

        ReleaseStep step = _walker.Step;

        // A stop the rack cannot fill is over rather than still owed: waiting for a warhead that is
        // not there strands the bus on one target with the rest of the walk unflown. The salvo's own
        // size is the bound that holds either way -- a launcher REFILLS a few seconds after a salvo,
        // so what is loaded stops meaning what is left the moment the first warhead goes.
        bool dry = weapon is not { TubesReadyToFire: > 0 } || _sequence.Emptied
                   || (_salvoSize > 0 && WarheadsAway >= _salvoSize);

        if (step.ReleaseHere && !dry) return;

        SayTheStop(step);

        if (step.Handover)
        {
            HopHold hold = TheTrimWillFlyTheNextHop(step);

            if (hold != HopHold.WillFly)
            {
                CurtailTheWalk(step, hold);
                return;
            }

            _walker.Advance();
            HandOverTo(step.NextTarget);
            return;
        }

        _walker.Finish();

        Log.Info(ReleaseWalker.SayWalk(KsaWorld.DisplayName(Craft), _wentTo,
                                       _trim.SpentMetresPerSecond, Config.TrimBudgetMetresPerSecond,
                                       Math.Max(0, _salvoSize - WarheadsAway)));
    }

    // Asked before the lead moves, and it is the trim's own question rather than the planner's: a
    // hop is priced as the velocity change between two solutions, and BusTrim is handed the whole
    // difference between the vehicle's velocity and the new solution's -- so the residual the
    // release just left on the bus is added to it. Over the ceiling the trim refuses the WHOLE pass;
    // over the budget it refuses nothing and the release happens with no divert flown.
    //
    // Zero cycles because HandOverTo resets the correction, so the next pass is the trim's first and
    // PostCutoffSequence hands it the constant.
    private HopHold TheTrimWillFlyTheNextHop(in ReleaseStep step)
        => ReleaseLoop.CanFlyTheHop(
               step.NextHopMetresPerSecond, _trim.ToGainMetresPerSecond,
               PostCutoffSequence.CeilingFor(0, Config.TrimBudgetMetresPerSecond,
                                             _trim.SpentMetresPerSecond),
               _trim.SpentMetresPerSecond, Config.TrimBudgetMetresPerSecond);

    // The stop the bus is on takes the rest: it is already trimmed and corrected onto this target,
    // so the warheads left are delivered rather than assigned to a place nothing flew the bus to.
    private void CurtailTheWalk(in ReleaseStep step, HopHold hold)
    {
        double owed = _trim.ToGainMetresPerSecond;
        double wants = ReleaseLoop.PassMustFly(step.NextHopMetresPerSecond, owed);
        int left = Math.Max(0, _salvoSize - WarheadsAway);

        _walker.StopHere();

        string why = hold == HopHold.BeyondTheBudget
            ? $"the flight has spent {_trim.SpentMetresPerSecond:F2} m/s of its "
              + $"{Config.TrimBudgetMetresPerSecond:F0} and cannot pay the {wants:F2} m/s more"
            : $"one pass would have to fly {wants:F2} m/s of "
              + $"{BusTrim.CeilingFor(double.NaN):F0} and would refuse all of it";

        Log.Info($"walk on {KsaWorld.DisplayName(Craft)}: the hop to target {step.NextTarget} is "
                 + $"{step.NextHopMetresPerSecond:F2} m/s and the bus still owes {owed:F2}, so "
                 + $"{why} -- the walk ends here and target {step.Target} takes the {left} "
                 + $"warhead{(left == 1 ? "" : "s")} left");
    }

    // What one stop delivered, in the shape a night is scored off. Step.Away rather than the quota:
    // a stop the rack could not fill sent fewer, and the line is the record of what actually left.
    private void SayTheStop(in ReleaseStep step)
    {
        string site = step.Target >= 0 && step.Target < _targets.Count
                          ? _targets.Entries[step.Target].Site.Describe()
                          : "an unknown place";

        _wentTo.Add((step.Target, site, step.Away));

        Log.Info(ReleaseWalker.SayRelease(_walker.Stop + 1, _walker.Walk.Stops,
                                          KsaWorld.DisplayName(Craft), step.Target, site, step.Away,
                                          _trim.SpentMetresPerSecond - _spentAtStopStart,
                                          Math.Max(0.0, Config.TrimBudgetMetresPerSecond
                                                        - _trim.SpentMetresPerSecond)));
    }

    // Put the bus on the next target. Everything cleared here belongs to the stop just flown, and
    // every one of them is a fresh start rather than a reset of the flight: the trim keeps what it
    // has spent and what it has learned about the thrusters, because the budget is the flight's and
    // the acceleration is the vehicle's.
    private void HandOverTo(int next)
    {
        // The plan and the list have to index the same entries, and a lead that will not take means
        // they no longer do. Ending the walk holds the rest of the warheads aboard, which is what an
        // unassigned one already does; carrying on would aim the bus at whatever slid into the gap.
        if (!_targets.SetLead(next))
        {
            _walker.Finish();
            Log.Warn($"walk on {KsaWorld.DisplayName(Craft)}: target {next} is no longer in the "
                     + "list, so the walk ends here and the rest of the warheads stay aboard");
            return;
        }

        // Retarget rather than Reset, which would re-seed the plant at the pre-burn 1/Gain.
        _aim.Retarget(Config.CarryAimBiasAcrossHops);

        // Re-solved to the same committed arrival, which is what makes a hop a hop rather than a
        // new shot: every warhead of the walk arrives at one instant however far apart they land.
        Program.CorrectCoastArc();

        // Resume, never Reset: Reset zeroes SpentMetresPerSecond and hands the whole budget back,
        // so a six-stop walk would price every hop as if it were the first.
        _trim.Resume();
        _postBoost.Reset();

        // Its tube reference is the attitude the last stop's correction converged at, and the bus
        // is about to turn off it.
        _sequence.Reset();

        // A probe of the previous target's trajectory solves the wrong separation kick, and the
        // miss-kick sums are the shape of that stop's group rather than of this one's.
        _salvoProbe.Forget();
        _flownKick = null;

        _trimAbandoned = false;
        _postBoostSaid = false;
        _measureDue = false;
        _freshMiss = double.NaN;
        _holdingCost = double.NaN;
        _holdingCostForPass = -1;
        _trimFloor = double.NaN;
        _trimFloorForPass = -1;
        _saidCleared = false;
        _saidTrim = "";
        _trimShape = "";
        _saidLast = "";
        _spentAtStopStart = _trim.SpentMetresPerSecond;

        Log.Info($"walk on {KsaWorld.DisplayName(Craft)}: re-aiming at target {next} "
                 + $"{_targets.Entries[next].Site.Describe()}, stop {_walker.Stop + 1} of "
                 + $"{_walker.Walk.Stops}, {_walker.Step.Warheads} warhead"
                 + $"{(_walker.Step.Warheads == 1 ? "" : "s")} to go there, "
                 + $"{_walker.Step.HopMetresPerSecond:F2} m/s of hop priced");
    }
}
