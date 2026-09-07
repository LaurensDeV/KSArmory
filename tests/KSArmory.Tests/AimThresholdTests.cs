using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The band the aim loop judges an improvement by, which has to shrink with the shot.
///
/// <para><c>AimCorrection.ImprovedByMetres</c> is 250 m flat. That was a quarter of the miss when it
/// was chosen and is twenty-five times the whole of it now: traced 2026-09-07, the aim is 8 and 18 m
/// off at release on two flights landing at 12 and 19 m, with 1 to 4 m made during the entire fall.
/// No cycle can close 250 m at that scale, so the loop always stops on
/// <see cref="PostBoostAim.PassesWithoutImprovement"/> whatever it might have achieved —
/// <c>docs/ACCURACY-PLAN.md</c> 3bw.</para>
/// </summary>
public class AimThresholdTests
{
    [Fact]
    public void TheShippedThresholdIsBlindAtTheMissTheShotNowFlies()
    {
        // The two flown release misses. A cycle would have to more than double the error to
        // register as a change at all, in either direction.
        foreach (double miss in new[] { 8.0, 18.0 })
        {
            Assert.True(AimCorrection.ImprovementThreshold(miss, tracksTheMiss: false) > miss * 10,
                        $"a {miss:F0} m miss is judged against "
                        + $"{AimCorrection.ImprovementThreshold(miss, false):F0} m");
        }
    }

    [Fact]
    public void FollowingTheMissKeepsTheBandBelowIt()
    {
        foreach (double miss in new[] { 8.0, 18.0, 100.0, 1_000.0 })
        {
            double band = AimCorrection.ImprovementThreshold(miss, tracksTheMiss: true);

            Assert.True(band < miss, $"{band:F1} m band against a {miss:F0} m miss");
        }
    }

    /// <summary>
    /// At the scale the constant was calibrated for, the relative form reproduces it — which is what
    /// makes this a re-scaling rather than a new rule.
    /// </summary>
    [Fact]
    public void AtAKilometreItAgreesWithTheConstantItReplaces()
    {
        Assert.Equal(AimCorrection.ImprovedByMetres,
                     AimCorrection.ImprovementThreshold(1_000.0, tracksTheMiss: true), 6);
    }

    /// <summary>
    /// The floor is what the predictor can resolve. Below it the loop would be chasing its own
    /// noise: `ImpactPredictor` is exact to 0.46 m and the round walks 1 to 4 m from its own release
    /// probe over a whole fall.
    /// </summary>
    [Fact]
    public void ItNeverAsksForLessThanTheInstrumentCanSee()
    {
        foreach (double miss in new[] { 0.0, 0.01, 1.0, 3.9 })
        {
            Assert.Equal(AimCorrection.ImprovedByFloorMetres,
                         AimCorrection.ImprovementThreshold(miss, tracksTheMiss: true), 6);
        }
    }

    [Fact]
    public void AnUnreadableMissFallsToTheFloorRatherThanToNothing()
    {
        foreach (double miss in new[] { double.NaN, double.PositiveInfinity, -1.0 })
        {
            Assert.Equal(AimCorrection.ImprovedByFloorMetres,
                         AimCorrection.ImprovementThreshold(miss, tracksTheMiss: true), 6);
        }
    }

    /// <summary>Off is the shipped constant, whatever the miss — every flown night has had this.</summary>
    [Fact]
    public void OffIsExactlyTheOldBehaviour()
    {
        foreach (double miss in new[] { 0.0, 8.0, 1_000.0, double.NaN })
        {
            Assert.Equal(AimCorrection.ImprovedByMetres,
                         AimCorrection.ImprovementThreshold(miss, tracksTheMiss: false), 6);
        }
    }
}
