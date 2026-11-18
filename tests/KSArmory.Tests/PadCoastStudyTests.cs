using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// How long the coast after cutoff is against the cheapest conic from the pad, over the stacks the rigs fly: what a
/// reach drawn before launch has to estimate the coast from. <c>docs/WIDE-SPREAD.md</c> step 8.
/// </summary>
[Trait("kind", "study")]
public class PadCoastStudyTests(ITestOutputHelper Out)
{
    private static IcbmConfig Shipped(double preference) => new()
    {
        Armed = true, MaxAccelerationGee = 8.0f, MinArrivalAngleDeg = 0.0, ArrivalPreference = preference, FlyAnyRange = true,
    };

    public static TheoryData<string> Stacks => new() { "GameStack", "AllSolid", "SolidOnly", "Liquid", "HotLiquid", "LowTwr" };

    private static IcbmFlightRig Stack(string name) => name switch
    {
        "GameStack" => GameStackShortRangeTests.GameStack(true),
        "AllSolid" => GameStackShortRangeTests.AllSolid(),
        "SolidOnly" => GameStackShortRangeTests.SolidOnly(),
        "Liquid" => GameStackShortRangeTests.Liquid(),
        "HotLiquid" => GameStackShortRangeTests.HotLiquid(),
        _ => LowTwrPushStudy.LowTwr(),
    };

    [Theory]
    [MemberData(nameof(Stacks))]
    public void TheCoastAgainstThePadsConic(string name)
    {
        foreach (double preference in new[] { 0.0, 0.5, 1.0 })
        foreach (double range in new[] { 500_000.0, 1_000_000.0, 2_000_000.0, 4_000_000.0, 6_181_000.0, 9_000_000.0, 12_902_000.0 })
        {
            IcbmFlightRig rig = Stack(name);
            BallisticBody body = rig.Body;
            double3 pad = rig.PositionCci;
            double3 aim = GameStackShortRangeTests.South(range);

            bool solved = BallisticArc.TryCheapest(body, pad, body.GroundVelocityCci(pad), aim, out BallisticArc.Solution conic);

            IcbmProgram program = new(Shipped(preference));
            IcbmFlightRig.Flight flight = rig.Fly(program, aim, 0.02, 6_000.0);
            double coast = program.CommittedArrivalFromNow;

            Out.WriteLine($"{name} pref {preference:F1} {range / 1000:F0} km: conic {(solved ? conic.FlightSeconds : double.NaN):F0} s, "
                          + (flight.Reached
                                 ? $"burn {flight.CutoffSeconds:F0} s, coast {coast:F0} s, coast/conic {coast / conic.FlightSeconds:F3}, "
                                   + $"conic - coast {conic.FlightSeconds - coast:F0} s"
                                 : $"never cut off ({flight.FinalPhase})"));
        }
    }
}
