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
}
