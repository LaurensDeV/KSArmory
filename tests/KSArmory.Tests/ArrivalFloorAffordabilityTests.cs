using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The misses here are large and mean nothing: no aim correction is wired, so every shot flies its
/// raw arc. What is read is the reach and the arrival angle.
/// </summary>
public class ArrivalFloorAffordabilityTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);
    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / ScaleHeight);
    private static double3 Downrange(double m) => new(R * Math.Cos(m / R), R * Math.Sin(m / R), 0);

    [Theory]
    [InlineData(12_902_000.0)]
    [InlineData(8_500_000.0)]
    [InlineData(2_736_000.0)]
    public void EveryFloorWorthFlyingIsReachable(double shotMetres)
    {
        Out.WriteLine($"--- {shotMetres / 1000:F0} km ---");
        Out.WriteLine($"{"floor",8}{"reach",16}{"arrival",10}{"miss km",10}");

        foreach (double floor in new[] { 0.0, 10.0, 15.0, 18.0, 20.0, 25.0, 30.0 })
        {
            IcbmFlightRig rig = new()
            {
                Body = Earth,
                PositionCci = new double3(R + 400_000.0, 0, 0),
                VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 400_000.0)), 0),
                Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
                CommandLatencyFrames = 1,
                ThrottleRatePerSecond = 2.0,
                MinThrottle = 0.12,
                StepJitter = 0.5,
            };

            IcbmProgram program = new(new IcbmConfig { Armed = true, MinArrivalAngleDeg = floor });
            double3 aim = Downrange(shotMetres);
            IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

            string arrival = "-", miss = "-";

            if (flight.Reached && ImpactPredictor.TryPredict(
                    Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
                    ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit, null, null,
                    new ImpactPredictor.Drag(DensityAt, BuiltIns.ReentryVehicleMk21)))
            {
                miss = $"{R * Vec.AngleBetween(hit.GroundFixedPointCci, Earth.CarryCci(aim, flight.CutoffSeconds + hit.Seconds)) / 1000.0:F2}";
                arrival = $"{program.Arc?.ArrivalAngleDeg ?? double.NaN:F1}";
            }

            Out.WriteLine($"{floor,8:F0}{program.Reach.ToString(),16}{arrival,10}{miss,10}");

            // The gate. A floor the tanks cannot pay for reports TooShallow rather than grazing.
            Assert.Equal(IcbmReach.Reachable, program.Reach);

            if (floor > 0.0 && flight.Reached && program.Arc is { } arc)
            {
                Assert.True(arc.ArrivalAngleDeg >= floor - 0.5,
                            $"a {floor:F0} deg floor arrived at {arc.ArrivalAngleDeg:F1}");
            }
        }
    }
}
