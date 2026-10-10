using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A stack whose throttle floor alone breaks its airframe as it empties, flown with
/// <see cref="IcbmConfig.PulsesBelowTheFloor"/>. <c>docs/ICBM-OUTSTANDING.md</c> 1.11.
/// </summary>
public class ThrottleFloorPulseTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;
    private static BallisticBody Earth => new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);

    // KSA Any OneStage as flown: four A2s under three LF3W6HB, 147.8 t at liftoff, a reported 11.6% floor that held
    // 1.69x that, and the 21.2 g limit KSA gave it.
    private static IcbmFlightRig OneStage()
    {
        double3 pad = GameStackShortRangeTests.South(0.0);
        return new IcbmFlightRig
        {
            Body = Earth, PositionCci = pad, VelocityCci = Earth.GroundVelocityCci(pad),
            BoundingSphereRadiusMetres = 250.0 / 21.2, DragAreaM2 = 25.0, StartsUnlit = true, ReportsStackDeltaV = true,
            CommandLatencyFrames = 1, MinThrottle = 0.116, FloorHeld = 0.116 * 1.69, ThrottleRatePerSecond = 0.7,
            StepJitter = 0.5, UnpoweredAttitudeRateDegPerSec = 1.0,
            Stages = [new() { DryMassKg = 14_450, PropellantKg = 133_350, ThrustNewtons = 25_724_000, ExhaustVelocity = 4_056 }],
        };
    }

    private static IcbmProgram Program(bool pulses)
        => new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, ArrivalPreference = 0.5, PulsesBelowTheFloor = pulses, FlyAnyRange = true,
        });

    // One frame at the floor the engine holds, at burnout: what a pulse puts on the cutoff.
    private const double FloorFrameMetresPerSecond = 0.116 * 1.69 * 25_724_000 / 14_450 / 60.0;

    [Fact]
    public void PulsingKeepsWholeAStackItsThrottleFloorBreaks()
    {
        double3 aim = GameStackShortRangeTests.South(6_179_000.0);

        IcbmFlightRig held = OneStage();
        IcbmFlightRig.Flight broke = held.Fly(Program(pulses: false), aim, 1.0 / 60.0, 3_000.0);

        IcbmFlightRig pulsed = OneStage();
        IcbmProgram program = Program(pulses: true);
        IcbmFlightRig.Flight whole = pulsed.Fly(program, aim, 1.0 / 60.0, 3_000.0);
        double residual = Vec.Len(whole.CutoffVelocityCci - program.Arc!.Value.RequiredVelocityCci);

        Out.WriteLine($"held at the floor: peak {broke.PeakFilteredGee:F1} g of {held.StructuralLimitGee:F1}; pulsed: peak "
                      + $"{whole.PeakFilteredGee:F1} g, {residual:F2} m/s left at cutoff against a {FloorFrameMetresPerSecond:F2} m/s frame");

        Assert.True(broke.BrokeUp, "held at its floor the stack should break");
        Assert.True(whole.Reached && !whole.BrokeUp, $"pulsed, it broke at {whole.PeakFilteredGee:F1} g");
        Assert.True(residual < 0.5 * FloorFrameMetresPerSecond, $"{residual:F2} m/s left at cutoff");
    }

    [Theory]
    [InlineData(700.0)]
    [InlineData(6_179.0)]
    public void APulsedShotLandsOnItsTarget(double km)
    {
        IcbmFlightRig rig = OneStage();
        PastCutoffRig past = PastCutoffRig.For(rig, Program(pulses: true), Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21),
                                               p => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0));
        past.ShoveMetresPerSecond = 1.0;

        PastCutoffRig.Outcome shot = past.Fly(GameStackShortRangeTests.South(km * 1000.0), 1.0 / 60.0, 3_000.0);
        Out.WriteLine($"{km} km: landed {shot.LandedMetres:F2} m, owed {shot.OwedAtSplitMetresPerSecond:F2} m/s at the split, "
                      + $"trim spent {shot.TrimSpentMetresPerSecond:F2} :: {shot.Said}");

        Assert.True(shot.Released, shot.Said);
        Assert.True(shot.LandedMetres < 1.0, $"landed {shot.LandedMetres:F2} m out");
    }
}
