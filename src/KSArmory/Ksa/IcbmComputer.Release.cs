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
    /// computer did not release.
    ///
    /// <para><b>The seam a harness scores a walk through.</b> A warhead's target is the stop that
    /// was current when it left, which nothing downstream can recover afterwards: the lead moves on
    /// at the next handover, so reading it at impact reports whichever target the bus finished on.
    /// Always answered, walk or no walk — a single-target flight records every round against target
    /// zero.</para>
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
            // Captured on the first one away, because it is the only instant the magazine's loaded
            // count is still readable: it reloads a few seconds after the salvo, and read then it
            // says the salvo never finished. WarheadsAway only ever increases, so the two
            // together are a monotonic "the salvo is finished".
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

                // Said, because an arm that cannot be seen in the log is an arm nobody can verify
                // engaged. The sub-step swap says so a few lines above.
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

    // Everything the correction loop will ever do is over by the first release, and otherwise none
    // of it survives the flight at INFO: the response and the plant readings are DEBUG lines
    // buried in hundreds of per-cycle ones, the release residual appears only when the trim
    // changes what it is doing, and the arrival angle is printed only when a floor is asked for
    // and cannot be met -- so a baseline shot would not record the one number cot(gamma) says
    // dominates its precision.
    private void SayWhatTheLoopLeft()
    {
        double arrival = Program.Arc?.ArrivalAngleDeg ?? double.NaN;

        Log.Info($"release summary on {KsaWorld.DisplayName(Craft)}: "
                 + $"cut off {Program.ResidualAtCutoff:F3} m/s short, "
                 + $"thrust spent {Program.ThrustSpentMetresPerSecond:F0} m/s against {Program.ToGainAtIgnition:F0} to gain at ignition, "
                 + $"trim owed {Rate(_owedAtSplit)} at the split and "
                 + $"{Rate(_trim.AtReleaseMetresPerSecond)} on release "
                 + $"({Rate(_trim.SpentMetresPerSecond)} spent"
                 + (_trim.GaveUp ? ", GAVE UP" : _trim.Done ? ", done" : ", still running") + "), "
                 + (double.IsFinite(arrival) ? $"arriving at {arrival:F1} deg" : "")
                 + FloorSaid()
                 + (double.IsFinite(arrival) ? ", " : "")
                 + $"aim response {_aim.Response:F2} (raw {_aim.LastRawResponse:F2}) off "
                 + $"{_aim.PlantMeasurements} plant reading(s), "
                 + $"bias {Distance.Say(Vec.Len(_aim.BiasCci))}, "
                 + $"best {Distance.Say(_aim.BestMissMetres)}, worse for {_aim.WorseFor}");
    }

    // What the ground the warheads are about to cross actually does, once per flight, beside the
    // release summary.
    //
    // The headless fixture is faithful to KSA's declared erosion spectrum and *undamped*; the game
    // scales every octave by the biome weight, a gradient-falloff power and `1 - |dot|`, and
    // docs/KSA-TERRAIN.md says of that product only that it is unmeasured. This is the measurement.
    // What matters is the amplitude surviving *below a kilometre of wavelength*, because that is
    // the band a round crossing a kilometre of ground per frame cannot resolve --
    // docs/ACCURACY-PLAN.md 3ae.
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

    // The terrain's loop gain at the aim, which decides whether re-aiming converges at all. The
    // profile above is kilometres of the approach; this is the slope where the round actually
    // stops. A residual miss lands on ground slope*d off and the arrival turns that back into
    // slope*d/tan(gamma) more, so past gain one there is no fixed point and the group's spread is
    // the ground's rather than the guidance's -- a quarter of rockets are past it at the site every
    // baseline has used (docs/ACCURACY-PLAN.md 3ej). Logged before a night is spent on a site
    // rather than reconstructed from it afterwards.
    //
    // Two scales because a height field answers differently at each: metres is what a miss spans,
    // tens of metres is what the detail textures resolve.
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

    // What bounded the search, and what the fraction was applied to. Silent for a shot that asked
    // for nothing, which is what ships -- and both numbers or neither, because a floor without its
    // multiplicand cannot be read back into the preference that produced it.
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

    // Most of a flight is spent before either trim number exists, and "NaN m/s" in a summary reads
    // as a fault rather than as a measurement nothing has taken yet.
    private static string Rate(double metresPerSecond)
        => double.IsFinite(metresPerSecond) ? $"{metresPerSecond:F2} m/s" : "nothing";


    // What the launcher should be holding, and whether a round may go. The sequencer turns the
    // vehicle so the tube about to fire lies on the line the aim correction assumed - which for a
    // launcher whose tubes are not canted is the line it is already on, and costs nothing.
    private ReleaseCommand DriveDeployment(double simStep, IManualFire? weapon, in IcbmState state)
    {
        double3 held = Command.ThrustDirectionCci;
        double3 roll = _rollReference;

        bool trimming = PostCutoffSequence.TrimHoldsTheRelease(Config.TrimBeforeRelease, Command.ReadyToDeploy,
                                                               _trimAbandoned, Program.ReleasesAtCutoff,
                                                               _trim.Done, _postBoost.Correcting);

        if (weapon is null || !Command.ReadyToDeploy || trimming)
        {
            return new ReleaseCommand(held, roll, false, -1, 0.0, "");
        }

        int next = weapon.NextTube;

        // One line per flight, whether or not anything went wrong, said the frame the magazine
        // empties -- which is the last moment the bus manoeuvres near what it dropped, and so the
        // moment the minimum is final. It is a measurement rather than a gate: without it a
        // collision can only be inferred from a thrashing trim, and a shot that grazes the stack
        // and survives leaves no other trace.
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

                // The launcher's own axis, read the same way the reference was: the cants cancel in
                // the mean. It is what the turn is applied to, so it has to be measured now rather
                // than taken from the attitude the vehicle was asked for.
                noseAxis = ReleasePointing.ReferenceAxis(_tubeAxes.AsSpan(0, live));
            }
        }

        // How long the release window has left, from the descent rather than from the arrival: it
        // closes when the launcher falls through the deploy altitude, not when the rounds land.
        double descent = -Vec.Dot(state.VelocityCci, state.UpCci);
        double window = descent > 0.0
                            ? (AltitudeMetres - Config.DeployAltitudeMetres) / descent
                            : double.NaN;

        return _sequence.Update(simStep, new ReleaseSituation(
            // The honest count, not a floor of one. The share-of-the-window division guards zero
            // itself, and the sequencer has to see the magazine reach empty -- that is what ends
            // the deployment, and a launcher reloads a few seconds later.
            ReadyToDeploy: true, NextTube: next,

            // A stop's quota rather than the magazine, so Emptied latches at the end of each stop
            // and is the signal that one is done. Hands back the magazine untouched for a flight
            // with no walk, which is every set of one.
            TubesLeft: _walker.TubesLeft(weapon.TubesReadyToFire),
            NextTubeAxisCci: nextAxis, NoseAxisCci: noseAxis, SweepMetresPerSecond: _tubeSpinSpeed,

            // Off the munition rather than assumed: it is what turns a tube's cant into the lateral
            // velocity the release is budgeted in, and it belongs to the round rather than to the
            // sequencer. A launcher carrying nothing prices a cant at nothing, which is right —
            // there is no round to throw off the line.
            EjectionMetresPerSecond: _warhead?.LaunchSpeed ?? 0.0,
            // None for a shot that releases at cutoff: waiting for a stack with no engine to settle
            // costs a coast through air, and flown at 300 km that was 53 s of it for a release that
            // went late anyway, 8.9 km out against a prediction of 1.4.
            SecondsLeftToDeploy: Program.ReleasesAtCutoff ? 0.0 : window,
            HeldDirectionCci: held, HeldRollCci: roll));
    }

    // Where a released round starts, as a difference from where the craft is.
    //
    // A difference rather than a state, because the prediction is taken from two different places -
    // the solved cutoff while burning, the live state while coasting - and the tube's offset from
    // the craft applies to both. It carries all three things a prediction taken from the orbit
    // state gets wrong: the tube mouth is metres away, the lever arm is sweeping, and the round is
    // thrown along the tube rather than along whatever attitude was commanded.
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
            // A launcher whose tubes will not resolve falls back to the commanded attitude below.
        }
    }

    private double3 ReleaseImpulseCci()
    {
        if (_releaseMeasured) return _releaseKickCci;
        if (_warhead is not { LaunchSpeed: > 0f } warhead) return Vec.Zero;

        double3 nose = Command.ThrustDirectionCci;
        return nose.Equals(Vec.Zero) || !Vec.IsFinite(nose) ? Vec.Zero : Vec.Unit(nose) * warhead.LaunchSpeed;
    }

    private double3 ReleaseOffsetCci() => _releaseMeasured ? _releaseOffsetCci : Vec.Zero;
}
