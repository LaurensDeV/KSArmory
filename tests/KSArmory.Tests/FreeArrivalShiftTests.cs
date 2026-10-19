using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The arrival a free hop moves (<see cref="IcbmConfig.FreeHopsCountTheArrivalTheyMove"/>), against what flew at
/// 12,902 km on 2026-10-19: seven targets 400 km apart along the track, each hop's arrival about 80 s earlier.
/// </summary>
public class FreeArrivalShiftTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    // Bus 5's first two releases as the warhead trace logged them (~/shots/2026-10-19-engine-12900-400), and how
    // much earlier the arrival came on the 400 km hop flown from each: the drop in each round's flight time less the
    // seconds between its release and the next.
    [Theory]
    [InlineData(2808508.1, -5345887.2, 2930844.5, 7210.2689, 343.6691, -771.9059, 92.0)]
    [InlineData(3465135.6, -5283898.9, 2842846.1, 6786.8695, 967.1335, -1099.4399, 88.0)]
    public void AFreeHopMovesTheArrivalAsFarAsTheFlightDid(double x, double y, double z, double vx, double vy, double vz,
                                                          double flownEarlierSeconds)
    {
        BallisticBody earth = new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21)),
                                   0.5, R, StopOnTheSurface: true);
        ReleaseFocus.FlownSensitivity? flown =
            ReleaseFocus.FlownSensitivity.TryFly(earth, new double3(x, y, z), new double3(vx, vy, vz), air);
        Assert.NotNull(flown);

        Assert.True(DivertFootprint.TryFrom(earth, flown, DivertFootprint.ArrivalClock.Free, fromTheRealState: true,
                                            out DivertFootprint free));
        Assert.True(DivertFootprint.TryAxisPrice(earth, flown, new double3(1, 0, 0), new double3(0, 1, 0),
                                                 new double3(0, 0, 1), out DivertFootprint.AxisPrice axes,
                                                 DivertFootprint.ArrivalClock.Free));
        Assert.True(DivertFootprint.TryAxisPrice(earth, flown, new double3(1, 0, 0), new double3(0, 1, 0),
                                                 new double3(0, 0, 1), out DivertFootprint.AxisPrice pinned));
        free = free with { Axes = axes };

        double shift = free.ArrivalShiftSeconds(-400_000.0, 0.0);
        Out.WriteLine($"400 km uprange: arrival {shift:F1} s, flown {-flownEarlierSeconds:F0}");

        Assert.InRange(shift, -flownEarlierSeconds - 3.0, -flownEarlierSeconds + 3.0);
        Assert.Equal(0.0, (free with { Axes = pinned }).ArrivalShiftSeconds(-400_000.0, 0.0));
    }

    private static ReleaseItinerary.Bus Engine
        => new(new IcbmConfig().ReleaseBeforeArrivalSeconds, CoastSeconds: 2_726.0, Warheads: 7,
               BudgetMetresPerSecond: 1_000.0, FromCutoff: true, HopAccelerationMetresPerSecond2: 1.7,
               FreeArrival: true, HopTurnSeconds: HopBurn.TurnsSeconds);

    private static ReleaseItinerary.Target[] Chain(double shiftSeconds)
    {
        var set = new ReleaseItinerary.Target[7];
        for (int i = 0; i < set.Length; i++)
        {
            set[i] = new ReleaseItinerary.Target(1, set.Length - i, HopMetresPerSecond: i == 0 ? 0.0 : 55.0,
                                                 ArrivalShiftSeconds: i == 0 ? 0.0 : shiftSeconds);
        }

        return set;
    }

    [Fact]
    public void EachStopIsAsMuchNearerArrivalAsTheHopsBeforeItMovedIt()
    {
        ReleaseItinerary held = ReleaseItinerary.Plan(Chain(0.0), Engine);
        ReleaseItinerary moved = ReleaseItinerary.Plan(Chain(-85.0), Engine);

        Assert.Equal(7, held.Count);
        Assert.Equal(7, moved.Count);
        for (int k = 1; k < moved.Count; k++)
        {
            double heldStep = held.Stops[k - 1].BeforeArrivalSeconds - held.Stops[k].BeforeArrivalSeconds;
            double movedStep = moved.Stops[k - 1].BeforeArrivalSeconds - moved.Stops[k].BeforeArrivalSeconds;
            Out.WriteLine($"stop {k + 1}: {held.Stops[k].BeforeArrivalSeconds:F0} s held, {moved.Stops[k].BeforeArrivalSeconds:F0} moved");

            Assert.Equal(heldStep, moved.Stops[k].SlotSeconds, 9);
            Assert.Equal(heldStep + 85.0, movedStep, 9);
        }

        Assert.Equal(held.Stops[0].BeforeArrivalSeconds, moved.Stops[0].BeforeArrivalSeconds, 9);
    }
}
