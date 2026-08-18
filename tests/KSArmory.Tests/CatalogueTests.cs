using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What the catalogue adds over the built-in registry it is seeded from.
///
/// <see cref="ArsenalTests"/> holds the built-ins themselves. These hold the two things that
/// change once the registry is something other code contributes to: that a name which resolves
/// can be told apart from one that does not, and that a weapons system with no launcher yet says
/// so rather than impersonating whichever launcher happens to be registered first.
/// </summary>
public class CatalogueTests
{
    [Fact]
    public void TheCatalogueReportsEveryBuiltIn()
    {
        Assert.Equal(Arsenal.Launchers.Count, Catalogue.Launchers.Count);
        Assert.Equal(Arsenal.Munitions.Count, Catalogue.Munitions.Count);
        Assert.Equal(Arsenal.Sensors.Count, Catalogue.Sensors.Count);
        Assert.Equal(Arsenal.Optics.Count, Catalogue.Optics.Count);
        Assert.Equal(Arsenal.Components.Count, Catalogue.Components.Count);

        // Same instances, not copies: the panel tunes a profile by reference and every system
        // running that loadout is meant to feel it.
        Assert.Same(Arsenal.PantsirS1, Catalogue.LauncherForPart(Arsenal.PantsirS1.PartId));
        Assert.Same(Arsenal.EoDirector, Catalogue.OpticForPart(Arsenal.EoDirector.PartId));
    }

    /// <summary>
    /// The capability the fallback cannot offer. <c>MunitionNamed</c> answers element zero for a
    /// name nobody carries, which keeps a typo playable and is indistinguishable from a hit; a
    /// loader deciding whether to accept a definition has to be able to tell the two apart.
    /// </summary>
    [Fact]
    public void AMissIsDistinguishableFromAHitOnlyThroughTheTryForm()
    {
        const string missing = "no such round";

        Assert.NotNull(Catalogue.MunitionNamed(missing));
        Assert.NotEqual(missing, Catalogue.MunitionNamed(missing).Name);
        Assert.Null(Catalogue.TryMunitionNamed(missing));

        Assert.NotNull(Catalogue.SensorNamed(missing));
        Assert.Null(Catalogue.TrySensorNamed(missing));

        // And it still finds what is there, so the null above means "absent" and not "broken".
        Assert.Same(Arsenal.Missile57E6, Catalogue.TryMunitionNamed(Arsenal.Missile57E6.Name));
        Assert.Same(Arsenal.SearchRadar1Rs1, Catalogue.TrySensorNamed(Arsenal.SearchRadar1Rs1.Name));
    }

    /// <summary>
    /// The placeholder has to be findable by nothing and armed with nothing. Seeding it from
    /// element zero instead gives an unadopted system a real weapon's reach and a real weapon's
    /// radar, which nothing on screen distinguishes from having adopted that weapon.
    /// </summary>
    [Fact]
    public void AnUnfittedSystemIsNotQuietlyTheFirstRegisteredLauncher()
    {
        foreach (LauncherProfile launcher in Catalogue.Launchers)
        {
            Assert.NotSame(LauncherProfile.Unfitted, launcher);
        }

        Assert.Null(Catalogue.LauncherForPart(LauncherProfile.Unfitted.PartId));

        Assert.Equal(0, LauncherProfile.Unfitted.TubeCount);
        Assert.False(LauncherProfile.Unfitted.Trains);
        Assert.False(LauncherProfile.Unfitted.HasCannon);
    }

    [Fact]
    public void AnUnfittedSystemReadsAsUnarmedRatherThanAsCarryingSomebodyElsesWeapon()
    {
        Assert.Equal(0f, MunitionProfile.None.MaxRange);
        Assert.Equal(0f, MunitionProfile.None.ChargeKg);
        Assert.Equal(0f, SensorProfile.None.Range);

        foreach (MunitionProfile round in Catalogue.Munitions) Assert.NotSame(MunitionProfile.None, round);
        foreach (SensorProfile set in Catalogue.Sensors) Assert.NotSame(SensorProfile.None, set);
    }
}
