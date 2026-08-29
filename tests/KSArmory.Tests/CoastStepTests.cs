using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the step the coast is integrated at is worth, on its own.
///
/// <para><b>It is not what separates a good session from a bad one.</b> Two nights flown thirty
/// hours apart integrated their coasts at 66 ms and 108 ms and landed 9.69 km and 25.97 km apart —
/// and the step alone moves the impact by <b>330 m</b> across that range, 2% of the difference. So
/// the coarser step does not cost accuracy by integrating badly. It costs it by starving the
/// post-boost correction, which ran 1.4 passes a flight in one regime and 0.23 in the other.
/// <c>docs/MIRV-NEXT.md</c> <b>8ac</b>.</para>
///
/// <para>That matters because it says where to look and where not to. Shortening the step is not
/// the fix, and neither is anything about the integrator.</para>
/// </summary>
public class CoastStepTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;
    private const double EarthSpin = 7.2921159e-5;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    private static double DensityAt(double3 p)
        => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / ScaleHeight);

    private static double3 Downrange(double m) => new(R * Math.Cos(m / R), R * Math.Sin(m / R), 0);

    /// <summary>Where the warheads come down, flown from the cutoff the rig reached.</summary>
    private static double ImpactMetres(double shotMetres, double coastStep)
    {
        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = new double3(R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = 2.0,
            MinThrottle = 0.12,
            StepJitter = 0.5,
            CoastStepSeconds = coastStep,
        };

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        double3 aim = Downrange(shotMetres);
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

        Assert.True(flight.Reached);
        Assert.True(ImpactPredictor.TryPredict(
            Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
            ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact impact, null, null,
            new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21)));

        return R * Vec.AngleBetween(impact.GroundFixedPointCci,
                                    Earth.CarryCci(aim, flight.CutoffSeconds + impact.Seconds));
    }

    /// <summary>
    /// The two regimes are 66 ms and 108 ms. The integrator cannot tell them apart.
    /// </summary>
    [Theory]
    [InlineData(3_459_000.0)]
    [InlineData(12_902_000.0)]
    public void TheStepDoesNotExplainTheDifferenceBetweenTheRegimes(double shotMetres)
    {
        double fast = ImpactMetres(shotMetres, 0.066);
        double slow = ImpactMetres(shotMetres, 0.108);

        double moved = Math.Abs(slow - fast);
        Out.WriteLine($"{shotMetres / 1000:F0} km: 66 ms vs 108 ms moves the impact {moved:F0} m");

        // The regimes differ by 16.3 km of flown miss. Anything this small is not the cause, and
        // the bound is loose on purpose -- it is a statement about orders, not a calibration.
        Assert.True(moved < 1_000.0, $"the step moved the impact {moved:F0} m");
    }

    /// <summary>
    /// It is not that the step does nothing — over a range nothing flies at, it does plenty. That
    /// is what makes the 330 m above a fact about the regimes rather than about the integrator.
    /// </summary>
    [Fact]
    public void ACoarseEnoughStepDoesMoveTheImpact()
    {
        double fine = ImpactMetres(12_902_000.0, 0.033);
        double coarse = ImpactMetres(12_902_000.0, 2.0);

        double moved = Math.Abs(coarse - fine);
        Out.WriteLine($"33 ms vs 2 s moves the impact {moved / 1000.0:F1} km");

        Assert.True(moved > 10_000.0);
    }
}
