using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Reading a weapons system off a craft the mod did not design.
///
/// <para>The point of surveying is that the geometry comes from the craft. A prefab launcher has
/// its tube positions generated into <c>Arsenal.cs</c> and cross-checked against the mesh by
/// <c>validate-parts.py</c>, because nothing at run time connects the two — a part the player
/// placed already knows where it is, and these assert that is what comes back.</para>
/// </summary>
public class WeaponSurveyTests
{
    private static readonly ComponentProfile Tube = new()
        { PartId = "KSArmory_Tube", Role = WeaponRole.Launcher, DisplayName = "Tube" };
    private static readonly ComponentProfile Radar = new()
        { PartId = "KSArmory_Radar", Role = WeaponRole.Sensor, DisplayName = "Radar" };
    private static readonly ComponentProfile Manager = new()
        { PartId = "KSArmory_Manager", Role = WeaponRole.FireControl, DisplayName = "Manager" };

    private static readonly IReadOnlyList<ComponentProfile> Registry = [Tube, Radar, Manager];

    private static SurveyedPart At(string id, double x, double y, double z)
        => new(id, new double3(x, y, z), doubleQuat.Identity);

    [Fact]
    public void ACraftWithNothingRecognisedIsNotAWeaponSystem()
    {
        WeaponInventory inv = WeaponSurvey.Survey(
            [At("CoreCommandA_Prefab_MediumCapsuleVariantA", 0, 0, 0), At("CoreFuelTankA", 0, 0, 1)],
            Registry);

        Assert.False(inv.IsWeaponSystem);
        Assert.Empty(inv.Components);
    }

    [Fact]
    public void EveryRecognisedPartIsFound()
    {
        WeaponInventory inv = WeaponSurvey.Survey(
            [At("KSArmory_Manager", 0, 0, 0),
             At("CoreFuelTankA", 0, 0, 1),
             At("KSArmory_Tube", 1, 0, 0),
             At("KSArmory_Tube", -1, 0, 0),
             At("KSArmory_Radar", 0, 0, 2)],
            Registry);

        Assert.True(inv.IsWeaponSystem);
        Assert.Equal(4, inv.Components.Count);
        Assert.Equal(2, inv.CountOf(WeaponRole.Launcher));
        Assert.Equal(1, inv.CountOf(WeaponRole.Sensor));
        Assert.Equal(1, inv.CountOf(WeaponRole.FireControl));
        Assert.Equal(0, inv.CountOf(WeaponRole.Gun));
    }

    /// <summary>
    /// The whole reason for surveying rather than tabulating: two identical parts differ only by
    /// where the player put them, and that is what the fire control needs.
    /// </summary>
    [Fact]
    public void TwoOfTheSamePartKeepTheirOwnPlacement()
    {
        WeaponInventory inv = WeaponSurvey.Survey(
            [At("KSArmory_Tube", 1.5, 0, 0.5), At("KSArmory_Tube", -1.5, 0, 0.5)],
            Registry);

        Assert.Equal(2, inv.Components.Count);
        Assert.Equal(1.5, inv.Components[0].PositionVehicleAsmb.X);
        Assert.Equal(-1.5, inv.Components[1].PositionVehicleAsmb.X);
    }

    [Fact]
    public void OrientationSurvivesTheSurvey()
    {
        doubleQuat turned = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), 1.0);

        WeaponInventory inv = WeaponSurvey.Survey(
            [new SurveyedPart("KSArmory_Tube", new double3(2, 0, 0), turned)], Registry);

        Assert.Single(inv.Components);
        Assert.Equal(turned.X, inv.Components[0].Asmb2VehicleAsmb.X, 9);
        Assert.Equal(turned.W, inv.Components[0].Asmb2VehicleAsmb.W, 9);
    }

    /// <summary>
    /// Tree order, so "launcher 2" means the same thing between frames. The battery already keys
    /// on a part ordinal rather than a Part reference for this reason — KSA rebuilds the tree
    /// during staging and docking.
    /// </summary>
    [Fact]
    public void OrderFollowsTheCraft()
    {
        WeaponInventory inv = WeaponSurvey.Survey(
            [At("KSArmory_Radar", 0, 0, 9), At("KSArmory_Tube", 0, 0, 1), At("KSArmory_Manager", 0, 0, 0)],
            Registry);

        Assert.Equal(WeaponRole.Sensor, inv.Components[0].Role);
        Assert.Equal(WeaponRole.Launcher, inv.Components[1].Role);
        Assert.Equal(WeaponRole.FireControl, inv.Components[2].Role);
    }

    /// <summary>
    /// Exact match, not substring. LauncherPart.FindSubPart matches on *containing* a marker,
    /// which is right for one marker inside one part and wrong across a whole craft: a player's
    /// part called "Tube Adapter" would otherwise arrive as a launcher.
    /// </summary>
    [Theory]
    [InlineData("KSArmory_Tube_Adapter")]
    [InlineData("Legacy_KSArmory_Tube")]
    [InlineData("ksarmory_tube")]
    public void ANearMissIsNotAMatch(string partId)
    {
        Assert.Null(WeaponSurvey.Match(partId, Registry));
        Assert.False(WeaponSurvey.Survey([At(partId, 0, 0, 0)], Registry).IsWeaponSystem);
    }

    [Fact]
    public void AnExactMatchIsAMatch()
        => Assert.Equal(Tube, WeaponSurvey.Match("KSArmory_Tube", Registry));

    [Fact]
    public void AnEmptyCraftOrAnEmptyRegistryFindNothing()
    {
        Assert.Empty(WeaponSurvey.Survey([], Registry).Components);
        Assert.Empty(WeaponSurvey.Survey([At("KSArmory_Tube", 0, 0, 0)], []).Components);
    }

    /// <summary>The shipped registry has to actually name the launcher the mod flies.</summary>
    [Fact]
    public void TheShippedRegistryFindsTheShippedLauncher()
    {
        WeaponInventory inv = WeaponSurvey.Survey(
            [At(Arsenal.PantsirS1.PartId, 0, 0, 0)], Arsenal.Components);

        Assert.True(inv.IsWeaponSystem);
        Assert.Equal(1, inv.CountOf(WeaponRole.Launcher));
    }
}
