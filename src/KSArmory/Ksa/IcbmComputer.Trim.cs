using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // One line per change of state, which is all any of this is worth while nothing is happening
    // on screen. The detail rides along with it rather than driving it.
    //
    // Compared with its numbers collapsed, because the sentence carries them and a reading is not a
    // state: "trimming 3.71 m/s on the tail" against "trimming 3.70 m/s on the tail" is the same
    // thing happening. Comparing whole sentences wrote a line every frame -- 21,000 from one coast,
    // which is the log's own weight on the frame the coast step is measured in. What still separates
    // is the words, so a direction change, a stall or a hand-back all still say so.
    private void Say(string what, string detail = "")
    {
        string shape = WithoutNumbers(what);
        if (shape == _trimShape) return;

        // Two fields, because they are two different things: the shape is what decides whether this
        // is a new state, and the sentence is what anybody reads. Keeping only the shape puts
        // "trimming # m/s on the back" on the panel, which is the comparison key with its numbers
        // already thrown away.
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

    // Put the bus back on its solution with its own thrusters, before anything leaves it. All the
    // deciding is in BusTrim; what is here is the same two conversions as everywhere else in this
    // class - the world into a situation and the answer into writes on somebody else's vehicle -
    // plus the one thing only this side can know, which is whether the split has actually landed.
    private void DriveTrim(double simStep, in IcbmState state, IManualFire? weapon)
    {
        // Nothing left to put on a solution. ReadyToDeploy stays true after the last warhead goes,
        // so without this the trim goes on solving and firing at an empty bus: measured at 12,902 km
        // as 8.24 m/s nulled and 19.26 more asked for after `0 left`, a fifth of the whole budget
        // spent on nobody -- and spent manoeuvring six metres from the spent stack, which is the
        // manoeuvre the clearance had just refused on safety grounds.
        // A vacuum trim has nothing to null onto in the air, and the warheads of a shot that releases
        // at cutoff are already away by the time one could settle.
        if (!Config.TrimBeforeRelease || !Command.ReadyToDeploy || SalvoFinished || Program.ReleasesAtCutoff)
        {
            if (_trim.Firing != TrimAxes.None)
            {
                VehicleCommand.DriveTranslation(Craft, TrimAxes.None);
                AttitudeHook.PulseMode(Craft, pulsing: false);
            }
            return;
        }

        // The error this exists to remove arrives *with* the split, and the split is deferred
        // through the engine's input buffer. Asking the joint again is the split itself rather than
        // a timer: the decoupler that was there is the one that just came apart, so the question
        // stops answering yes the moment it has. A launcher that never had one is past this
        // already, because SeparateOnce never set the flag.
        if (_awaitingSplit)
        {
            if (weapon is { CanSeparate: true }) return;
            _awaitingSplit = false;

            // Not `??=`. Rehome captures the vehicle the computer came *from*, and a decoupler
            // disposes the pre-split vehicle to make two new ones -- so that capture is a corpse,
            // IsAlive says so, and the clearance test reads no distance at all. Prefer whichever
            // half is actually alive.
            // Read before WhatWasDropped, which clears the census it counts.
            int before = _wasBeforeSplit.Count;

            if (!KsaWorld.IsAlive(_separatedFrom)) _separatedFrom = WhatWasDropped();

            // Said once, because two guesses at why the distance reads as unknown have both been
            // wrong and the next step is a measurement rather than a third. Everything the
            // clearance test depends on, at the one instant it is decided.
            _afterSplit.Clear();
            KsaWorld.CollectVehicles(_afterSplit);

            Log.Info($"split on {KsaWorld.DisplayName(Craft)}: "
                     + $"{(_separatedFrom is null ? "no stack captured" : KsaWorld.DisplayName(_separatedFrom))}, "
                     + $"alive {KsaWorld.IsAlive(_separatedFrom)}, "
                     + $"{before} vehicles before and {_afterSplit.Count} after");
        }

        // Armed at the split rather than at clearance, and held rather than skipped. It keeps
        // solving through the whole wait, so what the bus owes its solution is on record from the
        // moment the decoupler fired — which is the only thing that separates an error the
        // separation caused from one that grew while the vehicle coasted clear of it.
        Clearance clearance = _didSplit ? Clear(simStep) : new Clearance(true, false, "");

        // Given up on rather than waited out: the stack is readable and still too close, so there
        // is no manoeuvre to make here that does not fly into it. Release proceeds untrimmed.
        //
        // It also caps the correction loop, which is not what it is for and is load-bearing anyway:
        // the requirement balloons between passes -- 0.02 m/s trimmed, 12.63 asked next -- and this
        // is what stops the bus flying that number. Handing the safety question to the keep-out
        // interlock lifts the cap and flies it: 5.39 km median against 3.04. `docs/MIRV-NEXT.md`
        // item 8h.
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearance.IsClear, clearance.Abandoned, _postBoost.Cycles,
            Config.TrimBudgetMetresPerSecond, _trim.SpentMetresPerSecond,
            Config.KeepOutCoversTheClearance);

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

        // Bounded across the flight, not just per run. Each release re-arms the trim, and with the
        // warheads held until the arrival is close there is a long coast for it to keep finding
        // small corrections in -- which spends the tanks before the corrections that matter.
        //
        // Handed to the trim rather than enforced here, and for two reasons. The figure it keeps is
        // cumulative across every null, so a second total kept out here counts each finished run
        // again for every run that follows. And the stop has to *end* the trim: expressed as
        // withholding fire it never lifts, because only firing spends the tank -- and the warheads
        // do not leave until the trim is done.
        double budget = Config.TrimBudgetMetresPerSecond;

        if (!_saidBudget && !_trim.WithinBudget(budget))
        {
            _saidBudget = true;
            Log.Info($"trim: budget of {budget:F0} m/s spent; the warheads go on the aim as it is");
        }

        // Said here rather than left to the trim's own line, which is the only other thing that
        // reads this sentence and drops it: Say prints `trim.Said` alone once _mayTrim is true, so
        // the success branch's text is produced on exactly the frame it can no longer be logged on,
        // and its absence reads as the gate never opening -- `docs/MIRV-NEXT.md` item 8w. A gate
        // whose only observable is its failures is not an instrument.
        if (!_saidCleared && clearance.IsClear && _didSplit && clearance.Said.Length > 0)
        {
            _saidCleared = true;
            Log.Info($"clearance on {KsaWorld.DisplayName(Craft)}: {clearance.Said}");
        }

        _mayTrim = plan.MayTrim;

        _trim.Begin();

        double3 nose = Vec.Zero;
        double3 right = Vec.Zero;
        double3 down = Vec.Zero;

        // A frame that will not resolve is handed in as zeroes rather than skipped, because the
        // trim's own budget is what has to bound a part tree that never comes back - and it can
        // only run that budget down if it is being stepped.
        if (Parent is { } parent)
        {
            KsaWorld.TryControlFrameCci(Craft, parent, out nose, out right, out down);
        }

        // The trajectory the guidance flew to, which is the pair the arc and the cutoff position
        // make, and how far along it the vehicle should be by now.
        double3 referenceVelocity = Program.Arc?.RequiredVelocityCci ?? Vec.Zero;

        TrimCommand trim = _trim.Update(simStep, new TrimSituation(
            state.Body, state.PositionCci, state.VelocityCci,
            Program.ReferencePositionCci, referenceVelocity, Program.SecondsSinceReference,
            nose, right, down, _mayTrim, budget, _keepOutTowardCci,
            plan.CeilingMetresPerSecond,
            Config.PulseTrim ? Config.PulseSeconds : 0.0,
            Config.StallFallsBackToHolding,
            Config.TrimCountsTheCommandInFlight));

        // The mode goes through the attitude window for the same reason the aim does: applying a
        // worker's results copies the whole flight computer over anything written outside it. The
        // directions are commanded the same way either way.
        AttitudeHook.PulseMode(Craft, trim.Pulse);
        AttitudeHook.KeepJetsThroughTheClear(Craft, Config.JetsThroughTheKeyboardClear);
        VehicleCommand.DriveTranslation(Craft, trim.Fire);

        // The post-boost passes. With the thrusters quiet and the nose steady the correction gets a
        // clean look at where the bus is actually going; moving the aim and re-solving the arc from
        // here gives the trim something new to null onto, and the trim is the only thing left
        // aboard that can still move the impact. A trim that has given up ends it, because further
        // passes have no actuator.
        //
        // The same call ReleaseImpulseCci() feeds the prediction, so what the sequencer watches for
        // steadiness is the term that actually moves the reading rather than a proxy for it.
        ReleaseAnArrivalTheTrimCannotFly(trim);

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
            HoldingCostMetresPerSecond: double.IsFinite(_holdingCost)
                                            ? _holdingCost
                                            : Config.HoldingCostMetresPerSecond,
            DecideOnTheReading: Config.DecideOnTheReading,
            ReleaseInsideTheTrimFloor: Config.ReleaseInsideTheTrimFloor,
            TrimFloorMetres: _trimFloor));

        if (pass.MayMeasure) _measureDue = true;

        if (_postBoost.Cycles > passesBefore)
        {
            // Consumed, so the next decision waits for a reading taken after this correction has
            // actually been flown rather than re-reading the one that prompted it.
            _freshMiss = double.NaN;
            Program.CorrectCoastArc();
            _trim.Resume();
            Say($"post-boost: {pass.Said}", "");
        }

        // The reason it stopped, which Say above cannot report: that fires on a cycle being taken,
        // and finishing is precisely the decision that takes none. It is the line that says how
        // much of the miss was still on the table and why it was left there -- the largest term in
        // where the warheads land, and written down nowhere else.
        if (pass.MayRelease && !_postBoostSaid)
        {
            _postBoostSaid = true;

            // Named, because this is the line that says which rule ended the correction and that
            // is the largest single term in where the warheads land -- a loop that finished landed
            // at 140 m and every other ending at 5 to 45 km. Unattributed it can only be read once
            // per shot and then spread across every rocket in the world, which reports eight
            // flights of one craft's outcome: `docs/MIRV-NEXT.md` 8z's n=40 was six shots.
            //
            // Log.Info rather than Say: Say dedupes on the sentence's shape and drives the panel's
            // trim line, and this is neither a trim state nor one that repeats.
            Log.Info($"post-boost on {KsaWorld.DisplayName(Craft)}: {pass.Said}");

            // Stops the loop and reverts its bias to the best it scored, which moves no warhead: they
            // leave in this same frame on the trajectory the last trim pass flew, so "shipping" below
            // is the loop's bookkeeping, not the aim flown. Over 536 flights the size of the revert
            // predicts the release probe at +0.10. docs/ACCURACY-PLAN.md 3cp.
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

        // Deliberately NOT dropped when the trim reports done. A post-boost pass calls
        // _trim.Resume(), so passes keep arriving afterwards -- and those are the large ones. With
        // the reference gone they read "waiting to clear the spent stack, which cannot be read" and
        // fall through to SeparationClearance's 20 s clock, so the dangerous passes would be exactly
        // the ones flying blind. This caches no ANSWER; it keeps the QUESTION askable.

        // Said once per change. A trim that stalls looks exactly like one that has finished, and
        // the difference between them is kilometres on the ground.
        if (trim.Said.Length == 0) return;

        // Two audiences, one state. The panel gets a sentence it can fit; the log gets the numbers
        // that diagnose it, which are long enough to run off the edge of a narrow window.
        Say(_mayTrim ? trim.Said : clearance.Said + "; " + trim.Said,
            (trim.Acceleration > 0.0 ? $" (thrusters measured at {trim.Acceleration:F3} m/s2)" : "")
            + Grew()
            + Arrivals());
    }

    // The one state the latch cannot get itself out of. The arrival is pinned during the burn and
    // both branches that unpin it live there, so after cutoff it stands whatever the trajectory
    // does -- and what the trim is asked for is RequiredVelocity(arrival) - v, worth about 2.35 m/s
    // per second the arrival is out. BusTrim's ceiling is crossed at 4.3 s, and past it the trim
    // refuses before its first pulse and the warheads go out untrimmed: flown once in twelve shots,
    // all eight rockets 75-99 km out on burns nothing was wrong with.
    //
    // Asked only of a state that is ALREADY LOST -- the trim over its ceiling, having spent nothing
    // -- rather than on a threshold of its own. A guard that fires on a number has to be right about
    // the number; this one fires where the alternative is a certain 90 km, so being wrong about it
    // costs a re-solve.
    //
    // Once. A second release would be the cycle the latch exists to prevent, and IcbmProgram
    // re-latches nothing after cutoff, so this cannot become a loop.
    private void ReleaseAnArrivalTheTrimCannotFly(in TrimCommand trim)
    {
        if (_releasedTheArrival || !_trim.GaveUp) return;
        if (_trim.SpentMetresPerSecond > 0.0) return;
        if (!(trim.ToGainMetresPerSecond > BusTrim.MaxMetresPerSecond)) return;

        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed) || !double.IsFinite(flown)) return;

        _releasedTheArrival = true;

        if (!Program.ReleaseArrival()) return;

        _trim.Begin();

        Log.Info($"arrival released on {KsaWorld.DisplayName(Craft)}: the trim was asked for "
                 + $"{trim.ToGainMetresPerSecond:F1} m/s against a {BusTrim.MaxMetresPerSecond:F0} "
                 + $"ceiling and had spent nothing, solving to an arrival {committed:F0} s away "
                 + $"where the flown prediction says {flown:F0}. Giving the arrival up and taking "
                 + "the cheapest arc again.");
    }

    // The trim's debt at the split, decomposed into the four things that make it. `ACCURACY-PLAN.md`
    // 5h: the debt is 6.2x larger at a 54 degree arrival than at 44, and eight candidates have been
    // measured headlessly without finding it -- the arc amplifying a kick (flat), the clearance wait
    // (wrong direction), the cutoff residual (1.6x), the lighter bus (1.7x), the reference position
    // and arrival-time sensitivities (both FALL with steepness), the arrival-time error itself
    // (measured at 0 s) and the cutoff prediction (1 m at every angle).
    //
    // What that rules out is the geometry. What it leaves is a term the headless rig has no way to
    // produce, because it has no decoupler event and does not run the trim at all -- so the next
    // reading has to come from a flight, and this is it.
    //
    // BusTrim nulls `Kepler.TryCoast(reference, since).velocity - v`, so exactly four inputs decide
    // it. Printing all four beside the answer turns "the demand is large" into "this term is large",
    // which is the difference between another night of candidates and one reading.
    private void SayTheSplitDebt(in TrimCommand trim, double3 referenceVelocity, in IcbmState state,
                                 double3 nose, double3 right, double3 down)
    {
        double3 offPosition = state.PositionCci - Program.ReferencePositionCci;
        double3 offVelocity = state.VelocityCci - referenceVelocity;

        // Which way the debt points, in the frame the trim fires in. The scalar cannot tell a
        // decoupler pushing along the joint from a lateral term, and one rocket in eight draws three
        // to five times what the rest do with nothing -- seat, world or split order -- explaining it.
        // docs/ACCURACY-PLAN.md 3fe.
        double3 owed = _trim.ToGainCci;

        Log.Info($"split debt on {KsaWorld.DisplayName(Craft)}: "
                 + $"owed {trim.ToGainMetresPerSecond:F3} m/s, "
                 + $"{Program.SecondsSinceReference:F2} s since the reference, "
                 + $"{Vec.Len(offPosition) / 1000.0:F3} km from it, "
                 + $"{Vec.Len(offVelocity):F3} m/s off its velocity, "
                 + $"arc arrives {Program.Arc?.ArrivalAngleDeg ?? double.NaN:F1} deg, "
                 + $"owed along the bus {Vec.Dot(owed, Vec.Unit(nose)):+0.000;-0.000} nose, "
                 + $"{Vec.Dot(owed, Vec.Unit(right)):+0.000;-0.000} right, "
                 + $"{Vec.Dot(owed, Vec.Unit(down)):+0.000;-0.000} down");
    }

    private bool _releasedTheArrival;

    // Which arrival the trim is solving to, beside when the flown prediction says the warheads
    // actually get there. What the trim nulls is RequiredVelocity(arrival) - v, and that required
    // velocity moves about 2.35 m/s for every second the arrival is out at 12,902 km -- so
    // BusTrim.MaxMetresPerSecond is crossed at 4.3 s of disagreement, and a trim asking for tens is
    // a handful of seconds long before it is anything wrong with the vehicle.
    //
    // Printed unconditionally, and that is the point. Printed only once the demand is over the
    // ceiling, it reports the disagreement's tail and never its distribution -- the logger
    // describing its own trigger rather than the fault.
    //
    // The two are not the same quantity and need not match: the arrival is when a vacuum transfer
    // reaches the aim point, the prediction is when a warhead with drag reaches the ground. Their
    // gap is the measurement, not an error on its face.
    private string Arrivals()
    {
        double committed = Program.CommittedArrivalFromNow;
        double flown = PredictedImpact?.Seconds ?? double.NaN;

        if (!double.IsFinite(committed)) return " [no committed arrival]";

        return double.IsFinite(flown)
            ? $" [solving to an arrival {committed:F0} s away; the flown prediction says {flown:F0} s]"
            : $" [solving to an arrival {committed:F0} s away; nothing predicted]";
    }

    // What coasting clear cost, said only when it cost something. The same number at the split and
    // at the release means the wait was free and the error came off the decoupler; a number that
    // has grown means something moved the vehicle or the aim while it waited, which is a different
    // fault with a different fix.
    private string Grew()
    {
        double atRelease = _trim.AtReleaseMetresPerSecond;

        if (!double.IsFinite(atRelease) || !double.IsFinite(_owedAtSplit)) return "";
        if (Math.Abs(atRelease - _owedAtSplit) < 0.05) return "";

        return $" [owed {_owedAtSplit:F2} m/s at the split, {atRelease:F2} after "
               + $"{_sinceSplit:F0} s of clearing]";
    }

    // The fallback when the tubes cannot be resolved: the munition's ejection speed along the
    // direction the vehicle was told to hold. Wrong by however far the vehicle settled off that
    // command, which is why it is second choice rather than the rule.
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

    // Once a pass rather than once a solve: it is four impact predictions, the solve runs several
    // times a second, and the answer is a property of the trajectory, which moves over minutes.
    // A refusal leaves the previous measurement standing.
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
