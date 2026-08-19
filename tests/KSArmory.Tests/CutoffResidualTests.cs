using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What is left ungained when the engines stop, and what it costs.
///
/// <para>The rig used to honour a throttle command exactly and act on it the same frame, so it
/// measured residuals of a few thousandths of a metre a second while the game produced two. Both
/// of those lies are what hid this: KSA copies control inputs in <c>PrepareWorker</c>, which runs
/// before this mod's hook, so a cutoff lands a frame late, and a real throttle servo takes time to
/// come down. <see cref="IcbmFlightRig.CommandLatencyFrames"/> and
/// <see cref="IcbmFlightRig.ThrottleRatePerSecond"/> are those two facts, off by default so every
/// other suite keeps its old meaning.</para>
///
/// <para>The residual matters out of proportion to its size on a deorbit: the arc grazes for
/// thousands of kilometres, so a metre a second left along the track is about 1.8 km of miss and
/// the same metre left radially is about 3.4 km.</para>
/// </summary>
public class CutoffResidualTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 At(double lat, double lon)
        => new(R * Math.Cos(lat) * Math.Cos(lon), R * Math.Cos(lat) * Math.Sin(lon), R * Math.Sin(lat));

    /// <summary>A bus in a 300 km circular orbit, with an actuator that behaves like the game's.</summary>
    private static IcbmFlightRig InOrbit(double throttleRate = 2.0)
    {
        double3 position = new(R + 300_000.0, 0, 0);

        return new IcbmFlightRig
        {
            Body = Earth,
            PositionCci = position,
            VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = throttleRate,
        };
    }

    private static IcbmProgram Armed() => new(new IcbmConfig { Armed = true });

    [Fact]
    public void TheBurnStopsWithLittleLeftToGain()
    {
        IcbmFlightRig rig = InOrbit(throttleRate: 0.0);
        double3 aim = At(0.0, 1.2);

        IcbmProgram program = Armed();
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

        double residual = program.ResidualAtCutoff;
        Out.WriteLine($"residual at cutoff {residual:F3} m/s");
        Out.WriteLine($"  worth roughly {residual * 1.8:F1} km along track, {residual * 3.4:F1} km radial");

        // Counting the burn down along the line actually being thrust rather than along the length
        // of what is left keeps this near the frame quantum. Counting it down by the length
        // overshoots until the backstop catches it, which it only does a whole metre a second late.
        Assert.True(residual < 0.5,
                    $"the burn ended {residual:F2} m/s short, which is kilometres of miss here");
    }

    /// <summary>
    /// And the shot still arrives, so the tighter cutoff is not bought by stopping early.
    /// </summary>
    [Fact]
    public void AndTheShotStillArrives()
    {
        IcbmFlightRig rig = InOrbit();
        double3 aim = At(0.0, 1.2);

        IcbmProgram program = Armed();
        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");
        Assert.True(ImpactPredictor.TryPredict(Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci,
                                               2.0, 20_000.0, out ImpactPredictor.Impact hit),
                    "it never came down");

        double miss = R * Vec.AngleBetween(hit.GroundFixedPointCci,
                                           Earth.CarryCci(aim, flight.CutoffSeconds));

        Out.WriteLine($"miss {miss / 1000.0:F2} km with a one-frame-late cutoff and a real throttle servo");
        Assert.True(miss < 6_000.0, $"it landed {miss / 1000.0:F1} km from the aim");
    }
}
