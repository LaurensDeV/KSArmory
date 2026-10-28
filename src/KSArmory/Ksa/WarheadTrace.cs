using Brutal.Numerics;
using KSA;

namespace KSArmory;

/// <summary>
/// One nominated warhead, from the tube to the ground, beside <see cref="ImpactPredictor"/> re-flown
/// from wherever that warhead has got to.
///
/// <para><b>Measurement only.</b> Nothing here is read back by anything that flies, and the whole
/// class is behind <see cref="Config.TraceWarhead"/>, which is off.</para>
///
/// <para>The discriminator is <em>shape</em>: a prediction that walks off the release probe smoothly
/// is two flight models integrating apart, one that steps is something discrete. Only a flight can
/// say which, since every rig flies a planet at the origin. <c>docs/MIRV-NEXT.md</c> item 2.</para>
///
/// <para><b>Epochs.</b> Read from <see cref="IcbmComputer"/>'s update, after every system has
/// stepped its rounds, so the round and the celestial samples are both at the end of the step
/// (<c>docs/KSA-FRAME-ORDER.md</c> §3-§4). The round's own step clamp breaks that pairing; the
/// difference is the <c>lag</c> term, reported and never corrected.</para>
/// </summary>
internal sealed class WarheadTrace
{
    /// <summary>Everything the trace needs of the computer that owns it, gathered per frame.</summary>
    /// <param name="TrueAimCci">
    /// The aim point in this frame's inertial coordinates, at the frame's <em>edge</em>: a place on a
    /// turning planet moves through Cci, so carry it to the instant it is compared against.
    /// </param>
    /// <param name="TerrainRadiusAt">
    /// The computer's own delegate, not a second reader: the question is whether the round and the
    /// mod's predictor agree.
    /// </param>
    /// <param name="Craft">Several rockets write into one log, and a line has to pair with its flight.</param>
    /// <param name="StopOnTheSurface">
    /// Stops by the rule the aim was set by, since the walk is read against this probe.
    /// </param>
    internal readonly record struct Setup(
        Celestial Parent,
        BallisticBody Body,
        MunitionProfile Warhead,
        double3 TrueAimCci,
        double PredictStepSeconds,
        Func<double3, double> TerrainRadiusAt,
        Func<double3, double> DensityRatioAt,
        string Craft = "",
        bool StopOnTheSurface = false,
        bool StopOnTheTerrain = false);

    // In simulated seconds. Only the re-flight is expensive, about two hundred RK4 steps at release.
    private const double SampleIntervalSeconds = 2.0;
    private const double ReflyIntervalSeconds = 10.0;

    // Every frame across the release, the entry and the arrival. Entry is the round's own short step
    // in air, the question WarpPolicy asks, so the dense and short-step stretches coincide.
    private const double DenseAfterReleaseSeconds = 3.0;
    private const double DenseBeforeImpactSeconds = 10.0;
    private const double DenseReflyIntervalSeconds = 0.5;

    private IProjectile? _round;
    private int _lines;
    private int _frames;
    private int _lastSampleFrame = -1;
    private double _lastSampleAlt = double.NaN;
    private double _lastSampleAge = double.NaN;
    private double _worldSeconds;
    private double _longerOfStepAndSpan;
    private Int128 _clockAtBegin;
    private double _lastAge;
    private double _sinceSample;
    private double _sinceRefly;

    // Counted down every step, so the impact window arms on time rather than at the next re-flight.
    private double _remaining = double.NaN;

    // At the release epoch. Every later prediction is brought back to it before differencing: two
    // predictions taken T apart are in frames T of planet rotation apart.
    private double3 _probeGroundCci;
    private double3 _probeAlong;
    private double3 _probeCross;
    private double _probeSeconds = double.NaN;
    private bool _haveProbe;

    public bool Watching => _round is not null;

    /// <summary>Without reporting.</summary>
    public void Forget()
    {
        _round = null;
        ForgetProbe();
    }

    private void ForgetProbe()
    {
        _haveProbe = false;
        _probeGroundCci = Vec.Zero;
        _probeAlong = Vec.Zero;
        _probeCross = Vec.Zero;
        _probeSeconds = double.NaN;
    }

    /// <summary>
    /// Flies a probe from the round's own state, after every separation kick, unlike
    /// <see cref="IcbmComputer"/>'s release probe from the bus's state; the walk from it is the fall alone.
    /// </summary>
    public void Begin(IProjectile round, in Setup setup)
    {
        _round = round;
        _lines = 0;
        _frames = 0;
        _lastSampleFrame = -1;
        _lastSampleAlt = double.NaN;
        _lastSampleAge = double.NaN;
        _worldSeconds = 0.0;
        _longerOfStepAndSpan = 0.0;
        _clockAtBegin = KsaWorld.SimClockNanoseconds;
        _lastAge = round.Age;
        _sinceSample = 0.0;
        _sinceRefly = 0.0;
        _remaining = double.NaN;
        ForgetProbe();

        try
        {
            ToCci(setup, round, out double3 positionCci, out double3 velocityCci);

            // Full precision, so the release can be re-flown headlessly in the tests.
            Log.Info($"warhead trace on {setup.Craft}: {RoundLabel.For(round.Tube)} away"
                     + $" | Cci r=({positionCci.X:F1},{positionCci.Y:F1},{positionCci.Z:F1})"
                     + $" v=({velocityCci.X:F4},{velocityCci.Y:F4},{velocityCci.Z:F4})"
                     + $" | alt {setup.Body.AltitudeOf(positionCci) / 1000.0:F3} km,"
                     + $" {Vec.Len(velocityCci):F1} m/s inertial");

            if (!Predict(setup, positionCci, velocityCci, out ImpactPredictor.Impact hit,
                         out ImpactPredictor.Ending ending))
            {
                Log.Info("warhead trace: nothing predicted from the round's own release state -- "
                         + ending.Said(setup.Body.SurfaceRadius));
                return;
            }

            _probeGroundCci = hit.GroundFixedPointCci;
            _probeSeconds = hit.Seconds;
            _remaining = hit.Seconds;

            _haveProbe = ArrivalFrame.TryAt(hit.PointCci, hit.VelocityCci, out ArrivalFrame arrival);

            if (_haveProbe)
            {
                _probeAlong = arrival.Downrange;
                _probeCross = arrival.Cross;
            }

            Log.Info($"warhead trace on {setup.Craft}: probe from the round's own state ->"
                     + $" {LatLon(setup, hit.GroundFixedPointCci)},"
                     + $" {hit.Seconds:F1} s of flight,"
                     + $" {Ground(setup, hit.GroundFixedPointCci, setup.TrueAimCci):F3} m from the aim,"
                     + $" arriving at {Vec.Len(hit.VelocityCci):F0} m/s,"
                     + $" {ArrivalAngleDeg(hit):F1} deg below the horizontal,"
                     + $" {ProbeStop(setup, hit)}");

            if (_haveProbe) SayWhereThePrimaryLies(setup, arrival, hit.Seconds);
        }
        catch
        {
            // An exception inside the frame loop is the game, not a log line.
        }
    }

    // A round left out of the body's fall toward its primary drifts by half of it times the coast
    // squared, and the share along local up costs cot(gamma) times as much ground. Reported only.
    private static void SayWhereThePrimaryLies(in Setup setup, in ArrivalFrame arrival, double seconds)
    {
        double3 fallEcl = KsaWorld.BodyFallEcl(setup.Parent);
        double magnitude = Vec.Len(fallEcl);

        if (magnitude <= 0.0 || !double.IsFinite(seconds) || seconds <= 0.0) return;

        // A direction needs no conversion: Ecl and Cci differ by a translation.
        double3 parts = arrival.Resolve(Vec.Unit(fallEcl));
        double drift = 0.5 * magnitude * seconds * seconds;

        Log.Info($"warhead trace: the body falls at {magnitude * 1000.0:F3} mm/s2 toward its primary,"
                 + $" lying ({parts.X:+0.00;-0.00;0.00} up, {parts.Y:+0.00;-0.00;0.00} downrange,"
                 + $" {parts.Z:+0.00;-0.00;0.00} across) of the arrival"
                 + $" -- {drift:F0} m of drift over {seconds:F0} s, of which"
                 + $" {Math.Abs(drift * parts.X):F0} m is up");

        // The vector a stale force sample rides on: square to the fall on a near-circular orbit.
        double3 travelEcl = KsaWorld.BodyTravelEcl(setup.Parent);
        double speed = Vec.Len(travelEcl);

        if (speed <= 0.0) return;

        double3 travel = arrival.Resolve(Vec.Unit(travelEcl));

        Log.Info($"warhead trace: the body travels at {speed:F0} m/s,"
                 + $" lying ({travel.X:+0.00;-0.00;0.00} up, {travel.Y:+0.00;-0.00;0.00} downrange,"
                 + $" {travel.Z:+0.00;-0.00;0.00} across) of the arrival"
                 + $" -- one 17 ms frame of it is {speed * 0.017:F0} m of pull-centre offset");
    }

    /// <summary>
    /// <paramref name="simStep"/> is the step the engine applied, which the world moved across, not
    /// the possibly shorter one the round was integrated by.
    /// </summary>
    public void Update(double simStep, in Setup setup)
    {
        if (_round is not { } round) return;

        try
        {
            _frames++;

            if (double.IsFinite(simStep) && simStep > 0.0)
            {
                _worldSeconds += simStep;
                _longerOfStepAndSpan += KsaWorld.LastLongerOfStepAndSpan;
            }
            if (double.IsFinite(_remaining)) _remaining -= simStep;

            double age = round.Age;
            double advanced = age - _lastAge;
            _lastAge = age;

            if (round.State != RoundState.Flying) { Finish(setup, round); return; }

            // At INFO only the two ends are written, and the re-flight is never paid for.
            if (Log.Threshold > Log.Level.Debug) return;

            _sinceSample += simStep;
            _sinceRefly += simStep;

            bool dense = age < DenseAfterReleaseSeconds
                         || round.FaithfulStepSeconds <= Medium.FaithfulStepInAir
                         || (double.IsFinite(_remaining) && _remaining < DenseBeforeImpactSeconds);

            if (dense || _sinceSample >= SampleIntervalSeconds)
            {
                _sinceSample = 0.0;
                Sample(setup, round, simStep, advanced);
            }

            // Every frame for the first second, where a short shot's whole walk happens.
            double interval = age < 1.0 ? 0.0 : dense ? DenseReflyIntervalSeconds : ReflyIntervalSeconds;
            if (_sinceRefly >= interval)
            {
                _sinceRefly = 0.0;
                Refly(setup, round);
            }
        }
        catch
        {
            // As above.
        }
    }

    // `lag` is all the clamp has discarded so far. A jump in the walk on a frame where `dt` and
    // `step` disagree is the clamp; one where the simulation speed changed is a warp transition.
    private void Sample(in Setup setup, IProjectile round, double simStep, double advanced)
    {
        ToCci(setup, round, out double3 positionCci, out double3 velocityCci);

        double lag = _worldSeconds - round.Age;

        double altitude = setup.Body.AltitudeOf(positionCci);

        _lastSampleFrame = _frames;
        _lastSampleAlt = altitude;
        _lastSampleAge = round.Age;

        Log.Debug($"warhead trace {++_lines}: t={_worldSeconds:F2}s age={round.Age:F4}s"
                  + $" lag={lag * 1000.0:F1}ms dt={simStep * 1000.0:F1}ms step={advanced * 1000.0:F1}ms"
                  + $" sim={KsaWorld.SimulationSpeed:F2}x"
                  + $" frame={_frames}"
                  + $" alt={altitude / 1000.0:F3}km r={Vec.Len(positionCci):F1}"
                  + (round.Age < 1.0
                         ? $" cci=({positionCci.X:F4},{positionCci.Y:F4},{positionCci.Z:F4})"
                           + $" vcci=({velocityCci.X:F5},{velocityCci.Y:F5},{velocityCci.Z:F5})"
                         : "")
                  + $" v={Vec.Len(velocityCci):F1}m/s local={round.Speed:F1}m/s"
                  + Whose(setup, round));
    }

    // On the probe's arc, `t + remaining` would stay at the probe's flight time and the walk be zero.
    private void Refly(in Setup setup, IProjectile round)
    {
        ToCci(setup, round, out double3 positionCci, out double3 velocityCci);

        if (!Predict(setup, positionCci, velocityCci, out ImpactPredictor.Impact hit,
                     out ImpactPredictor.Ending ending))
        {
            Log.Debug($"warhead trace {++_lines}: t={_worldSeconds:F2}s -- nothing predicted from here, "
                      + ending.Exit);
            return;
        }

        _remaining = hit.Seconds;

        // Back to the release epoch: this answer is in the frame as it stands now.
        double3 atReleaseEpoch = setup.Body.UncarryCci(hit.GroundFixedPointCci, _worldSeconds);

        Log.Debug($"warhead trace {++_lines}: t={_worldSeconds:F2}s age={round.Age:F2}s"
                  + $" -> {LatLon(setup, hit.GroundFixedPointCci)}"
                  + $" in {hit.Seconds:F2}s (arrives at t={_worldSeconds + hit.Seconds:F2}s,"
                  + $" probe said {_probeSeconds:F2}s)"
                  + $" | alt {setup.Body.AltitudeOf(positionCci) / 1000.0:F3}km"
                  + $" v {Vec.Len(velocityCci):F0}m/s"
                  + Walk(setup, atReleaseEpoch)
                  + $"; {Ground(setup, hit.GroundFixedPointCci, setup.TrueAimCci):F0} m from the aim"
                  + (round.Age < 1.0
                         ? $" | aimR {setup.TerrainRadiusAt(setup.TrueAimCci):F4}"
                           + $" rho10km {setup.DensityRatioAt(setup.TrueAimCci * (1.0 + 10_000.0 / Vec.Len(setup.TrueAimCci))):E8}"
                           + $" hitR {Vec.Len(hit.GroundFixedPointCci):F4}"
                         : "")
                  + Whose(setup, round));
    }

    // Appended, so readers keyed on "warhead trace <n>:" keep working. On every line: the counter
    // is per trace, so one number appears once per warhead in the log.
    private static string Whose(in Setup setup, IProjectile round)
        => $" | {setup.Craft} {RoundLabel.For(round.Tube)}";

    private void Finish(in Setup setup, IProjectile round)
    {
        _round = null;

        try
        {
            // The live parent back-dated by the burst offset, as MissFromAim does: reached by a poll,
            // the round's position is frozen while the parent is not. One correction, never two: a
            // parent from the last flying frame and back-dated is a frame over. docs/MIRV-NEXT.md 8j.
            double3 parentAtBurst = InFrame.AtBurst(setup.Parent.GetPositionEcl(), setup.Parent.GetVelocityEcl(),
                                                    round.DetonationElapsedInFrame);

            doubleQuat cce2Cci = setup.Parent.GetCce2Cci();
            double3 positionCci = (round.PositionEcl - parentAtBurst).Transform(cce2Cci);
            double3 velocityCci = (round.VelocityEcl - setup.Parent.GetVelocityEcl()).Transform(cce2Cci);

            // Into the frame GroundTest's lookups are made in, by the ground's velocity with the spin:
            // without it a round stopped exactly on its surface reads a mean 0.16 m off it.
            double3 landingEcl = BlastSweep.GroundAtSample(round.PositionEcl,
                                                           KsaWorld.GroundVelocityAt(setup.Parent, round.PositionEcl),
                                                           round.DetonationElapsedInFrame);

            // DetonationElapsedInFrame is negative, between -dt and zero: the burst is inside the frame.
            double atBurst = _worldSeconds + round.DetonationElapsedInFrame;
            double lag = atBurst - round.Age;

            double3 atReleaseEpoch = setup.Body.UncarryCci(positionCci, atBurst);

            // The aim is at the frame's edge and the burst inside it: raw, the planet's rotation reads
            // as a miss, 0.416 m/ms at this latitude. BallisticScenario.MissFromAim carries the same.
            double3 aimAtBurst = setup.Body.CarryCci(setup.TrueAimCci, round.DetonationElapsedInFrame);

            Log.Info($"warhead trace on {setup.Craft}: {RoundLabel.For(round.Tube)} {Ended(round)}"
                     + $" at {LatLon(setup, positionCci)}"
                     + $" | {Ground(setup, positionCci, aimAtBurst):F2} m from the aim"
                     + Walk(setup, atReleaseEpoch)
                     + $" | flight {atBurst:F2}s by the world clock, {round.Age:F2}s by its own,"
                     + $" probe said {_probeSeconds:F2}s"
                     + $" | lag {lag * 1000.0:F1}ms = {lag * round.Speed:F0} m at {round.Speed:F0} m/s"
                     + $" over {_lines} sampled frames");

            // Against the universe clock: the round's age and _worldSeconds both sum the step handed,
            // so between them they cannot see the step being wrong.
            double elapsed = (double)(KsaWorld.SimClockNanoseconds - _clockAtBegin) / 1e9;
            Log.Info($"warhead trace: clock: universe time advanced {elapsed:F6} s over {_frames} frames;"
                     + $" the steps taken ran {(_worldSeconds - elapsed) * 1e9:+0.000;-0.000;0.000} ns from it,"
                     + $" the longer of step and span {(_longerOfStepAndSpan - elapsed) * 1e9:+0.000;-0.000;0.000} ns");

            Log.Info($"warhead trace: {Surfaces(setup, round, landingEcl, positionCci)}");

            if (round is Slug sampled) Log.Info($"warhead trace: {GroundSample(setup, sampled, landingEcl)}");

            // docs/MIRV-NEXT.md 8i. `alt` is the per-frame sample's own expression: near the last
            // sample's, the references differ; near the surface, the round moved.
            Log.Info($"warhead trace probe: frame {_frames} (last sample was frame {_lastSampleFrame}),"
                     + $" alt {setup.Body.AltitudeOf(positionCci):F1} m against the sample's"
                     + $" {_lastSampleAlt:F1} m, r {Vec.Len(positionCci):F1},"
                     + $" age {round.Age:F4}s against the sample's {_lastSampleAge:F4}s,"
                     + $" surfaceRadius {setup.Body.SurfaceRadius:F1}"
                     + (round is Slug slug
                            ? $", crossing tested against {slug.GroundRadiusUsed:F1}"
                              + $" ({slug.GroundRadiusUsed - Vec.Len(positionCci):+0.0;-0.0;0.0} m from where it stopped);"
                              + $" the round's own view of its stop is"
                              + $" {slug.StopAltitudeAgainstOwnGround:+0.0;-0.0;0.0} m"
                            : ", not a Slug"));
        }
        catch
        {
            // As above.
        }
    }

    // How far over the ground the prediction stopped. Off the terrain flag the crossing is on the chord
    // between samples, up to half a riser of the float-packed tread, worth cot(gamma) times as much
    // ground to a kick solved against this probe. docs/ACCURACY-PLAN.md 3es.
    private static string ProbeStop(in Setup setup, in ImpactPredictor.Impact hit)
    {
        // The ground-fixed point: TerrainRadiusAt wants the planet's rotation taken back out, as
        // ImpactPredictor.SurfaceUnder does. The radius is the same either way.
        double ground = setup.TerrainRadiusAt(hit.GroundFixedPointCci);
        if (!double.IsFinite(ground)) return "stopping over ground the height field would not answer for";

        double over = Vec.Len(hit.GroundFixedPointCci) - ground;
        double angle = ArrivalAngleDeg(hit) * Math.PI / 180.0;
        double cot = Math.Abs(Math.Sin(angle)) > 1e-9 ? Math.Cos(angle) / Math.Sin(angle) : double.NaN;

        return $"stopping {over:+0.0000;-0.0000;0.0000} m over the ground under it"
               + (double.IsFinite(cot) ? $" ({over * cot:+0.0000;-0.0000;0.0000} m of ground)" : "");
    }

    // The height field as the round read it through GroundTest, against the prediction's delegate.
    private static string Surfaces(in Setup setup, IProjectile round, double3 landingEcl,
                                   double3 positionCci)
    {
        double stoppedAt = Vec.Len(positionCci);

        // As the round recorded them at the crossing, at its own instant: re-querying at the raw
        // landing asks the height field at the frame's end rotation. docs/ACCURACY-PLAN.md 3do.
        double flown = round is Slug { GroundRadiusUsed: var r } && double.IsFinite(r)
                           ? r
                           : double.NaN;

        double3 askedAtEcl = round is Slug asked && Vec.IsFinite(asked.GroundSampledAtEcl)
                                 && !asked.GroundSampledAtEcl.Equals(Vec.Zero)
                             ? asked.GroundSampledAtEcl
                             : landingEcl;

        if (!double.IsFinite(flown))
        {
            return "surface at the landing point: the round recorded no surface to compare against";
        }

        // At the round's own point, so what is left is the readers disagreeing.
        double3 askedCci = (askedAtEcl - setup.Parent.GetPositionEcl())
            .Transform(setup.Parent.GetCce2Cci());
        double predicted = setup.TerrainRadiusAt(askedCci);

        return $"surface at the landing point: the round stopped on {flown:F3} m,"
               + $" the prediction flies to {predicted:F3} m ({flown - predicted:+0.000;-0.000;0.000} m apart,"
               + $" {(flown - predicted) * CotArrival:+0.000;-0.000;0.000} m of ground);"
               + $" the round is {stoppedAt - predicted:+0.000;-0.000;0.000} m off the prediction's";
    }

    // cot of the 32 deg arrival this mod flies.
    private const double CotArrival = 1.59;

    // The round's stopping-height error folds its own travel within the frame together with the
    // frame-newer body sample GroundTest differences against; this prints the height field with and
    // without the second, on KSA's own terrain. docs/KSA-FRAME-ORDER.md section 5.
    private static string GroundSample(in Setup setup, Slug round, double3 landingEcl)
    {
        double3 sampledAt = round.GroundSampledAtEcl;
        double step = round.GroundSampledOverSeconds;

        if (!Vec.IsFinite(sampledAt) || sampledAt.Equals(Vec.Zero) || !double.IsFinite(step))
        {
            return "ground sample: the round recorded none";
        }

        // Backwards by the velocity the round walked it forward with; forwards would correct twice.
        double3 walkedBy = round.GroundQueryAtOwnEpoch
            ? KsaWorld.GroundVelocityAt(setup.Parent, sampledAt)
            : setup.Parent.GetVelocityEcl();
        double3 asItUsedTo = sampledAt - walkedBy * step;

        bool read = GroundTest.Shared.TryGround(sampledAt, out double3 _, out double asRead);
        bool paired = GroundTest.Shared.TryGround(asItUsedTo, out double3 _, out double asUnpaired);
        bool truth = GroundTest.Shared.TryGround(landingEcl, out double3 _, out double flown);

        if (!read || !paired || !truth) return "ground sample: the height field would not answer";

        // With ResampleGroundNearImpact `step` is how far back from the frame's end the round crossed,
        // not the frame; named apart so it is not read as the frame rate. ACCURACY-PLAN.md 3dr.
        string over = round.ResampleGroundNearImpact
            ? $"crossing {step * 1000.0:F1} ms back from the frame's end"
            : $"over a {step * 1000.0:F1} ms frame";

        return $"ground sample: {over}, the body moved"
               + $" {Vec.Len(setup.Parent.GetVelocityEcl()) * step:F1} m;"
               + $" the round held {asRead - flown:+0.000;-0.000;0.000} m off the true surface,"
               + $" unpaired it would have held {asUnpaired - flown:+0.000;-0.000;0.000} m";
    }

    // Vector, not magnitude: docs/FRAMES-AND-EPOCHS.md, "Measure vectors, not magnitudes".
    private string Walk(in Setup setup, double3 atReleaseEpochCci)
    {
        if (!_haveProbe) return "";

        double metres = Ground(setup, atReleaseEpochCci, _probeGroundCci);

        // Carried to the arrival, where the axes were taken: both points are ground-fixed, and the
        // spin over a 340 s fall is 1.4 deg. Their slightly different flight times are real cross
        // content. As IcbmComputer.ProbeMissSaid; ACCURACY-PLAN.md 3dw.
        double3 separation = setup.Body.CarryCci(atReleaseEpochCci - _probeGroundCci, _probeSeconds);

        // Four decimals: increments between re-flies are tenths of a millimetre (ACCURACY-PLAN.md 3dq).
        // Not Distance.Say: shot-report.py parses the components as bare metres.
        return $" | walk from the release probe {metres:F4} m"
               + $" ({Vec.Dot(separation, _probeAlong):+0.0000;-0.0000;0.0000} down,"
               + $" {Vec.Dot(separation, _probeCross):+0.0000;-0.0000;0.0000} cross)";
    }

    private bool Predict(in Setup setup, double3 positionCci, double3 velocityCci,
                         out ImpactPredictor.Impact hit, out ImpactPredictor.Ending ending)
        => ImpactPredictor.TryPredict(setup.Body, positionCci, velocityCci, setup.PredictStepSeconds,
                                      ImpactPredictor.DefaultMaxSeconds, out hit, out ending,
                                      setup.TerrainRadiusAt, null,
                                      new ImpactPredictor.Drag(setup.DensityRatioAt, setup.Warhead),
                                      stopOnTheSurface: setup.StopOnTheSurface,
                                      stopOnTheTerrain: setup.StopOnTheTerrain);

    // Both terms from this frame's samples, so the subtraction removes the ~29.8 km/s carrier exactly.
    private static void ToCci(in Setup setup, IProjectile round,
                              out double3 positionCci, out double3 velocityCci)
    {
        doubleQuat cce2Cci = setup.Parent.GetCce2Cci();

        positionCci = (round.PositionEcl - setup.Parent.GetPositionEcl()).Transform(cce2Cci);
        velocityCci = (round.VelocityEcl - setup.Parent.GetVelocityEcl()).Transform(cce2Cci);
    }

    private static double Ground(in Setup setup, double3 a, double3 b)
        => setup.Body.SurfaceRadius * Vec.AngleBetween(a, b);

    private static string LatLon(in Setup setup, double3 pointCci)
    {
        double3 cce = pointCci.Transform(setup.Parent.GetCci2Cce());
        return $"{setup.Parent.GetLatitudeFromCce(cce):F4},{setup.Parent.GetLongitudeFromCce(cce):F4}";
    }

    // Positive descending.
    private static double ArrivalAngleDeg(in ImpactPredictor.Impact hit)
    {
        double3 up = Vec.Unit(hit.PointCci);
        return Vec.AngleBetween(up, hit.VelocityCci) * 180.0 / Math.PI - 90.0;
    }

    private static string Ended(IProjectile round) => round.State switch
    {
        RoundState.Detonated => round is Slug { HitGround: true } ? "landed" : "burst",
        RoundState.Expired => "expired in the air",
        RoundState.ShotDown => "was shot down",
        RoundState.Dud => "struck something before it armed",
        _ => "stopped",
    };
}
