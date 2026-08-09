using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a weapons system says it is fitted with, which is what the panel draws itself from.
///
/// <para>The shapes here are deliberately not the two the mod ships: a launcher with no tubes, one
/// with nothing that moves, one with no sensor and one whose magazine is deeper than its tubes.
/// The point of a description is that a system nobody has built yet can be described by it, and a
/// suite that only asks about the Pantsir cannot tell that apart from a description that happens
/// to fit the Pantsir.</para>
/// </summary>
public class WeaponFitTests
{
    private static SensorProfile Sensor(float range = 20000f) =>
        new() { Name = "set", DisplayName = "set", Range = range };

    private static readonly SensorProfile Blind = Sensor(0f);

    // A turret with two pods of missiles and a belt-fed cannon on the same mount.
    private static LauncherProfile Battery(int magazineDepth = 0) => new()
    {
        PartId = "Mod_Prefab_Battery",
        DisplayName = "battery",
        Munition = "round",
        Sensor = "set",
        Tubes = [new(1, 0, 0), new(1, 0, 0.4)],
        MagazineDepth = magazineDepth,
        TurretMarker = "Turret",
        PodsMarker = "Pods",
        RadarMarker = "Radar",
        GunsMarker = "Guns",
        OpticMarker = "Optic",
        GunMunition = "shell",
        GunMuzzles = [new(1, 0, 0)],
        GunAmmo = 480,
        GunReloadSeconds = 20f,
        ReloadSeconds = 12f,
    };

    // A rail: one round, and nothing on it turns.
    private static LauncherProfile Rail(string label = "Missiles") => new()
    {
        PartId = "Mod_Prefab_Rail",
        DisplayName = "rail",
        Munition = "round",
        Sensor = "set",
        Tubes = [new(1, 0, 0)],
        TubeArmamentLabel = label,
        ReloadSeconds = 0f,
    };

    // A close-in gun: a traverse, barrels, and no missiles at all.
    private static LauncherProfile Ciws() => new()
    {
        PartId = "Mod_Prefab_Ciws",
        DisplayName = "ciws",
        Munition = "shell",
        Sensor = "set",
        Tubes = [],
        TurretMarker = "Turret",
        GunsMarker = "Guns",
        GunMunition = "shell",
        GunMuzzles = [new(1, 0, 0)],
        GunAmmo = 1550,
    };

    // ---- What a system carries -----------------------------------------

    [Fact]
    public void AMixedMountListsBothArmamentsInFiringOrder()
    {
        WeaponFit fit = WeaponFit.Of(Battery(), Sensor());

        Assert.Equal(2, fit.Armaments.Count);
        Assert.Equal(ArmamentKind.Tubes, fit.Armaments[0].Kind);
        Assert.Equal(ArmamentKind.Belt, fit.Armaments[1].Kind);
        Assert.Equal("Missiles", fit.Armaments[0].Label);
        Assert.Equal("Cannon", fit.Armaments[1].Label);
        Assert.Equal("round", fit.Armaments[0].Munition);
        Assert.Equal("shell", fit.Armaments[1].Munition);
    }

    [Fact]
    public void AGunWithNoTubesCarriesOneArmamentAndNoSalvo()
    {
        WeaponFit fit = WeaponFit.Of(Ciws(), Sensor());

        Armament only = Assert.Single(fit.Armaments);
        Assert.Equal(ArmamentKind.Belt, only.Kind);
        Assert.Equal(1550, only.Capacity);
        Assert.Null(fit.FirstOf(ArmamentKind.Tubes));
        Assert.Equal(0, fit.SalvoCapacity);
        Assert.False(fit.Steers);
    }

    [Fact]
    public void ARailCarriesItsOneRoundAndNothingElse()
    {
        WeaponFit fit = WeaponFit.Of(Rail(), Sensor());

        Armament only = Assert.Single(fit.Armaments);
        Assert.Equal(ArmamentKind.Tubes, only.Kind);
        Assert.Equal(1, only.Capacity);
        Assert.False(only.Reloads);
        Assert.Null(fit.FirstOf(ArmamentKind.Belt));
    }

    [Fact]
    public void AnArmamentReloadsOnlyWhenItIsGivenTimeToDoIt()
    {
        WeaponFit fit = WeaponFit.Of(Battery(), Sensor());

        Assert.True(fit.Armaments[0].Reloads);
        Assert.True(fit.Armaments[1].Reloads);
        Assert.False(WeaponFit.Of(Rail(), Sensor()).Armaments[0].Reloads);
    }

    [Fact]
    public void ALabelTheProfileChoosesReachesEveryPlaceTheArmamentIsNamed()
    {
        Assert.Equal("Bombs", WeaponFit.Of(Rail("Bombs"), Sensor()).Armaments[0].Label);
    }

    // ---- Capacity -------------------------------------------------------

    /// <summary>
    /// A belt-fed launcher carries far more rounds than it has barrels, and the panel counts down
    /// from what the magazine was filled with. Reading the tube count instead says "1550/2".
    /// </summary>
    [Fact]
    public void ADeepMagazineReportsItsDepthRatherThanItsTubeCount()
    {
        LauncherProfile deep = Battery(magazineDepth: 200);

        Assert.Equal(200, WeaponFit.MagazineCapacity(deep));
        Assert.Equal(200, WeaponFit.Of(deep, Sensor()).Armaments[0].Capacity);
    }

    [Fact]
    public void AMagazineNoDeeperThanItsTubesIsOneRoundPerTube()
    {
        // Matches Magazine.Resize, which treats a depth at or below the tube count as no depth
        // at all. Two representations of one number that disagree is the failure being avoided.
        Assert.Equal(2, WeaponFit.MagazineCapacity(Battery()));
        Assert.Equal(2, WeaponFit.MagazineCapacity(Battery(magazineDepth: 2)));
        Assert.Equal(2, WeaponFit.MagazineCapacity(Battery(magazineDepth: 0)));
    }

    // ---- What a system can be told to do --------------------------------

    [Fact]
    public void AFullBatteryAnswersYesToEveryFaculty()
    {
        WeaponFit fit = WeaponFit.Of(Battery(), Sensor());

        Assert.True(fit.Aims);
        Assert.True(fit.Traverses);
        Assert.True(fit.Elevates);
        Assert.True(fit.SweepsASearchArray);
        Assert.True(fit.HasOpticalHead);
        Assert.True(fit.Searches);
        Assert.True(fit.Steers);
    }

    [Fact]
    public void ARailHasNoDrivesAtAll()
    {
        WeaponFit fit = WeaponFit.Of(Rail(), Sensor());

        Assert.False(fit.Aims);
        Assert.False(fit.Traverses);
        Assert.False(fit.Elevates);
        Assert.False(fit.SweepsASearchArray);
        Assert.False(fit.HasOpticalHead);
    }

    [Fact]
    public void AGunOnATraverseElevatesWithoutHavingPods()
    {
        WeaponFit fit = WeaponFit.Of(Ciws(), Sensor());

        Assert.True(fit.Aims);
        Assert.True(fit.Traverses);
        Assert.True(fit.Elevates);
        Assert.False(fit.SweepsASearchArray);
        Assert.False(fit.HasOpticalHead);
    }

    [Fact]
    public void ASetWithNoRangeIsNoSensor()
    {
        Assert.False(WeaponFit.Of(Battery(), Blind).Searches);
        Assert.True(WeaponFit.Of(Battery(), Sensor(500f)).Searches);
    }

    /// <summary>
    /// The fourth system the design has to survive: tubes, nothing that moves, and nothing that
    /// looks. Every question still answers, and the one thing it carries is still described.
    /// </summary>
    [Fact]
    public void AFixedMortarWithNoSensorIsStillFullyDescribed()
    {
        WeaponFit fit = WeaponFit.Of(Rail("Bombs"), Blind);

        Assert.False(fit.Searches);
        Assert.False(fit.Aims);
        Assert.Equal("Bombs", Assert.Single(fit.Armaments).Label);
        Assert.Equal(1, fit.SalvoCapacity);
    }

    // ---- What the panel prints ------------------------------------------

    [Fact]
    public void AnArmamentReadsAsWhatIsLeftAgainstAFullLoad()
    {
        WeaponFit fit = WeaponFit.Of(Battery(), Sensor());

        Assert.Equal("1/2", fit.Armaments[0].Tally(1));
        Assert.Equal("Missiles: 1/2", fit.Armaments[0].Describe(1, firing: false));
        Assert.Equal("Cannon: 300/480 FIRING", fit.Armaments[1].Describe(300, firing: true));
    }

    // ---- The switch each armament answers to ----------------------------

    [Fact]
    public void EachArmamentDrivesItsOwnSwitchAndNotTheOther()
    {
        SystemConfig policy = new();
        WeaponFit fit = WeaponFit.Of(Battery(), Sensor());

        Armament.EnabledIn(policy, fit.Armaments[0].Kind) = false;
        Assert.False(policy.MissilesEnabled);
        Assert.True(policy.GunsEnabled);

        Armament.EnabledIn(policy, fit.Armaments[1].Kind) = false;
        Assert.False(policy.GunsEnabled);
    }

    // ---- Against what the mod actually ships -----------------------------

    /// <summary>
    /// Every registered system describes itself as carrying something. A launcher that shoots
    /// with nothing has no rows, no switches and no tuning, and looks in the panel exactly like
    /// one whose part failed to resolve.
    /// </summary>
    [Fact]
    public void EveryRegisteredLauncherCarriesAtLeastOneArmament()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            WeaponFit fit = WeaponFit.Of(launcher, Arsenal.SensorNamed(launcher.Sensor));
            Assert.NotEmpty(fit.Armaments);

            foreach (Armament arm in fit.Armaments)
            {
                Assert.False(string.IsNullOrWhiteSpace(arm.Label));
                Assert.Equal(arm.Munition, Arsenal.MunitionNamed(arm.Munition).Name);
                Assert.True(arm.Capacity > 0, $"{launcher.DisplayName} carries no {arm.Label}");
            }
        }
    }

    // ---- Against the systems the mod actually registers ------------------
    //
    // The shapes above are invented, on purpose: a description that only fits what ships is not a
    // description. These run the same builder over the real registry, which is the half that
    // catches a profile whose fields say something the panel cannot render.

    [Fact]
    public void EveryRegisteredSystemIsDescribedBySomethingItCanShoot()
    {
        foreach (LauncherProfile launcher in Arsenal.Launchers)
        {
            SensorProfile sensor = Arsenal.SensorNamed(launcher.Sensor);
            WeaponFit fit = WeaponFit.Of(launcher, sensor);

            Assert.True(fit.Armaments.Count > 0,
                $"{launcher.DisplayName} declares nothing it can shoot, so the panel has no row "
                + "to draw and fire control has nothing to gate");

            foreach (Armament arm in fit.Armaments)
            {
                Assert.False(string.IsNullOrWhiteSpace(arm.Label),
                    $"{launcher.DisplayName}'s {arm.Kind} armament has no name to print");
                Assert.True(arm.Capacity > 0,
                    $"{launcher.DisplayName}'s {arm.Kind} armament holds {arm.Capacity} rounds");
                Assert.False(string.IsNullOrWhiteSpace(arm.Munition),
                    $"{launcher.DisplayName}'s {arm.Kind} armament names no round");
            }
        }
    }

    /// <summary>
    /// The gun-only system is the one a panel written around missiles gets wrong, so it is pinned
    /// by name rather than only by the invented shape above.
    /// </summary>
    [Fact]
    public void TheCiwsIsDescribedAsABeltAndNothingElse()
    {
        LauncherProfile ciws = Arsenal.LauncherForPart(Arsenal.Launchers, "KSArmory_Prefab_Ciws")!;
        WeaponFit fit = WeaponFit.Of(ciws, Arsenal.SensorNamed(ciws.Sensor));

        Assert.Equal(ArmamentKind.Belt, Assert.Single(fit.Armaments).Kind);
        Assert.Equal(0, fit.SalvoCapacity);
        Assert.False(fit.Steers, "a shell is unguided, so nothing should offer to tune its seeker");
        Assert.True(fit.Traverses, "the CIWS trains, and the panel has to offer its turret");
    }

    /// <summary>
    /// And the rail is the opposite shape: one round, nothing that moves. Between them they are
    /// the two ends the description has to span.
    /// </summary>
    [Fact]
    public void TheRailIsDescribedAsOneRoundOnAMountThatDoesNotMove()
    {
        LauncherProfile rail = Arsenal.LauncherForPart(Arsenal.Launchers,
                                                       "KSArmory_Prefab_SidewinderRail")!;
        WeaponFit fit = WeaponFit.Of(rail, Arsenal.SensorNamed(rail.Sensor));

        Assert.Equal(ArmamentKind.Tubes, Assert.Single(fit.Armaments).Kind);
        Assert.False(fit.Aims, "a rail cannot train, and offering it a turret is a lie");
        Assert.False(fit.Traverses);
        Assert.False(fit.Elevates);
    }
}
