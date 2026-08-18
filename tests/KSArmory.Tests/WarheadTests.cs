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
    /// The 57E6's calibrated pair. These pin the scaled distances: change them and every
    /// engagement the mod has been tuned against changes with them.
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

        // Above the floor, the drawn size follows the same law.
        Assert.Equal(2.0, Warhead.EffectScale(64.0) / Warhead.EffectScale(8.0), 6);
    }

    /// <summary>
    /// A cannon shell scales to 0.2 by the cube root, which draws 5 cm particles — proportionate
    /// and invisible. The floor is on the drawing only; what the shell destroys is untouched.
    /// </summary>
    [Fact]
    public void ASmallWarheadIsStillDrawnLargeEnoughToSee()
    {
        double shell = Arsenal.Cannon30Mm.ChargeKg;

        Assert.True(Math.Cbrt(shell / Warhead.ReferenceChargeKg) < Warhead.MinimumEffectScale,
                    "the shell should be below the floor, or this test proves nothing");
        Assert.Equal(Warhead.MinimumEffectScale, Warhead.EffectScale(shell), 9);

        // The radii are the physics and keep the law exactly.
        Assert.Equal(4.0, Warhead.LethalRadius(shell), 1);
    }

    /// <summary>
    /// The mirror of the floor. A nuclear charge scales past any size the authored emitter can be
    /// stretched to, and the scale multiplies particle size, speed and spawn radius together.
    ///
    /// <para>Capped for drawing only: what it destroys keeps the law exactly, so the burst is drawn
    /// far smaller than its lethal radius. That is deliberate and is the trade recorded on
    /// <see cref="Warhead.MaximumEffectScale"/>.</para>
    /// </summary>
    [Fact]
    public void ANuclearWarheadIsNotDrawnAsAThousandTimesTheEmitter()
    {
        // Well past the ceiling rather than the shipped device, which sits under it on purpose:
        // the cap is there to stop a megaton, not to shrink a tactical bomb.
        double huge = Warhead.ReferenceChargeKg * Math.Pow(Warhead.MaximumEffectScale + 10.0, 3.0);

        Assert.Equal(Warhead.MaximumEffectScale, Warhead.EffectScale(huge), 9);

        // Untouched by the cap: the law still says what it kills.
        Assert.Equal(Warhead.LethalScaledDistance * Math.Cbrt(huge), Warhead.LethalRadius(huge), 6);
    }

    /// <summary>
    /// The shipped device is drawn at its own size rather than at the ceiling. Pinned because the
    /// first cap shipped low enough to flatten it, and a burst that is quietly the same size as
    /// every other burst is exactly the bug that is hard to see.
    /// </summary>
    [Fact]
    public void ATacticalNuclearBombIsNotFlattenedByTheCeiling()
    {
        double nuke = Arsenal.NukeB61.ChargeKg;

        Assert.Equal(Math.Cbrt(nuke / Warhead.ReferenceChargeKg), Warhead.EffectScale(nuke), 9);
        Assert.True(Warhead.EffectScale(nuke) > Warhead.EffectScale(Arsenal.Missile57E6.ChargeKg) * 5.0,
                    "a nuclear burst should be visibly larger than a conventional warhead's");
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
