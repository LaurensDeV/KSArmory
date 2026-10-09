using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The cutoff residual against the frame step, off the orbit plane and in the regime the long shot
/// cuts off in: a near-dry stage at about 113 m/s², down to about a tenth of its throttle, where the
/// 5 m/s cap on the steering freeze binds. Flown, a 22% longer step nearly doubled the residual
/// (ACCURACY-PLAN 3fl), and <see cref="CutoffResidualTests"/>' stage cuts off at 7 m/s², where the cap
/// never binds.
/// </summary>
public class CutoffStepStudy(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 At(double lat, double lon)
        => new(R * Math.Cos(lat) * Math.Cos(lon), R * Math.Cos(lat) * Math.Sin(lon), R * Math.Sin(lat));

    // 1.42 MN on 2.6 t dry ends the burn near the flown 113 m/s², with about 10 t of propellant left.
    private static IcbmFlightRig Rig(double dryKg, int latency, double jitter, double servo)
    {
        double3 position = new(R + 300_000.0, 0, 0);

        return new IcbmFlightRig
        {
            Body = Earth,
            PositionCci = position,
            VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = dryKg, PropellantKg = 40_000, ThrustNewtons = 1_420_000, ExhaustVelocity = 3_100 }],
            CommandLatencyFrames = latency,
            ThrottleRatePerSecond = servo,
            MinThrottle = 0.12,
            StepJitter = jitter,
        };
    }

    private static IcbmProgram Armed()
        => new(new IcbmConfig { Armed = true, ArrivalPreference = FixtureGeometry.ArrivalPreference, MinArrivalAngleDeg = 0.0 });

    [Fact]
    [Trait("kind", "study")]
    public void TheResidualAgainstTheStepWhereTheFreezeIsCapped()
    {
        foreach ((string name, double dry, int latency, double jitter, double servo) in
                 (ReadOnlySpan<(string, double, int, double, double)>)
                 [
                     ("the game's actuator", 2_600, 1, 0.5, 2.0),
                     ("  ...no jitter", 2_600, 1, 0.0, 2.0),
                     ("  ...no latency", 2_600, 0, 0.5, 2.0),
                     ("  ...instant servo", 2_600, 1, 0.5, double.PositiveInfinity),
                 ])
        {
            Out.WriteLine($"\n{name}");
            Out.WriteLine("   step   resid    along   square  throttle   accel   frame  holdBelow");

            foreach (double step in (double[])[0.0167, 0.0233, 0.0280, 0.0400])
            {
                List<double> resid = [], along = [], square = [], thr = [], acc = [], frame = [], hold = [];

                foreach (double lon in (double[])[0.7, 1.0, 1.3, 1.6])
                {
                    IcbmFlightRig rig = Rig(dry, latency, jitter, servo);
                    IcbmProgram program = Armed();
                    IcbmFlightRig.Flight flight = rig.Fly(program, At(0.45, lon), step, 6_000.0);
                    Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

                    double a = Vec.Dot(program.ResidualVectorCci, flight.CoastDirectionCci);
                    resid.Add(program.ResidualAtCutoff);
                    along.Add(Math.Abs(a));
                    square.Add(Vec.Len(program.ResidualVectorCci - flight.CoastDirectionCci * a));
                    thr.Add(program.ThrottleAtCutoff);
                    acc.Add(program.AccelerationAtCutoff);
                    frame.Add(program.AccelerationAtCutoff * program.StepAtCutoff * program.ThrottleAtCutoff);
                    hold.Add(program.HoldDirectionBelowNow);
                }

                Out.WriteLine($"   {step * 1000,4:F1} {Med(resid),7:F4} {Med(along),8:F4} {Med(square),8:F4} "
                              + $"{Med(thr),8:P0} {Med(acc),7:F1} {Med(frame),7:F4} {Med(hold),9:F3}");
            }
        }
    }

    private static double Med(List<double> v)
    {
        List<double> s = [.. v.Where(double.IsFinite).OrderBy(x => x)];
        return s.Count == 0 ? double.NaN : s.Count % 2 == 1 ? s[s.Count / 2] : 0.5 * (s[s.Count / 2 - 1] + s[s.Count / 2]);
    }
}
