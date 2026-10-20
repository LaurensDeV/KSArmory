using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class InfraredSearchTests
{
    private static readonly SensorProfile Irst = Arsenal.InfraredSearchSet;

    // ISA density ratios.
    private const double SeaLevel = 1.0;
    private const double TenKm = 0.338;
    private const double ThirtyKm = 0.0151;

    private const double EarthRadius = 6_371_000.0;

    private static readonly double Fighter = Signature.HeatOfCraft(75_000.0, 1.0);
    private static readonly double Booster = Signature.HeatOfCraft(1.0e6, 1.0);
    private static readonly double EnginesOut = Signature.HeatOfCraft(0.0, 0.0);

    private static double Reach(double heat, double airAtEye, double airAtTarget, bool againstGround = false)
    {
        double seen = 1.0, lost = 5.0e6;
        for (int i = 0; i < 200; i++)
        {
            double mid = Math.Sqrt(seen * lost);
            if (InfraredSearch.Sees(Irst, heat, mid, airAtEye, airAtTarget, againstGround)) seen = mid;
            else lost = mid;
        }
        return seen;
    }

    [Fact]
    public void AFighterAtMilitaryPowerIsSeenAt50To90KmWithBothAt10Km()
    {
        Assert.InRange(Reach(Fighter, TenKm, TenKm), 50_000.0, 90_000.0);
    }

    [Fact]
    public void AMegaNewtonBoosterInThinAirIsSeenBeyond200Km()
    {
        Assert.True(Reach(Booster, TenKm, ThirtyKm) > 200_000.0);
    }

    [Fact]
    public void ACraftWithItsEnginesOutAtSeaLevelIsSeenWithinAFewKilometresNotTens()
    {
        Assert.InRange(Reach(EnginesOut, SeaLevel, SeaLevel), 1_000.0, 10_000.0);
    }

    [Fact]
    public void LookedDownOnAgainstTheGroundTheSameCraftIsSeenAtAFractionOfThat()
    {
        double sky = Reach(EnginesOut, SeaLevel, SeaLevel);
        double ground = Reach(EnginesOut, SeaLevel, SeaLevel, againstGround: true);

        Assert.True(ground < 0.7 * sky, $"{ground:F0} m against the ground, {sky:F0} m against the sky");
    }

    [Fact]
    public void WithNoAirTheRangeIsTheThresholdAlone()
    {
        double expected = Math.Sqrt(Fighter * 1000.0 / Irst.IrThresholdWPerM2);

        Assert.Equal(1.0, InfraredSearch.Transmission(500_000.0, 0.0, 0.0));
        Assert.Equal(expected, Reach(Fighter, 0.0, 0.0), expected * 1e-6);
    }

    [Fact]
    public void ASlantPathIsThinnedByTheVapourAlongItNotByTheMeanOfItsEnds()
    {
        double slant = InfraredSearch.Transmission(50_000.0, SeaLevel, TenKm);
        double meanOfEnds = Math.Exp(-InfraredSearch.ExtinctionPerMetre * 50_000.0
                                     * 0.5 * (1.0 + Math.Pow(TenKm, InfraredSearch.VapourPower)));

        Assert.True(slant > meanOfEnds);
        Assert.True(slant < InfraredSearch.Transmission(50_000.0, TenKm, TenKm));
    }

    [Fact]
    public void AContactBelowTheEyeIsSeenAgainstTheGroundAndOneAboveItAgainstTheSky()
    {
        var centre = new double3(0.0, 0.0, 0.0);
        var eye = new double3(EarthRadius + 10_000.0, 0.0, 0.0);

        var below = new double3(EarthRadius + 1_000.0, 20_000.0, 0.0);
        var above = new double3(EarthRadius + 15_000.0, 20_000.0, 0.0);

        // Level with the eye and far off: the line past it grazes over the limb.
        var level = new double3(EarthRadius + 10_000.0, 300_000.0, 0.0);

        Assert.True(InfraredSearch.SeenAgainstGround(eye, below, centre, EarthRadius));
        Assert.False(InfraredSearch.SeenAgainstGround(eye, above, centre, EarthRadius));
        Assert.False(InfraredSearch.SeenAgainstGround(eye, level, centre, EarthRadius));
    }

    [Fact]
    public void TheAnswerDoesNotMoveWithTheFramesEclipticOffset()
    {
        var centre = new double3(0.0, 0.0, 0.0);
        var eye = new double3(EarthRadius + 3_000.0, 0.0, 0.0);
        var target = new double3(EarthRadius + 500.0, 5_200.0, 0.0);

        // A planet's distance from the sun, along every axis.
        var shift = new double3(1.5e11, -0.7e11, 0.2e11);

        for (double heat = 0.5; heat < 50.0; heat *= 1.3)
        {
            bool here = InfraredSearch.Sees(Irst, heat, eye, target, SeaLevel, SeaLevel, centre, EarthRadius);
            bool there = InfraredSearch.Sees(Irst, heat, eye + shift, target + shift, SeaLevel, SeaLevel,
                                             centre + shift, EarthRadius);
            Assert.Equal(here, there);
        }
    }

    [Fact]
    public void ThePositionalFormAppliesTheLookDownPenalty()
    {
        var centre = new double3(0.0, 0.0, 0.0);
        var eye = new double3(EarthRadius + 3_000.0, 0.0, 0.0);
        var target = new double3(EarthRadius + 500.0, 5_200.0, 0.0);
        double range = Vec.Len(target - eye);

        Assert.True(InfraredSearch.Sees(Irst, EnginesOut, range, SeaLevel, SeaLevel, againstGround: false));
        Assert.False(InfraredSearch.Sees(Irst, EnginesOut, eye, target, SeaLevel, SeaLevel, centre, EarthRadius));
    }

    [Fact]
    public void TheSetIsPassiveSoNothingCanHomeOnIt()
    {
        Assert.True(Irst.Infrared);
        Assert.False(Irst.Emits);
    }
}
