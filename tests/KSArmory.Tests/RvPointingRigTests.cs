using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.PointRvsAtEntry"/> flown past cutoff on <see cref="PastCutoffRig"/>: the bus turns once after
/// its split and every warhead leaves along the airflow it will meet. <c>docs/RV-POINTING.md</c>.
/// </summary>
public class RvPointingRigTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;
    private const double Range = 6_181_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    // The game's stack at the flown 6,181 km, on the flown bus's 0.55 m/s^2 jets and a flown-sized split.
    private PastCutoffRig Fly(bool pointAtEntry, int stops, out PastCutoffRig.Outcome last)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5, FlyAnyRange = true,
            PulsesBelowTheFloor = false, PointRvsAtEntry = pointAtEntry,
        });

        PastCutoffRig rig = PastCutoffRig.For(GameStackShortRangeTests.GameStack(true), program,
                                              Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21), DensityAt);
        rig.ThrusterMetresPerSecond2 = 0.55;
        rig.ShoveMetresPerSecond = 1.0;
        for (int k = 1; k < stops; k++) rig.WalkAimsAtEpoch.Add(GameStackShortRangeTests.South(Range - k * 4_000.0));

        last = rig.Fly(GameStackShortRangeTests.South(Range));
        for (int k = 0; k < rig.Stops.Count; k++)
        {
            Out.WriteLine($"stop {k + 1}: landed {rig.Stops[k].LandedMetres:F3} m, nose "
                          + $"{(k < rig.NoseFromEntryDeg.Count ? rig.NoseFromEntryDeg[k] : double.NaN):F2} deg off the entry airflow");
        }

        return rig;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void EveryWarheadLeavesAlongItsEntryAirflowAndStillLands(int stops)
    {
        PastCutoffRig rig = Fly(pointAtEntry: true, stops, out PastCutoffRig.Outcome last);

        Assert.True(last.Released, last.Said);
        Assert.Equal(stops, rig.Stops.Count);
        Assert.False(rig.ReleasedTurning, "a warhead left while the bus was still turning");
        Assert.All(rig.NoseFromEntryDeg, deg => Assert.True(deg < 2.0, $"nose {deg:F2} deg off the entry airflow"));
        Assert.All(rig.Stops, stop => Assert.True(stop.LandedMetres < 1.0, $"landed {stop.LandedMetres:F2} m out"));
    }

    // Without it a 2-degree bound could pass on a geometry where the cutoff line already points at the entry.
    [Fact]
    public void WithoutTheTurnTheNoseLeavesFarFromItsAirflow()
    {
        PastCutoffRig rig = Fly(pointAtEntry: false, stops: 1, out PastCutoffRig.Outcome last);

        Assert.True(last.Released, last.Said);
        Assert.All(rig.NoseFromEntryDeg, deg => Assert.True(deg > 30.0, $"nose only {deg:F2} deg off the entry airflow"));
    }
}
