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

    /// <summary>Everything the game's actuator does: a late command, a servo, a floor, an uneven step.</summary>
    private static IcbmFlightRig LikeTheGame()
    {
        IcbmFlightRig rig = InOrbit();
        rig.MinThrottle = 0.12;
        rig.StepJitter = 0.5;
        return rig;
    }

    /// <summary>
    /// The residual split about the line the burn ended on, which is the only line thrust could
    /// still have removed anything along.
    /// </summary>
    private static (double Along, double Perp) SplitAboutTheThrustLine(
        IcbmProgram program, in IcbmFlightRig.Flight flight)
    {
        double along = Vec.Dot(program.ResidualVectorCci, flight.CoastDirectionCci);
        return (along, Vec.Len(program.ResidualVectorCci - flight.CoastDirectionCci * along));
    }

    private static double FrameAtCutoff(IcbmProgram program)
        => program.AccelerationAtCutoff * program.StepAtCutoff * program.ThrottleAtCutoff;

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

    /// <summary>
    /// What is left at cutoff has to lie <em>along</em> the line the burn ended on, because that is
    /// the only line thrust could still have removed it from. Anything square to it arrived after
    /// the direction stopped being updated, and no amount of further burning would have touched it.
    ///
    /// <para>Holding the direction below a fixed five metres a second freezes it seconds early on a
    /// throttled-down burn, and the residual then comes out 90-99% square to the thrust line and
    /// ten to forty times the frame. <b>A constant step cannot see it</b>:
    /// <see cref="IcbmFlightRig.StepJitter"/> is what makes the solve move between frames.</para>
    /// </summary>
    [Fact]
    public void WhatIsLeftAtCutoffLiesAlongTheLineTheBurnEndedOn()
    {
        IcbmFlightRig rig = LikeTheGame();

        IcbmProgram program = Armed();
        IcbmFlightRig.Flight flight = rig.Fly(program, At(0.0, 1.2), 0.02, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

        (double along, double perp) = SplitAboutTheThrustLine(program, flight);

        Out.WriteLine($"residual {program.ResidualAtCutoff:F4} m/s = {along:F4} along, {perp:F4} square");
        Out.WriteLine($"  one frame is {FrameAtCutoff(program):F4} m/s at {program.ThrottleAtCutoff:P0} throttle");

        Assert.True(perp < 0.5 * program.ResidualAtCutoff,
                    $"{perp:F4} of a {program.ResidualAtCutoff:F4} m/s residual is square to the thrust "
                    + "line, so the direction was held while the answer was still moving");
    }

    /// <summary>
    /// And the whole residual stays near the frame quantum, which is the only floor the cutoff is
    /// supposed to have. Several times it means something other than timing is setting it.
    /// </summary>
    [Theory]
    [InlineData(0.7)]
    [InlineData(1.0)]
    [InlineData(1.3)]
    [InlineData(1.6)]
    public void TheResidualStaysNearTheFrameItCannotBeat(double lon)
    {
        IcbmFlightRig rig = LikeTheGame();

        IcbmProgram program = Armed();
        IcbmFlightRig.Flight flight = rig.Fly(program, At(0.0, lon), 0.02, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

        double frame = FrameAtCutoff(program);
        double ratio = program.ResidualAtCutoff / frame;

        Out.WriteLine($"lon {lon:F1}: residual {program.ResidualAtCutoff:F4} m/s, "
                    + $"{ratio:F1}x the {frame:F4} m/s frame");

        Assert.True(ratio < 4.0,
                    $"the burn ended {program.ResidualAtCutoff:F4} m/s short, {ratio:F1} times what one "
                    + "frame adds - a bias rather than a rounding");
    }
}
