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
/// <para>What it exists to settle: a warhead misses its own release probe by <b>1.7 km</b> in flight
/// and by <b>394 m</b> headlessly, and no rig can close that gap — every one of them flies a planet
/// at the origin, which is the one case where a frame carrier is identically zero.
/// <c>docs/MIRV-NEXT.md</c> item 2 has the headless decomposition and item -1 has why it is not
/// evidence about the flight.</para>
///
/// <para>The discriminator is <em>shape</em>, not size. A prediction that walks away from the
/// release probe smoothly is two flight models integrating apart; one that steps is something
/// discrete — a step change, a warp transition, a surface the two models disagree about. Only the
/// flight can say which, so this writes down enough per frame to tell them apart afterwards.</para>
///
/// <para><b>Epochs.</b> The round is read from <see cref="IcbmComputer"/>'s update, which
/// <c>KSArmoryMod</c> runs <em>after</em> every weapons system has stepped its rounds — so the round
/// and the celestial samples the Cci conversion uses are both at the end of the step just applied
/// (<c>docs/KSA-FRAME-ORDER.md</c> §3-§4). The one thing that breaks that pairing is the round's own
/// clamp: rounds are stepped by <c>min(dtSim, FaithfulStepInFlight())</c> and this by <c>dtSim</c>,
/// so a clamped frame leaves the round short of the world. That difference is the <c>lag</c> term
/// below — reported, never corrected.</para>
/// </summary>
internal sealed class WarheadTrace
{
    /// <summary>Everything the trace needs of the computer that owns it, gathered per frame.</summary>
    /// <param name="TrueAimCci">
    /// The aim point in <em>this</em> frame's inertial coordinates, at the frame's <em>edge</em>. It
    /// is re-derived every frame because a place on a turning planet moves through Cci, so pairing
    /// it with anything from another frame reads the planet's own rotation as a miss — and a burst
    /// is somewhere <em>inside</em> the frame, so that holds at sub-frame scale too. Carry it to the
    /// instant it is being compared against.
    /// </param>
    /// <param name="TerrainRadiusAt">
    /// The surface the <em>prediction</em> stops on. Deliberately the computer's own delegate rather
    /// than one built here: the question is whether the round and the mod's own predictor agree, and
    /// a second height-field reader would answer a different one.
    /// </param>
    /// <param name="Craft">
    /// Whose warhead this is. Eight rockets write into one log, so a line without it is a reading
    /// that cannot be paired with the flight it came from — and the aim points are 12 km apart but
    /// two of the eight sit 100 m apart, so recovering it from the landing coordinate does not work
    /// either. Same fault the cutoff line and <c>why_it_ended</c> already carry a name for.
    /// </param>
    /// <param name="StopOnTheSurface">
    /// Whether the prediction's crossing is placed on the ground, as the computer's own are. The walk
    /// is read against this probe, so it has to stop by the rule the aim was set by.
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

    // How often the round's own state is written down, and how often the prediction is re-flown from
    // it. Only the second is expensive - about two hundred RK4 steps at release, falling to a
    // handful at the end - so it is the one that is rationed. Both are counted in *simulated*
    // seconds, so a coasting warhead under 8x costs a line every quarter second of anyone's evening
    // rather than every two.
    private const double SampleIntervalSeconds = 2.0;
    private const double ReflyIntervalSeconds = 10.0;

    // Every frame across the three stretches a discrete fault would land on: the release, the
    // entry, and the arrival. Entry is bounded by the round's own opinion of the air rather than by
    // an altitude, because that is the same question WarpPolicy is answering when it slows the
    // world - so the dense stretch and the short-step stretch are the same stretch by construction.
    // A 400 s flight writes a few hundred lines.
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

    // Counted down by the step between re-flights, so the impact window arms on time rather than on
    // whenever the next re-flight happens to fall.
    private double _remaining = double.NaN;

    // The release probe's impact, in inertial coordinates as they stood at the release epoch, with
    // the arrival's own ground frame beside it. Every later prediction is brought back to this epoch
    // before being differenced against it: the predictor un-rotates its answer by its own flight
    // time, so two predictions taken T apart are expressed in frames T of planet rotation apart.
    private double3 _probeGroundCci;
    private double3 _probeAlong;
    private double3 _probeCross;
    private double _probeSeconds = double.NaN;
    private bool _haveProbe;

    /// <summary>True while a warhead is being followed.</summary>
    public bool Watching => _round is not null;

    /// <summary>
    /// Drop whatever is being followed, without reporting. What a new designation and a switched-off
    /// trace both do.
    /// </summary>
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
    /// Start following one warhead, and write down the state it left on beside the prediction flown
    /// from exactly that state.
    ///
    /// <para>This is <em>not</em> the same probe <see cref="IcbmComputer"/> already writes at
    /// release: that one is flown from the bus's orbit state plus an assumed offset and kick, and
    /// this one from the round's own position and velocity, after every separation kick. The gap
    /// between the two lines is what the tube and the separation did that the mean release state
    /// could not express, and the walk from this one is the fall alone.</para>
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

            // Full precision on purpose: this line is what lets the same release be re-flown in
            // tests/KSArmory.Tests without the game, which is the only way the flight and the rig
            // can be made to answer the same question.
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

            Log.Info($"warhead trace: probe from the round's own state ->"
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
            // A diagnostic that throws inside the frame loop is the game, not a log line.
        }
    }

    // Where the body's own fall lies against the arrival, which is the one thing that decides what
    // it costs. The round carries none of this acceleration and the ground under it carries all of
    // it, so the round is left behind by half of it times the square of the coast -- and the share
    // resolved along local up is multiplied by cot(gamma) again before it reaches the ground.
    //
    // Reported, not corrected. It is a term nothing has ever written down, and the flown spread of
    // what it can be worth on this trajectory is nought to nearly ten kilometres.
    private static void SayWhereThePrimaryLies(in Setup setup, in ArrivalFrame arrival, double seconds)
    {
        double3 fallEcl = KsaWorld.BodyFallEcl(setup.Parent);
        double magnitude = Vec.Len(fallEcl);

        if (magnitude <= 0.0 || !double.IsFinite(seconds) || seconds <= 0.0) return;

        // A direction is identical in Ecl and Cci -- the two differ by a translation -- so the
        // acceleration needs no conversion before it is resolved. Only a position would.
        double3 parts = arrival.Resolve(Vec.Unit(fallEcl));
        double drift = 0.5 * magnitude * seconds * seconds;

        Log.Info($"warhead trace: the body falls at {magnitude * 1000.0:F3} mm/s2 toward its primary,"
                 + $" lying ({parts.X:+0.00;-0.00;0.00} up, {parts.Y:+0.00;-0.00;0.00} downrange,"
                 + $" {parts.Z:+0.00;-0.00;0.00} across) of the arrival"
                 + $" -- {drift:F0} m of drift over {seconds:F0} s, of which"
                 + $" {Math.Abs(drift * parts.X):F0} m is up");

        // The other vector, and the one a stale force sample rides on. Square to the fall on a
        // near-circular orbit, so resolving one says nothing about the other.
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
    /// One frame. <paramref name="simStep"/> is the step the engine applied, which is the interval
    /// the world moved across — not the possibly shorter one the round was integrated by.
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

            // The per-frame half is DEBUG and the two ends are INFO, so at INFO this degrades to
            // release-and-impact rather than to nothing - and the re-flight, which is the expensive
            // part, is never paid for a line that would be discarded.
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

            if (_sinceRefly >= (dense ? DenseReflyIntervalSeconds : ReflyIntervalSeconds))
            {
                _sinceRefly = 0.0;
                Refly(setup, round);
            }
        }
        catch
        {
            // As above. A trace that cannot answer says nothing and lets the flight continue.
        }
    }

    // The cheap line: the two clocks and where the round is. `lag` is the whole of what the clamp
    // has discarded so far, and `dt`/`step` separate this frame's share of it from the world's own
    // pacing - a jump in the walk that lands on a frame where those two disagree is the clamp, and
    // one that lands where the simulation speed changed is a warp transition.
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
                  + $" v={Vec.Len(velocityCci):F1}m/s local={round.Speed:F1}m/s"
                  + Whose(setup, round));
    }

    // The expensive line: the same predictor, re-flown from where the round has got to. If the
    // round were still on the arc the release probe flew, `t + remaining` would be constant at the
    // probe's own flight time and the walk would be zero. Neither is a summary of the other - the
    // arrival time says the round has left the arc, the walk says what that costs on the ground.
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

        // Back to the release epoch before differencing. The predictor un-carries its answer by its
        // own flight time, so this one is expressed in the frame as it stood *now* and the release
        // probe's in the frame as it stood at release.
        double3 atReleaseEpoch = setup.Body.UncarryCci(hit.GroundFixedPointCci, _worldSeconds);

        Log.Debug($"warhead trace {++_lines}: t={_worldSeconds:F2}s age={round.Age:F2}s"
                  + $" -> {LatLon(setup, hit.GroundFixedPointCci)}"
                  + $" in {hit.Seconds:F2}s (arrives at t={_worldSeconds + hit.Seconds:F2}s,"
                  + $" probe said {_probeSeconds:F2}s)"
                  + $" | alt {setup.Body.AltitudeOf(positionCci) / 1000.0:F3}km"
                  + $" v {Vec.Len(velocityCci):F0}m/s"
                  + Walk(setup, atReleaseEpoch)
                  + $"; {Ground(setup, hit.GroundFixedPointCci, setup.TrueAimCci):F0} m from the aim"
                  + Whose(setup, round));
    }

    // Which warhead a line belongs to, appended rather than prefixed so every reader keyed on
    // "warhead trace <n>:" keeps working.
    //
    // It has to be on the sample lines, not just the named ones: the counter is per trace, so with
    // six warheads of eight rockets in one log the same `warhead trace 412:` appears 48 times and
    // concurrent streams sit within a few counts of each other. Keying by arrival time and
    // predicted lat/lon works and is not something a reader should have to invent.
    private static string Whose(in Setup setup, IProjectile round)
        => $" | {setup.Craft} {RoundLabel.For(round.Tube)}";

    // Where it actually stopped, and the four things that could put it there: the clock it kept
    // against the world's, the arc it was on against the arc predicted for it, the surface it stopped
    // on against the surface the prediction flew to, and how it ended.
    private void Finish(in Setup setup, IProjectile round)
    {
        _round = null;

        try
        {
            // The same correction MissFromAim makes on the scoring path, which is the one that
            // measures right: the LIVE parent, back-dated by the sub-frame burst offset. Finish is
            // reached by a poll, so the round's PositionEcl is frozen from an earlier step while
            // the parent is not -- and the round detonates on the first sub-step of its frame, so
            // DetonationElapsedInFrame is very nearly a whole frame of the body's 29.78 km/s.
            //
            // One correction, not two. Pairing a parent captured on the last flying frame AND
            // back-dating it overshoots by exactly a frame: flown as -493 m below the surface
            // uncorrected against +517 m above it double-corrected. docs/MIRV-NEXT.md item 8j.
            double3 parentAtBurst = setup.Parent.GetPositionEcl()
                                    + setup.Parent.GetVelocityEcl() * round.DetonationElapsedInFrame;

            doubleQuat cce2Cci = setup.Parent.GetCce2Cci();
            double3 positionCci = (round.PositionEcl - parentAtBurst).Transform(cce2Cci);
            double3 velocityCci = (round.VelocityEcl - setup.Parent.GetVelocityEcl()).Transform(cce2Cci);

            // Carried into the frame every live lookup is made in, so GroundTest -- which takes an
            // Ecl point and finds the body itself -- reads the same geometry rather than the leak.
            // By the ground's velocity, spin included: the lookup is answered at the frame's end
            // rotation, and without the spin a round stopped exactly on its surface reads a mean
            // 0.16 m off it.
            double3 landingEcl = round.PositionEcl
                                 - KsaWorld.GroundVelocityAt(setup.Parent, round.PositionEcl)
                                   * round.DetonationElapsedInFrame;

            // The burst is somewhere inside this frame while _worldSeconds is at its edge, and
            // DetonationElapsedInFrame is that offset - negative, between -dt and zero. At 7 km/s a
            // whole frame is kilometres, so it is the difference between an epoch and a guess.
            double atBurst = _worldSeconds + round.DetonationElapsedInFrame;
            double lag = atBurst - round.Age;

            double3 atReleaseEpoch = setup.Body.UncarryCci(positionCci, atBurst);

            // The aim is re-derived at the frame's edge while the burst is inside it, so pairing the
            // two raw reads one sub-frame of the planet's own rotation as a miss -- 0.416 m/ms at
            // this latitude, which is metres on a median frame and eight on a slow one.
            // BallisticScenario.MissFromAim carries the same term for the same reason.
            double3 aimAtBurst = setup.Body.CarryCci(setup.TrueAimCci, round.DetonationElapsedInFrame);

            Log.Info($"warhead trace on {setup.Craft}: {RoundLabel.For(round.Tube)} {Ended(round)}"
                     + $" at {LatLon(setup, positionCci)}"
                     + $" | {Ground(setup, positionCci, aimAtBurst):F2} m from the aim"
                     + Walk(setup, atReleaseEpoch)
                     + $" | flight {atBurst:F2}s by the world clock, {round.Age:F2}s by its own,"
                     + $" probe said {_probeSeconds:F2}s"
                     + $" | lag {lag * 1000.0:F1}ms = {lag * round.Speed:F0} m at {round.Speed:F0} m/s"
                     + $" over {_lines} sampled frames");

            // Against the planets' own clock rather than the mod's: the round's age and _worldSeconds
            // both sum the step it was handed, so comparing those two cannot see the step being wrong.
            double elapsed = (double)(KsaWorld.SimClockNanoseconds - _clockAtBegin) / 1e9;
            Log.Info($"warhead trace: clock: universe time advanced {elapsed:F6} s over {_frames} frames;"
                     + $" the steps taken ran {(_worldSeconds - elapsed) * 1e9:+0.000;-0.000;0.000} ns from it,"
                     + $" the longer of step and span {(_longerOfStepAndSpan - elapsed) * 1e9:+0.000;-0.000;0.000} ns");

            Log.Info($"warhead trace: {Surfaces(setup, round, landingEcl, positionCci)}");

            if (round is Slug sampled) Log.Info($"warhead trace: {GroundSample(setup, sampled, landingEcl)}");

            // The probe for docs/MIRV-NEXT.md item 8i. Across 24 flown shots the last sampled
            // altitude is ~500 m and the landing is tens of metres BELOW the same surface, with the
            // round's own age unchanged between the two -- 500 m in under 10 ms, which nothing can
            // fly. So either the round moves without its clock recording it, or the two altitudes
            // are not measured against the same thing, and every other candidate is eliminated:
            // both terrain reads agree to +0.0 m, the sub-step overshoot is bounded at ~5 m, and
            // `lag` reconciles the clocks to 1 ms rather than the frame a late Finish would cost.
            //
            // `alt` here is the SAME expression the per-frame sample logs, so a value near the last
            // sample's says the references differ and a value near the surface says the round moved.
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

    // The one comparison a headless rig cannot make at all: the height field as the *round* reads it
    // through GroundTest, and as the *prediction* reads it through the computer's own delegate, at
    // the one point where it matters. docs/MIRV-NEXT.md item 2 lists a surface disagreement as a
    // candidate for the missing kilometre, and a metre of it is about eleven metres of ground on
    // this arrival.
    // How far over the ground the prediction stopped, and what that is worth once it lands. The crossing
    // sits on the chord between the samples bracketing it unless PredictionStopsOnTheTerrain is on, and
    // over the engine's float-packed tread that is up to half a riser of height -- handed to the round as
    // cot(gamma) times as much ground, because the miss kick is solved against this probe. Zero on the
    // flat, which is why only a sloped seat shows it. docs/ACCURACY-PLAN.md 3es.
    private static string ProbeStop(in Setup setup, in ImpactPredictor.Impact hit)
    {
        // The GROUND-FIXED point, never the inertial one: TerrainRadiusAt wants the planet's rotation
        // already taken back out, exactly as ImpactPredictor.SurfaceUnder un-carries before asking. Read
        // at the raw impact this line said +37.6 m over a 364 s flight, which is the planet's turn and
        // not a height. Un-carrying is a rotation, so the radius is the same either way.
        double ground = setup.TerrainRadiusAt(hit.GroundFixedPointCci);
        if (!double.IsFinite(ground)) return "stopping over ground the height field would not answer for";

        double over = Vec.Len(hit.GroundFixedPointCci) - ground;
        double angle = ArrivalAngleDeg(hit) * Math.PI / 180.0;
        double cot = Math.Abs(Math.Sin(angle)) > 1e-9 ? Math.Cos(angle) / Math.Sin(angle) : double.NaN;

        return $"stopping {over:+0.0000;-0.0000;0.0000} m over the ground under it"
               + (double.IsFinite(cot) ? $" ({over * cot:+0.0000;-0.0000;0.0000} m of ground)" : "");
    }

    private static string Surfaces(in Setup setup, IProjectile round, double3 landingEcl,
                                   double3 positionCci)
    {
        double stoppedAt = Vec.Len(positionCci);

        // The surface the round ACTUALLY stopped against, and the point it asked at -- both
        // recorded by the round at the crossing, back-dated to its own instant within the frame.
        //
        // Re-querying GroundTest at the raw landing instead asks the height field at the frame's
        // END rotation, which is item 40's fault reintroduced in the diagnostic that is supposed to
        // detect it: measured over 50 traced warheads that read the two surfaces -0.34 +/- 1.46 m
        // apart, worth 1.64 m of ground at cot(gamma), while the walk it is meant to explain is
        // 35 mm and regresses on it at +0.013 m/m where a real surface error predicts -1.59.
        // docs/ACCURACY-PLAN.md 3do.
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

        // Asked at the same point the round asked at, so what is left between the two numbers is
        // the two height-field readers disagreeing rather than the instant they were asked at.
        double3 askedCci = (askedAtEcl - setup.Parent.GetPositionEcl())
            .Transform(setup.Parent.GetCce2Cci());
        double predicted = setup.TerrainRadiusAt(askedCci);

        return $"surface at the landing point: the round stopped on {flown:F3} m,"
               + $" the prediction flies to {predicted:F3} m ({flown - predicted:+0.000;-0.000;0.000} m apart,"
               + $" {(flown - predicted) * CotArrival:+0.000;-0.000;0.000} m of ground);"
               + $" the round is {stoppedAt - predicted:+0.000;-0.000;0.000} m off the prediction's";
    }

    // The arrival this mod flies, for turning a height disagreement into the ground it would be
    // worth. A round comes in at 32 deg, so cot is about 1.59.
    private const double CotArrival = 1.59;

    // Where the round read the ground against where it stopped, which is the whole of its stopping
    // height error and therefore -- times cot(gamma) -- the whole of its walk from the release probe.
    //
    // Two displacements are folded together in that error and the flown log cannot separate them:
    // the round's own travel over the ground within the frame, and the frame-newer body sample
    // Ksa/GroundTest.cs differences the lookup against, worth the ground's velocity times dt -- the
    // body's travel, and its spin under the round. This prints the
    // height field at the sampled point and at that point back-dated by the second term, so the two
    // are read apart on KSA's own terrain rather than argued about. docs/KSA-FRAME-ORDER.md section 5
    // is the epoch table; WeaponSystem.AirDensityIntoFrame is the same correction already applied to
    // a neighbouring lookup.
    private static string GroundSample(in Setup setup, Slug round, double3 landingEcl)
    {
        double3 sampledAt = round.GroundSampledAtEcl;
        double step = round.GroundSampledOverSeconds;

        if (!Vec.IsFinite(sampledAt) || sampledAt.Equals(Vec.Zero) || !double.IsFinite(step))
        {
            return "ground sample: the round recorded none";
        }

        // The counterfactual is the lookup un-back-dated, so it goes backwards by the same velocity
        // the round walked it forward with: the body's travel, plus its spin under the round when it
        // asks at its own epoch. Going FORWARD would apply the correction a second time, which is
        // worse than either -- flown, -12.3 m held against -208.5 m doubled.
        double3 walkedBy = round.GroundQueryAtOwnEpoch
            ? KsaWorld.GroundVelocityAt(setup.Parent, sampledAt)
            : setup.Parent.GetVelocityEcl();
        double3 asItUsedTo = sampledAt - walkedBy * step;

        bool read = GroundTest.Shared.TryGround(sampledAt, out double3 _, out double asRead);
        bool paired = GroundTest.Shared.TryGround(asItUsedTo, out double3 _, out double asUnpaired);
        bool truth = GroundTest.Shared.TryGround(landingEcl, out double3 _, out double flown);

        if (!read || !paired || !truth) return "ground sample: the height field would not answer";

        // Both candidate radii against the truth under the landing point, so the log says what the
        // correction would have been worth on this warhead rather than leaving it to be inferred.
        // What `step` IS depends on the round, and the two are different quantities: with
        // ResampleGroundNearImpact -- shipped on -- it is how far back from the frame's end the
        // round crossed, and without it the frame's whole duration. Saying "over a X ms frame" for
        // both reads as the frame rate, which it is not: regressing the walk on it looks like a
        // per-frame test and is actually item 40's crossing-phase residual. ACCURACY-PLAN.md 3dr.
        string over = round.ResampleGroundNearImpact
            ? $"crossing {step * 1000.0:F1} ms back from the frame's end"
            : $"over a {step * 1000.0:F1} ms frame";

        return $"ground sample: {over}, the body moved"
               + $" {Vec.Len(setup.Parent.GetVelocityEcl()) * step:F1} m;"
               + $" the round held {asRead - flown:+0.000;-0.000;0.000} m off the true surface,"
               + $" unpaired it would have held {asUnpaired - flown:+0.000;-0.000;0.000} m";
    }

    // Vector, not magnitude: a bare distance mixes an overshoot with a cross-track error, and which
    // of the two it is decides what to look at next - downrange is energy or timing, cross-track is
    // the plane or the clock. docs/FRAMES-AND-EPOCHS.md, "Measure vectors, not magnitudes".
    private string Walk(in Setup setup, double3 atReleaseEpochCci)
    {
        if (!_haveProbe) return "";

        double metres = Ground(setup, atReleaseEpochCci, _probeGroundCci);

        // Carried to the epoch the axes stand at. Both points are GROUND-FIXED -- each un-carried by
        // its own flight time -- while _probeAlong and _probeCross were taken from the arrival, so
        // resolving the raw separation turns it by the planet's spin over the fall: 1.4 deg at
        // 340 s, a couple of centimetres across for every metre downrange. IcbmComputer.ProbeMissSaid
        // has carried it since it was written; this did not. ACCURACY-PLAN.md 3dw.
        //
        // The two points are un-carried by slightly DIFFERENT times, and that residue stays: a round
        // that stops higher arrives earlier and genuinely lands on ground the planet has turned less
        // far. That is the cross channel's real content, not an artefact to remove.
        double3 separation = setup.Body.CarryCci(atReleaseEpochCci - _probeGroundCci, _probeSeconds);

        // Four decimals, because the print is the endpoint's resolution and the endpoint has moved
        // by two orders of magnitude. Whole metres was wrong when the walk was metres (3ci); two
        // decimals is wrong now it is 21 mm, and it is wrong in a way that hides the question being
        // asked of it -- the increments BETWEEN re-flies are tenths of a millimetre, so at 10 mm
        // they are almost all zero and a rate read off them is quantisation. Measured: whether the
        // walk grows per frame or per simulated second reads r = -0.185 against +0.083 on 399
        // intervals, which is noise either way. ACCURACY-PLAN.md 3dq.
        //
        // Deliberately NOT Distance.Say, which is this repo's general answer to a fixed unit going
        // blind: every reader of this line -- shot-report.py, miss43.py, the scratch scripts --
        // parses the components as bare metres, and a unit that changes with the value silently
        // rescales a night's worth of them. The magnitude carries a unit because it always has.
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

    // Ecl to the parent's inertial frame, both terms from this frame's samples. The subtraction is
    // what removes the ~29.8 km/s carrier exactly rather than approximately, which is the whole
    // reason every ballistic quantity in this mod is in Cci.
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

    // Signed so that a descending arrival is positive, which is every arrival this reports on. It
    // is the whole lever on how much a residual costs on the ground - docs/ARRIVAL-ANGLE.md.
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
        _ => "stopped",
    };
}
