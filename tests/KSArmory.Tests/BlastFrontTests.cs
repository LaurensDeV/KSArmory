using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// One burst's front, which the drawn ring, the cloud held inside it, the loads and their log all ask:
/// a surface burst in sea-level air is the pinned law to the bit, and an air burst's is the free-air sphere.
/// </summary>
public class BlastFrontTests(ITestOutputHelper output)
{
    private static readonly BodyAir Earth = new(101_325, 1.225, 8_000, 167_000, 9.81, 6.371e6, BodyTraits.Default);

    [Fact]
    public void ASurfaceBurstInSeaLevelAirIsThePinnedFront()
    {
        foreach (double kt in new[] { 0.3, 20.0, 1000.0, 50000.0 })
        {
            double kg = kt * 1e6;
            BlastFront sea = BlastFront.For(kg, AmbientAir.SeaLevel, BlastWave.SoundMetresPerSecond, 0.0);
            Assert.Equal(BlastFront.SeaLevel(kg), sea);

            foreach (double age in new[] { 0.05, 0.5, 2.0, 10.0, 60.0, 200.0 })
            {
                Assert.Equal(MushroomCloud.ShockRadius(MushroomCloud.KilotonsFor(kg), age), sea.Radius(age));

                MushroomCloud.Shape pinned = MushroomCloud.At(kg, age, 0.0);
                MushroomCloud.Shape fronted = MushroomCloud.At(kg, age, 0.0, 1.0, sea);
                Assert.Equal(pinned, fronted);
            }
        }
    }

    [Fact]
    public void AnAirBurstsFrontIsTheFreeAirSphere()
    {
        double kg = 20e6;
        BlastFront air = BlastFront.For(kg, AmbientAir.SeaLevel, BlastWave.SoundMetresPerSecond, 900.0);
        BlastFront ground = BlastFront.SeaLevel(kg);

        Assert.Equal(1.0, air.Reflection);
        Assert.True(air.Radius(0.5) < ground.Radius(0.5));
        Assert.Equal(Math.Pow(0.5, 0.2), air.Radius(0.01) / ground.Radius(0.01), 12);

        double down = air.ArrivalSeconds(900.0);
        output.WriteLine($"20 kt at 900 m: the front reaches the ground at {down:F2} s against {ground.ArrivalSeconds(900.0):F2} doubled");
        Assert.True(down > ground.ArrivalSeconds(900.0));
    }

    [Fact]
    public void AFrontInThinAirIsTheBurstsOwn()
    {
        double kg = 1e9;
        BlastFront high = BlastFront.For(kg, Earth.AirAt(30_000), Earth.SoundMetresPerSecond, 30_000.0);
        BlastFront low = BlastFront.For(kg, Earth.AirAt(0), Earth.SoundMetresPerSecond, 30_000.0);

        Assert.True(high.Radius(1.0) > low.Radius(1.0));
        Assert.Equal(double.PositiveInfinity,
                     BlastFront.For(kg, AmbientAir.None, 0.0, 0.0).ArrivalSeconds(1000.0));
        Assert.Equal(high, high.WithCharge(2e9).WithCharge(kg));
    }
}
