using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Charge to reach. The cube root is the whole content: doubling a warhead multiplies its reach
/// by 1.26, not by 2, and writing three independent radii instead lets a profile describe a
/// warhead whose lethal radius exceeds its blast radius.
/// </summary>
public class WarheadTests
{
    /// <summary>
    /// The numbers the 57E6 was flown and tested with. These pin the scaled distances: change
    /// them and every engagement that has been verified in flight changes with them.
    /// </summary>
    [Fact]
    public void TheTestedMissileNumbersAreUnchanged()
    {
        Assert.Equal(20.0, Warhead.LethalRadius(20.0), 1);
        Assert.Equal(60.0, Warhead.BlastRadius(20.0), 1);
    }

    /// <summary>Eight times the charge is exactly twice the reach, not eight times.</summary>
    [Fact]
    public void ReachGoesAsTheCubeRoot()
    {
        double small = Warhead.LethalRadius(1.0);
        double big = Warhead.LethalRadius(8.0);

        Assert.Equal(2.0, big / small, 6);
        Assert.Equal(2.0, Warhead.BlastRadius(8.0) / Warhead.BlastRadius(1.0), 6);
        Assert.Equal(2.0, Warhead.EffectScale(8.0) / Warhead.EffectScale(1.0), 6);
    }

    /// <summary>
    /// Ordering that cannot be violated, whatever the charge. Three free fields could; one
    /// figure and a shared law cannot, which is the reason for the change.
    /// </summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.16)]
    [InlineData(20.0)]
    [InlineData(500.0)]
    public void TheFireballIsInsideTheLethalRadiusWhichIsInsideTheBlast(double kg)
    {
        Assert.True(Warhead.FireballRadius(kg) < Warhead.LethalRadius(kg));
        Assert.True(Warhead.LethalRadius(kg) < Warhead.BlastRadius(kg));
    }

    /// <summary>The authored effect is drawn at its reference charge, unscaled.</summary>
    [Fact]
    public void TheReferenceChargeNeedsNoScaling()
        => Assert.Equal(1.0, Warhead.EffectScale(Warhead.ReferenceChargeKg), 9);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void NoChargeReachesNothing(double kg)
    {
        Assert.Equal(0.0, Warhead.LethalRadius(kg));
        Assert.Equal(0.0, Warhead.BlastRadius(kg));
        Assert.Equal(0.0, Warhead.EffectScale(kg));
    }

    /// <summary>
    /// The cannon keeps the 4 m lethal radius it was tuned with. Its blast radius is a
    /// consequence rather than a choice now, and that is the one number this change moves.
    /// </summary>
    [Fact]
    public void TheCannonKeepsItsLethalRadius()
    {
        Assert.Equal(4.0, Arsenal.Cannon30Mm.LethalRadius, 1);
        Assert.True(Arsenal.Cannon30Mm.BlastRadius > Arsenal.Cannon30Mm.LethalRadius);
    }
}
