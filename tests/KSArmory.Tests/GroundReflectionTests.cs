using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the ground under a burst adds to its blast: the hemisphere's doubling on the ground, and in
/// the air the reflected front where it stands on the ground as the Mach stem.
/// </summary>
public class GroundReflectionTests(ITestOutputHelper output)
{
    private const double Charge = 20.0e6;
    private static readonly double3 Up = new(0, 0, 1);

    private static GroundReflection AirBurst(double height)
        => new(new double3(0, 0, height), Up, height, MushroomCloud.GroundCoupling(20.0, height));

    // A point at a ground range and a height over the ground, for a burst over the origin.
    private static double3 At(double range, double over) => new(range, 0, over);

    [Fact]
    public void FreeAirAddsNothing()
    {
        Assert.Equal(1.0, GroundReflection.FreeAir.GainAt(At(500, 10), Charge));
    }

    [Fact]
    public void ASurfaceBurstIsTheHemisphereEverywhere()
    {
        GroundReflection surface = new(Vec.Zero, Up, 0.0, 1.0);

        foreach (double3 at in new[] { At(300, 0), At(2000, 5), At(500, 800) })
        {
            Assert.Equal(GroundReflection.SurfaceGain, surface.GainAt(at, Charge), 9);
        }
    }

    [Fact]
    public void AnAirBurstLoadsTheGroundUnderTheStemAndNothingOverIt()
    {
        const double height = 800.0;
        GroundReflection air = AirBurst(height);
        Assert.True(air.Coupling < 0.01);

        // On the ground, near ground zero and out past the stem's onset.
        Assert.True(air.GainAt(At(0, 1), Charge) >= GroundReflection.SurfaceGain);
        Assert.True(air.GainAt(At(3000, 1), Charge) >= GroundReflection.SurfaceGain);

        // Up in free air above the stem.
        Assert.Equal(1.0, air.GainAt(At(0, 400), Charge), 9);
        Assert.Equal(1.0, air.GainAt(At(3000, 1500), Charge), 9);

        // The stem's top climbs with range past where it forms.
        double near = GroundReflection.InReflection(height, 800, 100);
        double far = GroundReflection.InReflection(height, 2000, 100);
        Assert.True(far > near, $"at 100 m up, {near:F2} in the reflection at 800 m and {far:F2} at 2 km");
    }

    [Fact]
    public void AStrongFrontReflectsHarderThanAWeakOne()
    {
        GroundReflection air = AirBurst(800.0);

        double close = air.GainAt(At(200, 1), Charge);
        double far = air.GainAt(At(8000, 1), Charge);
        output.WriteLine($"on the ground at 200 m: {close:F2}x free air; at 8 km: {far:F2}x");

        Assert.True(close > far);
        Assert.InRange(far, GroundReflection.SurfaceGain, 2.3);
        Assert.True(close <= 8.0 + 1e-9);
    }

    /// <summary>
    /// Why weapons are air-burst: at the right height the stem loads the ground near ground zero
    /// harder than a surface burst does at the same ground range, though it is further away.
    /// </summary>
    [Fact]
    public void AtTheRightHeightAnAirBurstLoadsTheGroundHarderThanASurfaceBurst()
    {
        const double range = 300.0;
        const double height = 200.0;

        GroundReflection surface = new(Vec.Zero, Up, 0.0, 1.0);
        GroundReflection air = new(new double3(0, 0, height), Up, height, 0.0);

        // The damage law goes as the cube of the distance: a gain of g at a distance r loads as g / r³.
        double fromSurface = surface.GainAt(At(range, 1), Charge) / Math.Pow(range, 3);
        double slant = Math.Sqrt((range * range) + (height * height));
        double fromAir = air.GainAt(At(range, 1), Charge) / Math.Pow(slant, 3);

        output.WriteLine($"20 kt, {range} m out on the ground: air burst at {height} m loads {fromAir / fromSurface:F2}x "
                         + "what a surface burst does");
        Assert.True(fromAir > fromSurface);
    }

    /// <summary>
    /// Near ground zero the front comes down from above; in the stem it is a wall walking along the
    /// ground and pushes level; above the stem, straight from the burst.
    /// </summary>
    [Fact]
    public void TheStemPushesAlongTheGround()
    {
        const double height = 800.0;
        GroundReflection air = AirBurst(height);
        double3 burst = air.BurstEcl;

        double3 underneath = At(100, 2);
        Assert.Equal(burst, air.PushFrom(burst, underneath));

        double3 inStem = At(4000, 5);
        double3 from = air.PushFrom(burst, inStem);
        Assert.Equal(0.0, Vec.Dot(Vec.Unit(inStem - from), Up), 9);

        double3 overStem = At(4000, 3000);
        Assert.Equal(burst, air.PushFrom(burst, overStem));

        GroundReflection surface = new(Vec.Zero, Up, 0.0, 1.0);
        Assert.Equal(Vec.Zero, surface.PushFrom(Vec.Zero, inStem));
    }

    [Fact]
    public void TheWaveDefaultsToTheSurfaceBurstItAlwaysWas()
    {
        Assert.Equal(BlastWave.PeakOverpressurePascals(Charge, 2000.0),
                     BlastWave.PeakOverpressurePascals(Charge, 2000.0, reflection: BlastWave.SurfaceReflection));
        Assert.True(BlastWave.PeakOverpressurePascals(Charge, 2000.0, reflection: 1.0)
                    < BlastWave.PeakOverpressurePascals(Charge, 2000.0));
    }
}
