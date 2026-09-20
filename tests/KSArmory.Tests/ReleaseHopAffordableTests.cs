using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The walker's acceptance and the trim's refusal, asked as one question.
///
/// <para><b>The flight this exists for.</b> Three of eight rockets walked, and where they did the
/// warheads landed <b>4.00 km</b> out — exactly the target spacing. The walk priced the hop at
/// <b>9.73 m/s</b> against a 10 m/s ceiling and accepted it; by the time <see cref="BusTrim"/> was
/// asked the demand was <b>10.29</b>, because the residual the release had just left on the bus is
/// added to the hop. Over the ceiling the trim refuses the <em>whole</em> pass, so the bus never
/// moved — and three warheads already recorded against the next target left on the old
/// solution.</para>
/// </summary>
public class ReleaseHopAffordableTests(ITestOutputHelper Out)
{
    private const double Ceiling = BusTrim.MaxMetresPerSecond;

    private static double Gate => new IcbmConfig().ReleaseBeforeArrivalSeconds;

    private static ReleaseItinerary.Bus SixThousand
        => new(Gate, CoastSeconds: 1315.0, Warheads: 6);

    // Two targets, three warheads each, the bus aimed at entry 0 and the hop priced explicitly so
    // the flown numbers can be used as they were measured.
    private static ReleaseWalk Walk(double hopMetresPerSecond)
        => ReleaseLoop.Plan(
               [new ReleaseItinerary.Target(3, 8000.0, HopMetresPerSecond: 0.0),
                new ReleaseItinerary.Target(3, 4000.0, HopMetresPerSecond: hopMetresPerSecond)],
               SixThousand, reachMetresPerMetrePerSecond: null, lead: 0);

    /// <summary>
    /// <b>The gap, as the flight measured it.</b> A hop the planner accepts is not a pass the trim
    /// will fly, and the difference is what the bus still owes.
    /// </summary>
    [Fact]
    public void AHopUnderTheCeilingIsNotAPassUnderTheCeiling()
    {
        const double Hop = 9.73;
        const double Owed = 0.56;

        Assert.True(Walk(Hop).Walks, "the planner accepts it: the hop alone is under the ceiling");
        Assert.True(Hop <= Ceiling);

        // And the trim is handed both.
        Assert.Equal(10.29, ReleaseLoop.PassMustFly(Hop, Owed), 6);
        Assert.False(ReleaseLoop.OnePassWillFly(Hop, Owed, Ceiling));

        Out.WriteLine($"hop {Hop:F2} accepted, pass must fly "
                      + $"{ReleaseLoop.PassMustFly(Hop, Owed):F2} of {Ceiling:F0}");
    }

    /// <summary>
    /// <b>What the refusal has to do.</b> The walk ends at the stop the bus is on, and that stop
    /// takes every warhead left — so nothing is recorded against a target the bus never flew to.
    /// </summary>
    [Fact]
    public void ARefusedHopLeavesTheWarheadsWithTheTargetTheBusIsOn()
    {
        ReleaseWalker walker = new();
        walker.Plan(Walk(9.73));

        for (int n = 0; n < 3; n++) walker.WarheadAway();

        ReleaseStep step = walker.Step;

        Assert.True(step.Handover, "the first stop is done and another follows");
        Assert.Equal(9.73, step.NextHopMetresPerSecond, 6);
        Assert.False(ReleaseLoop.OnePassWillFly(step.NextHopMetresPerSecond, 0.56, Ceiling));

        walker.StopHere();

        // Not walking, so the gate goes back to the setting and nothing hands over again.
        Assert.False(walker.Walking);
        Assert.True(walker.Curtailed);
        Assert.False(walker.Done);
        Assert.True(double.IsNaN(walker.GateOverrideSeconds(Gate)));

        // The three still aboard go HERE, and are recorded here.
        Assert.Equal(3, walker.TubesLeft(3));
        Assert.Equal(0, walker.TargetIndex(99));
        Assert.NotEqual(step.NextTarget, walker.TargetIndex(99));

        Out.WriteLine($"curtailed on target {walker.TargetIndex(99)}, "
                      + $"{walker.TubesLeft(3)} warheads still to go there");
    }

    /// <summary>An affordable hop still hands over, so the guard costs a walk that fits nothing.</summary>
    [Fact]
    public void AnAffordableHopStillHandsOver()
    {
        // 2 km of spacing at the flown 411 m per m/s reach, which is the re-fly's geometry.
        const double Hop = 4.87;

        ReleaseWalker walker = new();
        walker.Plan(Walk(Hop));

        for (int n = 0; n < 3; n++) walker.WarheadAway();

        Assert.True(ReleaseLoop.OnePassWillFly(Hop, 0.56, Ceiling));
        Assert.True(ReleaseLoop.OnePassWillFly(Hop, 5.0, Ceiling), "9.87 of 10 still fits");
        Assert.False(ReleaseLoop.OnePassWillFly(Hop, 5.2, Ceiling), "10.07 does not");

        walker.Advance();

        Assert.True(walker.Walking);
        Assert.Equal(1, walker.TargetIndex(99));
        Assert.Equal(3, walker.TubesLeft(3));
    }

    /// <summary>
    /// The ceiling is the trim's own, and a loop above it that allows more raises it — never lowers
    /// it below <see cref="BusTrim.MaxMetresPerSecond"/>.
    /// </summary>
    [Fact]
    public void TheCeilingIsTheOneTheTrimWillUse()
    {
        Assert.Equal(Ceiling, BusTrim.CeilingFor(double.NaN));
        Assert.Equal(Ceiling, BusTrim.CeilingFor(0.0));
        Assert.Equal(Ceiling, BusTrim.CeilingFor(-5.0));
        Assert.Equal(Ceiling, BusTrim.CeilingFor(4.0));
        Assert.Equal(44.0, BusTrim.CeilingFor(44.0));

        // So a budget-derived ceiling widens what a hop may cost, exactly as it widens the trim.
        Assert.True(ReleaseLoop.OnePassWillFly(9.73, 0.56, 44.0));
    }

    /// <summary>A demand nobody can read is no demand: an unarmed trim must not refuse every hop.</summary>
    [Fact]
    public void AnUnreadableResidualRefusesNothing()
    {
        Assert.Equal(9.73, ReleaseLoop.PassMustFly(9.73, double.NaN), 6);
        Assert.True(ReleaseLoop.OnePassWillFly(9.73, double.NaN, Ceiling));

        // And a negative reading is not a discount.
        Assert.Equal(9.73, ReleaseLoop.PassMustFly(9.73, -4.0), 6);
    }
}
