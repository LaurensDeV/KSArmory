using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which of KSA's explosions a warhead goes off as. The premise comes first, because it is what
/// the thresholds exist for: KSA's size floor swallows every conventional charge, so without a
/// preset per size every warhead in the mod would go off the same.
/// </summary>
public class WarheadExplosionTests
{
    [Fact]
    public void EveryConventionalWarheadIsUnderKsasFloorAndANuclearOneIsNot()
    {
        double floor = WarheadExplosion.KsaFloorIntensity * WarheadExplosion.KsaReferenceJoules;

        Assert.True(WarheadExplosion.Joules(Arsenal.MissileAgm88.ChargeKg) < floor,
                    "the heaviest conventional warhead should be under the floor, or the preset need not carry size");
        Assert.True(WarheadExplosion.Joules(Arsenal.NukeB61.ChargeKg) > floor,
                    "a nuclear charge should be large enough for its energy to grow the explosion");
    }

    [Fact]
    public void ShellsPopMissilesBurnAndTheHeavyWarheadsConflagrate()
    {
        Assert.Equal(WarheadExplosion.Pop, WarheadExplosion.PresetFor(Arsenal.Cannon20Mm.ChargeKg));
        Assert.Equal(WarheadExplosion.Pop, WarheadExplosion.PresetFor(BuiltIns.Cannon30Mm.ChargeKg));
        Assert.Equal(WarheadExplosion.SmallFire, WarheadExplosion.PresetFor(Arsenal.Shell5In54.ChargeKg));
        Assert.Equal(WarheadExplosion.SmallFire, WarheadExplosion.PresetFor(Arsenal.Missile9J.ChargeKg));
        Assert.Equal(WarheadExplosion.SmallFire, WarheadExplosion.PresetFor(BuiltIns.Missile57E6.ChargeKg));
        Assert.Equal(WarheadExplosion.Conflagration, WarheadExplosion.PresetFor(Arsenal.MissileAgm88.ChargeKg));

        // And a charge that grows a cloud takes this mod's own, which is that same preset without
        // Core's smoke volume -- the mod is drawing the smoke itself, a thousand times the size.
        Assert.Equal(WarheadExplosion.NuclearBurst, WarheadExplosion.PresetFor(Arsenal.NukeB61.ChargeKg));
        Assert.Equal(WarheadExplosion.NuclearBurst, WarheadExplosion.PresetFor(MushroomCloud.ThresholdKg));
        Assert.Equal(WarheadExplosion.Conflagration,
                     WarheadExplosion.PresetFor(MushroomCloud.ThresholdKg - 1.0));
    }

    [Theory]
    [InlineData(0.30, WarheadExplosion.Pop)]
    [InlineData(0.42, WarheadExplosion.SmallFire)]
    [InlineData(38.0, WarheadExplosion.SmallFire)]
    [InlineData(45.0, WarheadExplosion.Conflagration)]
    public void TheChangeOversSitWhereTheFireballsMeet(double kg, string preset)
        => Assert.Equal(preset, WarheadExplosion.PresetFor(kg));

    [Fact]
    public void AHeavierChargeNeverGoesOffAsASmallerPreset()
    {
        int last = 0;
        for (double kg = 0.01; kg < 1.0e8; kg *= 1.1)
        {
            int rank = Array.IndexOf(WarheadExplosion.Presets, WarheadExplosion.PresetFor(kg));
            Assert.True(rank >= last, $"{kg:G3} kg went off as a smaller preset than a lighter charge");
            last = rank;
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void NoChargeGoesOffAsNothing(double kg)
    {
        Assert.Null(WarheadExplosion.PresetFor(kg));
        Assert.Equal(0f, WarheadExplosion.Joules(kg));
    }
}
