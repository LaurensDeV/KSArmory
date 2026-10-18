using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.HopsFlyTheirOwnCeiling"/> on a walk started at cutoff, where a metre a second moves a landing
/// furthest and a hop prices over one pass's <see cref="BusTrim.MaxMetresPerSecond"/> (<c>docs/ICBM-OUTSTANDING.md</c> 4.12).
/// </summary>
public class HopCeilingRigTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    // APlannedWalkLandsEveryStopItFliesAndTheReserveBuysAnother's bus and shot, the walk started at cutoff, six targets
    // spaced wider than a gate-ending walk can reach at all.
    private PastCutoffRig Planned(bool ownCeiling, double spacingMetres)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = true,
            WalkStartsAtCutoff = true, HopsFlyTheirOwnCeiling = ownCeiling,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21), DensityAt);
        rig.ThrusterMetresPerSecond2 = 0.55;
        rig.ShoveMetresPerSecond = 1.0;
        for (int k = 0; k < 6; k++)
        {
            rig.PlannedTargets.Add((GameStackShortRangeTests.South(6_181_000.0 - k * spacingMetres), 1));
        }
        rig.PlannedLead = 0;

        PastCutoffRig.Outcome last = rig.Fly(GameStackShortRangeTests.South(6_181_000.0));
        Out.WriteLine($"own ceiling {(ownCeiling ? "on" : "off")}, {spacingMetres / 1_000.0:F0} km: planned "
                      + $"{rig.Plan.Stops} stops ({rig.Plan.Hold}, dearest hop {rig.Plan.HopMetresPerSecond:F2} m/s), "
                      + $"flew {rig.Stops.Count}, curtailed {rig.Curtailed}: {last.Said}");
        for (int k = 0; k < rig.Stops.Count; k++)
        {
            Out.WriteLine($"  stop {k + 1}: landed {rig.Stops[k].LandedMetres:F2} m, divert "
                          + $"{rig.Stops[k].DivertMetresPerSecond:F2} m/s, spent {rig.Stops[k].SpentMetresPerSecond:F2}, nose "
                          + $"{(k < rig.NoseFromEntryDeg.Count ? rig.NoseFromEntryDeg[k] : double.NaN):F2} deg off the entry airflow");
        }

        return rig;
    }

    [Theory]
    [InlineData(10_000.0)]
    [InlineData(12_000.0)]
    public void AHopPricedOverOnePassIsFlownOnItsOwnCeiling(double spacingMetres)
    {
        PastCutoffRig off = Planned(ownCeiling: false, spacingMetres);
        PastCutoffRig on = Planned(ownCeiling: true, spacingMetres);

        Assert.Equal(ReleaseWalkHold.HopBeyondOnePass, off.Plan.Hold);
        Assert.True(on.Stops.Count > off.Stops.Count, $"its own ceiling flew {on.Stops.Count} stops against {off.Stops.Count}");

        int landedOwn = on.Curtailed ? on.Stops.Count - 1 : on.Stops.Count;
        for (int k = 0; k < landedOwn; k++)
        {
            Assert.True(on.Stops[k].LandedMetres < 1.0, $"stop {k + 1} landed {on.Stops[k].LandedMetres:F2} m out");
        }
    }

    [Fact]
    public void AWalkFromCutoffReleasesEveryWarheadAlongItsEntryAirflow()
    {
        PastCutoffRig rig = Planned(ownCeiling: true, 10_000.0);

        Assert.True(rig.Stops.Count > 1, $"the fixture walks {rig.Stops.Count} stop");
        Assert.False(rig.ReleasedTurning, "a warhead left while the bus was still turning");
        Assert.All(rig.NoseFromEntryDeg, deg => Assert.True(deg < 2.0, $"nose {deg:F2} deg off the entry airflow"));
        Assert.All(rig.Stops, stop => Assert.True(stop.LandedMetres < 1.0, $"landed {stop.LandedMetres:F2} m out"));
    }
}
