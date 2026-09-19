using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which parts of a craft a burst breaks.
///
/// <para>The rule that matters is that a part's own strength sets the reach, so the same warhead
/// takes a radome and leaves the tank beside it — and that the calibration the mod already flew is
/// unchanged for a part of reference strength.</para>
/// </summary>
public class BlastDamageTests
{
    // 29.8 km/s of ecliptic motion, oblique to everything, which is the general case.
    private static readonly double3 Carrier = new(29_800 * 0.6, 29_800 * 0.8, 0);

    private static MunitionProfile Warhead20Kg() => new()
    {
        Name = "test",
        DisplayName = "test",
        ChargeKg = 20f,
    };

    /// <summary>
    /// The whole point of the anchor: a part of reference strength fails at exactly the radius the
    /// mod already killed whole craft at, so nothing about the 57E6's flown calibration moves.
    /// </summary>
    [Fact]
    public void AReferenceStrengthPartFailsAtTheLethalRadius()
    {
        Assert.Equal(Warhead.LethalRadius(20.0),
                     BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals), 9);
    }

    [Fact]
    public void AWeakerPartFailsFurtherOutAndAStrongerOneNearer()
    {
        double reference = BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals);

        Assert.True(BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals / 8.0) > reference);
        Assert.True(BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals * 8.0) < reference);
    }

    /// <summary>
    /// Cube-root scaling on the strength, the same law the charge obeys. Eight times the strength
    /// is half the reach, exactly as eight times the charge is twice it — which is what makes this
    /// one law re-anchored rather than a second damage model with its own curve.
    /// </summary>
    [Fact]
    public void StrengthObeysTheSameCubeRootTheChargeDoes()
    {
        double reference = BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals);

        Assert.Equal(reference / 2.0,
                     BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals * 8.0), 9);

        Assert.Equal(reference * 2.0, Warhead.LethalRadius(20.0 * 8.0), 9);
    }

    /// <summary>
    /// The outer radius is what the panel, the overlay and the near-miss line all describe the
    /// weapon by. A damage rule reaching past it would make every one of them lie, so the weakest
    /// part the engine can derive still fails inside it.
    /// </summary>
    [Fact]
    public void NothingFailsOutsideTheBlastRadius()
    {
        MunitionProfile m = Warhead20Kg();

        // The engine clamps a derived tolerance to 0.1 MPa at the bottom; go an order past it.
        Assert.True(BlastDamage.FailureRadius(m.ChargeKg, 1.0e4) <= m.BlastRadius);
        Assert.True(BlastDamage.FailureRadius(m.ChargeKg, 1.0e5) <= m.BlastRadius);
    }

    /// <summary>
    /// A tolerance the engine could not answer for is the reference part, not an invulnerable one
    /// and not one made of paper. Reading a zero as "no strength" would shred a craft the engine
    /// simply had not finished building.
    /// </summary>
    [Fact]
    public void AnUnreadableToleranceIsTheReferencePart()
    {
        double reference = BlastDamage.FailureRadius(20.0, BlastDamage.ReferencePascals);

        Assert.Equal(reference, BlastDamage.FailureRadius(20.0, 0.0), 9);
        Assert.Equal(reference, BlastDamage.FailureRadius(20.0, -1.0), 9);
        Assert.Equal(reference, BlastDamage.FailureRadius(20.0, double.NaN), 9);
    }

    [Fact]
    public void AChargeOfNothingReachesNothing()
    {
        Assert.Equal(0.0, BlastDamage.FailureRadius(0.0, BlastDamage.ReferencePascals));
    }

    // ---- The sweep -------------------------------------------------------

    private static DamageablePart PartAt(int index, double metres, double pascals)
        => new(index, new double3(metres, 0, 0), 0.0, pascals);

    /// <summary>
    /// The case the whole feature is for: one burst, two parts side by side, and only the weak one
    /// goes. A rule keyed on the craft cannot express this at all.
    /// </summary>
    [Fact]
    public void TheWeakPartGoesAndTheStrongOneBesideItSurvives()
    {
        MunitionProfile m = Warhead20Kg();

        // Both at 1.5x the lethal radius. The reference part survives there; a part an order
        // weaker does not.
        double range = Warhead.LethalRadius(m.ChargeKg) * 1.5;

        DamageablePart[] parts =
        [
            PartAt(0, range, BlastDamage.ReferencePascals / 10.0),
            PartAt(1, range, BlastDamage.ReferencePascals),
        ];

        List<int> failed = [];
        BlastDamage.Sweep(Vec.Zero, 0.0, Vec.Zero, parts, m, failed);

        Assert.Equal([0], failed);
    }

    /// <summary>
    /// The gap is to the part's surface. A long tank whose centre is out of reach is still broken
    /// by a burst against its skin, which is the same rule the craft sweep uses and the reason a
    /// part carries a radius at all.
    /// </summary>
    [Fact]
    public void ThePartsOwnExtentCounts()
    {
        MunitionProfile m = Warhead20Kg();
        double beyond = Warhead.LethalRadius(m.ChargeKg) + 12.0;

        List<int> failed = [];

        BlastDamage.Sweep(Vec.Zero, 0.0, Vec.Zero,
                          [new DamageablePart(0, new double3(beyond, 0, 0), 0.0,
                                              BlastDamage.ReferencePascals)],
                          m, failed);
        Assert.Empty(failed);

        BlastDamage.Sweep(Vec.Zero, 0.0, Vec.Zero,
                          [new DamageablePart(0, new double3(beyond, 0, 0), 15.0,
                                              BlastDamage.ReferencePascals)],
                          m, failed);
        Assert.Equal([0], failed);
    }

    /// <summary>
    /// The sample is carried to the burst's instant, exactly as the craft sweep carries it. A part
    /// closing at speed is nearer than the frame-start sample says, and across a fuse radius that
    /// is the difference between a hit and nothing.
    /// </summary>
    [Fact]
    public void ThePartsAreCarriedForwardToTheBurst()
    {
        MunitionProfile m = Warhead20Kg();
        double outside = Warhead.LethalRadius(m.ChargeKg) + 15.0;

        List<int> failed = [];

        // Still 15 m clear at the frame start.
        BlastDamage.Sweep(Vec.Zero, 0.0, Vec.Zero, [PartAt(0, outside, BlastDamage.ReferencePascals)],
                          m, failed);
        Assert.Empty(failed);

        // 20 ms of closing at 1000 m/s brings it inside.
        BlastDamage.Sweep(Vec.Zero, 0.02, new double3(-1000, 0, 0),
                          [PartAt(0, outside, BlastDamage.ReferencePascals)], m, failed);
        Assert.Equal([0], failed);
    }

    /// <summary>
    /// The frame rule, which every sweep in this mod obeys: both terms are ecliptic, so the 29.8
    /// km/s they share cancels. Add the planet's motion to the whole scene and the same parts fail.
    /// </summary>
    [Fact]
    public void SharedMotionDoesNotReachTheVerdict()
    {
        MunitionProfile m = Warhead20Kg();
        const double since = 1.0 / 60.0;

        double3 burst = new(1.5e11, 6.371e6, 0);
        double3 velocity = new(-900, 40, 0);

        DamageablePart[] still =
        [
            new(0, burst + new double3(12, 0, 0), 0.0, BlastDamage.ReferencePascals),
            new(1, burst + new double3(90, 0, 0), 0.0, BlastDamage.ReferencePascals),
        ];

        DamageablePart[] carried =
        [
            new(0, still[0].PositionEcl, 0.0, still[0].CrashTolerancePascals),
            new(1, still[1].PositionEcl, 0.0, still[1].CrashTolerancePascals),
        ];

        List<int> a = [];
        List<int> b = [];

        BlastDamage.Sweep(burst, since, velocity, still, m, a);
        BlastDamage.Sweep(burst + (Carrier * since), since, velocity + Carrier, carried, m, b);

        Assert.Equal(a, b);
    }

    /// <summary>
    /// Indices are the caller's, not the sweep's own ordering. The caller hands over whatever
    /// subset of a part tree it could read, and gets back handles it can act on.
    /// </summary>
    [Fact]
    public void TheIndicesHandedBackAreTheOnesHandedIn()
    {
        MunitionProfile m = Warhead20Kg();
        double inside = Warhead.LethalRadius(m.ChargeKg) * 0.5;

        List<int> failed = [];
        BlastDamage.Sweep(Vec.Zero, 0.0, Vec.Zero,
                          [PartAt(7, inside, BlastDamage.ReferencePascals),
                           PartAt(3, inside, BlastDamage.ReferencePascals)],
                          m, failed);

        Assert.Equal([7, 3], failed);
    }
}
