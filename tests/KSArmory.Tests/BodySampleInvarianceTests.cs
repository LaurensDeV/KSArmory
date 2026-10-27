using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a body sample one frame stale is worth: a pull or ground centre displaced by <c>v·dt</c>,
/// 512 m on a 17.2 ms frame of the carrier's 29.8 km/s. <c>DeorbitShot</c>'s planet does not move,
/// so the rig displaces the centres by hand. docs/KSA-FRAME-ORDER.md §5 has which forms lost.
/// </summary>
public class BodySampleInvarianceTests(ITestOutputHelper Out)
{
    private const double CarrierMetresPerSecond = 29_800.0;

    private const double FlownFrameSeconds = 0.0172;

    private static double Offset => CarrierMetresPerSecond * FlownFrameSeconds;

    private static double Downrange(double3 referenceCci, double3 pointCci, double3 alongCci)
    {
        double metres = DeorbitShot.GroundMetres(referenceCci, pointCci);
        return Vec.Dot(pointCci - referenceCci, alongCci) >= 0.0 ? metres : -metres;
    }

    private static (double3 GroundFixed, double Seconds) Fly(double3 gravityCentre, double3 groundCentre)
    {
        BallisticArc.Solution shot = DeorbitShot.Shot(out double3 from, out _);

        return DeorbitShot.FlyTheRound(
            from, shot.RequiredVelocityCci, FlownFrameSeconds, DeorbitShot.Refresh.AsFlown,
            new DeorbitShot.Ball { CentreEcl = groundCentre }, default, gravityCentre);
    }

    private static (double Moved, double Seconds) Against(double3 gravityCentre,
                                                          double3 groundCentre = default)
    {
        (double3 honest, double honestSeconds) = Fly(Vec.Zero, Vec.Zero);
        (double3 moved, double seconds) = Fly(gravityCentre, groundCentre);

        double3 up = Vec.Unit(honest);
        double3 along = Vec.RejectFrom(moved - honest, up);

        // Nowhere to measure "downrange" from when nothing moved; the caller asserts on zero.
        if (Vec.Len(along) <= 0.0) return (0.0, seconds - honestSeconds);

        return (Downrange(honest, moved, Vec.Unit(along)), seconds - honestSeconds);
    }

    /// <summary>
    /// Gravity's centre displaced and the ground's not: hundreds of metres to kilometres, depending
    /// only on which way the planet is travelling.
    /// </summary>
    [Theory]
    [InlineData(1.0, 0.0, "radially outward")]
    [InlineData(-1.0, 0.0, "radially inward")]
    [InlineData(0.0, 1.0, "along the track")]
    [InlineData(0.0, -1.0, "against the track")]
    public void GravityAloneOnADisplacedCentreMovesTheImpact(double radial, double along, string what)
    {
        double3 offset = (new double3(radial, along, 0) * Offset);

        (double moved, double seconds) = Against(offset);

        Out.WriteLine($"{what,-20} {moved,8:F0} m downrange, {seconds,+7:F3} s");

        Assert.True(Math.Abs(moved) > 50.0,
                    $"a {Offset:F0} m displacement of the pull centre {what} moved the impact "
                    + $"only {moved:F0} m — the rig is not seeing the term");
    }

    /// <summary>
    /// By a factor of thirty, so what matters is how much of the body's travel lies along local up
    /// at the arrival — why <c>WarheadTrace</c> logs it on the arrival's own axes.
    /// </summary>
    [Fact]
    public void OnlyTheRadialShareOfTheDisplacementCosts()
    {
        (double outward, _) = Against(new double3(1, 0, 0) * Offset);
        (double inward, _) = Against(new double3(-1, 0, 0) * Offset);
        (double along, _) = Against(new double3(0, 1, 0) * Offset);

        Out.WriteLine($"{Offset:F0} m of displacement: outward {outward:F0} m, inward {inward:F0} m, "
                      + $"along the track {along:F0} m");

        Assert.True(Math.Abs(along) < Math.Abs(outward) / 10.0,
                    $"along-track cost {along:F0} m against {outward:F0} m radial — the anisotropy "
                    + "this term is diagnosed by has gone");

        // Radial in and out are the same size; the arrival simply moves the other way.
        Assert.InRange(Math.Abs(inward) / Math.Abs(outward), 0.9, 1.1);
    }

    [Fact]
    public void TheCostIsLinearInTheDisplacement()
    {
        double3 unit = new(-1, 0, 0);

        (double one, _) = Against(unit * Offset);
        (double two, _) = Against(unit * (2.0 * Offset));

        Out.WriteLine($"{Offset:F0} m -> {one:F0} m, {2.0 * Offset:F0} m -> {two:F0} m "
                      + $"({two / one:F3}x)");

        Assert.InRange(two / one, 1.8, 2.2);
    }

    /// <summary>
    /// The ground figure is an upper bound: this displaces the sphere for the whole flight, where an
    /// uncorrected stale sample is a sawtooth that vanishes at the last sub-step of every frame.
    /// </summary>
    [Fact]
    public void BothLookupsMoveTheImpactFurtherThanTheFrameDisplacesThem()
    {
        (double gravity, _) = Against(new double3(-1, 0, 0) * Offset);
        (double ground, _) = Against(Vec.Zero, new double3(-1, 0, 0) * Offset);

        Out.WriteLine($"{Offset:F0} m inward: gravity {gravity:F0} m, "
                      + $"ground sphere {ground:F0} m (an upper bound on the shipped sawtooth)");

        Assert.True(Math.Abs(gravity) > Offset, $"gravity moved only {gravity:F0} m");
        Assert.True(Math.Abs(ground) > Offset, $"the ground sphere moved only {ground:F0} m");
    }

    [Fact]
    public void AnUndisplacedSampleCostsNothing()
    {
        (double moved, double seconds) = Against(Vec.Zero);

        Assert.Equal(0.0, moved, 6);
        Assert.Equal(0.0, seconds, 9);
    }
}
