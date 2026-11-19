using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The walk the pad promises before launch against the one the coast then plans, on the post-boost bus with every
/// wide-walk flag on. Against the coast's plan, not what flies: with a 134 kg tank the coast itself plans 2 and flies 1.
/// </summary>
public class PadReachTests(ITestOutputHelper Out)
{
    [Theory]
    [InlineData(2_000_000.0, 50_000.0)]
    [InlineData(6_181_000.0, 200_000.0)]
    [InlineData(12_902_000.0, 300_000.0)]
    [InlineData(12_902_000.0, 300_000.0, 134.0, 3_622.0)]
    public void ThePadNeverPlansMoreStopsThanTheCoastFlies(double range, double spacing, double tankKg = 500.0,
                                                          double busKg = 3_800.0)
    {
        IcbmConfig c = PadFreeReachStudyTests.Wide();
        PadAndAim(range, out var pad, out var aim);

        Assert.True(PadFreeReachStudyTests.TryPadFree(pad, aim, c.ReleaseBeforeArrivalSeconds, out DivertFootprint free));
        int onThePad = PadFreeReachStudyTests.Planned(free, c, spacing, PadFreeReachStudyTests.PadCoast(pad, aim), tankKg, busKg);

        PastCutoffRig coast = PadFreeReachStudyTests.FlyTheWalk(range, spacing, tankKg, busKg);
        Out.WriteLine($"{range / 1000:F0} km, {spacing / 1000:F0} km apart, {tankKg:F0} kg: pad {onThePad}, coast planned {coast.Plan.Stops}, "
                      + $"flew {coast.Stops.Count}");

        Assert.True(onThePad >= 1);
        Assert.True(onThePad <= coast.Plan.Stops, $"the pad plans {onThePad} where the coast plans {coast.Plan.Stops}");
    }

    [Fact]
    public void FromTwelveThousandKilometresThePadPlansAWideWalkThePinnedDiscRefuses()
    {
        IcbmConfig c = PadFreeReachStudyTests.Wide();
        PadAndAim(12_902_000.0, out var pad, out var aim);

        Assert.True(PadFreeReachStudyTests.TryPadFree(pad, aim, c.ReleaseBeforeArrivalSeconds, out DivertFootprint free));
        Assert.True(DivertFootprint.TryAtTheEpoch(default, c.ReleaseBeforeArrivalSeconds, out DivertFootprint pinned));

        int onFree = PadFreeReachStudyTests.Planned(free, c, 200_000.0, PadFreeReachStudyTests.PadCoast(pad, aim));
        int onPinned = PadFreeReachStudyTests.Planned(pinned, c, 200_000.0, double.NaN);

        Assert.Equal(1, onPinned);
        Assert.True(onFree >= 4, $"the free reach plans {onFree}");
    }

    [Fact]
    public void TheCoastFloorIsAShareOfTheCheapestConic()
    {
        PadAndAim(6_181_000.0, out var pad, out var aim);
        BallisticBody earth = new(3.986004418e14, 6_371_000.0, new double3(0, 0, 1), 7.2921159e-5);

        Assert.True(BallisticArc.TryCheapest(earth, pad, earth.GroundVelocityCci(pad), aim, out BallisticArc.Solution conic));
        Assert.Equal(PadReach.CoastShareOfTheConic * conic.FlightSeconds, PadFreeReachStudyTests.PadCoast(pad, aim), 6);
    }

    private static void PadAndAim(double range, out double3 pad, out double3 aim)
    {
        pad = GameStackShortRangeTests.GameStack(true).PositionCci;
        aim = GameStackShortRangeTests.South(range);
    }
}
