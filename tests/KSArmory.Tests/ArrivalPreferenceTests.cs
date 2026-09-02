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

    /// <summary>
    /// The floor divided by what it was a fraction of gives the preference back, at every reading.
    ///
    /// <para>Which is the only thing that makes a night flown at several fractions readable: the
    /// affordable angle keeps moving, so the multiplicand has to be the one caught at the latch and
    /// not whatever it reads by the time anything asks.</para>
    /// </summary>
    [Fact]
    public void TheMultiplicandIsTheOneTheFractionWasAppliedTo()
    {
        const double preference = 0.6;

        IcbmConfig config = new() { Armed = true, ArrivalPreference = preference };
        IcbmProgram program = new(config);

        List<double> floors = [];
        List<double> affordable = [];
        List<double> from = [];

        IcbmFlightRig rig = InOrbit();
        rig.AimLoop = new Watch(floors, affordable, from);
        rig.Fly(program, Downrange(DeorbitShot.RangeMetres), 0.02, 12_000.0);

        List<(double floor, double got, double live)> seen =
            [.. floors.Select((f, i) => (f, from[i], affordable[i]))
                      .Where(t => double.IsFinite(t.Item1) && double.IsFinite(t.Item2))];

        Assert.True(seen.Count > 10, $"only {seen.Count} readings");

        double drift = seen.Max(t => t.live) - seen.Min(t => t.live);
        Out.WriteLine($"the multiplicand held at {seen[0].got:F2} deg while the live affordable "
                      + $"angle moved {drift:F1}");

        Assert.True(drift > 1.0, "the affordable angle did not move, so this proves nothing");

        foreach ((double floor, double got, _) in seen) Assert.Equal(preference, floor / got, 6);
    }

    /// <summary>Reads the angles each pass and corrects nothing, so only the floor is on trial.</summary>
    private sealed class Watch(List<double> floors, List<double> affordable, List<double>? from = null)
        : IcbmFlightRig.IAimLoop
    {
        public double3 Apply(double3 aimNowCci) => aimNowCci;

        public bool IsSteady => true;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci,
                                double stepSeconds)
        {
            floors.Add(program.ArrivalFloorDeg);
            affordable.Add(program.SteepestAffordableArrivalDeg);
            from?.Add(program.ArrivalFloorFromDeg);
        }
    }

    /// <summary>
    /// <b>A budget of zero is not an angle, and latching a fraction of it kills the preference for
    /// the flight.</b> <see cref="ArrivalBudget.SteepestAffordableDeg"/> answers 0.0 — finite — when
    /// no arc at all is affordable from where it is asked, which is the ordinary state of a vehicle
    /// on the pad with the whole burn still to fly.
    ///
    /// <para>Flown 2026-09-02 on <c>SOLVER SCALE 8</c>: three of the four rockets carrying
    /// <c>ArrivalPreference = 0.5</c> latched <c>a 0.0 deg floor, 50% of the 0.0 deg the tanks could
    /// afford</c> straight off the pad and arrived at 17.7, 22.2 and 22.3 degrees — whatever the
    /// unfloored arc gave them. The one that escaped was the only rocket whose pad reach read
    /// Reachable rather than Unknown.</para>
    ///
    /// <para>This fails against a latch guarded on <c>double.IsFinite</c>, which is what it was.</para>
    /// </summary>
    [Fact]
    public void ABudgetOfNothingIsNotLatchedAsAFloorOfNothing()
    {
        IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = 0.5 });

        // A stack with nothing in the tanks, so the budget's honest answer is "no arc is affordable"
        // -- the same shape as a vehicle still on the pad with the whole burn ahead of it.
        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = new double3(DeorbitShot.R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
        };

        rig.Fly(program, Downrange(DeorbitShot.RangeMetres), 0.02, 600.0);

        Out.WriteLine($"steepest affordable {program.SteepestAffordableArrivalDeg:F1} deg, "
                      + $"floor {program.ArrivalFloorDeg:F1}");

        Assert.False(program.SteepestAffordableArrivalDeg > 0.0,
                     "the fixture did not produce an unaffordable shot, so it tests nothing");

        Assert.False(double.IsFinite(program.ArrivalFloorDeg),
                     $"a budget of {program.SteepestAffordableArrivalDeg:F1} deg latched a floor of "
                     + $"{program.ArrivalFloorDeg:F1}");
    }
}
