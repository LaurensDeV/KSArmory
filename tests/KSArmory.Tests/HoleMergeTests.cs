using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class HoleMergeTests
{
    private const double TwentyMm = 0.015;

    [Fact]
    public void TwoHolesAHandWidthApartAreOneOpening()
    {
        double3[] at = [new(0, 0, 0), new(0.1, 0, 0)];
        int[] lead = HoleMerge.Groups(at, [TwentyMm, TwentyMm], [0, 0]);

        Assert.Equal([0, 0], lead);
    }

    [Fact]
    public void HolesAMetreApartLeakApart()
    {
        double3[] at = [new(0, 0, 0), new(1, 0, 0)];

        Assert.Equal([0, 1], HoleMerge.Groups(at, [TwentyMm, TwentyMm], [0, 0]));
    }

    [Fact]
    public void HolesInDifferentSkinsNeverMerge()
    {
        double3[] at = [new(0, 0, 0), new(0.05, 0, 0)];

        Assert.Equal([0, 1], HoleMerge.Groups(at, [TwentyMm, TwentyMm], [0, 1]));
    }

    /// <summary>A row of hits tears into one, though its ends are further apart than any two may be.</summary>
    [Fact]
    public void ARowOfHolesChainsIntoOne()
    {
        double3[] at = [new(0, 0, 0), new(0.12, 0, 0), new(0.24, 0, 0), new(0.36, 0, 0)];

        Assert.Equal([0, 0, 0, 0], HoleMerge.Groups(at, [TwentyMm, TwentyMm, TwentyMm, TwentyMm], [0, 0, 0, 0]));
    }

    /// <summary>The stream is kept under its group's first hole, so a later hit joining it leaves it in place.</summary>
    [Fact]
    public void AHoleJoiningLaterNeverTakesTheLead()
    {
        double3[] at = [new(0.3, 0, 0), new(0, 0, 0), new(0.15, 0, 0)];

        Assert.Equal([0, 0, 0], HoleMerge.Groups(at, [TwentyMm, TwentyMm, TwentyMm], [0, 0, 0]));
    }

    [Fact]
    public void AnOpeningKeepsItsHolesAreaAtItsAreaWeightedMiddle()
    {
        double3[] at = [new(0, 0, 0), new(0.1, 0, 0)];
        double[] radius = [0.03, 0.01];
        int[] lead = HoleMerge.Groups(at, radius, [0, 0]);

        (double3 middle, double r) = HoleMerge.Opening(at, radius, lead, 0);

        Assert.Equal(Math.Sqrt((0.03 * 0.03) + (0.01 * 0.01)), r, 12);
        Assert.Equal(0.1 * 0.0001 / 0.001, middle.X, 12);
    }
}
