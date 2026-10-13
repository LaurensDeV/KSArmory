using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Every stack <see cref="GameStackShortRangeTests"/> flies, at every range, with the settings that ship: the burn has
/// to end, and the warhead flown from where it ended has to land within a bound of the target. No aim correction, trim
/// or release, so the bound is the ascent's floor -- roughly twice what each stack lands at today -- and a flight that
/// gets much worse, or stops cutting off, is what this catches.
/// </summary>
public class ShortShotCompletionTests
{
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static readonly double[] Ranges = [25.0, 50.0, 100.0, 200.0, 300.0, 500.0, 1_000.0, 2_000.0];

    private static double DensityAt(double3 pointCci) => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    // The game stack is flown both ways because only the craft being flown reports its stack's delta-v: the other
    // rockets in a world read zero, and have to finish anyway.
    [Theory]
    [InlineData("game", 12.0)]
    [InlineData("game, no stack delta-v", 12.0)]
    [InlineData("all-solid", 10.0)]
    [InlineData("solid only", 50.0)]
    [InlineData("liquid", 6.0)]
    [InlineData("hot liquid", 80.0)]
    public void EveryStackCutsOffAtEveryRangeNearItsAscentFloor(string stack, double boundKm)
    {
        MunitionProfile warhead = Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);
        BallisticBody earth = new(3.986004418e14, R, new double3(0, 0, 1), 7.2921159e-5);

        foreach (double km in Ranges)
        {
            IcbmFlightRig rig = stack switch
            {
                "all-solid" => GameStackShortRangeTests.AllSolid(),
                "solid only" => GameStackShortRangeTests.SolidOnly(),
                "liquid" => GameStackShortRangeTests.Liquid(),
                "hot liquid" => GameStackShortRangeTests.HotLiquid(),
                "game, no stack delta-v" => GameStackShortRangeTests.GameStack(false),
                _ => GameStackShortRangeTests.GameStack(true),
            };

            IcbmProgram program = new(new IcbmConfig
            {
                Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = 0.5,
                FlyAnyRange = true,
            });

            double3 aim = GameStackShortRangeTests.South(km * 1000.0);
            IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);

            Assert.True(flight.Reached, $"{stack} at {km} km never cut off: {flight.FinalPhase} '{flight.Hold}'");
            Assert.True(ImpactPredictor.TryPredict(earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, 1.0,
                                                   ImpactPredictor.DefaultMaxSeconds, out ImpactPredictor.Impact hit,
                                                   null, null, new ImpactPredictor.Drag(DensityAt, warhead)),
                        $"{stack} at {km} km: no impact from the cutoff state");

            double missKm = R * Vec.AngleBetween(hit.GroundFixedPointCci, earth.CarryCci(aim, flight.CutoffSeconds)) / 1000.0;
            Assert.True(missKm < boundKm, $"{stack} at {km} km lands {missKm:F1} km out, past {boundKm} km");
        }
    }

    // The drag areas that stood still over a 25 km target for eighteen minutes; the Mk 21 aboard is what runs the drag
    // solve that stalls the throttle-down.
    [Theory]
    [InlineData(30.0)]
    [InlineData(45.0)]
    public void ALiquidStackCutsOffAt25KmRatherThanHovering(double dragAreaM2)
    {
        IcbmFlightRig rig = ShortShotHoverTests.RealLiquid2(dragAreaM2);
        IcbmProgram program = new(new IcbmConfig { Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, FlyAnyRange = true });
        double3 aim = ShortShotHoverTests.At(ShortShotHoverTests.PadLatitudeDeg - double.RadiansToDegrees(25_000.0 / R));

        IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 1_200.0);

        Assert.True(flight.Reached, $"never cut off: {flight.FinalPhase} '{flight.Hold}'");
        Assert.True(flight.CutoffSeconds < 200.0, $"cut off {flight.CutoffSeconds:F0} s after launch");
    }

    // Only the craft being flown reports its whole stack. The rest are reckoned off the running stage, which before it
    // is lit has no exhaust velocity: what it has is then unknown, never zero.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePadDeltaVLineIsTheWholeStackOrNothing(bool reportsStackDeltaV)
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(reportsStackDeltaV);
        IcbmProgram program = new(new IcbmConfig { Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, FlyAnyRange = true });

        rig.Fly(program, GameStackShortRangeTests.South(500_000.0), 0.02, 0.1);

        Assert.True(program.NeedMetresPerSecond > 0.0, $"needs {program.NeedMetresPerSecond}");
        Assert.Equal(reportsStackDeltaV, program.HaveIsTheWholeStack);
        Assert.Equal(reportsStackDeltaV, double.IsFinite(program.HaveMetresPerSecond));
    }

    // What a reach drawn before launch has to allow for: the thrust spent beyond what the pad asked of the stack.
    [Fact]
    public void TheAscentSpendsMoreThanThePadAsked()
    {
        IcbmFlightRig rig = GameStackShortRangeTests.GameStack(true);
        IcbmProgram program = new(new IcbmConfig { Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, FlyAnyRange = true });

        IcbmFlightRig.Flight flight = rig.Fly(program, GameStackShortRangeTests.South(1_000_000.0), 0.02, 6_000.0);

        Assert.True(flight.Reached, flight.Hold);
        Assert.True(program.ToGainAtIgnition > 0.0, $"to gain at ignition {program.ToGainAtIgnition}");
        Assert.True(program.ThrustSpentMetresPerSecond > program.ToGainAtIgnition,
                    $"spent {program.ThrustSpentMetresPerSecond:F0} against {program.ToGainAtIgnition:F0}");
    }
}
