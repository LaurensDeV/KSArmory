using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // One line per change of state, compared with its numbers collapsed: whole sentences wrote a line
    // every frame, 21,000 from one coast. The words still separate a direction change or a stall.
    private void Say(string what, string detail = "")
    {
        string shape = WithoutNumbers(what);
        if (shape == _trimShape) return;

        // The shape decides a new state; the sentence, with its numbers, is what the panel shows.
        _trimShape = shape;
        _saidTrim = what;
        Log.Info($"trimming the bus on {KsaWorld.DisplayName(Craft)}: {what}{detail}");
    }

    // Every run of digits down to one mark, so two readings of one state compare equal.
    private static string WithoutNumbers(string said)
    {
        Span<char> shape = stackalloc char[said.Length + 1];
        int n = 0;
        bool number = false;

        foreach (char c in said)
        {
            if (char.IsAsciiDigit(c) || c == '.')
            {
                number = true;
                continue;
            }

            if (number)
            {
                shape[n++] = '#';
                number = false;
            }

            shape[n++] = c;
        }

        if (number) shape[n++] = '#';

        return new string(shape[..n]);
    }

    // BusTrim decides; this converts the world into its situation and its answer into writes, and
    // knows the one thing it cannot: whether the split has actually landed.
    private void DriveTrim(double simStep, in IcbmState state, IManualFire? weapon)
    {
        // ReadyToDeploy stays true after the last warhead goes, so SalvoFinished stops a trim firing at
        // an empty bus. A shot releasing at cutoff is away before a vacuum trim could settle.
        if (!Config.TrimBeforeRelease || !Command.ReadyToDeploy || SalvoFinished || Program.ReleasesAtCutoff)
        {
            if (_trim.Firing != TrimAxes.None)
            {
                VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
                AttitudeHook.PulseMode(Craft, pulsing: false);
            }
            return;
        }

        // The split is deferred through the engine's input buffer; asking the joint again, rather than
        // a timer, says when it has landed.
        if (_awaitingSplit)
        {
            if (weapon is { CanSeparate: true }) return;
            _awaitingSplit = false;

            // Not `??=`: Rehome may have captured the pre-split vehicle, which the decoupler disposes.
            // Read before WhatWasDropped, which clears the census it counts.
            int before = _wasBeforeSplit.Count;

            if (!KsaWorld.IsAlive(_separatedFrom)) _separatedFrom = WhatWasDropped();

            // Everything the clearance test depends on, at the one instant it is decided.
            _afterSplit.Clear();
            KsaWorld.CollectVehicles(_afterSplit);

            Log.Info($"split on {KsaWorld.DisplayName(Craft)}: "
                     + $"{(_separatedFrom is null ? "no stack captured" : KsaWorld.DisplayName(_separatedFrom))}, "
                     + $"alive {KsaWorld.IsAlive(_separatedFrom)}, "
                     + $"{before} vehicles before and {_afterSplit.Count} after");
        }

        // Armed at the split, not at clearance, so the debt is on record from the decoupler on: what
        // separates an error the separation caused from one that grew while coasting clear.
        Clearance clearance = _didSplit ? Clear(simStep) : new Clearance(true, false, "");

        // Abandoned when the stack is readable and still too close. It also caps the correction loop,
        // which is load-bearing: handing safety to the keep-out interlock flew 5.39 km median against
        // 3.04 (docs/MIRV-NEXT.md 8h).
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearance.IsClear, clearance.Abandoned, _postBoost.Cycles,
            TrimBudgetMetresPerSecond, _trim.SpentMetresPerSecond,
            Config.KeepOutCoversTheClearance, _hopCeilingMetresPerSecond);

        if (plan.Abandon)
        {
            _trimAbandoned = true;
            if (_trim.Firing != TrimAxes.None)
            {
                VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
                AttitudeHook.PulseMode(Craft, pulsing: false);
            }
            Say(clearance.Said, "");
            return;
        }

        // Bounded across the flight, and enforced by the trim: its figure is cumulative, and the stop
        // must end the trim, since withheld fire never lifts and the warheads wait for the trim.
        double budget = TrimBudgetMetresPerSecond;

        if (!_saidBudget && !_trim.WithinBudget(budget))
        {
            _saidBudget = true;
            Log.Info($"trim: budget of {budget:F0} m/s spent; the warheads go on the aim as it is");
        }

        // Said here: once _mayTrim is true Say prints trim.Said alone, so the cleared sentence would
        // never reach the log (docs/MIRV-NEXT.md 8w).
        if (!_saidCleared && clearance.IsClear && _didSplit && clearance.Said.Length > 0)
        {
            _saidCleared = true;
            Log.Info($"clearance on {KsaWorld.DisplayName(Craft)}: {clearance.Said}");
        }

        // The turn's jets and the trim's are kept apart: the trim's acceleration estimate assumes a steady frame.
        _mayTrim = plan.MayTrim && !Turning && !HopWaitsForItsWarhead;

        _trim.Begin();

        double3 nose = Vec.Zero;
        double3 right = Vec.Zero;
        double3 down = Vec.Zero;

        // Zeroes rather than a skip: only a stepped trim runs down the budget that bounds a part
        // tree that never comes back.
        if (Parent is { } parent)
        {
            KsaWorld.TryControlFrameCci(Craft, parent, out nose, out right, out down);
        }

        double3 referenceVelocity = Program.Arc?.RequiredVelocityCci ?? Vec.Zero;

        TrimCommand trim = _trim.Update(simStep, new TrimSituation(
            state.Body, state.PositionCci, state.VelocityCci,
            Program.ReferencePositionCci, referenceVelocity, Program.SecondsSinceReference,
            nose, right, down, _mayTrim, budget, _keepOutTowardCci,
            plan.CeilingMetresPerSecond,
            Config.PulseTrim ? Config.PulseSeconds : 0.0,
            Config.StallFallsBackToHolding,
            Config.TrimCountsTheCommandInFlight,
            MaxSeconds: Config.HopsSizeTheirOwnSlots ? BusTrim.MaxSeconds + _hopBurnSeconds : double.NaN));

        // Through the attitude window: applying a worker's results copies the flight computer over
        // anything written outside it.
        AttitudeHook.PulseMode(Craft, trim.Pulse);
        AttitudeHook.KeepJetsThroughTheClear(Craft, Config.JetsThroughTheKeyboardClear);
        VehicleCommand.DriveTranslation(Craft, trim.Fire);
        _lastOwed = trim.ToGainMetresPerSecond;

        // Stepped through a turn, the passes count it as the bus not holding still and end the correction.
        if (Turning) return;

        // The post-boost passes: the trim is the only actuator left, so one that gave up ends them.
        // ReleaseImpulseCci() is what the prediction is fed, so steadiness is watched on that term.
        ReleaseAnArrivalTheTrimCannotFly(trim, state);

        if (ResolvedTheHopAgain()) return;

        int passesBefore = _postBoost.Cycles;

        MeasureHoldingCost();
        MeasureTrimFloor(simStep, in state);

        PostBoostAim.Decision pass = _postBoost.Update(simStep, new PostBoostSituation(
            TrimSettled: _trim.Done,
            ReleaseDirectionCci: ReleaseImpulseCci(),
            PredictedMissMetres: _freshMiss,
            AimHasSettled: _aim.Settled,
            TrimGaveUp: _trim.GaveUp,
            TrimSpentMetresPerSecond: _trim.SpentMetresPerSecond,
            BudgetMetresPerSecond: Config.HopsSizeTheirOwnSlots
                                       ? ReleaseLoop.PlanningBudget(TrimBudgetMetresPerSecond)
                                       : PostBoostAim.MaxTrimMetresPerSecond + ReserveReleasedMetresPerSecond,
            MaxSeconds: Config.HopsSizeTheirOwnSlots ? PostBoostAim.MaxSeconds + _hopBurnSeconds : double.NaN,
            HoldingCostMetresPerSecond: double.IsFinite(_holdingCost)
                                            ? _holdingCost
                                            : Config.HoldingCostMetresPerSecond,
            DecideOnTheReading: Config.DecideOnTheReading,
            ReleaseInsideTheTrimFloor: Config.ReleaseInsideTheTrimFloor,
            TrimFloorMetres: _trimFloor));

        if (pass.MayMeasure) _measureDue = true;

        if (_postBoost.Cycles > passesBefore)
        {
            // Consumed, so the next decision waits for a reading taken after this correction flew.
            _freshMiss = double.NaN;
            Program.CorrectCoastArc();
            _trim.Resume();
            Say($"post-boost: {pass.Said}", "");
        }

        // Which rule ended the correction, named per craft: the largest term in where the warheads
        // land (docs/MIRV-NEXT.md 8z). Not Say, which fires only when a cycle is taken.
        if (pass.MayRelease && !_postBoostSaid)
        {
            _postBoostSaid = true;

            Log.Info($"post-boost on {KsaWorld.DisplayName(Craft)}: {pass.Said}");

            // The revert moves no warhead: they leave this frame on the trajectory the last trim pass
            // flew, so "shipping" is bookkeeping, not the aim flown (docs/ACCURACY-PLAN.md 3cp).
            double3 walkedTo = _aim.BiasCci;
            _aim.Freeze();
            double reverted = Vec.Len(_aim.BiasCci - walkedTo);

            Log.Info($"aim frozen on {KsaWorld.DisplayName(Craft)}: shipping "
                     + $"{Distance.Say(Vec.Len(_aim.BiasCci))}, "
                     + (reverted > 0.0
                            ? $"reverted {Distance.Say(reverted)} from the {Distance.Say(Vec.Len(walkedTo))} "
                              + "the loop had walked to"
                            : "which is where the loop had walked to")
                     + $", best {Distance.Say(_aim.BestMissMetres)}");
        }

        if (!double.IsFinite(_owedAtSplit) && double.IsFinite(trim.ToGainMetresPerSecond))
        {
            _owedAtSplit = trim.ToGainMetresPerSecond;
            SayTheSplitDebt(trim, referenceVelocity, state, nose, right, down);
        }

        // A trim that stalls looks exactly like one that has finished, kilometres apart on the ground.
        if (trim.Said.Length == 0) return;

        // The panel gets the sentence; the log gets the numbers too.
        Say(_mayTrim ? trim.Said : clearance.Said + "; " + trim.Said,
            (trim.Acceleration > 0.0 ? $" (thrusters measured at {trim.Acceleration:F3} m/s2)" : "")
            + Grew()
            + Arrivals());
    }

    // Before the passes may measure: the arc solved from where the hop's burn left the bus.
    private bool ResolvedTheHopAgain()
    {
        if (_hopRound < 0 || !_trim.Done || _postBoost.Cycles > 0) return false;

        double spent = _trim.SpentMetresPerSecond - _spentAtHopRound;
        if (!ReleaseLoop.ResolvesTheHopAgain(_hopRound, spent))
        {
            _hopRound = -1;
            return false;
        }

        _hopRound++;
        _spentAtHopRound = _trim.SpentMetresPerSecond;
        if (Config.HopsFreeTheirArrival) Program.FreeTheHopArrival();
        else Program.CorrectCoastArc();
        _trim.Resume();
        _measureDue = false;
        _freshMiss = double.NaN;
        Log.Info($"walk on {KsaWorld.DisplayName(Craft)}: the hop's burn spent {spent:F1} m/s, so its arc is solved "
                 + $"again from where it left the bus (round {_hopRound} of at most {ReleaseLoop.MaxHopRounds})");
        return true;
    }

    // After cutoff nothing unpins the arrival, and each second it is out asks the trim ~2.35 m/s, so
    // past 4.3 s the trim refuses outright. Asked only of that already-lost state, once a flight.
    private void ReleaseAnArrivalTheTrimCannotFly(in TrimCommand trim, in IcbmState state)
    {
        if (_releasedTheArrival || !_trim.GaveUp) return;
        if (_trim.SpentMetresPerSecond > 0.0) return;
        if (!(trim.ToGainMetresPerSecond > BusTrim.MaxMetresPerSecond)) return;

        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed) || !double.IsFinite(flown)) return;

        _releasedTheArrival = true;

        string asked = $"the trim was asked for {trim.ToGainMetresPerSecond:F1} m/s against a "
                       + $"{BusTrim.MaxMetresPerSecond:F0} ceiling and had spent nothing, solving to an arrival "
                       + $"{committed:F0} s away where the flown prediction says {flown:F0}";

        if (Config.RefusedTrimTakesTheCheapestArrival)
        {
            if (!Program.TryCommitTheCheapestArrival(state))
            {
                Log.Info($"arrival kept on {KsaWorld.DisplayName(Craft)}: {asked}, and no cheaper arrival was found.");
                return;
            }

            _trim.Resume();
            Log.Info($"arrival moved on {KsaWorld.DisplayName(Craft)}: {asked}. Committed the cheapest arc from "
                     + $"here, {Program.CoastArrivalMovedSeconds:F2} s from the old arrival.");
            return;
        }

        if (!Program.ReleaseArrival()) return;

        _trim.Begin();

        // Nothing after cutoff re-solves an arc and Begin is a no-op on an armed trim, so this leaves the trim given up
        // on the arc it refused and opens the gate.
        Log.Info($"arrival released on {KsaWorld.DisplayName(Craft)}: {asked}. Giving the arrival up, which "
                 + "opens the release gate on the arc the trim refused.");
    }

    // BusTrim nulls `Kepler.TryCoast(reference, since).velocity - v`, so these four inputs decide the
    // debt at the split (docs/ACCURACY-PLAN.md 3bu).
    private void SayTheSplitDebt(in TrimCommand trim, double3 referenceVelocity, in IcbmState state,
                                 double3 nose, double3 right, double3 down)
    {
        double3 offPosition = state.PositionCci - Program.ReferencePositionCci;
        double3 offVelocity = state.VelocityCci - referenceVelocity;

        // In the frame the trim fires in: a scalar cannot tell a push along the joint from a lateral
        // term (docs/ACCURACY-PLAN.md 3fe).
        double3 owed = _trim.ToGainCci;

        Log.Info($"split debt on {KsaWorld.DisplayName(Craft)}: "
                 + $"owed {trim.ToGainMetresPerSecond:F3} m/s{BusJetsSaid()}, "
                 + $"{Program.SecondsSinceReference:F2} s since the reference, "
                 + $"{Vec.Len(offPosition) / 1000.0:F3} km from it, "
                 + $"{Vec.Len(offVelocity):F3} m/s off its velocity, "
                 + $"arc arrives {Program.Arc?.ArrivalAngleDeg ?? double.NaN:F1} deg, "
                 + $"owed along the bus {Vec.Dot(owed, Vec.Unit(nose)):+0.000;-0.000} nose, "
                 + $"{Vec.Dot(owed, Vec.Unit(right)):+0.000;-0.000} right, "
                 + $"{Vec.Dot(owed, Vec.Unit(down)):+0.000;-0.000} down");
    }

    private bool _releasedTheArrival;

    // Printed always, so the log has the distribution and not only the tail. The two need not
    // match: a vacuum transfer to the aim against a warhead with drag to the ground. At 12,902 km
    // each second apart asks the trim ~2.35 m/s.
    private string Arrivals()
    {
        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed)) return " [no committed arrival]";

        return double.IsFinite(flown)
            ? $" [solving to an arrival {committed:F0} s away; the flown prediction says {flown:F0} s]"
            : $" [solving to an arrival {committed:F0} s away; nothing predicted]";
    }

    // Said only when the debt grew while coasting clear, a different fault from the decoupler's.
    private string Grew()
    {
        double atRelease = _trim.AtReleaseMetresPerSecond;

        if (!double.IsFinite(atRelease) || !double.IsFinite(_owedAtSplit)) return "";
        if (Math.Abs(atRelease - _owedAtSplit) < 0.05) return "";

        return $" [owed {_owedAtSplit:F2} m/s at the split, {atRelease:F2} after "
               + $"{_sinceSplit:F0} s of clearing]";
    }

    // What the trim can resolve, carried to the ground: its stop band over what a metre of aim costs
    // it on this arc. Once a pass, like the holding cost, and NaN when the arc will not price, which
    // releases nothing on it. docs/ACCURACY-PLAN.md 3cu.
    private void MeasureTrimFloor(double simStep, in IcbmState state)
    {
        if (!Config.ReleaseInsideTheTrimFloor) { _trimFloor = double.NaN; return; }
        if (_postBoost.Cycles == _trimFloorForPass) return;

        _trimFloorForPass = _postBoost.Cycles;

        double band = BusTrim.StopBand(_trim.Acceleration, simStep,
                                       Config.PulseTrim ? Config.PulseSeconds : 0.0);

        _trimFloor = AimAuthority.TryRate(state.Body, state.PositionCci, _trueAimCci,
                                          Program.CommittedArrivalFromNow, out double perMetre)
                     && perMetre > 0.0
                         ? band / perMetre
                         : double.NaN;

        Log.Debug($"trim floor on {KsaWorld.DisplayName(Craft)}: "
                  + (double.IsFinite(_trimFloor)
                         ? $"{_trimFloor:F1} m ({band:F3} m/s x {1.0 / perMetre:F0} m per m/s)"
                         : "the arc would not price"));
    }

    // Once a pass: four impact predictions, for a property of the trajectory that moves over
    // minutes. A refusal leaves the previous measurement standing.
    private void MeasureHoldingCost()
    {
        if (!Config.DeriveHoldingCost) { _holdingCost = double.NaN; return; }
        if (Parent is not { } parent || _warhead is not { } warhead) return;
        if (_postBoost.Cycles == _holdingCostForPass) return;

        _holdingCostForPass = _postBoost.Cycles;

        try
        {
            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 positionCci = (KsaWorld.PositionEcl(Craft) - parent.GetPositionEcl()).Transform(cce2Cci);
            double3 velocityCci = (KsaWorld.VelocityEcl(Craft) - parent.GetVelocityEcl()).Transform(cce2Cci);

            if (HoldingCost.TryMeasure(Body, positionCci, velocityCci, ReleaseImpulseCci(),
                                       PredictStepSeconds, out double measured,
                                       new ImpactPredictor.Drag(_densityRatio ??= DensityRatioAt, warhead),
                                       stopOnTheSurface: Config.PredictionStopsOnTheSurface,
                                       stopOnTheTerrain: Config.PredictionStopsOnTheTerrain))
            {
                if (!double.IsFinite(_holdingCost) || Math.Abs(measured - _holdingCost) > 0.05)
                {
                    Log.Debug($"holding cost on {KsaWorld.DisplayName(Craft)}: {measured:F2} m/s "
                              + $"measured, against the {PostBoostAim.HoldingCostsMetresPerSecond:F0} "
                              + "the constant assumes");
                }

                _holdingCost = measured;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"holding cost could not be measured: {e.Message}");
        }
    }
}
