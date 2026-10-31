using Xunit;

namespace KSArmory.Tests;

public class ArmourLayerTests
{
    private static readonly ArmourMaterial Rolled = new()
    {
        Name = "Rolled", DisplayName = "Rolled", DensityKgPerM3 = 7850f, LambertCoefficient = 4000f,
        ResistancePascals = 5.0e9f,
    };

    private static readonly ArmourMaterial Aluminium = new()
    {
        Name = "Aluminium", DisplayName = "Aluminium", DensityKgPerM3 = 2770f, LambertCoefficient = 1750f,
        ResistancePascals = 2.24e9f,
    };

    // BRL-MR-3960's form worked on a 7.62 cm, 7,078.56 g rod 20 cm long into 8 cm of armour at 55 degrees.
    [Fact]
    public void LambertsLimitReproducesTheWorkedExample()
    {
        ArmourLayer plate = new(Rolled, 0.08);
        double limit = plate.BallisticLimit(7.07856, 0.0762, 20.0 / 7.62, Math.Cos(55.0 * Math.PI / 180.0), 0.08);

        Assert.Equal(1031.345815, limit, 4);
    }

    [Fact]
    public void ThePhalanxDartGetsThroughAbout44MmOfRolledArmour()
    {
        Assert.True(new ArmourLayer(Rolled, 0.042).BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.042) < 1113.0);
        Assert.True(new ArmourLayer(Rolled, 0.046).BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.046) > 1113.0);
    }

    [Fact]
    public void AluminiumIsHeavierThanSteelForTheSameStop()
    {
        double steel = 0.044 * 7850.0;
        double aluminiumThickness = 0.0;
        for (double t = 0.001; t < 0.3; t += 0.0005)
        {
            if (new ArmourLayer(Aluminium, t).BallisticLimit(0.1, 0.01275, 4.0, 1.0, t) > 1113.0) { aluminiumThickness = t; break; }
        }

        Assert.True(aluminiumThickness * 2770.0 > steel);
    }

    // Clear of the whole plate's limit; at it, the whole plate stops what two halves let through.
    [Fact]
    public void TwoHalfPlatesLetThroughAboutWhatOneDoes()
    {
        ArmourLayer half = new(Rolled, 0.022), whole = new(Rolled, 0.044);
        double through = half.ResidualSpeed(1600.0, 0.1, 0.01275, 4.0, 1.0, 0.022);
        double second = half.ResidualSpeed(through, 0.1, 0.01275, 4.0, 1.0, 0.022);
        double one = whole.ResidualSpeed(1600.0, 0.1, 0.01275, 4.0, 1.0, 0.044);

        Assert.InRange(second, 0.9 * one, 1.1 * one);
    }

    [Fact]
    public void ARoundAtTheLimitLeavesWithNothingAndOneAboveItKeepsSome()
    {
        ArmourLayer plate = new(Rolled, 0.02);
        double limit = plate.BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.02);

        Assert.Equal(0.0, plate.ResidualSpeed(limit, 0.1, 0.01275, 4.0, 1.0, 0.02));
        double residual = plate.ResidualSpeed(limit * 1.5, 0.1, 0.01275, 4.0, 1.0, 0.02);
        Assert.InRange(residual, 1.0, limit * 1.5);
    }

    [Fact]
    public void SlopeAddsLessThanItsSecant()
    {
        ArmourLayer plate = new(Rolled, 0.02);
        double square = plate.BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.02);
        double sloped = plate.BallisticLimit(0.1, 0.01275, 4.0, 0.5, 0.02);
        double doubled = plate.BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.04);

        Assert.True(sloped > square && sloped < doubled);
    }

    [Fact]
    public void AGrazingRoundThatStopsGlancesAndASquareOneDoesNot()
    {
        ArmourLayer plate = new(Rolled, 0.05);
        double density = ArmourLayer.RoundDensity(0.1, 0.01275, 4.0);

        Assert.True(plate.Glances(600.0, Math.Cos(80.0 * Math.PI / 180.0), density));
        Assert.False(plate.Glances(600.0, Math.Cos(10.0 * Math.PI / 180.0), density));
    }

    [Fact]
    public void ARoundThatStopsGoesAsDeepAsItsSpeedIsTheLimitFor()
    {
        ArmourLayer plate = new(Rolled, 0.05);
        double dent = plate.DentMetres(700.0, 0.1, 0.01275, 4.0, 1.0);

        Assert.InRange(dent, 0.001, 0.05);
        Assert.Equal(700.0, plate.BallisticLimit(0.1, 0.01275, 4.0, 1.0, dent), 0);
    }

    [Fact]
    public void AQuarterOfTheDamageTakesTwiceTheSpeed()
    {
        ArmourLayer plate = new(Rolled, 0.02);
        Assert.Equal(2.0 * plate.BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.02),
                     plate.BallisticLimit(0.1, 0.01275, 4.0, 1.0, 0.02, damageScale: 0.25), 6);
    }

    [Fact]
    public void AShellBurstHolesOnlyAPlateThinnerThanItsCasingCan()
    {
        Assert.True(new ArmourLayer(Rolled, 0.012).HoledByShellBurst(0.02));
        Assert.False(new ArmourLayer(Rolled, 0.020).HoledByShellBurst(0.02));
    }

    [Fact]
    public void NoLayerStopsNothing()
    {
        Assert.Equal(0.0, default(ArmourLayer).BallisticLimit(0.1, 0.01, 4.0, 1.0, 0.01));
        Assert.True(default(ArmourLayer).HoledByShellBurst(0.02));
    }
}
