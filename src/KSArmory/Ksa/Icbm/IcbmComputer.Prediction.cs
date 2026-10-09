using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    // The departure velocity the last prediction was flown from, and the one the aim was last read
    // against, so the impact's wander can be priced against the only thing that can cause it.
    private double3 _lastPredictedFromVelCci = new(double.NaN, double.NaN, double.NaN);
    private double3 _lastObservedVelCci = new(double.NaN, double.NaN, double.NaN);

    private double _sincePredict = double.PositiveInfinity;
    private double _sincePredictWall = double.PositiveInfinity;

    // Where the mod thinks the arc lands, as a place rather than a distance. A distance says the
    // solution is wrong; the place says which way, and short-versus-sideways are different faults.
    private string PredictedImpactSaid()
    {
        if (!double.IsFinite(PredictedMissMetres) || PredictedImpact is not { } hit) return "";
        if (Parent is not { } parent) return "";

        try
        {
            // The prediction is un-carried to its own epoch, so it is a place on the ground in the
            // same terms the aim point is - which is what makes the two comparable at all.
            double3 cce = hit.GroundFixedPointCci.Transform(parent.GetCci2Cce());

            return $", own prediction {Distance.Say(PredictedMissMetres)} off "
                   + $"(lands {parent.GetLatitudeFromCce(cce):F3},{parent.GetLongitudeFromCce(cce):F3})";
        }
        catch
        {
            return $", own prediction {Distance.Say(PredictedMissMetres)} off";
        }
    }

    private void Predict(double simStep, in IcbmState state)
    {
        // Two clocks, because this reading has two consumers with different needs. Guidance reads
        // it while the engines are lit and when a post-boost pass asks, and there it has to keep
        // step with the world. Everything else is a readout, and a readout paced by simulated time
        // costs a whole re-flown trajectory per frame once the coast is warped.
        _sincePredict += simStep;
        _sinceObserve += simStep;
        _sincePredictWall += state.PlayerStepSeconds;

        bool guidanceWants = Program.IsBurning || _measureDue;

        if (guidanceWants
                ? _sincePredict < PredictIntervalSeconds
                : _sincePredictWall < ReadoutIntervalSeconds)
        {
            return;
        }

        _sincePredict = 0.0;
        _sincePredictWall = 0.0;

        // While the engines are running, predict from where the arc *departs* rather than from
        // where the vehicle is. The current state is mid-burn and describes a trajectory nobody
        // intends to fly, so a correction driven by it never sees the shot being aimed - which
        // leaves the aim uncorrected for the whole burn, and by the coast the arc is fixed and the
        // warheads are already going.
        bool fromCutoff = Program.IsBurning && Program.Arc is not null;

        double3 fromCci = fromCutoff ? Program.CutoffPositionCci : state.PositionCci;
        double3 alongCci = fromCutoff ? Program.Arc!.Value.RequiredVelocityCci : state.VelocityCci;

        // How far in the future the predicted arc departs. Zero once the engines are off, and the
        // rest of the burn while they are running.
        double departsIn = fromCutoff && double.IsFinite(Command.SecondsToCutoff)
                         ? Math.Max(0.0, Command.SecondsToCutoff)
                         : 0.0;

        // Held in a field because the terrain callback runs inside the prediction and needs it too.
        _departsIn = departsIn;

        fromCci += ReleaseOffsetCci();
        alongCci += ReleaseImpulseCci();

        // What the predictor is actually a function of. On a coast it is an exact function of this
        // pair, so any wander in its answer is a wander in here -- 0.4 to 2.9 km per m/s along track
        // on this arc, and far more across it. Differencing positions cannot see that: on a coast
        // they move by v*dt whatever is wrong, so a position probe can only ever report the bus's
        // speed. docs/ACCURACY-PLAN.md 3as.
        _lastPredictedFromVelCci = alongCci;

        // Predicted with the warhead's drag rather than in vacuum. On a shallow deorbit arrival a
        // vacuum arc lands tens of kilometres beyond anything that actually flies it, and the aim
        // correction reads its own drag-free prediction - so it converges, reports zero, and the
        // rounds go on falling short. Measured at 54.6 km.
        ImpactPredictor.Drag? air =
            _warhead is { } warhead ? new ImpactPredictor.Drag(DensityRatioAt, warhead) : null;

        if (ImpactPredictor.TryPredict(Body, fromCci, alongCci, PredictStepSeconds,
                                       ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                       TerrainRadiusAt, _path, air,
                                       stopOnTheSurface: Config.PredictionStopsOnTheSurface,
                                       stopOnTheTerrain: Config.PredictionStopsOnTheTerrain))
        {
            // The predictor un-carries its impact by its own flight time, which puts the ground
            // point in the body-fixed frame of the instant the arc *departs*. Mid-burn that instant
            // is the cutoff, seconds away, while the target is known in the frame of now - so
            // comparing them measures the planet's turn over the rest of the burn and calls it miss.
            //
            // It is not a small term and it is not a bias: it shrinks to nothing as cutoff arrives,
            // so the correction chases a ruler moving at ~400 m/s against a target moving at 465.
            // Flown headless at 2,000 km, a shot needing no correction at all was put 191.6 km wrong
            // by nulling it; at 3,459 km, 37.1 km against 0.4 km once both are in one epoch.
            hit = hit with
            {
                GroundFixedPointCci = departsIn > 0.0
                                    ? Body.CarryCci(hit.GroundFixedPointCci, -departsIn)
                                    : hit.GroundFixedPointCci,
            };

            PredictedImpact = hit;
            PredictsFromCutoff = fromCutoff;

            // Restarted, not aged. Ageing it by the interval since the last prediction freezes the
            // readout the moment predicting stops, leaving a timer holding at twenty or thirty
            // seconds while the warheads land. This is run down by the simulated step in
            // Update instead, so it keeps counting for as long as the world does.
            if (!_salvoAway) _arrivalLeft = hit.Seconds;

            // Measured against the *target*, not against the biased aim: the bias is the correction
            // being applied, so scoring it against itself would report a perfect shot however far
            // the rounds actually land from the place the player picked.
            PredictedMissMetres = state.HasAim
                ? Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci)
                : double.NaN;

            // Not while the trim is firing, and this is the whole reason the two can coexist. The
            // correction's only observer is this prediction, and the trim is actively moving the
            // vehicle it is taken from - so the bias absorbs a displacement the trim then reads as
            // a larger error and burns harder at. Same shape as the release sequence's latched
            // reference, and it runs away rather than merely drifting: flown, a shot 0.1 km off at
            // cutoff wound up by a factor of ten every ten cycles to 139 m/s of commanded trim.
            // What the correction is being told and what it has done about it, per cycle. A bias
            // that ends at its limit says nothing about how it got there - walked, jumped, or
            // pushed back and forth - and those want different fixes.
            //
            // Named, for the reason why_it_ended and the cutoff line are: eight rockets write this
            // into one log, so unattributed it is a set of readings rather than eight traces and no
            // bias can be paired with the miss its own rocket landed at.
            Log.Debug($"aim on {KsaWorld.DisplayName(Craft)}: bias {Distance.Say(AimBiasMetres)}, predicted miss "
                      + $"{Distance.Say(PredictedMissMetres)}, from "
                      + $"{(fromCutoff ? "the solved cutoff" : "the live state")}, "
                      + $"kick {Vec.Len(ReleaseImpulseCci()):F2} m/s");

            // During the burn every cycle is a measurement. After it they are rationed: the
            // correction's only observer is this prediction, so one taken between the aim moving
            // and the trim having flown the new arc reads its own unspent correction as error and
            // steps again on top of it.
            // And not from inside the air. Mid-burn the prediction departs from the projected
            // cutoff, which before the vehicle has flown is the pad -- so the arc is flown with drag
            // from sea level and lands thousands of kilometres short of a target nothing is wrong
            // with. AimCorrection.DepartureIsWorthObserving has the flown numbers.
            // After cutoff the reading has to come off a correction the bus has FLOWN, which is
            // `_trim.Done` and not `!TrimIsFiring`. The two differ only through `_mayTrim`, and
            // `_mayTrim` goes false whenever the separation interlock re-closes -- which it does on
            // 45 flights of 160, by centimetres, because the trim's first job is to null the very
            // shove that is carrying the halves apart. So the thrusters fall quiet with the
            // correction still in transit, this gate opens, and the impact is sampled mid-flight.
            //
            // What it costs is not the one reading. The next reading's secant attributes the whole
            // overshoot to the aim move and freezes the plant estimate at 2.59-2.76 where the true
            // plant is 1.04-1.11, so the loop converges three times too slowly and the flat 250 m
            // band stops it two readings early. Across 2026-09-10-release that is exactly the six
            // flights whose response exceeded 2.55, and exactly the six that released 263-481 m off
            // against a 2-24 m norm -- perfect separation, both ways. ACCURACY-PLAN.md 3cl.
            //
            // PostBoostAim is handed `TrimSettled: _trim.Done` and refuses to judge a pass on an
            // unflown correction. This is the same question asked one call earlier.
            if (Config.CorrectAim && state.HasAim && !TrimIsFiring
                && AimCorrection.DepartureIsWorthObserving(DensityRatioAt(fromCci))
                && (Program.IsBurning || (_measureDue && _trim.Done))
                && !AimWaitsForTheSolids(state)
                && !(Program.IsBurning && Program.AimSitsOutTheBurn))
            {
                double biasWas = Vec.Len(_aim.BiasCci);

                // How far the state the prediction DEPARTS FROM travelled since the last reading. The impact moves 3,520 m
                // a cycle against an aim move of 78, and 3,520 m over the 0.5 s prediction interval
                // is 7.0 km/s -- the bus's own speed. If the two match, the impact being differenced
                // is carrying the vehicle's motion rather than reporting where it will land, which
                // is the frame-and-epoch fault docs/FRAMES-AND-EPOCHS.md exists for.
                double departureVel = Vec.IsFinite(_lastObservedVelCci)
                                          ? Vec.Len(_lastPredictedFromVelCci - _lastObservedVelCci)
                                          : double.NaN;
                _lastObservedVelCci = _lastPredictedFromVelCci;

                double sinceLast = _sinceObserve;
                _sinceObserve = 0.0;

                _aim.Observe(hit.GroundFixedPointCci, _trueAimCci);

                // The loop's own state, which nothing else reports. A demand that grows pass over
                // pass and a step sized by a response stuck at the clamp's floor are the same
                // reading, and without this they are indistinguishable from a shot that simply
                // wants a large correction.
                Log.Debug($"aim loop on {KsaWorld.DisplayName(Craft)}: "
                          + $"{PredictedMissMetres / 1000.0:F2} km out, best "
                          + $"{_aim.BestMissMetres / 1000.0:F2}, response {_aim.Response:F2}, "
                          + $"bias {Distance.Say(biasWas)} -> {Distance.Say(Vec.Len(_aim.BiasCci))}, "
                          + $"worse for {_aim.WorseFor}, "
                          + $"{_aim.PlantMeasurements} plant reading(s), raw {_aim.LastRawResponse:F2}"
                          // What the trim had actually delivered when the impact was read. A plant
                          // of 0.14 means the impact followed a seventh of the aim move, and the two
                          // causes want opposite fixes: a trim that converged and an impact that
                          // still moved a seventh is the trajectory, and the step is too small; a
                          // trim that flew a seventh of what it was asked is the actuator, and a
                          // larger step makes it worse.
                          + $" | departure vel {departureVel:F4} m/s over {sinceLast:F2} s"
                          + $" | aim moved {_aim.LastAimMoveMetres:F0} m, impact moved "
                          + $"{_aim.LastImpactMoveMetres:F0} m of which "
                          + $"{_aim.LastImpactAlongAimMetres:F0} along it"
                          + $" | trim owes {_trim.ToGainMetresPerSecond:F2} of "
                          + $"{_trim.SpentMetresPerSecond:F2} spent"
                          + (_trim.GaveUp ? ", GAVE UP" : _trim.Done ? ", done" : ", running"));

                if (!Program.IsBurning)
                {
                    _measureDue = false;
                    _freshMiss = PredictedMissMetres;
                }
            }
        }

        else
        {
            PredictedImpact = null;
            PredictedMissMetres = double.NaN;
        }
    }
}
