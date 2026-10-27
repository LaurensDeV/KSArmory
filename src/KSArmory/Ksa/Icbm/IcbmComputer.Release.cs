using Brutal.Numerics;
using KSA;

namespace KSArmory;

internal sealed partial class IcbmComputer
{
    private readonly double3[] _tubeAxes = new double3[64];
    private double3 _releaseOffsetCci;
    private double3 _releaseKickCci;
    private double _tubeSpinSpeed;

    /// <summary>
    /// Which of <see cref="Targets"/> a released warhead was sent to, or <c>-1</c> for a round this
    /// computer did not release. Recorded at release, because the lead moves on at each handover.
    /// </summary>
    public int TargetOfRound(IProjectile round)
    {
        for (int i = 0; i < _sentTo.Count; i++)
        {
            if (ReferenceEquals(_sentTo[i].Round, round)) return _sentTo[i].Target;
        }

        return -1;
    }

    /// <summary>Let one warhead go at the aim point, if there is one to let go and it is ready.</summary>
    public bool Release(IManualFire? weapon)
    {
        if (weapon is null || !weapon.ReadyToFire) return false;
        if (TargetEcl() is not { } targetEcl) return false;

        // Read before the warhead is counted away: the cursor moves at the handover, which is the
        // same frame the last of a stop's quota leaves.
        int sentTo = _walker.TargetIndex(_targets.LeadIndex);

        bool away = weapon.FireAt(targetEcl);

        if (away)
        {
            // On the first one away: the magazine reloads a few seconds after the salvo, and read
            // then it says the salvo never finished.
            if (WarheadsAway == 0)
            {
                _salvoSize = 1 + weapon.TubesReadyToFire;
                _salvoProbe.Forget();
                SayWhatTheLoopLeft();
                SayWhatTheGroundUnderTheAimIsLike();
            }

            WarheadsAway++;
            _walker.WarheadAway();

            // On the round rather than the munition: the profile is one instance shared by every
            // rocket in the world, and a paired night needs each rocket to fly its own setting.
            IProjectile? sent = weapon is IRoundsInFlight { Rounds: { Count: > 0 } flying }
                                    ? flying[^1]
                                    : null;

            if (sent is not null) _sentTo.Add((sent, sentTo));

            Slug? released = sent as Slug;

            if (released is not null)
            {
                released.ResampleGroundNearImpact = Config.ResampleGroundAtImpact;
                released.SecondOrder = Config.SecondOrderWarheads;
                released.DragAtMidpointVelocity = Config.DragAtMidpointVelocity;
                released.StopOnTheTerrain = Config.StopWarheadsOnTheTerrain;
                released.AirVelocityAtOwnSubStep = Config.WarheadAirVelocityPerSubStep;

                // Logged so a flight can confirm the arm engaged.
                if (Config.DragAtMidpointVelocity)
                {
                    Log.Info($"ICBM computer on {KsaWorld.DisplayName(Craft)}: warheads take their "
                             + "drag at the sub-step's midpoint velocity");
                }
                released.GroundQueryAtOwnEpoch = Config.GroundQueryAtOwnEpoch;
            }

            ReleaseProbe? probe = ChooseProbe(ProbeRelease(), released);

            if (released is not null) SayTheSpin(weapon, released, probe);

            // Before the trace, which reads the round's release state as it begins: its walk from its own
            // probe then measures the fall and none of the separation.
            if ((Config.FocusTubesOnTheAim || Config.CancelSpinAtSeparation || Config.CancelProbeMissAtSeparation)
                && released is not null)
            {
                KickAtSeparation(weapon, released, probe);
            }

            BeginTrace(weapon);
        }

        return away;
    }

    // The correction loop is over by the first release, and nothing else records its result at
    // INFO -- including the arrival angle, whose cot(gamma) dominates the precision.
    private void SayWhatTheLoopLeft()
    {
        double arrival = Program.Arc?.ArrivalAngleDeg ?? double.NaN;

        Log.Info($"release summary on {KsaWorld.DisplayName(Craft)}: "
                 + $"cut off {Program.ResidualAtCutoff:F3} m/s short by {Program.CutOffBy}, "
                 + (Program.ResidualOffTheMissFreeLine < Program.ResidualAtCutoff - 1e-6
                        ? $"{Program.ResidualOffTheMissFreeLine:F3} of it off the line that misses nothing, "
                        : "")
                 + $"thrust spent {Program.ThrustSpentMetresPerSecond:F0} m/s against {Program.ToGainAtIgnition:F0} to gain at ignition, "
                 + $"trim owed {Rate(_owedAtSplit)} at the split and "
                 + $"{Rate(_trim.AtReleaseMetresPerSecond)} on release "
                 + $"({Rate(_trim.SpentMetresPerSecond)} spent"
                 + (_trim.GaveUp ? ", GAVE UP" : _trim.Done ? ", done" : ", still running") + "), "
                 + (double.IsFinite(Program.CoastArrivalMovedSeconds)
                        ? $"coast arrival moved {Program.CoastArrivalMovedSeconds:F2} s, " : "")
                 + (double.IsFinite(arrival) ? $"arriving at {arrival:F1} deg" : "")
                 + FloorSaid()
                 + (double.IsFinite(arrival) ? ", " : "")
                 + $"aim response {_aim.Response:F2} (raw {_aim.LastRawResponse:F2}) off "
                 + $"{_aim.PlantMeasurements} plant reading(s), "
                 + $"bias {Distance.Say(Vec.Len(_aim.BiasCci))}, "
                 + $"best {Distance.Say(_aim.BestMissMetres)}, worse for {_aim.WorseFor}");
    }

    // The in-game terrain amplitude below a kilometre of wavelength, the band a round crossing a
    // kilometre per frame cannot resolve; the headless fixture is undamped (ACCURACY-PLAN.md 3ae).
    private void SayWhatTheGroundUnderTheAimIsLike()
    {
        if (Parent is not { } parent) return;

        try
        {
            double3 up = Vec.Unit(_trueAimCci);
            double3 back = Program.CutoffPositionCci - _trueAimCci;
            double3 along = Vec.Unit(back - up * Vec.Dot(back, up));

            if (!Vec.IsFinite(along) || Vec.Len2(along) < 0.5) return;

            const int Half = 100;
            const double Spacing = 25.0;
            const int Window = 40;

            double[] height = new double[2 * Half + 1];

            for (int i = 0; i < height.Length; i++)
            {
                height[i] = TerrainRadiusAt(_trueAimCci + along * ((i - Half) * Spacing))
                            - parent.MeanRadius;
            }

            double lo = double.MaxValue, hi = double.MinValue;
            double fineLo = double.MaxValue, fineHi = double.MinValue, sumSquares = 0.0;
            int counted = 0;

            for (int i = 0; i < height.Length; i++)
            {
                lo = Math.Min(lo, height[i]);
                hi = Math.Max(hi, height[i]);

                // High-pass by subtracting a one-kilometre boxcar, which leaves exactly what a
                // sample grid coarser than that steps over.
                int from = i - Window / 2, to = i + Window / 2;
                if (from < 0 || to >= height.Length) continue;

                double mean = 0.0;
                for (int k = from; k <= to; k++) mean += height[k];
                mean /= to - from + 1;

                double residual = height[i] - mean;

                fineLo = Math.Min(fineLo, residual);
                fineHi = Math.Max(fineHi, residual);
                sumSquares += residual * residual;
                counted++;
            }

            if (counted == 0) return;

            Log.Info($"ground under the aim on {KsaWorld.DisplayName(Craft)}: "
                     + $"{height.Length} samples over "
                     + $"{(height.Length - 1) * Spacing / 1000.0:F1} km of the approach, "
                     + $"swing {hi - lo:F1} m, below a 1 km wavelength "
                     + $"{fineHi - fineLo:F1} m peak-to-peak and "
                     + $"{Math.Sqrt(sumSquares / counted):F1} m rms");

            SayWhatThatGroundCostsTheShot();
        }
        catch (Exception e)
        {
            Log.Warn($"could not profile the ground under the aim: {e.Message}");
        }
    }

    // The terrain's loop gain where the round stops: past one, re-aiming has no fixed point
    // (docs/ACCURACY-PLAN.md 3ej). At a metre, what a miss spans, and 25 m, what detail resolves.
    private void SayWhatThatGroundCostsTheShot()
    {
        double arrivalDeg = Program.Arc?.ArrivalAngleDeg ?? double.NaN;
        if (!double.IsFinite(arrivalDeg) || arrivalDeg <= 0.0) return;

        MapFrame? frame = MapFrame.TryAt(Vec.Zero, _trueAimCci, new double3(0, 0, 1));
        if (frame is not { } at) return;

        double radius = Vec.Len(_trueAimCci);
        double arrival = arrivalDeg * Math.PI / 180.0;

        foreach (double metres in new[] { 1.0, 25.0 })
        {
            if (!GroundSlope.TryAround(at, dir => TerrainRadiusAt(dir * radius), metres, 8, arrival,
                                       out GroundSlope.Reading r))
            {
                continue;
            }

            Log.Info($"ground under the aim on {KsaWorld.DisplayName(Craft)}: at {metres:F0} m, "
                     + $"{r.Describe()} (arriving {arrivalDeg:F1} deg)");
        }
    }

    // Both numbers or neither: a floor without what the fraction was applied to cannot be read
    // back into the preference.
    private string FloorSaid()
    {
        double floor = Program.ArrivalFloorDeg;
        double from = Program.ArrivalFloorFromDeg;

        if (!double.IsFinite(floor)) return "";

        return double.IsFinite(from)
            ? $" against a {floor:F1} deg floor, {Config.ArrivalPreference:P0} of the {from:F1} "
              + "deg the tanks could afford"
            : $" against a {floor:F1} deg floor";
    }

    private static string Rate(double metresPerSecond)
        => double.IsFinite(metresPerSecond) ? $"{metresPerSecond:F2} m/s" : "nothing";


    // The sequencer turns the vehicle so the tube about to fire lies on the line the aim correction
    // assumed, which for uncanted tubes is the line it is already on.
    private ReleaseCommand DriveDeployment(double simStep, IManualFire? weapon, in IcbmState state)
    {
        double3 held = Command.ThrustDirectionCci;
        double3 roll = _rollReference;

        bool trimming = PostCutoffSequence.TrimHoldsTheRelease(Config.TrimBeforeRelease, Command.ReadyToDeploy,
                                                               _trimAbandoned, Program.ReleasesAtCutoff,
                                                               _trim.Done, _postBoost.Correcting);

        if (weapon is null || !Command.ReadyToDeploy || trimming || Turning || _reachDueAfterTurn || _hopBurn.Holds)
        {
            return new ReleaseCommand(held, roll, false, -1, 0.0, "");
        }

        int next = weapon.NextTube;

        // Once, when the magazine empties and the minimum is final: a graze that survives leaves
        // no other trace.
        if (!_saidProximity && _didSplit && next < 0 && weapon.TubesReadyToFire == 0)
        {
            _saidProximity = true;
            Log.Info($"{KsaWorld.DisplayName(Craft)}: {_proximity.Closest.Said}");
        }

        double3 nextAxis = Vec.Zero;
        double3 noseAxis = Vec.Zero;

        if (next >= 0 && Parent is { } body)
        {
            int live = weapon.TubeAxesEcl(_tubeAxes);

            if (live > next)
            {
                doubleQuat cce2Cci = body.GetCce2Cci();
                for (int i = 0; i < live; i++) _tubeAxes[i] = _tubeAxes[i].Transform(cce2Cci);

                nextAxis = _tubeAxes[next];

                // Measured now, not taken from the commanded attitude: the turn is applied to it.
                noseAxis = ReleasePointing.ReferenceAxis(_tubeAxes.AsSpan(0, live));
            }
        }

        // The window closes at the deploy altitude, not when the rounds land.
        double descent = -Vec.Dot(state.VelocityCci, state.UpCci);
        double window = descent > 0.0
                            ? (AltitudeMetres - Config.DeployAltitudeMetres) / descent
                            : double.NaN;

        return _sequence.Update(simStep, new ReleaseSituation(
            ReadyToDeploy: true, NextTube: next,

            // A stop's quota, so Emptied latches at the end of each stop; may reach zero, which ends
            // the deployment. The magazine itself for a set of one.
            TubesLeft: _walker.TubesLeft(weapon.TubesReadyToFire),
            NextTubeAxisCci: nextAxis, NoseAxisCci: noseAxis, SweepMetresPerSecond: _tubeSpinSpeed,

            // Turns a tube's cant into the lateral velocity the release is budgeted in.
            EjectionMetresPerSecond: _warhead?.LaunchSpeed ?? 0.0,
            // None for a shot releasing at cutoff: flown at 300 km, waiting cost 53 s of coast through
            // air and a release 8.9 km out against a prediction of 1.4.
            SecondsLeftToDeploy: Program.ReleasesAtCutoff ? 0.0 : window,
            HeldDirectionCci: held, HeldRollCci: roll));
    }

    // A difference from the craft, because the prediction departs from the solved cutoff or the
    // live state: the tube mouth's offset, its sweep and its throw along the tube apply to both.
    private void MeasureRelease(IManualFire? weapon)
    {
        _releaseMeasured = false;
        _releaseOffsetCci = Vec.Zero;
        _releaseKickCci = Vec.Zero;
        _tubeSpinSpeed = 0.0;

        if (weapon is null || Parent is not { } parent) return;

        try
        {
            if (!weapon.TryMeanReleaseStateEcl(out double3 positionEcl, out double3 velocityEcl,
                                               out double spinSpeed))
            {
                return;
            }

            _tubeSpinSpeed = spinSpeed;

            doubleQuat cce2Cci = parent.GetCce2Cci();
            double3 offset = (positionEcl - KsaWorld.PositionEcl(Craft)).Transform(cce2Cci);
            double3 kick = (velocityEcl - KsaWorld.VelocityEcl(Craft)).Transform(cce2Cci);

            if (!Vec.IsFinite(offset) || !Vec.IsFinite(kick)) return;

            _releaseOffsetCci = offset;
            _releaseKickCci = kick;
            _releaseMeasured = true;
        }
        catch
        {
            // Falls back to the commanded attitude in ReleaseImpulseCci.
        }
    }

    // Unmeasured, the ejection speed along the commanded hold: wrong by however far the vehicle
    // settled off it.
    private double3 ReleaseImpulseCci()
    {
        if (_releaseMeasured) return _releaseKickCci;
        if (_warhead is not { LaunchSpeed: > 0f } warhead) return Vec.Zero;

        double3 nose = Command.ThrustDirectionCci;
        return nose.Equals(Vec.Zero) || !Vec.IsFinite(nose) ? Vec.Zero : Vec.Unit(nose) * warhead.LaunchSpeed;
    }

    private double3 ReleaseOffsetCci() => _releaseMeasured ? _releaseOffsetCci : Vec.Zero;
}
