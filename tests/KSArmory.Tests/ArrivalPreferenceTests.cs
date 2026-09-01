using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The arrival floor worked out rather than typed: a fraction of the steepest arrival the tanks can
/// pay for, which <see cref="ArrivalBudget"/> already computed every cycle and nothing used.
///
/// <para><b>Why it is worth having.</b> With the correction floor out of the way the miss tracks
/// <c>cot γ</c> exactly — flown at 0.56x / 0.44x / 0.43x for 25.9, 33.0 and 41.1 degrees against
/// theory's 0.65 / 0.49 / 0.36 — so steeper is simply better and what bounds it is the propellant.
/// A player owns the trade; nobody should own the angle.</para>
/// </summary>
public class ArrivalPreferenceTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double3 Downrange(double metres)
        => new(DeorbitShot.R * Math.Cos(metres / DeorbitShot.R),
               DeorbitShot.R * Math.Sin(metres / DeorbitShot.R), 0);

    private static IcbmFlightRig InOrbit() => new()
    {
        Body = Earth,
        PositionCci = new double3(DeorbitShot.R + 300_000.0, 0, 0),
        VelocityCci = new double3(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + 300_000.0)), 0),
        Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
    };

    private static IcbmProgram Fly(IcbmConfig config)
    {
        IcbmProgram program = new(config);

        InOrbit().Fly(program, Downrange(DeorbitShot.RangeMetres), 0.02, 12_000.0);

        return program;
    }

    /// <summary>Zero leaves the operator's own number alone, which is what ships.</summary>
    [Fact]
    public void APreferenceOfZeroLeavesTheAskedForFloorAlone()
    {
        IcbmProgram program = Fly(new IcbmConfig { Armed = true, MinArrivalAngleDeg = 12.0 });

        Assert.False(double.IsFinite(program.ArrivalFloorDeg));
    }

    /// <summary>
    /// Above zero the floor is that share of what the tanks can pay for — so it is the propellant
    /// that sets the angle, and the player only says how much of it to spend on precision.
    /// </summary>
    [Fact]
    public void APreferenceAsksForThatShareOfWhatTheTanksCanAfford()
    {
        IcbmProgram program = Fly(new IcbmConfig { Armed = true, ArrivalPreference = 0.6 });

        Out.WriteLine($"steepest affordable {program.SteepestAffordableArrivalDeg:F1} deg, "
                      + $"floor latched at {program.ArrivalFloorDeg:F1}");

        Assert.True(double.IsFinite(program.ArrivalFloorDeg), "nothing was latched");
        Assert.True(program.ArrivalFloorDeg > 0.0);

        // Against the affordable angle as it stood when the latch happened, which the flight has
        // since moved -- so this is a bound rather than an equality.
        Assert.True(program.ArrivalFloorDeg <= ArrivalBudget.SteepestConsideredDeg);
    }

    /// <summary>A preference never gives away an angle that was asked for outright.</summary>
    [Fact]
    public void APreferenceNeverLowersAFloorTheOperatorAskedFor()
    {
        IcbmProgram program = Fly(new IcbmConfig
        {
            Armed = true,
            MinArrivalAngleDeg = 35.0,
            ArrivalPreference = 0.05,
        });

        Assert.True(program.ArrivalFloorDeg >= 35.0,
                    $"asked for 35 deg and latched {program.ArrivalFloorDeg:F1}");
    }

    /// <summary>
    /// <b>Latched, not followed.</b> The steepest affordable arrival moves through a flight as the
    /// stack lightens, and a floor that tracked it would re-open the search against a different
    /// bound every cycle — which is `docs/ARRIVAL-ANGLE.md`'s reason for refusing Loft as an arrival
    /// control. This fails against a floor recomputed each pass.
    /// </summary>
    [Fact]
    public void TheFloorIsLatchedAndDoesNotFollowTheAffordableAngle()
    {
        IcbmConfig config = new() { Armed = true, ArrivalPreference = 0.6 };
        IcbmProgram program = new(config);

        List<double> floors = [];
        List<double> affordable = [];

        IcbmFlightRig rig = InOrbit();
        rig.AimLoop = new Watch(floors, affordable);
        rig.Fly(program, Downrange(DeorbitShot.RangeMetres), 0.02, 12_000.0);

        List<double> seen = floors.Where(double.IsFinite).ToList();
        List<double> bound = affordable.Where(double.IsFinite).ToList();

        Assert.True(seen.Count > 10, $"only {seen.Count} readings");
        Assert.True(bound.Count > 10);

        double floorSpread = seen.Max() - seen.Min();
        double affordSpread = bound.Max() - bound.Min();

        Out.WriteLine($"floor moved {floorSpread:F3} deg while the affordable angle moved "
                      + $"{affordSpread:F1}");

        Assert.True(affordSpread > 1.0,
                    "the affordable angle did not move, so this proves nothing about latching");
        Assert.Equal(0.0, floorSpread, 6);
    }

    /// <summary>Reads the two angles each pass and corrects nothing, so only the floor is on trial.</summary>
    private sealed class Watch(List<double> floors, List<double> affordable) : IcbmFlightRig.IAimLoop
    {
        public double3 Apply(double3 aimNowCci) => aimNowCci;

        public bool IsSteady => true;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci,
                                double stepSeconds)
        {
            floors.Add(program.ArrivalFloorDeg);
            affordable.Add(program.SteepestAffordableArrivalDeg);
        }
    }
}
