using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Where each rocket of a group aims, so the first warhead down does not take the sample with it.
/// </summary>
public class AimSpreadTests
{
    private const double EarthRadius = 6_371_000.0;

    // The Mk 21 as shipped: 20 kt scales to 2.0 km lethal through Warhead's cube root.
    private static double Mk21Lethal => Warhead.LethalRadius(Arsenal.ReentryVehicleMk21.ChargeKg);

    [Fact]
    public void TheSpacingClearsTheLethalRadiusOfTheRoundBeingSpread()
    {
        double spacing = AimSpread.SpacingMetres(Mk21Lethal);

        Assert.True(spacing > Mk21Lethal,
                    $"{spacing:F0} m of spacing does not clear a {Mk21Lethal:F0} m kill radius");

        // The flown requirement, not merely "bigger": a burst at one point must not reach a warhead
        // arriving at the next with both of them missing. 0.4 km was the worst seen at 2,000 km.
        Assert.True(spacing > Mk21Lethal + 2.0 * 400.0);
    }

    [Fact]
    public void AYieldTheSpreadWasNotTunedForStillClearsItself()
    {
        // 300 kt, which ReentryVehicleMk21 says in as many words is one number away. A constant
        // chosen against 20 kt would put every group back inside the next one's kill radius.
        double bigLethal = Warhead.LethalRadius(300_000_000.0);

        Assert.True(AimSpread.SpacingMetres(bigLethal) > bigLethal);
    }

    [Fact]
    public void AdjacentAimPointsAreOneSpacingApartOnTheGround()
    {
        double spacing = AimSpread.SpacingMetres(Mk21Lethal);

        for (int i = 0; i < 7; i++)
        {
            var a = AimSpread.For(10.622, -80.604, i, 8, spacing, 90.0, EarthRadius);
            var b = AimSpread.For(10.622, -80.604, i + 1, 8, spacing, 90.0, EarthRadius);

            double gap = AimSpread.GroundMetresBetween(a.LatitudeDeg, a.LongitudeDeg,
                                                       b.LatitudeDeg, b.LongitudeDeg, EarthRadius);

            Assert.Equal(spacing, gap, 1.0);
        }
    }

    [Fact]
    public void NoTwoRocketsInAGroupLandInsideEachOthersKillRadius()
    {
        double spacing = AimSpread.SpacingMetres(Mk21Lethal);

        // The flown case: eight rockets, 2,000 km, and the worst miss ever recorded at that range.
        const double WorstMiss = 400.0;

        for (int i = 0; i < 8; i++)
        {
            for (int j = i + 1; j < 8; j++)
            {
                var a = AimSpread.For(10.622, -80.604, i, 8, spacing, 90.0, EarthRadius);
                var b = AimSpread.For(10.622, -80.604, j, 8, spacing, 90.0, EarthRadius);

                double gap = AimSpread.GroundMetresBetween(a.LatitudeDeg, a.LongitudeDeg,
                                                           b.LatitudeDeg, b.LongitudeDeg, EarthRadius);

                Assert.True(gap - 2.0 * WorstMiss > Mk21Lethal,
                            $"rockets {i} and {j} aim {gap / 1000.0:F1} km apart, which two "
                            + $"{WorstMiss:F0} m misses close to inside a "
                            + $"{Mk21Lethal / 1000.0:F1} km kill radius");
            }
        }
    }

    [Fact]
    public void TheFirstRocketLandsWhereTheOperatorAimed()
    {
        var first = AimSpread.For(10.622, -80.604, 0, 8, 12_000.0, 90.0, EarthRadius);

        Assert.Equal(10.622, first.LatitudeDeg, 9);
        Assert.Equal(-80.604, first.LongitudeDeg, 9);
    }

    [Fact]
    public void ASingleRocketIsNotSpreadAtAll()
    {
        var only = AimSpread.For(10.622, -80.604, 0, 1, 12_000.0, 90.0, EarthRadius);

        Assert.Equal(10.622, only.LatitudeDeg, 9);
        Assert.Equal(-80.604, only.LongitudeDeg, 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void ASpacingThatCannotBeUsedGivesTheAimBackUnchanged(double spacing)
    {
        var aim = AimSpread.For(10.622, -80.604, 3, 8, spacing, 90.0, EarthRadius);

        Assert.Equal(10.622, aim.LatitudeDeg, 9);
        Assert.Equal(-80.604, aim.LongitudeDeg, 9);
    }

    [Fact]
    public void AnUnusableBearingGivesTheAimBackRatherThanDisplacingItSomewhere()
    {
        var aim = AimSpread.For(10.622, -80.604, 3, 8, 12_000.0, double.NaN, EarthRadius);

        Assert.Equal(10.622, aim.LatitudeDeg, 9);
        Assert.Equal(-80.604, aim.LongitudeDeg, 9);
    }

    [Fact]
    public void TheSpreadIsSquareToTheShotSoDownrangeBarelyMoves()
    {
        // The flown scenario: due south from Cape Canaveral, 2,000 km.
        const double PadLat = 28.608, PadLon = -80.604;
        const double AimLat = 10.622, AimLon = -80.604;

        double bearing = AimSpread.CrossRangeBearingDeg(PadLat, PadLon, AimLat, AimLon);
        double spacing = AimSpread.SpacingMetres(Mk21Lethal);

        double straight = AimSpread.GroundMetresBetween(PadLat, PadLon, AimLat, AimLon, EarthRadius);

        var far = AimSpread.For(AimLat, AimLon, 7, 8, spacing, bearing, EarthRadius);
        double displaced = AimSpread.GroundMetresBetween(PadLat, PadLon,
                                                         far.LatitudeDeg, far.LongitudeDeg,
                                                         EarthRadius);

        // Square to the range means the extra distance is second order: 84 km across a 2,000 km
        // shot is under two. A spread laid *along* the range would add the whole 84.
        Assert.True(Math.Abs(displaced - straight) < 2_000.0,
                    $"the outermost rocket flies {(displaced - straight) / 1000.0:F1} km further, "
                    + "which is along the range rather than across it");
    }

    [Fact]
    public void TheCrossRangeBearingIsSquareToTheShot()
    {
        double shot = AimSpread.BearingDeg(28.608, -80.604, 10.622, -80.604);
        double cross = AimSpread.CrossRangeBearingDeg(28.608, -80.604, 10.622, -80.604);

        Assert.Equal(180.0, shot, 6);   // due south
        Assert.Equal(270.0, cross, 6);  // due west, which is square to it
    }

    [Fact]
    public void AShotWithNoBearingIsRefusedRatherThanGuessedAt()
    {
        Assert.True(double.IsNaN(AimSpread.CrossRangeBearingDeg(10.0, 20.0, 10.0, 20.0)));
    }

    [Fact]
    public void DueNorthSurvivesTheDegenerateCheck()
    {
        // The east component of a due-north bearing is exactly zero, so a guard written on that
        // alone refuses the commonest bearing there is.
        Assert.Equal(0.0, AimSpread.BearingDeg(10.0, 20.0, 30.0, 20.0), 6);
    }

    [Fact]
    public void WalkingOutAndBackReturnsToTheStart()
    {
        var out_ = AimSpread.Along(10.622, -80.604, 37.0, 250_000.0, EarthRadius);
        double back = AimSpread.BearingDeg(out_.LatitudeDeg, out_.LongitudeDeg, 10.622, -80.604);
        var home = AimSpread.Along(out_.LatitudeDeg, out_.LongitudeDeg, back, 250_000.0, EarthRadius);

        Assert.Equal(10.622, home.LatitudeDeg, 6);
        Assert.Equal(-80.604, home.LongitudeDeg, 6);
    }

    [Fact]
    public void ASpreadOverAPoleDoesNotDivideByACosineOfNothing()
    {
        // The cheap metres/(R cos lat) form goes to infinity here. The great-circle one walks over
        // the pole and comes down the far side, which is a real place.
        var aim = AimSpread.For(89.99, 0.0, 1, 2, 100_000.0, 0.0, EarthRadius);

        Assert.True(double.IsFinite(aim.LatitudeDeg) && double.IsFinite(aim.LongitudeDeg));
        Assert.InRange(aim.LatitudeDeg, -90.0, 90.0);
        Assert.InRange(aim.LongitudeDeg, -180.0, 180.0);

        double gap = AimSpread.GroundMetresBetween(89.99, 0.0, aim.LatitudeDeg, aim.LongitudeDeg,
                                                   EarthRadius);
        Assert.Equal(100_000.0, gap, 1.0);
    }

    [Fact]
    public void TheHaversineAgreesWithAKnownGroundDistance()
    {
        // The flown shot: 28.608N to 10.622N down one meridian is 17.986 degrees of arc, which
        // is the 2,000 km every short-range run reports.
        double metres = AimSpread.GroundMetresBetween(28.608, -80.604, 10.622, -80.604, EarthRadius);

        Assert.Equal(17.986 * Math.PI / 180.0 * EarthRadius, metres, 1.0);
        Assert.Equal(2_000_000.0, metres, 100.0);
    }
}
