using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a second of holding the warheads actually costs, measured rather than assumed.
///
/// <para><see cref="PostBoostAim.HoldingCostsMetresPerSecond"/> is the floor under every shot —
/// <c>payback</c> releases once the predicted miss falls under <c>cycle seconds x this</c> — and it
/// ships as one number derived from one flight. These measure it the way that flight did, by
/// differencing the impact of a release now against one a second later, and find it is not a
/// constant at all: it spans a factor of twenty-five over the ranges this mod flies.</para>
/// </summary>
public sealed class HoldingCostTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static readonly BallisticBody Body = new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>The release impulse the bus actually gives a warhead.</summary>
    private const double KickMetresPerSecond = 0.5;

    private const double Step = 0.25;

    private static bool Impact(double3 p, double3 v, out ImpactPredictor.Impact impact)
        => ImpactPredictor.TryPredict(Body, p, v, Step, 3600.0, out impact);

    /// <summary>Integrated rather than solved, because that is how the bus actually coasts.</summary>
    private static (double3 P, double3 V) Coast(double3 p, double3 v, double seconds)
    {
        const double dt = 0.05;

        for (double t = 0.0; t < seconds; t += dt)
        {
            double h = System.Math.Min(dt, seconds - t);
            p += (v += Body.GravityCci(p) * h) * h;
        }

        return (p, v);
    }

    /// <summary>A release state above the air that falls the wanted distance downrange.</summary>
    private static (double3 P, double3 V, double ArrivalDeg) ReleaseFor(double rangeMetres)
    {
        double3 p = new(R + 400_000.0, 0, 0);
        double lo = 1000.0, hi = 8000.0, speed = 0.0;

        for (int i = 0; i < 60; i++)
        {
            speed = 0.5 * (lo + hi);

            if (!Impact(p, new double3(0, speed, 0), out var probe)) { hi = speed; continue; }

            double ground = System.Math.Acos(System.Math.Clamp(
                Vec.Dot(Vec.Unit(p), Vec.Unit(probe.PointCci)), -1, 1)) * R;

            if (ground < rangeMetres) lo = speed; else hi = speed;
        }

        double3 v = new(0, speed, 0);
        Assert.True(Impact(p, v, out var landed));

        double arrival = 90.0 - Vec.AngleBetween(landed.VelocityCci, -landed.PointCci)
                                * 180.0 / System.Math.PI;

        return (p, v, arrival);
    }

    /// <summary>How far the release impulse moves the impact, from a state coasted forward.</summary>
    private static double WorthAt(double3 p0, double3 v0, double seconds)
    {
        var (p, v) = Coast(p0, v0, seconds);

        Assert.True(Impact(p, v, out var plain));
        Assert.True(Impact(p, v + Vec.Unit(v) * KickMetresPerSecond, out var kicked));

        return (plain.GroundFixedPointCci - kicked.GroundFixedPointCci).Length();
    }

    private static double DecayAt(double rangeMetres)
    {
        var (p, v, _) = ReleaseFor(rangeMetres);
        return (WorthAt(p, v, 0.0) - WorthAt(p, v, 106.0)) / 106.0;
    }

    /// <summary>
    /// The shipped constant is a long-range number, and applying it at 2,000 km overcharges every
    /// correction cycle by more than an order of magnitude.
    /// </summary>
    [Fact]
    public void TheShippedHoldingCostIsMoreThanTenTimesTheRealOneAtTwoThousandKilometres()
    {
        double decay = DecayAt(2_000_000.0);

        Assert.InRange(decay, 0.5, 3.0);
        Assert.True(PostBoostAim.HoldingCostsMetresPerSecond > 10.0 * decay,
                    $"the constant is {PostBoostAim.HoldingCostsMetresPerSecond} and the measured "
                    + $"decay at 2,000 km is {decay:F2} m/s");
    }

    /// <summary>
    /// It is a function of the geometry, not a property of the bus — so no single number is right,
    /// which is why the constant is wrong at every range except the one it was taken at.
    /// </summary>
    [Fact]
    public void TheHoldingCostRisesSteeplyWithRange()
    {
        double near = DecayAt(1_000_000.0);
        double mid = DecayAt(4_000_000.0);
        double far = DecayAt(8_000_000.0);

        Assert.True(near < mid, $"{near:F2} should be under {mid:F2}");
        Assert.True(mid < far, $"{mid:F2} should be under {far:F2}");
        Assert.True(far > 8.0 * near, $"{far:F2} should be many times {near:F2}");
    }

    /// <summary>
    /// The derivation agrees with the same measurement taken by hand, so what the vehicle computes
    /// in flight is the number this file's tables are built from.
    /// </summary>
    [Theory]
    [InlineData(1_000_000.0)]
    [InlineData(2_000_000.0)]
    [InlineData(8_000_000.0)]
    public void TheDerivationReproducesTheMeasuredDecay(double rangeMetres)
    {
        var (p, v, _) = ReleaseFor(rangeMetres);

        Assert.True(HoldingCost.TryMeasure(Body, p, v, Vec.Unit(v) * KickMetresPerSecond, Step,
                                           out double derived));

        double byHand = DecayAt(rangeMetres);

        // A second's difference against a hundred and six: the same quantity, and the gap between
        // them is the curvature over that span rather than disagreement.
        Assert.InRange(derived, byHand * 0.5, byHand * 2.0);
    }

    /// <summary>
    /// It answers a different number at a different geometry, which is the whole reason for
    /// measuring rather than typing one.
    /// </summary>
    [Fact]
    public void TheDerivationIsNotAConstant()
    {
        var (pn, vn, _) = ReleaseFor(1_000_000.0);
        var (pf, vf, _) = ReleaseFor(8_000_000.0);

        Assert.True(HoldingCost.TryMeasure(Body, pn, vn, Vec.Unit(vn) * KickMetresPerSecond, Step,
                                           out double near));
        Assert.True(HoldingCost.TryMeasure(Body, pf, vf, Vec.Unit(vf) * KickMetresPerSecond, Step,
                                           out double far));

        Assert.True(far > 5.0 * near, $"{far:F2} m/s at 8,000 km should dwarf {near:F2} at 1,000");
    }

    /// <summary>A probe it cannot fly is refused, never reported as a free correction.</summary>
    [Fact]
    public void AKickItCannotMeasureIsRefusedRatherThanZero()
    {
        var (p, v, _) = ReleaseFor(2_000_000.0);

        Assert.False(HoldingCost.TryMeasure(Body, p, v, Vec.Zero, Step, out double none));
        Assert.True(double.IsNaN(none));

        // An escape trajectory never comes down, so neither probe has an impact to difference.
        Assert.False(HoldingCost.TryMeasure(Body, p, v * 3.0, Vec.Unit(v) * KickMetresPerSecond,
                                            Step, out double escaped));
        Assert.True(double.IsNaN(escaped));
    }

    /// <summary>
    /// <b>The measurement must not depend on the ground under the aim.</b> The holding cost is how
    /// fast the release impulse's leverage decays along the arc; the hillside decides where a round
    /// stops, not that. Sampling it makes the two probes land on different relief and their
    /// difference carries the roughness — flown at 12,902 km as 15.78, 112.40, 150.05 and
    /// 194.82 m/s against a true value near 3, which released a correction over a kilometre out.
    /// </summary>
    [Fact]
    public void TheMeasurementDoesNotDependOnTheGroundUnderTheAim()
    {
        var (p, v, _) = ReleaseFor(8_000_000.0);
        double3 kick = Vec.Unit(v) * KickMetresPerSecond;

        List<double> along = [];

        // Sampled down the coast, which is where the loop asks. On rough ground a terrain-sampling
        // probe swings by tens of metres a second between these; this one must not.
        for (double t = 0.0; t < 120.0; t += 12.0)
        {
            var (q, w) = Coast(p, v, t);

            if (HoldingCost.TryMeasure(Body, q, w, kick, Step, out double got)) along.Add(got);
        }

        Assert.True(along.Count >= 5, $"only {along.Count} probes answered");

        double spread = along.Max() - along.Min();

        Assert.True(spread < 5.0,
                    $"the measurement wandered by {spread:F2} m/s down one coast, so it is reading "
                    + "something other than the arc");
    }
}
