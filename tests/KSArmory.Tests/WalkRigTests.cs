using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A walk between targets flown past cutoff on <see cref="PastCutoffRig"/>, so what a hop costs can be read without a
/// night: the per-axis sum and the shrinking reach were both found by flying (<c>docs/MIRV-TARGETS.md</c>, "six stops to
/// the ground").
/// </summary>
public class WalkRigTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    // The game's stack at 2,000 km, walked inward from the farthest of four targets 4 km apart, on the flown bus's
    // 0.55 m/s^2 jets and a flown-sized split.
    private PastCutoffRig Walk(out PastCutoffRig.Outcome last)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            // Pulsed, this bus comes back to 5.3 m of its stack rather than 13.5 and is held 72 s, which leaves the first hop
            // just over one pass: the keep-out's knife edge, not the walk. The entry turn's 9 s tips this forced walk over
            // the same edge; the planned one the computer flies is RvPointingRigTests'.
            PulsesBelowTheFloor = false, PointRvsAtEntry = false,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              Arsenal.ReentryVehicleMk21.Copy(), DensityAt);
        rig.ThrusterMetresPerSecond2 = 0.55;
        rig.ShoveMetresPerSecond = 1.0;
        for (int k = 1; k < 4; k++) rig.WalkAimsAtEpoch.Add(GameStackShortRangeTests.South(2_000_000.0 - k * 4_000.0));

        last = rig.Fly(GameStackShortRangeTests.South(2_000_000.0));
        for (int k = 0; k < rig.Stops.Count; k++)
        {
            Out.WriteLine($"stop {k + 1}: landed {rig.Stops[k].LandedMetres:F2} m, divert {rig.Stops[k].DivertMetresPerSecond:F2} "
                          + $"m/s, spent {rig.Stops[k].SpentMetresPerSecond:F2}");
        }

        return rig;
    }

    [Fact]
    public void EveryStopOfAWalkLandsOnItsOwnTarget()
    {
        PastCutoffRig rig = Walk(out PastCutoffRig.Outcome last);

        Assert.True(last.Released, last.Said);
        Assert.Equal(4, rig.Stops.Count);
        Assert.All(rig.Stops, stop => Assert.True(stop.LandedMetres < 1.0, $"landed {stop.LandedMetres:F2} m out"));
    }

    // Each hop is flown nearer arrival than the one before, where a metre a second moves the landing less.
    [Fact]
    public void EachHopCostsMoreThanTheOneBefore()
    {
        PastCutoffRig rig = Walk(out _);

        for (int k = 2; k < rig.Stops.Count; k++)
        {
            Assert.True(rig.Stops[k].DivertMetresPerSecond > rig.Stops[k - 1].DivertMetresPerSecond,
                        $"stop {k + 1} cost {rig.Stops[k].DivertMetresPerSecond:F2} after {rig.Stops[k - 1].DivertMetresPerSecond:F2}");
        }
    }

    // The flown 4 km six-target walk at 6,181 km (MIRV-TARGETS, "six stops to the ground"): 12 of 12 buses walked four
    // stops without the split reserve and five with it. The rig plans four and six: a stop optimistic with the reserve.
    private PastCutoffRig Planned(bool reserve)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, SpendTheSplitReserveAfterTheFirstStop = reserve,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              Arsenal.ReentryVehicleMk21.Copy(), DensityAt);
        rig.ThrusterMetresPerSecond2 = 0.55;
        rig.ShoveMetresPerSecond = 1.0;
        for (int k = 0; k < 6; k++) rig.PlannedTargets.Add((GameStackShortRangeTests.South(6_181_000.0 - k * 4_000.0), 1));
        rig.PlannedLead = 0;

        PastCutoffRig.Outcome last = rig.Fly(GameStackShortRangeTests.South(6_181_000.0));
        Out.WriteLine($"reserve {(reserve ? "on" : "off")}: planned {rig.Plan.Stops} stops, flew {rig.Stops.Count}, "
                      + $"curtailed {rig.Curtailed}: {last.Said}");
        for (int k = 0; k < rig.Stops.Count; k++)
        {
            Out.WriteLine($"  stop {k + 1}: landed {rig.Stops[k].LandedMetres:F2} m, divert {rig.Stops[k].DivertMetresPerSecond:F2} m/s");
        }

        return rig;
    }

    [Fact]
    public void APlannedWalkLandsEveryStopItFliesAndTheReserveBuysAnother()
    {
        PastCutoffRig off = Planned(reserve: false);
        PastCutoffRig on = Planned(reserve: true);

        Assert.True(off.Plan.Stops >= 4, $"planned {off.Plan.Stops} stops without the reserve");
        Assert.True(on.Stops.Count > off.Stops.Count, $"the reserve flew {on.Stops.Count} stops against {off.Stops.Count}");

        foreach (PastCutoffRig rig in new[] { off, on })
        {
            int landedOwn = rig.Curtailed ? rig.Stops.Count - 1 : rig.Stops.Count;
            for (int k = 0; k < landedOwn; k++) Assert.True(rig.Stops[k].LandedMetres < 1.0, $"stop {k + 1} landed {rig.Stops[k].LandedMetres:F2} m out");
        }
    }
}
