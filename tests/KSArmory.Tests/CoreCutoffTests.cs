using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The GeoSat FAT cut off on its 380 m/s2 core, 700-1,200 km: flown at 1,000 km, 64 of 100 rockets landed over a
/// kilometre out (ICBM-OUTSTANDING 1.23).
/// </summary>
public class CoreCutoffTests(ITestOutputHelper Out)
{
    // What BallisticScenario.Commit forces on the defaults.
    private static IcbmConfig FlightConfig() => new() { Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0 };

    private static IcbmFlightRig GeoSatFat()
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        rig.UsesKsaAttitudeLaw = true;
        rig.AttitudeHasInertia = true;
        return rig;
    }

    private static IcbmProgram Fly(double km, double step, bool holdsTheLever, bool throttledBackstop = false)
    {
        IcbmConfig config = FlightConfig();
        config.PauseHoldsTheThrottleDown = holdsTheLever;
        config.BackstopRisesOnTheThrottledFrame = throttledBackstop;
        IcbmProgram program = new(config);
        IcbmFlightRig.Flight flight = GeoSatFat().Fly(program, GameStackShortRangeTests.South(km * 1000.0), step, 6_000.0);
        Assert.True(flight.Reached, $"{km} km never cut off: {flight.Hold}");
        return program;
    }

    // The burn pauses to coast out of the air and relights to finish; the lever is what the relight comes back at.
    [Fact]
    public void ACoreRelitAfterThePauseComesBackAtTheFloorWhenTheLeverIsHeld()
    {
        IcbmProgram wound = Fly(1_000.0, 0.028, holdsTheLever: false);
        IcbmProgram held = Fly(1_000.0, 0.028, holdsTheLever: true);

        Assert.True(wound.ThrottleAtCutoff > 0.5, $"unheld, the core cut off at {wound.ThrottleAtCutoff:F2}: the fixture no longer winds the lever up");
        Assert.True(held.ThrottleAtCutoff < 0.15, $"held, the core still cut off at {held.ThrottleAtCutoff:F2}");
    }

    [Trait("kind", "study")]
    [Fact]
    public void WhereTheGeoSatFatCutsOff()
    {
        foreach ((bool hold, bool throttled) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            List<double> residuals = [];
            foreach (double km in new[] { 700.0, 800.0, 900.0, 1000.0, 1100.0, 1200.0 })
            foreach (double step in new[] { 0.010, 0.016, 0.022, 0.028 })
            {
                IcbmProgram p = Fly(km, step, hold, throttled);
                residuals.Add(p.ResidualAtCutoff);
                Out.WriteLine($"{(hold ? "hold" : "-"),-4} {(throttled ? "thr" : "-"),-3} {km,5:F0} km {step * 1000,2:F0} ms: "
                              + $"by {p.CutOffBy,-12} residual {p.ResidualAtCutoff,6:F2} m/s at {p.ThrottleAtCutoff:F2} of "
                              + $"{p.AccelerationAtCutoff:F0} m/s2");
            }

            residuals.Sort();
            Out.WriteLine($"  median {residuals[residuals.Count / 2]:F2} m/s, {residuals.Count(r => r > BusTrim.MaxMetresPerSecond)} "
                          + $"of {residuals.Count} over the trim's first pass");
        }
    }
}
