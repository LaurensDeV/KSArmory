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
        // Two clocks: guidance keeps step with the world on simulated time; a readout on simulated
        // time would re-fly a trajectory every frame once the coast is warped.
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

        // Mid-burn, from where the arc departs: the live state is a trajectory nobody will fly, and a
        // correction driven by it never sees the shot being aimed.
        bool fromCutoff = Program.IsBurning && Program.Arc is not null;

        double3 fromCci = fromCutoff ? Program.CutoffPositionCci : state.PositionCci;
        double3 alongCci = fromCutoff ? Program.Arc!.Value.RequiredVelocityCci : state.VelocityCci;

        double departsIn = fromCutoff && double.IsFinite(Command.SecondsToCutoff)
                         ? Math.Max(0.0, Command.SecondsToCutoff)
                         : 0.0;

        // Read before the coast to the release: carried there, the pad's arc leaves the air and passes the gate.
        bool departureIsWorthObserving = AimCorrection.DepartureIsWorthObserving(DensityRatioAt(fromCci));

        double coasted = 0.0;
        if (Config.KickAtTheRelease)
        {
            // The arrival the release gate counts against, never the last prediction's: that is a cycle old, and
            // through the burn's swings in the aim it lagged by enough to settle the loop and latch mid-swing.
            double committed = Program.CommittedArrivalFromNow;
            double flight = double.IsFinite(committed) ? committed - departsIn
                          : fromCutoff ? Program.Arc!.Value.FlightSeconds
                          : double.NaN;

            fromCci = ReleaseCoast.ToRelease(Body, fromCci, alongCci, flight, Program.ReleaseGate,
                                             DensityRatioAt, out alongCci, out coasted);
        }

        // Held in a field because the terrain callback runs inside the prediction and needs it too.
        _departsIn = departsIn;
        _coastedToRelease = coasted;

        fromCci += ReleaseOffsetCci();
        alongCci += PredictedReleaseImpulseCci(Body, fromCci, alongCci);

        // The velocity the prediction is a function of, kept because positions move by v*dt whatever
        // is wrong (docs/ACCURACY-PLAN.md 3as).
        _lastPredictedFromVelCci = alongCci;

        // With the warhead's drag: on a shallow deorbit a vacuum arc lands beyond anything that flies
        // it, so the correction converged on zero and the rounds fell 54.6 km short.
        ImpactPredictor.Drag? air =
            _warhead is { } warhead ? new ImpactPredictor.Drag(DensityRatioAt, warhead) : null;

        bool landed = ImpactPredictor.TryPredict(Body, fromCci, alongCci, PredictStepSeconds,
                                                 ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                                 TerrainRadiusAt, _path, air,
                                                 stopOnTheSurface: Config.PredictionStopsOnTheSurface,
                                                 stopOnTheTerrain: Config.PredictionStopsOnTheTerrain);

        // The release probe asks the same terrain callback, from a state that is already at the release.
        _coastedToRelease = 0.0;

        if (landed)
        {
            // The impact is in the body-fixed frame of the departure, and the target of now, so it is
            // carried back over the rest of the burn: left out, a shot needing no correction at 2,000 km
            // was put 191.6 km wrong, headless, and one at 3,459 km 37.1 km against 0.4.
            double carry = departsIn + coasted;
            hit = hit with
            {
                GroundFixedPointCci = carry > 0.0
                                    ? Body.CarryCci(hit.GroundFixedPointCci, -carry)
                                    : hit.GroundFixedPointCci,
                Seconds = hit.Seconds + coasted,
            };

            PredictedImpact = hit;
            PredictsFromCutoff = fromCutoff;

            // Restarted, not aged: Update runs it down on the simulated step, so it keeps counting
            // after predicting stops.
            if (!_salvoAway) _arrivalLeft = hit.Seconds;

            // Against the target, not the biased aim, which would score the correction against itself.
            PredictedMissMetres = state.HasAim
                ? Body.SurfaceRadius * Vec.AngleBetween(hit.GroundFixedPointCci, _trueAimCci)
                : double.NaN;

            Log.Debug($"aim on {KsaWorld.DisplayName(Craft)}: bias {Distance.Say(AimBiasMetres)}, predicted miss "
                      + $"{Distance.Say(PredictedMissMetres)}, from "
                      + $"{(fromCutoff ? "the solved cutoff" : "the live state")}, "
                      + $"kick {Vec.Len(ReleaseImpulseCci()):F2} m/s");

            // Not while the trim fires: the bias absorbs its displacement, and a shot 0.1 km off wound up
            // 139 m/s of trim. Not from inside the air, where the departure is the pad. After cutoff only
            // once the trim is Done, which an interlock re-closing cannot fake (ACCURACY-PLAN.md 3cl).
            if (Config.CorrectAim && state.HasAim && !TrimIsFiring
                && departureIsWorthObserving
                && (Program.IsBurning || (_measureDue && _trim.Done))
                && !AimWaitsForTheSolids(state)
                && !(Program.IsBurning && Program.AimSitsOutTheBurn))
            {
                double biasWas = Vec.Len(_aim.BiasCci);

                // How far the departure state moved since the last reading: an impact move that matches
                // it is carrying the vehicle's motion, a frame-and-epoch fault.
                double departureVel = Vec.IsFinite(_lastObservedVelCci)
                                          ? Vec.Len(_lastPredictedFromVelCci - _lastObservedVelCci)
                                          : double.NaN;
                _lastObservedVelCci = _lastPredictedFromVelCci;

                double sinceLast = _sinceObserve;
                _sinceObserve = 0.0;

                _aim.Observe(hit.GroundFixedPointCci, _trueAimCci);

                // A demand growing pass over pass and a response stuck at the clamp's floor look, without
                // this, like a shot that simply wants a large correction.
                Log.Debug($"aim loop on {KsaWorld.DisplayName(Craft)}: "
                          + $"{PredictedMissMetres / 1000.0:F2} km out, best "
                          + $"{_aim.BestMissMetres / 1000.0:F2}, response {_aim.Response:F2}, "
                          + $"bias {Distance.Say(biasWas)} -> {Distance.Say(Vec.Len(_aim.BiasCci))}, "
                          + $"worse for {_aim.WorseFor}, "
                          + $"{_aim.PlantMeasurements} plant reading(s), raw {_aim.LastRawResponse:F2}"
                          // What the trim had delivered: a low plant from a converged trim is the
                          // trajectory, from an unfinished one the actuator, and they want opposite fixes.
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
