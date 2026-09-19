using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a released store is aimed at, and which controls describe a system that releases rather
/// than engages.
/// </summary>
public class ReleaseOntoDesignationTests
{
    [Fact]
    public void AGuidedTailKitSteersButIsStillReleased()
    {
        MunitionProfile b61 = Catalogue.MunitionNamed("B61");

        // The two must not collapse into one. Steers is what earns it an aimpoint; Powered is
        // what would make it demand a lock before the trigger did anything.
        Assert.True(b61.Steers);
        Assert.False(b61.Powered);
    }

    [Fact]
    public void AnUnguidedStoreSteersNothing()
    {
        MunitionProfile rv = Catalogue.MunitionNamed("MK21");
        Assert.False(rv.Steers);
        Assert.False(rv.Powered);
    }

    [Theory]
    [InlineData("KSArmory_Prefab_NukeRack", false)]
    [InlineData("KSArmory_Prefab_Launcher6", true)]
    [InlineData("KSArmory_Prefab_Ciws", true)]
    public void OnlyASystemThatEngagesOnItsOwnOffersTheAutoEngageControls(string partId, bool expected)
    {
        // The panel hides the salvo size, the no-friendly-fire switch and auto-engage on anything
        // that answers false here, so this is what decides whether those rows exist at all.
        LauncherProfile launcher = Catalogue.LauncherForPart(partId)!;
        WeaponFit fit = WeaponFit.Of(launcher, Catalogue.SensorNamed(launcher.Sensor));
        Assert.Equal(expected, fit.AutoEngages);
    }

    [Fact]
    public void ARackOfStoresOffersNoMouseAim()
    {
        // Mouse aim drives a traverse and an elevation. A rack has neither, and its own row says
        // it shoots where the craft points.
        LauncherProfile rack = Catalogue.LauncherForPart("KSArmory_Prefab_NukeRack")!;
        WeaponFit fit = WeaponFit.Of(rack, Catalogue.SensorNamed(rack.Sensor));
        Assert.False(fit.Aims);
    }

    [Fact]
    public void AGunMountStillEngagesOnItsOwn()
    {
        // The belt is the case that would be lost by keying auto-engagement on Powered alone:
        // a cannon has no tubes at all, and must not lose its arm and salvo controls.
        LauncherProfile gun = Catalogue.LauncherForPart("KSArmory_Prefab_Ciws")!;
        WeaponFit fit = WeaponFit.Of(gun, Catalogue.SensorNamed(gun.Sensor));
        Assert.True(fit.AutoEngages);
        Assert.Equal(0, fit.SalvoCapacity);
    }

    [Fact]
    public void ASingleStoreRackHasNoSalvoToSize()
    {
        // The slider was showing 2 on a rack that holds one bomb, because its range collapsed to
        // 1..1 while the stored default stayed. Below two there is nothing to choose.
        LauncherProfile rack = Catalogue.LauncherForPart("KSArmory_Prefab_NukeRack")!;
        WeaponFit fit = WeaponFit.Of(rack, Catalogue.SensorNamed(rack.Sensor));
        Assert.True(fit.SalvoCapacity <= 1);
    }
}
