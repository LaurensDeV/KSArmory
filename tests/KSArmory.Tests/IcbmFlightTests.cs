using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The whole thing, flown: pad to burnout to impact, against a vehicle the guidance was never told
/// about and a flight model it does not share.
///
/// <para>These are the tests that mean anything. Every intermediate quantity in the loop can be
/// wrong in a way that still produces a smooth-looking ascent, and the only symptom is a warhead in
/// the wrong ocean — so the assertion is always the miss distance, never the shape of the
/// trajectory.</para>
/// </summary>
public class IcbmFlightTests
{
    private const double EarthMu = 3.986004418e14;
    private const double EarthRadius = 6_371_000.0;
    private const double EarthSpin = 7.2921159e-5;

    private static BallisticBody Earth => new(EarthMu, EarthRadius, new double3(0, 0, 1), EarthSpin);

    private static double3 Equator(double longitudeRad, double altitude = 0.0)
        => new((EarthRadius + altitude) * Math.Cos(longitudeRad),
               (EarthRadius + altitude) * Math.Sin(longitudeRad), 0.0);

    private static double3 At(double latRad, double lonRad, double altitude = 0.0)
    {
        double r = EarthRadius + altitude;
        return new(r * Math.Cos(latRad) * Math.Cos(lonRad),
                   r * Math.Cos(latRad) * Math.Sin(lonRad),
                   r * Math.Sin(latRad));
    }

    /// <summary>A two-stage stack with about 7 km/s in it — an ICBM's worth.</summary>
    private static IcbmFlightRig Rig(double3 padCci, BallisticBody body)
        => new()
        {
            Body = body,
            PositionCci = padCci,
            VelocityCci = body.GroundVelocityCci(padCci),
            Stages =
            [
                new() { DryMassKg = 4_000, PropellantKg = 46_000, ThrustNewtons = 1_400_000, ExhaustVelocity = 2_600 },
                new() { DryMassKg = 1_200, PropellantKg = 12_000, ThrustNewtons = 260_000, ExhaustVelocity = 3_000 },
            ],
        };

    private static double MissMetres(IcbmFlightRig rig, in IcbmFlightRig.Flight flight, double3 aimAtEpoch)
    {
        Assert.True(ImpactPredictor.TryPredict(rig.Body, flight.CutoffPositionCci, flight.CutoffVelocityCci,
                                               2.0, 12_000.0, out ImpactPredictor.Impact hit),
                    "the vehicle never came down");

        // The predictor's frame is the inertial one at cutoff, so the target has to be carried to
        // the same instant before the two can be compared at all.
        double3 targetAtCutoff = rig.Body.CarryCci(aimAtEpoch, flight.CutoffSeconds);
        return EarthRadius * Vec.AngleBetween(hit.GroundFixedPointCci, targetAtCutoff);
    }

    [Fact]
    public void ItFliesItselfFromThePadToWithinAFewKilometresOfATargetFiveThousandKilometresAway()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 500.0, $"missed by {miss:F0} m after a {flight.CutoffSeconds:F0} s boost");
    }

    /// <summary>
    /// Off the equator and across latitudes, which is where a solve that got the plane right by
    /// accident stops working.
    /// </summary>
    [Fact]
    public void ItReachesATargetOnADifferentLatitudeAndLongitude()
    {
        double3 pad = At(0.75, -1.8);
        double3 aim = At(0.95, 0.65);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 500.0, $"missed by {miss:F0} m");
    }

    /// <summary>
    /// The claim that justifies the whole design: the loop re-solves against the vehicle's real
    /// state, so a vehicle that flies worse than the model still arrives. Halving the attitude rate
    /// and quadrupling the drag changes the ascent completely and must not change where it lands.
    /// </summary>
    [Fact]
    public void AVehicleThatFliesNothingLikeTheModelStillArrives()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        rig.AttitudeRateDegPerSec = 5.0;
        rig.DragAreaOverMass = 1.6e-4;

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 500.0, $"missed by {miss:F0} m");
    }

    [Fact]
    public void TheStackIsNeverFlownAcrossItsOwnAirflowWhileThereIsAirToDoItIn()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(1.1);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmConfig config = new() { Armed = true, MaxAngleOfAttackDeg = 8.0 };
        IcbmProgram program = new(config);

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached);
        Assert.True(flight.PeakAngleOfAttackDeg < 25.0,
                    $"flew at {flight.PeakAngleOfAttackDeg:F1} deg of attack under load");
    }

    [Fact]
    public void ItStagesWhenTheRunningStageIsEmpty()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true, AutoStage = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached);
        Assert.True(rig.StageIndex >= 1, "the first stage should have been dropped");
    }

    /// <summary>
    /// A rocket on a pad has nothing lit, and firing the next sequence is the only thing that
    /// changes that. The computer used to refuse to ask for one until something had already pushed
    /// — which is a launch that can never happen: it held the vertical rise on the attitude
    /// thrusters, saw no propellant for four seconds, and reported a burn that ended short of a
    /// solution it had never begun.
    /// </summary>
    [Fact]
    public void ItLightsTheFirstEngineItselfFromAColdPad()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        rig.StartsUnlit = true;

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");
        Assert.DoesNotContain("never lit", flight.Hold);

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 500.0, $"missed by {miss:F0} m after a {flight.CutoffSeconds:F0} s boost");
    }

    /// <summary>
    /// And a stack that never lit says so. It is the same reading as a burn that ran out of
    /// propellant — the whole velocity still to gain — and the two want completely different things
    /// done about them.
    /// </summary>
    [Fact]
    public void AColdPadWithAutomaticStagingOffSaysNothingEverLit()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        rig.StartsUnlit = true;

        IcbmProgram program = new(new IcbmConfig { Armed = true, AutoStage = false });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.Equal(IcbmPhase.Coast, flight.FinalPhase);
        Assert.Contains("never lit", flight.Hold);
        Assert.DoesNotContain("short of the solution", flight.Hold);
    }

    /// <summary>
    /// The stack is held under the limit the <em>engine</em> will destroy it at, with nobody having
    /// typed a number. KSA computes that limit from the vehicle's own bounding sphere, so it is not
    /// something an operator can be expected to know about somebody else's rocket — and this stack
    /// reaches 8.3 g at first-stage burnout without one, which is an ordinary thing for a stack to
    /// do as it empties.
    /// </summary>
    [Fact]
    public void ItHoldsTheStackUnderTheLimitTheAirframeActuallyHas()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        rig.BoundingSphereRadiusMetres = 41.67;   // KSA holds a stack this long to 6.0 g

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1800.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");
        Assert.True(flight.PeakThrustGee <= 6.0,
                    $"pulled {flight.PeakThrustGee:F1} g against a 6.0 g airframe");
    }

    /// <summary>
    /// Uncapped, the same stack tears itself apart. Without this the test above passes against a
    /// guidance that never throttles, because nothing else in the suite ever asks what the stack
    /// was pulling.
    /// </summary>
    [Fact]
    public void TheSameStackWithNoLimitAtAllPullsFarMoreThanAnAirframeSurvives()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);

        Assert.True(flight.Reached);
        Assert.True(flight.PeakThrustGee > 8.0,
                    $"only reached {flight.PeakThrustGee:F1} g, so the cap above proves nothing");
    }

    /// <summary>
    /// <b>A known bound, pinned as a measurement rather than fixed.</b> The cap is right and the
    /// actuator is too slow: KSA's throttle is a servo moving at 0.7 a second, so a stage that
    /// lights <em>hot</em> — a big booster dropped and a punchy upper left on a much lighter stack —
    /// spends about a second over the limit while the throttle walks down, and the engine's filtered
    /// load has a time constant of a fifth of that.
    ///
    /// <para>Both arms fly the identical stack against the identical cap, and only the throttle rate
    /// differs: 5.79 g of a 6.00 g limit with an instant throttle, 7.44 g and destroyed with KSA's
    /// own. That is what says the remedy is an instrument faster than the throttle rather than a
    /// tighter margin — the only one in the game is the engine switch, and whether cutting it makes
    /// the stack read <em>dry</em> is a flight question. <c>docs/ICBM-GUIDANCE.md</c> has it.</para>
    /// </summary>
    [Fact]
    public void AStageThatLightsHotIsLostToTheThrottleServoAndNotToTheCap()
    {
        Assert.False(HotStagedFlight(double.PositiveInfinity).BrokeUp);

        IcbmFlightRig.Flight slewed = HotStagedFlight(KsaThrottleRatePerSecond);
        Assert.True(slewed.BrokeUp,
                    $"peaked at {slewed.PeakFilteredGee:F2} g, so the servo no longer costs the stack");
    }

    /// <summary>The rate KSA's own throttle control moves at, per second of player time.</summary>
    private const double KsaThrottleRatePerSecond = 0.7;

    // A light upper stage on a motor big enough to pull 15 g the instant it lights, which is what
    // separates a stage transition from a burn that simply gets lighter as it goes.
    private static IcbmFlightRig.Flight HotStagedFlight(double throttleRate)
    {
        double3 pad = Equator(0.0);

        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            BoundingSphereRadiusMetres = 41.67,
            ThrottleRatePerSecond = throttleRate,
            Stages =
            [
                new() { DryMassKg = 4_000, PropellantKg = 46_000, ThrustNewtons = 1_400_000, ExhaustVelocity = 2_600 },
                new() { DryMassKg = 1_200, PropellantKg = 12_000, ThrustNewtons = 1_940_000, ExhaustVelocity = 3_000 },
            ],
        };

        return rig.Fly(new IcbmProgram(new IcbmConfig { Armed = true }), Equator(0.7848), 0.02, 1800.0);
    }

    /// <summary>
    /// Two limits saying different things: the operator's is about this shot, the airframe's is what
    /// the engine destroys it at. The tighter one wins, in both directions, and a missing airframe
    /// reading is absent rather than unlimited.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(8.0, 0.0, 8.0)]
    [InlineData(0.0, 10.0, 9.0)]
    [InlineData(4.0, 10.0, 4.0)]
    [InlineData(12.0, 10.0, 9.0)]
    public void TheAccelerationCapIsTheTighterOfWhatWasAskedAndWhatTheAirframeHas(
        double asked, double airframe, double expected)
    {
        double3 pad = Equator(0.0);
        IcbmFlightRig rig = Rig(pad, Earth);

        IcbmProgram program = new(new IcbmConfig { Armed = true, MaxAccelerationGee = (float)asked });

        IcbmState state = new(Earth, rig.PositionCci, rig.VelocityCci, Equator(0.7848), HasAim: true,
                              rig.Performance(), 1.0, PropellantAvailable: true,
                              StructuralLimitGee: airframe);

        Assert.Equal(expected, program.AccelerationCapGee(state), 6);
    }

    /// <summary>
    /// A stack short of the delta-v is <em>not</em> refused before launch, and that is deliberate.
    /// How much a vehicle has left is only knowable one stage at a time — KSA reports the running
    /// stage's engines, not the stack's — so a launch gate built on it would turn away every
    /// multi-stage rocket in the game. It flies, it falls short, and it says by how much, which is
    /// a thing the player can act on.
    /// </summary>
    [Fact]
    public void AStackShortOfTheDeltaVFliesAndSaysHowShortItEnded()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(2.2);

        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            Stages = [new() { DryMassKg = 6_000, PropellantKg = 18_000, ThrustNewtons = 600_000, ExhaustVelocity = 2_600 }],
        };

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1200.0);

        Assert.Contains("short of the solution", flight.Hold);
        Assert.Equal(IcbmPhase.Coast, flight.FinalPhase);
    }

    [Fact]
    public void AnUnarmedComputerLightsNothing()
    {
        double3 pad = Equator(0.0);
        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = false });

        IcbmState state = new(Earth, rig.PositionCci, rig.VelocityCci, Equator(0.7848), HasAim: true,
                              rig.Performance(), 1.0, PropellantAvailable: true);

        IcbmCommand command = program.Update(0.1, state);

        Assert.False(command.EngineOn);
        Assert.Equal(IcbmPhase.Idle, command.Phase);
        Assert.Equal("not armed", command.Hold);
    }

    [Fact]
    public void WithNoTargetItHoldsRatherThanPickingOne()
    {
        double3 pad = Equator(0.0);
        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmState state = new(Earth, rig.PositionCci, rig.VelocityCci, Vec.Zero, HasAim: false,
                              rig.Performance(), 1.0, PropellantAvailable: true);

        IcbmCommand command = program.Update(0.1, state);

        Assert.False(command.EngineOn);
        Assert.Equal("no target designated", command.Hold);
    }


    /// <summary>
    /// A lofted shot has to be as accurate as the cheapest one, and the way it stops being is
    /// invisible in every other reading. Once committed, guidance follows the cheapest arc from
    /// wherever the vehicle currently is — which is the arc the vehicle is already on. Multiplying
    /// that by a loft factor every cycle walks the answer outward, and the shot chases a trajectory
    /// that runs away from it.
    /// </summary>
    [Theory]
    [InlineData(0.85)]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.4)]
    public void EveryLoftArrivesOnTheTarget(double loft)
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true, Loft = loft });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1200.0);
        Assert.True(flight.Reached, $"loft {loft}: {flight.Hold}");

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 500.0, $"loft {loft} missed by {miss:F0} m");
        Assert.True(flight.PropellantLeftKg > 0.0, $"loft {loft} burnt the stack dry");
    }

    /// <summary>
    /// The accuracy must not come from the step. A cutoff timed against the frame is the one part
    /// of this that genuinely depends on how often it is asked, and if the whole answer moves with
    /// the step then something else is being fitted to it.
    /// </summary>
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    [InlineData(0.02)]
    public void TheShotIsAsGoodAtAnyFrameRate(double step)
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, step, 1200.0);
        Assert.True(flight.Reached);

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss < 500.0, $"at a {step * 1000.0:F0} ms step it missed by {miss:F0} m");
    }

    /// <summary>
    /// A burn that ends because the tanks did is not the same as one that ends because the shot is
    /// complete, and from outside they look identical: engines off, vehicle coasting, warheads
    /// aboard. Releasing on the second is the shot; releasing on the first scatters them over
    /// whatever is short of the target.
    /// </summary>
    [Fact]
    public void AShotThatRunsOutOfPropellantSaysSoAndHoldsItsWarheads()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(1.9);

        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 30_000, ThrustNewtons = 900_000, ExhaustVelocity = 2_700 }],
        };

        IcbmProgram program = new(new IcbmConfig { Armed = true, DeployAltitudeMetres = 1000.0 });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1200.0);

        Assert.Contains("short of the solution", flight.Hold);

        IcbmState after = new(Earth, rig.PositionCci, rig.VelocityCci, Earth.CarryCci(aim, flight.CutoffSeconds),
                              HasAim: true, rig.Performance(), 0.0, PropellantAvailable: false);
        Assert.False(program.Update(0.02, after).ReadyToDeploy,
                     "a trajectory known to fall short must not release warheads");
    }

    /// <summary>
    /// It has to work somewhere that is not Earth. An airless body hands straight from the vertical
    /// rise to closed-loop guidance, because there is no dynamic pressure to wait for — and the
    /// horizon floor is the only thing then stopping it steering into the ground at 250 m.
    /// </summary>
    [Fact]
    public void ItFliesOnAnAirlessBodyWhereThereIsNoPitchProgrammeToSpeakOf()
    {
        BallisticBody moon = new(4.9048695e12, 1_737_400.0, new double3(0, 0, 1), 2.6617e-6);

        double3 pad = new(moon.SurfaceRadius, 0, 0);
        double3 aim = new(moon.SurfaceRadius * Math.Cos(0.6), moon.SurfaceRadius * Math.Sin(0.6), 0);

        IcbmFlightRig rig = new()
        {
            Body = moon,
            PositionCci = pad,
            VelocityCci = moon.GroundVelocityCci(pad),
            ScaleHeightMetres = 0.0,
            DragAreaOverMass = 0.0,
            Stages = [new() { DryMassKg = 1_500, PropellantKg = 6_000, ThrustNewtons = 45_000, ExhaustVelocity = 3_000 }],
        };

        IcbmProgram program = new(new IcbmConfig { Armed = true, TurnEndMetres = 20_000.0 });
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 2000.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");
        Assert.True(moon.AltitudeOf(flight.CutoffPositionCci) > 0.0, "it flew into the ground");

        Assert.True(ImpactPredictor.TryPredict(moon, flight.CutoffPositionCci, flight.CutoffVelocityCci,
                                               2.0, 20_000.0, out ImpactPredictor.Impact hit));
        double miss = moon.SurfaceRadius
                    * Vec.AngleBetween(hit.GroundFixedPointCci, moon.CarryCci(aim, flight.CutoffSeconds));
        Assert.True(miss < 500.0, $"missed by {miss:F0} m on the Moon");
    }

    /// <summary>
    /// The arc round the far side exists in the solver and is not offered as a setting. Pinned
    /// because it is the reason the transfer solver is written for two families of solution rather
    /// than one: a solver told there is only the near side fails at the boundary between them.
    /// </summary>
    [Fact]
    public void TheSolverCanTakeTheArcRoundTheFarSideAndItCostsFarMore()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.35);
        double3 frame = Earth.GroundVelocityCci(pad);

        Assert.True(BallisticArc.TryCheapest(Earth, pad, frame, aim, out BallisticArc.Solution direct));
        Assert.True(BallisticArc.TryCheapest(Earth, pad, frame, aim, out BallisticArc.Solution around,
                                             longWay: true));

        Assert.True(Vec.Len(around.VelocityToGain(frame)) > Vec.Len(direct.VelocityToGain(frame)),
                    "going the long way round has to cost more than going straight there");

        // Both arcs are real trajectories that arrive; the far-side one simply costs orbital-grade
        // delta-v to fly, which is why nothing offers it as a choice.
        Assert.True(around.LowestRadius >= EarthRadius - 1.0);
        Assert.True(around.FlightSeconds > direct.FlightSeconds);
    }


    /// <summary>
    /// Why the world has to be held down for a burn, stated as the failure it prevents.
    ///
    /// <para>An engine can only be shut down on a frame boundary, so the velocity left at cutoff is
    /// whatever the last step added. At a step of a few seconds that is tens of metres a second and
    /// the shot lands in the wrong country; at the 170-second steps high timewarp hands out it is
    /// kilometres a second. <see cref="IcbmProgram.MaxFaithfulStep"/> is what asks
    /// <see cref="WarpPolicy"/> to keep it short, and this is the test that fails if that number is
    /// ever loosened on the grounds that the guidance "seems fine".</para>
    ///
    /// <para>For calibration, the degradation is smooth rather than a cliff: a one-second step
    /// already costs about 1.5 km, which is why the limit is set two orders of magnitude below
    /// that rather than at the point where it becomes obvious.</para>
    /// </summary>
    [Theory]
    [InlineData(5.0)]
    [InlineData(20.0)]
    public void AtAStepTooLongToCutOffOnItMissesBadly(double step)
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, step, 1200.0);
        Assert.True(flight.Reached);

        double miss = MissMetres(rig, flight, aim);
        Assert.True(miss > 20_000.0,
                    $"a {step:F0} s step should wreck the cutoff; it only missed by {miss:F0} m");
        Assert.True(step > IcbmProgram.MaxFaithfulStep,
                    "and the policy must consider a step this long unflyable");
    }


    /// <summary>
    /// The bus must not spin at cutoff, and the reason it wants to is arithmetic rather than
    /// control. Velocity still to gain is a <em>difference</em>, so as it closes on zero its
    /// direction is the difference of two nearly equal vectors and swings wildly — measured in
    /// flight at 161 degrees between consecutive samples, right at the cutoff instant.
    ///
    /// <para>That is the exact moment the vehicle should be holding still, because the warheads
    /// leave along the line it was cut off on.</para>
    /// </summary>
    [Fact]
    public void TheAttitudeAtCutoffIsHeldRatherThanChasingAVanishingDifference()
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.7848);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1200.0);
        Assert.True(flight.Reached);

        Assert.False(flight.LastBurnDirectionCci.Equals(Vec.Zero), "nothing was ever commanded");
        Assert.False(flight.CoastDirectionCci.Equals(Vec.Zero), "the coast commands nothing at all");

        double swing = Vec.AngleBetween(flight.LastBurnDirectionCci, flight.CoastDirectionCci)
                       * 180.0 / Math.PI;

        Assert.True(swing < 5.0,
                    $"the attitude swung {swing:F0} deg between the burn and the coast");
    }


    /// <summary>
    /// What is left to gain when the engines stop, which is the whole story of a shot that lands
    /// short on an otherwise perfect trajectory.
    ///
    /// <para>A salvo that lands short as a tight group has spent too little velocity, and the
    /// amount is small: at about a kilometre of range per metre a second, forty of them is fifty
    /// kilometres. So the burn ending a few tens of metres a second early and the burn ending
    /// perfectly look identical from anywhere except this number, which is why it is asserted
    /// rather than watched.</para>
    /// </summary>
    [Theory]
    [InlineData(0.7848)]
    [InlineData(1.2)]
    [InlineData(0.405)]
    public void TheBurnEndsWithAlmostNothingLeftToGain(double downrangeRadians)
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(downrangeRadians);

        IcbmFlightRig rig = Rig(pad, Earth);
        IcbmProgram program = new(new IcbmConfig { Armed = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1200.0);
        Assert.True(flight.Reached, flight.Hold);

        Assert.True(double.IsFinite(program.ResidualAtCutoff),
                    "the residual at cutoff has to be recorded, not cleared and reported as zero");

        Assert.True(program.ResidualAtCutoff < 1.0,
                    $"cut off {program.ResidualAtCutoff:F1} m/s short, which is kilometres at the far end");
    }
}
