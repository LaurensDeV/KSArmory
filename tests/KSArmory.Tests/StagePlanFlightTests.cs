using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

public class StagePlanFlightTests(ITestOutputHelper Out)
{
    private const double EarthRadius = 6_371_000.0;

    private static BallisticBody Earth => new(3.986004418e14, EarthRadius, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 Equator(double longitudeRad)
        => new(EarthRadius * Math.Cos(longitudeRad), EarthRadius * Math.Sin(longitudeRad), 0.0);

    private readonly record struct Frame(double Seconds, int Stage, IcbmPhase Phase, double Countdown, double Throttle);

    // A 640 km shot off a 900 kN first stage hands over to the closed loop about ten seconds before the staging, so
    // the countdown it starts there spans the staging.
    private static (double WorstError, double Miss) Fly(bool plan)
    {
        double3 pad = Equator(0.0);
        double3 aim = Equator(0.1);
        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            Stages =
            [
                new() { DryMassKg = 4_000, PropellantKg = 46_000, ThrustNewtons = 900_000, ExhaustVelocity = 2_600 },
                new() { DryMassKg = 1_200, PropellantKg = 12_000, ThrustNewtons = 260_000, ExhaustVelocity = 3_000 },
            ],
            ReportsStagePlan = true,
        };

        IcbmProgram program = new(new IcbmConfig { Armed = true, FlyTheStagePlan = plan });
        List<Frame> frames = [];
        rig.StepTrace = (t, phase, _, _, throttle) => frames.Add(new Frame(t, rig.StageIndex, phase, program.Countdown, throttle));

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 900.0);
        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold} in {flight.FinalPhase}");

        // The countdown is in full-throttle seconds, so it is judged against the throttle actually flown to cutoff.
        double flown = 0.0;
        double worst = 0.0;
        int judged = 0;
        for (int i = frames.Count - 2; i >= 0; i--)
        {
            flown += (frames[i + 1].Seconds - frames[i].Seconds) * frames[i].Throttle;
            if (frames[i].Stage != 0 || frames[i].Phase != IcbmPhase.ClosedLoop) continue;
            worst = Math.Max(worst, Math.Abs(frames[i].Countdown - flown));
            judged++;
        }
        Assert.True(judged > 100, $"only {judged} closed-loop frames before the staging");

        Assert.True(ImpactPredictor.TryPredict(rig.Body, flight.CutoffPositionCci, flight.CutoffVelocityCci, 2.0, 12_000.0,
                                               out ImpactPredictor.Impact hit));
        double miss = EarthRadius * Vec.AngleBetween(hit.GroundFixedPointCci, rig.Body.CarryCci(aim, flight.CutoffSeconds));
        return (worst, miss);
    }

    [Fact]
    public void ACountdownSpanningAStagingIsCloserToTheCutoffOnTheStagePlan()
    {
        (double single, double singleMiss) = Fly(plan: false);
        (double staged, double stagedMiss) = Fly(plan: true);
        Out.WriteLine($"worst countdown error before the staging: {single:F2} s without the plan, {staged:F2} s on it; "
                      + $"missed by {singleMiss:F2} m and {stagedMiss:F2} m");

        Assert.True(staged < 4.0 && staged < 0.5 * single,
                    $"worst countdown error before the staging: {staged:F2} s on the plan against {single:F2} s without");
        Assert.True(stagedMiss < 50.0, $"missed by {stagedMiss:F1} m on the plan, {singleMiss:F1} m without");
    }
}
