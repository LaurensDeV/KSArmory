using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Three things sample the surface on a ballistic shot and they have to agree: where the aim point
/// is placed, where the prediction stops, and where the round stops.
///
/// <para>Two of them read the height field raw. The third — <see cref="IGroundTest"/>, which is the
/// one the round obeys — passes it through <see cref="GroundSurface.Height"/> first, because a
/// height field answers with terrain and under an ocean that is the <em>seabed</em>. So over water
/// the round stops kilometres of height above the surface the other two believe in, and on a
/// shallow arrival a kilometre of height is eight to eleven kilometres of ground.</para>
///
/// <para>These are pricing tests rather than regression guards: they take both surfaces as
/// arguments, so they hold whether or not the wiring in <c>Ksa/IcbmComputer.cs</c> ever asks
/// <see cref="GroundSurface"/> the same question. <c>docs/KSA-TERRAIN.md</c> has the measurement
/// they are calibrated against — 71.2% of Earth's shipped height cubemap is below its waterline, at
/// a mean depth of 3,776 m.</para>
/// </summary>
public class SurfaceAgreementTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    /// <summary>Mean depth of Earth_Height.ktx2 where it reads below the waterline.</summary>
    private const double MeanOceanDepth = 3_776.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 0.0);

    /// <summary>
    /// A terminal state on a shallow arrival: 5 km up at 7 km/s, so little of the angle is left to
    /// steepen and the arrival is the angle asked for rather than an angle set far away and bent by
    /// the fall. At six degrees this lands at 6.1 and trades 9.3 m of ground for every metre of
    /// height, which is the middle of the eight-to-eleven the flown shot arrives on.
    /// </summary>
    private static void ShallowArrival(double angleDeg, out double3 positionCci, out double3 velocityCci)
    {
        positionCci = new double3(R + 5_000.0, 0, 0);

        double3 up = new(1, 0, 0);
        double3 along = new(0, 1, 0);
        double a = angleDeg * Math.PI / 180.0;
        velocityCci = (along * Math.Cos(a) - up * Math.Sin(a)) * 7_000.0;
    }

    /// <summary>What the height field answers with: terrain, which under an ocean is the bottom.</summary>
    private static Func<double3, double> Seabed(double depth) => _ => R - depth;

    /// <summary>What a round actually meets, which is <see cref="GroundSurface"/> over the same field.</summary>
    private static Func<double3, double> Sea(double depth)
        => _ => R + GroundSurface.Height(-depth, seaLevel: 0.0, hasSea: true);

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    private static double3 Land(Func<double3, double> surface, double angleDeg = 6.0)
    {
        ShallowArrival(angleDeg, out double3 r, out double3 v);
        Assert.True(ImpactPredictor.TryPredict(Earth, r, v, 1.0, 600.0, out ImpactPredictor.Impact hit,
                                               surface));
        return hit.PointCci;
    }

    /// <summary>
    /// The whole of the disagreement, priced on the mean depth of the shipped Earth height map.
    ///
    /// <para>The prediction and the aim point both fly to the seabed; the round bursts on the
    /// waterline above it. Nothing upstream is wrong and nothing downstream can see it — the aim
    /// correction observes the prediction, so a surface only the round knows about is a surface no
    /// amount of correcting reaches.</para>
    /// </summary>
    [Fact]
    public void ThePredictionFliesToTheSeabedAndTheRoundStopsOnTheSeaAboveIt()
    {
        double3 toSeabed = Land(Seabed(MeanOceanDepth));
        double3 toSea = Land(Sea(MeanOceanDepth));

        double gap = GroundMetres(toSeabed, toSea);
        Out.WriteLine($"mean ocean depth {MeanOceanDepth:F0} m -> {gap / 1000.0:F1} km of ground at a six-degree arrival");

        // Priced against the depth rather than as a bare number, because the whole claim is the
        // exchange rate: a metre of disagreement about the surface is many metres of ground.
        Assert.True(gap > 6.0 * MeanOceanDepth, $"expected many times the depth, measured {gap:F0} m");
    }

    /// <summary>
    /// The exchange rate itself, which is what makes a sub-metre surface question first order.
    ///
    /// <para>Everything else about a shallow arrival is priced through this number, so it is worth
    /// having measured rather than assumed: a metre of disagreement about where the surface is
    /// costs about <c>cot(gamma)</c> metres of ground.</para>
    /// </summary>
    [Theory]
    [InlineData(5.0)]
    [InlineData(6.0)]
    [InlineData(20.0)]
    public void AMetreOfHeightIsCotangentMetresOfGround(double angleDeg)
    {
        double3 flat = Land(Seabed(0.0), angleDeg);
        double3 lower = Land(Seabed(1_000.0), angleDeg);

        double perMetre = GroundMetres(flat, lower) / 1_000.0;
        Out.WriteLine($"{angleDeg:F0} deg arrival -> {perMetre:F2} m of ground per metre of height");

        // Steeper is cheaper, always, and by a lot: the whole reason a shallow arrival makes the
        // height field's own resolution matter.
        Assert.True(perMetre > 1.0);
        Assert.True(perMetre < 40.0);
    }

    /// <summary>Steeper arrivals cost less per metre of height, monotonically.</summary>
    [Fact]
    public void AShallowerArrivalPaysMoreForTheSameMetre()
    {
        double Cost(double deg) => GroundMetres(Land(Seabed(0.0), deg), Land(Seabed(1_000.0), deg));


        double shallow = Cost(5.0);
        double middling = Cost(6.0);
        double steep = Cost(20.0);

        Out.WriteLine($"1 km of height: 5 deg {shallow / 1000.0:F1} km, 6 deg {middling / 1000.0:F1} km, "
                      + $"20 deg {steep / 1000.0:F1} km");

        Assert.True(shallow > middling);
        Assert.True(middling > steep);
    }

    /// <summary>
    /// The floor the height field itself puts under any of this: one 16-bit level of Earth's
    /// declared range, which is 0.2985 m and cannot be improved by anything the mod does.
    /// </summary>
    [Fact]
    public void TheHeightFieldsOwnQuantumIsMetresOfGroundAndNoMore()
    {
        const double Quantum = (8_631.0 - -10_930.0) / 65_535.0;

        double3 at = Land(Seabed(0.0));
        double3 oneLevelDown = Land(Seabed(Quantum));

        double gap = GroundMetres(at, oneLevelDown);
        Out.WriteLine($"one R16 level is {Quantum:F4} m of height -> {gap:F2} m of ground at a six-degree arrival");

        Assert.True(gap < 10.0, $"expected a metres-scale floor, measured {gap:F2} m");
    }
}
