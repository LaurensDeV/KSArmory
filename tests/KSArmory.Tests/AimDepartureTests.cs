using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What an impact predicted from inside the air says about the aim, which is nothing.
///
/// <para><c>docs/ACCURACY-PLAN.md</c> 3ah. While the engines are lit the aim correction's only
/// observer departs from the guidance's <em>projected</em> cutoff, and before the vehicle has flown
/// that projection is the pad. The arc is then flown with drag from sea level, and the miss it
/// reports is the atmosphere rather than the aim.</para>
/// </summary>
public class AimDepartureTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double3 Downrange(double metres)
        => new(DeorbitShot.R * Math.Cos(metres / DeorbitShot.R),
               DeorbitShot.R * Math.Sin(metres / DeorbitShot.R), 0);

    /// <summary>
    /// The same arc, departed from sea level and from above the air. The vacuum solution is
    /// identical by construction — only the altitude it is flown from differs.
    /// </summary>
    [Fact]
    public void ADepartureAtSeaLevelReportsTheAtmosphereAsMiss()
    {
        double3 aim = Downrange(4_000_000.0);

        Out.WriteLine($"{"departs",12}{"density",12}{"observed?",12}{"miss km",12}");

        double atSeaLevel = double.NaN, aboveTheAir = double.NaN;

        foreach (double altitude in new[] { 0.0, 40_000.0, 74_000.0, 150_000.0, 300_000.0 })
        {
            double3 from = new(DeorbitShot.R + altitude, 0, 0);

            // One transfer, solved in vacuum, so every row is aimed at exactly the same place.
            if (!BallisticArc.TrySolve(Earth, from, aim, 1_200.0, out BallisticArc.Solution arc))
            {
                continue;
            }

            double density = DeorbitShot.DensityAt(from);
            bool observed = AimCorrection.DepartureIsWorthObserving(density);

            string missKm = "-";

            if (ImpactPredictor.TryPredict(
                    Earth, from, arc.RequiredVelocityCci, 1.0, ImpactPredictor.DefaultMaxSeconds,
                    out ImpactPredictor.Impact hit, null, null,
                    new ImpactPredictor.Drag(DeorbitShot.DensityAt, DeorbitShot.Warhead)))
            {
                double miss = DeorbitShot.GroundMetres(hit.GroundFixedPointCci, aim) / 1000.0;
                missKm = $"{miss:F1}";

                if (altitude == 0.0) atSeaLevel = miss;
                if (altitude == 300_000.0) aboveTheAir = miss;
            }

            Out.WriteLine($"{altitude / 1000.0,12:F0}{density,12:E1}{observed.ToString(),12}{missKm,12}");
        }

        // The fault: an aim that is exactly right reads as a miss of hundreds of kilometres purely
        // because the arc was flown from the bottom of the atmosphere.
        Assert.True(atSeaLevel > 100.0,
                    $"a sea-level departure reported only {atSeaLevel:F1} km, so this tests nothing");

        Assert.True(aboveTheAir < atSeaLevel / 10.0,
                    $"above the air {aboveTheAir:F1} km against {atSeaLevel:F1} at sea level");
    }

    /// <summary>
    /// The gate itself. Sea level is refused, a coast is allowed, and a body with no atmosphere is
    /// allowed everywhere — there is nothing there to corrupt the arc.
    /// </summary>
    [Theory]
    [InlineData(1.0, false)]            // the pad
    [InlineData(1e-2, false)]           // still in it
    [InlineData(Medium.NoticeableDensity, false)]
    [InlineData(1e-5, true)]            // above the air
    [InlineData(0.0, true)]             // vacuum, or an airless body
    [InlineData(double.NaN, false)]     // nothing was measured, so nothing is claimed
    public void TheGateAsksWhetherTheAirCanStillMoveTheAnswer(double density, bool expected)
        => Assert.Equal(expected, AimCorrection.DepartureIsWorthObserving(density));
}
