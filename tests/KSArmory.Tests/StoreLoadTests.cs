using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A launcher part's mass follows what it still carries, worked out from the magazine each time
/// rather than subtracted at each release -- so every way a round comes back puts its mass back.
/// </summary>
public class StoreLoadTests
{
    private static readonly IReadOnlyList<IProjectile> Nothing = [];

    private static double Shed(Magazine m, double perRound) => StoreLoad.ShedKg(perRound, m.Full, m.Ammo);

    [Fact]
    public void ABombRackIsLighterByItsBombOnceItIsGoneAndWholeAgainOnReload()
    {
        var rack = new Magazine();
        rack.Resize(1);
        Assert.Equal(0.0, Shed(rack, 374.0));

        Assert.True(rack.TryTakeTube(Nothing, out _));
        Assert.Equal(374.0, Shed(rack, 374.0));

        rack.RefillAll();
        Assert.Equal(0.0, Shed(rack, 374.0));
    }

    [Fact]
    public void ARefusedShotHandsItsMassBackWithItsRound()
    {
        var rails = new Magazine();
        rails.Resize(6);

        Assert.True(rails.TryTakeTube(Nothing, out int tube));
        Assert.Equal(250.0, Shed(rails, 250.0));

        rails.Return(tube);
        Assert.Equal(0.0, Shed(rails, 250.0));
    }

    [Fact]
    public void ADeepMagazineShedsByTheRoundNotByTheTube()
    {
        var belt = new Magazine();
        belt.Resize(1, depth: 40);
        Assert.Equal(40, belt.Full);

        for (int i = 0; i < 3; i++) Assert.True(belt.TryTakeTube(Nothing, out _));
        Assert.Equal(3 * 10.0, Shed(belt, 10.0), 9);
    }

    [Fact]
    public void ARoundWithNoMassShedsNothing() => Assert.Equal(0.0, StoreLoad.ShedKg(0.0, 12, 0));

    [Fact]
    public void TheFactorTakesOffTheShedMassAndNeverAllOfIt()
    {
        Assert.Equal(1.0, StoreLoad.Factor(405.0, 0.0));
        Assert.Equal(31.0 / 405.0, StoreLoad.Factor(405.0, 374.0), 9);

        // A part declared lighter than what it fires still keeps some mass.
        Assert.Equal(0.01, StoreLoad.Factor(100.0, 500.0));
    }

    [Fact]
    public void EveryShippedStoreIsLighterThanTheRoundsItRidesWith()
    {
        // The masses come from the part file's own comments; a typo of a zero would shed a craft away.
        Assert.Equal(374f, Arsenal.NukeRack.StoreMassKg);
        Assert.True(Arsenal.HarmRail.StoreMassKg is > 0f and < 405f);
        Assert.True(Arsenal.AmraamRail.StoreMassKg is > 0f and < 201f);
        Assert.True(Arsenal.SidewinderRail.StoreMassKg is > 0f and < 120f);
        Assert.True(Arsenal.MirvBus.StoreMassKg * Arsenal.MirvBus.TubeCount < 2750f);
        Assert.True(Arsenal.Ciws.GunRoundMassKg * Arsenal.Ciws.GunAmmo < 6200f);
        Assert.True(Arsenal.M197.GunRoundMassKg * Arsenal.M197.GunAmmo < 330f);
    }
}
