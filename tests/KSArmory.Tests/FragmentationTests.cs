using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class FragmentationTests
{
    private static readonly FragmentSpray Band = new(1500, 0.006, 1500.0, 90.0, 5.0);
    private static readonly double3 Nose = new(1, 0, 0);

    [Fact]
    public void AStillWarheadPutsCountOverTheBandsSolidAngleOnEachSquareMetre()
    {
        FragmentLoad load = Fragmentation.OnPart(Band, Vec.Zero, Nose, Vec.Zero, new double3(0, 10, 0), 1.0, 0.0);

        double band = 2.0 * Math.PI * 2.0 * Math.Sin(5.0 * Math.PI / 180.0);
        Assert.Equal(1500.0 / band / 100.0, load.ExpectedHits, 9);
        Assert.Equal(1500.0, load.ImpactSpeed, 9);
    }

    [Fact]
    public void DensityFallsAsTheSquareOfTheRange()
    {
        double near = Fragmentation.OnPart(Band, Vec.Zero, Nose, Vec.Zero, new double3(0, 10, 0), 1.0, 0.0).ExpectedHits;
        double far = Fragmentation.OnPart(Band, Vec.Zero, Nose, Vec.Zero, new double3(0, 20, 0), 1.0, 0.0).ExpectedHits;

        Assert.Equal(near / 4.0, far, 9);
    }

    [Fact]
    public void APartOutsideTheBandTakesNothingHoweverClose()
    {
        FragmentLoad ahead = Fragmentation.OnPart(Band, Vec.Zero, Nose, Vec.Zero, new double3(3, 3, 0), 1.0, 0.0);

        Assert.Equal(0.0, ahead.ExpectedHits);
    }

    /// <summary>
    /// Fragments thrown square to a warhead closing at <c>v·cot 60°</c> arrive 60 degrees off its nose, so a
    /// part there is hit by a moving warhead and missed by the same one standing still.
    /// </summary>
    [Fact]
    public void TheWarheadsOwnSpeedTiltsTheBandForward()
    {
        double3 part = new double3(Math.Cos(Math.PI / 3.0), Math.Sin(Math.PI / 3.0), 0) * 15.0;
        double3 closing = Nose * (1500.0 / Math.Tan(Math.PI / 3.0));

        Assert.Equal(0.0, Fragmentation.OnPart(Band, Vec.Zero, Nose, Vec.Zero, part, 1.0, 0.0).ExpectedHits);

        FragmentLoad moving = Fragmentation.OnPart(Band, Vec.Zero, Nose, closing, part, 1.0, 0.0);
        Assert.True(moving.ExpectedHits > 0.0);
        Assert.Equal(Math.Sqrt((1500.0 * 1500.0) + Vec.Dot(closing, closing)), moving.ImpactSpeed, 6);
    }

    [Fact]
    public void AirSlowsAFragmentAndVacuumDoesNot()
    {
        Assert.Equal(1500.0, Fragmentation.ImpactSpeed(0.006, 1500.0, 30.0, 0.0));

        // A 6 g cube's decay length in sea-level air is 66 m.
        double slowed = Fragmentation.ImpactSpeed(0.006, 1500.0, 30.0, 1.225);
        Assert.InRange(slowed, 900.0, 1000.0);
    }

    [Fact]
    public void AWarheadWithNoAttitudeThrowsEveryWayAlike()
    {
        FragmentLoad side = Fragmentation.OnPart(Band, Vec.Zero, Vec.Zero, Vec.Zero, new double3(0, 10, 0), 1.0, 0.0);
        FragmentLoad ahead = Fragmentation.OnPart(Band, Vec.Zero, Vec.Zero, Vec.Zero, new double3(10, 0, 0), 1.0, 0.0);

        Assert.Equal(1500.0 / (4.0 * Math.PI * 100.0), side.ExpectedHits, 9);
        Assert.Equal(side.ExpectedHits, ahead.ExpectedHits, 9);
    }

    [Theory]
    [InlineData(0.7)]
    [InlineData(4.0)]
    [InlineData(80.0)]
    public void HitsAverageWhatWasExpected(double expected)
    {
        Random dice = new(1234);
        const int draws = 20_000;
        long total = 0;
        for (int i = 0; i < draws; i++) total += Fragmentation.Hits(expected, dice);

        Assert.Equal(expected, total / (double)draws, expected * 0.03);
    }

    /// <summary>
    /// A warhead bursting at its nearest point throws its band ahead of a target it passes fast; the
    /// matched fuze fires early by as much as puts the target on the band's middle.
    /// </summary>
    [Fact]
    public void AMatchedFuzePutsAFastPassOnTheBand()
    {
        double3 velocity = Nose * 900.0;
        double3 abeam = new(0, 10, 0);

        Assert.Equal(0.0, Fragmentation.OnPart(Band, Vec.Zero, Nose, velocity, abeam, 1.0, 0.0).ExpectedHits);

        double3 lead = Fragmentation.FuzeLead(Band, velocity, Vec.Zero, 10.0);
        Assert.True(Vec.Dot(lead, Nose) < 0.0);
        Assert.True(Fragmentation.OnPart(Band, lead, Nose, velocity, abeam, 1.0, 0.0).ExpectedHits > 0.0);
    }
}
