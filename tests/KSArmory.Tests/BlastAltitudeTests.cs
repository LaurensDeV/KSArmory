using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// G&amp;D's rule for a burst's blast by altitude: efficiency by the air at the burst, Sachs by the air at
/// the target, and the sea-level laws returned bit for bit at sea level.
/// </summary>
public class BlastAltitudeTests(ITestOutputHelper output)
{
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, 6.371e6, BodyTraits.Default);

    private static AmbientAir Real(double pressureRatio) => new(pressureRatio, pressureRatio);

    private static readonly double[] Charges = [20.0, 0.3e6, 20e6, 1e9, 50e9];
    private static readonly double[] Ranges = [50.0, 500.0, 2000.0, 10000.0, 40000.0];

    [Fact]
    public void AtSeaLevelEveryLawIsTheSeaLevelLawToTheBit()
    {
        AmbientAir sea = AmbientAir.SeaLevel;
        foreach (double kg in Charges)
        {
            Assert.Equal(1.0, BlastAltitude.Efficiency(kg, sea));
            Assert.Equal(kg, BlastAltitude.EquivalentChargeKg(kg, sea));
            foreach (double r in Ranges)
            {
                Assert.Equal(BlastWave.PeakOverpressurePascals(kg, r), BlastAltitude.PeakPascals(kg, sea, sea, r));
                Assert.Equal(BlastWave.PeakOverpressurePascals(kg, r, reflection: 1.0),
                             BlastAltitude.PeakPascals(kg, sea, sea, r, 1.0));
                Assert.Equal(BlastWave.PositivePhaseSeconds(kg, r),
                             BlastAltitude.PositivePhaseSeconds(kg, sea, sea, BlastWave.SoundMetresPerSecond, r));

                double kt = MushroomCloud.KilotonsFor(kg);
                if (kg < MushroomCloud.ThresholdKg) continue;
                Assert.Equal(MushroomCloud.ShockArrivalSeconds(kt, r),
                             BlastAltitude.FrontArrivalSeconds(kg, r, sea, BlastWave.SoundMetresPerSecond));
            }

            foreach (double age in new[] { 0.05, 0.5, 2.0, 10.0, 60.0 })
            {
                Assert.Equal(MushroomCloud.ShockRadius(MushroomCloud.KilotonsFor(kg), age),
                             BlastAltitude.FrontRadius(kg, age, sea, BlastWave.SoundMetresPerSecond));
            }
        }
    }

    [Fact]
    public void EfficiencyFollowsTheTableAndFallsWithTheAir()
    {
        Assert.Equal(1.0, BlastAltitude.Efficiency(1e9, Real(0.1915)));
        Assert.Equal(0.55, BlastAltitude.Efficiency(1e9, Real(0.00428)), 12);
        Assert.Equal(0.3, BlastAltitude.Efficiency(1e9, Real(0.00127)), 12);

        double last = 1.0;
        for (double p = 1.0; p > 1e-9; p *= 0.7)
        {
            double eta = BlastAltitude.Efficiency(1e9, Real(p));
            Assert.True(eta <= last + 1e-15, $"efficiency rose to {eta} at {p}");
            last = eta;
        }

        Assert.Equal(0.0, BlastAltitude.Efficiency(1e9, AmbientAir.None));
        Assert.Equal(1.0, BlastAltitude.Efficiency(1e9, AmbientAir.Unknown));
    }

    [Fact]
    public void AConventionalChargeIsUntouchedAtAnyHeight()
    {
        foreach (double p in new[] { 1.0, 1e-2, 1e-5, 0.0 })
        {
            Assert.Equal(1.0, BlastAltitude.Efficiency(20.0, Real(p)));
            Assert.Equal(20.0, BlastAltitude.EquivalentChargeKg(20.0, Real(p)));
        }
    }

    /// <summary>The anchor the rule is restated in: 50 Mt at 37 km puts 5-8 kPa on the ground under it.</summary>
    [Fact]
    public void FiftyMegatonsAtThirtySevenKilometresIsAGustOnTheGround()
    {
        double pascals = BlastAltitude.PeakPascals(50e9, Real(0.00428), AmbientAir.SeaLevel, 37_000, 1.0);
        double inGame = BlastAltitude.PeakPascals(50e9, Earth.AirAt(37_000), Earth.AirAt(0), 37_000, 1.0);
        output.WriteLine($"50 Mt at 37 km: {pascals / 1000:F1} kPa in real air, {inGame / 1000:F1} kPa in KSA's Earth");

        Assert.InRange(pascals, 5_000, 8_500);
        Assert.InRange(inGame, 2_500, 24_000);
        Assert.True(BurstHearing.Heard(inGame));
    }

    /// <summary>Teak, 3.8 Mt at 77 km: nothing was felt on Johnston Island under it.</summary>
    [Fact]
    public void TeakIsNotFeltOnTheGroundUnderIt()
    {
        double pascals = BlastAltitude.PeakPascals(3.8e9, Real(1.7e-5), AmbientAir.SeaLevel, 77_000, 2.0);
        output.WriteLine($"Teak on the ground: {pascals:F1} Pa");
        Assert.False(BurstHearing.Heard(pascals));
    }

    /// <summary>
    /// The damage charge dips with the blast and comes back with the X-rays: a burst high in the air is
    /// weaker than one low down, and one above it is judged as today.
    /// </summary>
    [Fact]
    public void TheDamageChargeFallsWithTheBlastAndRisesWithTheXRays()
    {
        double w = 50e9;
        double low = BlastAltitude.EquivalentChargeKg(w, Earth.AirAt(20_000));
        double high = BlastAltitude.EquivalentChargeKg(w, Earth.AirAt(70_000));
        double above = BlastAltitude.EquivalentChargeKg(w, Earth.AirAt(200_000));
        output.WriteLine($"50 Mt judged as {low / w:F3} at 20 km, {high / w:E2} at 70 km, {above / w:F3} above the air");

        Assert.True(high < low / 3.0);
        Assert.Equal(w, above);
        Assert.True(Warhead.BlastRadius(high) < 70_000, "a 70 km burst still reaches the ground");
    }

    /// <summary>Sachs: a front in thin air runs further in the same time, and at the same sound speed.</summary>
    [Fact]
    public void TheFrontRunsFurtherInThinnerAir()
    {
        double sound = Earth.SoundMetresPerSecond;
        double thick = BlastAltitude.FrontRadius(1e9, 1.0, Earth.AirAt(0), sound, 1.0);
        double thin = BlastAltitude.FrontRadius(1e9, 1.0, new AmbientAir(0.2, 0.2), sound, 1.0);
        Assert.True(thin > thick);

        double r = 20_000;
        double t = BlastAltitude.FrontArrivalSeconds(1e9, r, Earth.AirAt(10_000), sound, 1.0);
        Assert.Equal(r, BlastAltitude.FrontRadius(1e9, t, Earth.AirAt(10_000), sound, 1.0), 3);
        Assert.Equal(double.PositiveInfinity, BlastAltitude.FrontArrivalSeconds(1e9, r, AmbientAir.None, sound));
        Assert.Equal(0.0, BlastAltitude.FrontRadius(1e9, 5.0, AmbientAir.None, sound));
    }

    /// <summary>
    /// Yucca, 1.7 kt at 26 km: its front reached a ship about 67 km off 196 s later. A guard rather than a
    /// fit -- it holds because KSA's isothermal air carries sound at 340 m/s, faster than the real stratosphere.
    /// </summary>
    [Fact]
    public void YuccasFrontArrivesWithinAFifth()
    {
        double t = BlastAltitude.FrontArrivalSeconds(1.7e6, 67_500, Earth.AirAt(26_000), Earth.SoundMetresPerSecond, 1.0);
        output.WriteLine($"Yucca's front at 67.5 km: {t:F0} s against 196");
        Assert.InRange(t, 196 * 0.8, 196 * 1.2);
    }
}
