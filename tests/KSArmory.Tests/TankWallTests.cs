using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class TankWallTests
{
    // Core's 3 m tank: 4 mm of aluminium at 415 MPa, rated by KSA's cylinder hoop rule.
    private static readonly TankWall Thin = new(0.004, 415e6, 0.004 / (1.5 - 0.002) * 415e6 / 2.0);

    // Core's one 85 mm wall, taken as steel.
    private static readonly TankWall Thick = new(0.085, 500e6, 0.0);

    [Fact]
    public void ADartAndAFragmentBothGoThroughAThinTank()
    {
        Assert.True(Thin.Perforates(0.5 * 0.1 * 1000.0 * 1000.0, 0.020));

        double fragment = 0.005;
        Assert.True(Thin.Perforates(0.5 * fragment * 1500.0 * 1500.0, TankWall.FragmentDiameterMetres(fragment)));
    }

    [Fact]
    public void ASlowFragmentStopsOnAThickWall()
    {
        double fragment = 0.005;
        Assert.False(Thick.Perforates(0.5 * fragment * 300.0 * 300.0, TankWall.FragmentDiameterMetres(fragment)));
    }

    [Fact]
    public void WithNoWallKnownEverythingGetsThroughAndNothingBursts()
    {
        TankWall none = default;

        Assert.True(none.Perforates(1e-9, 0.02));
        Assert.False(none.Bursts(1e9, 0.0, 1.0));
    }

    [Fact]
    public void TheWallHoldsLessTheBiggerTheHole()
    {
        Assert.Equal(2.0 * Thin.RatedPascals, Thin.HoldsPascals(0.0), 6);
        Assert.Equal(Thin.RatedPascals, Thin.HoldsPascals(4.0 * TankWall.CrackLengthMetres), 6);
        Assert.True(Thin.HoldsPascals(0.03) < Thin.HoldsPascals(0.01));
    }

    [Fact]
    public void AFullTankBurstsThroughAShellsHoleAndOnlyVentsThroughAPinhole()
    {
        double full = Thin.RatedPascals + 101_325.0;

        Assert.True(Thin.Bursts(full, 101_325.0, 0.030));
        Assert.False(Thin.Bursts(full, 101_325.0, 0.003));
    }

    // NASA TM X-52063: 0.8 mm walls loaded to yield took 5.6 mm spheres as plain holes below the speed
    // at which the liquid behind them ran a crack; a plane-strain crack length burst them on the puncture.
    [Fact]
    public void APunctureInAWallLoadedToYieldDoesNotRunOnItsOwn()
    {
        Assert.False(Thin.Bursts(2.0 * Thin.RatedPascals, 0.0, 0.0028));
    }

    [Fact]
    public void AQuarterFullTankVentsThroughTheSameHole()
    {
        Assert.False(Thin.Bursts(0.25 * Thin.RatedPascals, 0.0, 0.030));
    }

    [Fact]
    public void AtATenthOfTheDamageAShellsHoleOnlyVentsAFullTank()
    {
        double full = Thin.RatedPascals + 101_325.0;
        TankWall tenth = Thin.Scaled(0.1);

        Assert.False(tenth.Bursts(full, 101_325.0, 0.030));
        Assert.Equal(10.0 * Thin.PlugJoules(0.02), tenth.PlugJoules(0.02), 6);
    }

    [Fact]
    public void AHitThatStopsGoesAsDeepAsItsEnergyWouldPunch()
    {
        double joules = 0.5 * Thick.PlugJoules(0.02);
        double dent = Thick.DentMetres(joules, 0.02);

        Assert.Equal(Thick.ThicknessMetres / Math.Sqrt(2.0), dent, 9);
        Assert.False(Thick.Perforates(joules, 0.02, dent * 1.01));
        Assert.True(Thick.Perforates(joules, 0.02, dent * 0.99));
    }

    // A tenth of the energy to go through, so about three hits at one spot before the fourth gets through.
    [Fact]
    public void RepeatedHitsOnOneSpotGetThroughAWallOneHitCannot()
    {
        WallCraters craters = new();
        object skin = new();
        double3 spot = new(0.1, 0.2, 0.3);
        double across = 0.02;
        double joules = 0.1 * Thick.PlugJoules(across);

        int hits = 0;
        while (!Thick.Perforates(joules, across, craters.ThicknessLeft(skin, spot, across, Thick.ThicknessMetres)))
        {
            craters.Dent(skin, spot, across, Thick.DentMetres(joules, across));
            hits++;
            Assert.True(hits < 10);
        }

        Assert.InRange(hits, 2, 4);
        Assert.Equal(Thick.ThicknessMetres, craters.ThicknessLeft(skin, spot + new double3(0.05, 0, 0), across,
                                                                    Thick.ThicknessMetres), 9);
    }

    [Fact]
    public void ASkinKeepsItsDeepestCratersWhenFull()
    {
        WallCraters craters = new();
        object skin = new();
        for (int i = 0; i < WallCraters.MostPerSkin; i++) craters.Dent(skin, new double3(i, 0, 0), 0.02, 0.01);

        craters.Dent(skin, new double3(-100, 0, 0), 0.02, 0.05);

        Assert.Equal(0.035, craters.ThicknessLeft(skin, new double3(-100, 0, 0), 0.02, 0.085), 9);
        Assert.Equal(0.085, craters.ThicknessLeft(skin, new double3(0, 0, 0), 0.02, 0.085), 9);
    }

    [Fact]
    public void FragmentsThatStopInATanksWallLightNothingAndCostOnlyTheDent()
    {
        MunitionProfile amraam = Catalogue.MunitionNamed("AIM120C");
        double3 nose = new(1, 0, 0);
        double3 lead = Fragmentation.FuzeLead(FragmentSpray.Of(amraam), nose * 600.0, Vec.Zero, 10.0);
        BurstContext burst = new(Vec.Zero, 0.0, Vec.Zero, nose * 600.0, nose, 1.225, lead, 1.0);
        DamageablePart part = new(0, new double3(0, 10, 0), 2.0, 9e6, MassKg: 1500.0, PresentedAreaM2: 6.0);

        List<(int, int Hits)> open = [];
        BlastDamage.Costs(burst, [part], amraam, new Random(7), [], open);

        List<(int, double Share)> armouredShares = [];
        List<(int, int Hits)> armoured = [];
        BlastDamage.Costs(burst, [part with { Wall = new TankWall(1.0, 500e6, 0.0) }], amraam, new Random(7),
                          armouredShares, armoured);
        List<(int, double Share)> openShares = [];
        BlastDamage.Costs(burst, [part], amraam, new Random(7), openShares);

        Assert.NotEmpty(open);
        Assert.Empty(armoured);
        Assert.InRange(armouredShares.Sum(s => s.Share), double.Epsilon, openShares.Sum(s => s.Share) * 0.5);
    }
}
