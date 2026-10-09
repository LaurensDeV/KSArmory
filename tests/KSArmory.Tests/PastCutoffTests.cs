using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whole flights through the loop after cutoff, on <see cref="PastCutoffRig"/>, scored where they land.
/// </summary>
public class PastCutoffTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci) => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static MunitionProfile Mk21 => Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

    [Fact]
    public void APadShotNullsItsShoveCorrectsItsAimAndLandsOnTheTarget()
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5,
            FlyAnyRange = true,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program, Mk21, DensityAt);
        PastCutoffRig.Outcome shot = Say(rig.Fly(GameStackShortRangeTests.South(2_000_000.0)));

        Assert.True(shot.Released, shot.Said);
        Assert.True(shot.OwedAtSplitMetresPerSecond > 5.0, $"owed {shot.OwedAtSplitMetresPerSecond:F2} m/s at the split");
        Assert.True(shot.PostBoostPasses >= 1, "no post-boost pass was taken");
        Assert.True(shot.ProbeMissMetres < 10.0, $"the bus released {shot.ProbeMissMetres:F1} m out");
        Assert.True(shot.LandedMetres < 1.0, $"landed {shot.LandedMetres:F2} m out");
    }

    // The orbital case of docs/ICBM-OUTSTANDING.md 1.8: 0.70 m/s of separation and 0.38 shared by both halves, so the
    // bus owes 1.08 back toward the stack and the pair close at 0.38 once it is paid; 0.32 restitution makes that the
    // 0.5 m/s contact measured in flight.
    private PastCutoffRig.Outcome OwingTowardTheStack(bool waits, out PastCutoffRig rig)
    {
        IcbmProgram program = new(new IcbmConfig { Armed = true, TrimWaitsOutTheStack = waits });
        rig = PastCutoffRig.For(InOrbit(), program, Mk21, DensityAt);
        rig.ShoveMetresPerSecond = 0.70;
        rig.SharedErrorAlongNoseMetresPerSecond = 0.38;
        rig.ContactRestitution = 0.32;
        rig.StageRadiusMetres = 3.5;
        rig.ThrusterMetresPerSecond2 = 0.55;
        return Say(rig.Fly(Downrange(2_416_000.0)));
    }

    [Fact]
    public void TrimmingAsSoonAsClearReachesTheStackAndHoldsAPassOffIt()
    {
        PastCutoffRig.Outcome shot = OwingTowardTheStack(waits: false, out PastCutoffRig rig);

        Assert.True(shot.Released, shot.Said);
        Assert.True(shot.Closest.Breached, shot.Closest.Said);
        Assert.True(rig.Contacts >= 1, "the bus never reached the stack");
        Assert.True(shot.SecondsHeldOffTheStack > 10.0, $"held off for {shot.SecondsHeldOffTheStack:F1} s");
    }

    [Fact]
    public void WaitingOutTheStackStaysOutsideTheKeepOutAndLandsOnTheTarget()
    {
        PastCutoffRig.Outcome shot = OwingTowardTheStack(waits: true, out PastCutoffRig rig);

        Assert.True(shot.Released, shot.Said);
        Assert.True(shot.OwedAtSplitMetresPerSecond is > 0.8 and < 1.2, $"owed {shot.OwedAtSplitMetresPerSecond:F2} m/s");
        Assert.False(shot.Closest.Breached, shot.Closest.Said);
        Assert.Equal(0, rig.Contacts);
        Assert.Equal(0.0, shot.SecondsHeldOffTheStack);
        Assert.False(shot.PostBoostEnded.StartsWith("released after", StringComparison.Ordinal), shot.PostBoostEnded);
        Assert.True(shot.ProbeMissMetres < 5.0, $"the bus released {shot.ProbeMissMetres:F1} m out");
        Assert.True(shot.LandedMetres < 1.0, $"landed {shot.LandedMetres:F2} m out");
    }

    // Aimed behind the bus, so it holds for its burn window before it burns: a resume spent in that hold leaves the
    // aim frozen after cutoff. docs/ICBM-OUTSTANDING.md 1.9.
    [Fact]
    public void ABusThatHoldsForItsWindowStillCorrectsItsAimAfterCutoff()
    {
        IcbmProgram program = new(new IcbmConfig { Armed = true, AimResumesAtCutoff = true });
        PastCutoffRig rig = PastCutoffRig.For(InOrbit(), program, Mk21, DensityAt);
        rig.MaxSecondsAfterCutoff = 6_000.0;
        PastCutoffRig.Outcome shot = Say(rig.Fly(Downrange(-400_000.0)));

        Assert.True(shot.Released, shot.Said);
        Assert.True(shot.PostBoostPasses >= 1, $"no post-boost pass was taken: '{shot.PostBoostEnded}'");
        Assert.True(shot.ProbeMissMetres < 10.0, $"the bus released {shot.ProbeMissMetres:F1} m out");
    }

    private PastCutoffRig.Outcome Say(PastCutoffRig.Outcome o)
    {
        Out.WriteLine($"released {o.Released}, landed {o.LandedMetres:F3} m, probe {o.ProbeMissMetres:F1} m, "
                      + $"split to release {o.SecondsFromSplitToRelease:F1} s, {o.PostBoostPasses} pass(es) "
                      + $"'{o.PostBoostEnded}', owed {o.OwedAtSplitMetresPerSecond:F2} m/s, spent "
                      + $"{o.TrimSpentMetresPerSecond:F2}, held off {o.SecondsHeldOffTheStack:F1} s, "
                      + $"{o.Closest.Said} :: {o.Said}");
        return o;
    }

    private static double3 Downrange(double metres) => new(R * Math.Cos(metres / R), R * Math.Sin(metres / R), 0);

    // AimConvergenceTests' bus, with the game's actuator.
    private static IcbmFlightRig InOrbit()
        => new()
        {
            Body = Earth,
            PositionCci = new double3(R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = 2.0,
            MinThrottle = 0.12,
            StepJitter = 0.5,
        };
}
