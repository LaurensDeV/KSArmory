using Xunit;

namespace KSArmory.Tests;

public class EngineDamageAndFireTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.8, 1.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.2, 0.0)]
    [InlineData(0.05, 0.0)]
    public void AnEngineLosesThrustBetweenItsTwoThresholds(double health, double share)
        => Assert.Equal(share, EngineDamage.ThrustShare(health), 9);

    [Fact]
    public void TheGimbalGoesBeforeTheEngineStops()
    {
        Assert.False(EngineDamage.GimbalLost(0.6));
        Assert.True(EngineDamage.GimbalLost(0.4));
        Assert.False(EngineDamage.Dead(0.4));
        Assert.True(EngineDamage.Dead(0.2));
    }

    [Fact]
    public void MoreHitsMakeAFireLikelierAndNoneMakeNone()
    {
        Assert.Equal(0.0, PartFire.IgnitionChance(0, 0.35));
        Assert.Equal(0.35, PartFire.IgnitionChance(1, 0.35), 9);
        Assert.Equal(1.0 - Math.Pow(0.92, 10), PartFire.IgnitionChance(10, 0.08), 9);
        Assert.Equal(0.0, PartFire.IgnitionChance(10, 0.0));
    }

    [Fact]
    public void AFireBurnsItsPartOutInTwentyToAHundredSeconds()
    {
        PartFire fires = new();
        object part = new();
        Assert.True(fires.TryIgnite(part, 1, 1.0, new Random(3)));

        double perSecond = fires.Cost(part, 1.0);
        Assert.InRange(1.0 / perSecond, 20.0, 100.0);
        Assert.Equal(perSecond * 0.25, fires.Cost(part, 0.25), 12);
    }

    [Fact]
    public void APartIsLitOnceAndPuttingItOutStopsTheCost()
    {
        PartFire fires = new();
        object part = new();
        Random dice = new(5);

        Assert.True(fires.TryIgnite(part, 1, 1.0, dice));
        Assert.False(fires.TryIgnite(part, 1, 1.0, dice));

        fires.PutOut(part);
        Assert.False(fires.IsBurning(part));
        Assert.Equal(0.0, fires.Cost(part, 1.0));
    }

    [Fact]
    public void ADamagedPartIsListedWithWhatIsLeftAndAnUntouchedOneIsNot()
    {
        PartHealth ledger = new();
        object hurt = new(), whole = new();
        ledger.Hit(hurt, 0.3, 1.0);
        ledger.Of(whole);

        (object Part, double Health)[] damaged = ledger.Damaged().ToArray();
        Assert.Single(damaged);
        Assert.Same(hurt, damaged[0].Part);
        Assert.Equal(0.7, damaged[0].Health, 9);
    }
}
