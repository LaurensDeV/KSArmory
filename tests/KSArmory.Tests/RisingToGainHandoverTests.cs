using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// <see cref="IcbmConfig.HandsOverWhenBurningAddsToTheShot"/> on <see cref="HotCoreTests.RealSrb4"/>, whose boosters and
/// per-stage drag areas are fitted to the 2026-10-12 flights at 418, 700 and 1,000 km.
/// </summary>
public class RisingToGainHandoverTests
{
    private static (IcbmFlightRig.Flight Flight, IcbmProgram Program) Fly(IcbmFlightRig rig, double km, bool guard)
    {
        IcbmProgram program = new(new IcbmConfig
        {
            Armed = true, MaxAccelerationGee = 8.0f, HandsOverWhenBurningAddsToTheShot = guard,
        });
        return (rig.Fly(program, GameStackShortRangeTests.South(km * 1000.0), 0.02, 6_000.0), program);
    }

    private static IcbmFlightRig RealSrb4()
    {
        IcbmFlightRig rig = HotCoreTests.RealSrb4();
        rig.UsesKsaAttitudeLaw = true;
        return rig;
    }

    // The off half is the flight: 3 of 3 stayed on the schedule to 88 km and burned out 12.8-13.7 km/s short.
    [Fact]
    public void WithoutTheGuardRealSrb4At1000KmBurnsOutOnTheSchedule()
    {
        (_, IcbmProgram program) = Fly(RealSrb4(), 1000.0, guard: false);
        Assert.True(program.ResidualAtCutoff > 5_000.0, $"cut off {program.ResidualAtCutoff:F0} m/s short: the fixture no longer flies as flown");
    }

    [Theory]
    [InlineData(1000.0)]
    [InlineData(2500.0)]
    public void WithTheGuardRealSrb4HandsOverAndCutsOff(double km)
    {
        (IcbmFlightRig.Flight flight, IcbmProgram program) = Fly(RealSrb4(), km, guard: true);
        Assert.True(flight.Reached, $"never cut off: {flight.Hold}");
        Assert.True(program.ResidualAtCutoff < 2.0, $"cut off {program.ResidualAtCutoff:F1} m/s short");
    }

    // A long shot's velocity to gain rises on the schedule too, but stays a small share of the stack; the guard must not see it.
    [Theory]
    [InlineData("game", 1600.0)]
    [InlineData("game", 2000.0)]
    [InlineData("game", 6000.0)]
    [InlineData("srb4", 418.0)]
    [InlineData("srb4", 700.0)]
    public void WhereTheStackHasRoomTheGuardChangesNothing(string stack, double km)
    {
        IcbmFlightRig Rig() => stack == "game" ? GameStackShortRangeTests.GameStack(true) : RealSrb4();
        IcbmFlightRig.Flight off = Fly(Rig(), km, guard: false).Flight;
        IcbmFlightRig.Flight on = Fly(Rig(), km, guard: true).Flight;

        Assert.True(off.Reached, $"never cut off: {off.Hold}");
        Assert.Equal(off.CutoffSeconds, on.CutoffSeconds);
        Assert.Equal(off.CutoffPositionCci, on.CutoffPositionCci);
        Assert.Equal(off.CutoffVelocityCci, on.CutoffVelocityCci);
    }
}
