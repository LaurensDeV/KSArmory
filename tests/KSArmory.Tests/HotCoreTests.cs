using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The Real SRB4's hot core: 7-13 g, 0.8 g still at its 0.115 floor. Flown at 500 km with the trim backstop at 2 m/s it
/// closed to 2.4 m/s, drove through, and chased its own line at up to 50 deg/s for minutes (SHORT-RANGE, "A hot core
/// overshot its cutoff"). The rig's stand-in attitude cut off clean either way; this flies KSA's own gimbal law.
/// </summary>
public class HotCoreTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    // Stages from make-rocket.py's "KSA Real SRB4": four boosters, one A2 on three LF3W6HB, one A3 on an LF3W3HB.
    internal static IcbmFlightRig RealSrb4()
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        return new IcbmFlightRig
        {
            Body = rig.Body,
            PositionCci = rig.PositionCci,
            VelocityCci = rig.VelocityCci,
            BoundingSphereRadiusMetres = rig.BoundingSphereRadiusMetres,
            DragAreaM2 = rig.DragAreaM2,
            UnpoweredAttitudeRateDegPerSec = 1.0,
            StartsUnlit = true,
            ReportsStackDeltaV = true,
            CommandLatencyFrames = 1,
            MinThrottle = 0.115,
            ThrottleRatePerSecond = 0.7,
            StepJitter = 0.5,
            AttitudeHasInertia = true,
            Stages =
            [
                new()
                {
                    DryMassKg = 38_400, PropellantKg = 334_300, ThrustNewtons = 6_340_000, ExhaustVelocity = 2_150,
                    VacuumExhaustVelocity = 2_700, Solid = true, BurnoutMassFlowRatio = 1.4,
                    TailOffFraction = 0.17, TailOffEndRatio = 0.15, DragAreaM2 = 150.0,
                },
                new()
                {
                    DryMassKg = 5_600, PropellantKg = 134_800, ThrustNewtons = 6_430_000, ExhaustVelocity = 4_170,
                    DragAreaM2 = 80.0,
                },
                new()
                {
                    DryMassKg = 4_100, PropellantKg = 27_700, ThrustNewtons = 956_000, ExhaustVelocity = 4_700,
                    DragAreaM2 = 15.0,
                },
            ],
        };
    }

    [Trait("kind", "study")]
    [Theory]
    [InlineData(500.0)]
    [InlineData(700.0)]
    public void TheHotCoreUnderKsasOwnLaw(double km)
    {
        foreach (bool ksa in new[] { false, true })
        foreach (bool backstop in new[] { false, true })
        {
            IcbmFlightRig rig = RealSrb4();
            rig.UsesKsaAttitudeLaw = ksa;
            IcbmConfig config = FloorHoldTests.Config(0.5, true, true);
            config.ShortShotBackstopsAtTheTrim = backstop;
            IcbmProgram program = new(config);
            IcbmFlightRig.Flight flight = rig.Fly(program, GameStackShortRangeTests.South(km * 1000.0), 0.02, 6_000.0);

            Out.WriteLine($"{km,4:F0} km, {(ksa ? "KSA law" : "stand-in")}, backstop {(backstop ? "on " : "off")}: "
                          + $"cut off {flight.Reached} at {flight.CutoffSeconds:F1} s, residual {program.ResidualAtCutoff:F2} m/s, "
                          + $"peak rate {rig.PeakClosedLoopRateDegPerSec:F1} deg/s, at the floor {rig.PeakFloorRateDegPerSec:F1}");
        }
    }

    // Under KSA's law the 700 km shot reproduces the flown chase: the backstop off, it cuts off three minutes late after
    // turning at over 60 deg/s; on, as flown since 10-06. The off half is what makes the on half mean anything.
    [Fact]
    public void TheBackstopStopsTheHotCoreChasingItsLine()
    {
        double off = CutoffSeconds(700.0, backstop: false);
        double on = CutoffSeconds(700.0, backstop: true);

        Assert.True(off - on > 60.0, $"without the backstop it cut off at {off:F0} s against {on:F0}: the fixture no longer chases");
        Assert.True(on < 300.0, $"with the backstop it still cut off at {on:F0} s");
    }

    private static double CutoffSeconds(double km, bool backstop)
    {
        IcbmFlightRig rig = RealSrb4();
        rig.UsesKsaAttitudeLaw = true;
        IcbmConfig config = FloorHoldTests.Config(0.5, true, true);
        config.ShortShotBackstopsAtTheTrim = backstop;
        IcbmFlightRig.Flight flight = rig.Fly(new IcbmProgram(config), GameStackShortRangeTests.South(km * 1000.0), 0.02, 6_000.0);
        Assert.True(flight.Reached, $"never cut off: {flight.Hold}");
        return flight.CutoffSeconds;
    }
}
