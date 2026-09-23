using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class BlastWaveTests
{
    private const double OneKilotonKg = 1.0e6;

    /// <summary>
    /// A kiloton at the ground reaches five psi about half a kilometre out and one psi about a
    /// kilometre and a half out, which is Glasstone's surface-burst scale to within the spread of
    /// his own curves.
    /// </summary>
    [Fact]
    public void AKilotonAtTheGroundHasGlasstonesRanges()
    {
        double fivePsi = 34_474.0;
        double onePsi = 6_895.0;

        Assert.InRange(BlastWave.PeakOverpressurePascals(OneKilotonKg, 450.0), fivePsi, double.MaxValue);
        Assert.InRange(BlastWave.PeakOverpressurePascals(OneKilotonKg, 750.0), 0.0, fivePsi);
        Assert.InRange(BlastWave.PeakOverpressurePascals(OneKilotonKg, 1200.0), onePsi, double.MaxValue);
        Assert.InRange(BlastWave.PeakOverpressurePascals(OneKilotonKg, 2200.0), 0.0, onePsi);
    }

    /// <summary>Twice the charge gives the same pressure at the cube root of two further out.</summary>
    [Theory]
    [InlineData(20.0, 30.0)]
    [InlineData(3.0e5, 800.0)]
    [InlineData(2.0e7, 5000.0)]
    public void ThePressureScalesAsTheCubeRoot(double chargeKg, double distance)
    {
        Assert.Equal(BlastWave.PeakOverpressurePascals(chargeKg, distance),
                     BlastWave.PeakOverpressurePascals(chargeKg * 2.0, distance * Math.Cbrt(2.0)), 6);
    }

    [Fact]
    public void ThePressureFallsWithDistanceAndWithTheAir()
    {
        double near = BlastWave.PeakOverpressurePascals(3.0e5, 300.0);
        double far = BlastWave.PeakOverpressurePascals(3.0e5, 900.0);

        Assert.True(near > far && far > 0.0);
        Assert.Equal(far * 0.25, BlastWave.PeakOverpressurePascals(3.0e5, 900.0, BlastWave.SeaLevelPascals * 0.25), 6);
        Assert.Equal(0.0, BlastWave.PeakOverpressurePascals(3.0e5, 900.0, 0.0));
    }

    /// <summary>
    /// The wind is much weaker than the pressure far out and overtakes it close in: 4 kPa of wind
    /// behind 5 psi, Glasstone's 0.6 psi, and more wind than pressure only past about 70 psi.
    /// </summary>
    [Fact]
    public void TheWindIsTheRankineHugoniotOne()
    {
        Assert.InRange(BlastWave.PeakWindPascals(34_474.0), 3_900.0, 4_100.0);
        Assert.True(BlastWave.PeakWindPascals(3.0e5) < 3.0e5);
        Assert.True(BlastWave.PeakWindPascals(6.0e5) > 6.0e5);
    }

    /// <summary>A bigger burst pushes for longer at the same pressure, as the cube root of the charge.</summary>
    [Fact]
    public void ThePositivePhaseScalesAsTheCubeRoot()
    {
        double small = BlastWave.PositivePhaseSeconds(3.0e5, 500.0);
        double large = BlastWave.PositivePhaseSeconds(3.0e5 * 8.0, 1000.0);

        Assert.InRange(small, 0.05, 1.0);
        Assert.Equal(small * 2.0, large, 6);
    }

    /// <summary>
    /// A 0.3 kt burst 500 m off a ten-metre rocket three metres across kicks it by a fraction of a
    /// metre a second, and 200 m off by metres a second: a jolt on the pad, then a shove.
    /// </summary>
    [Fact]
    public void ARocketIsJoltedAtHalfAKilometreAndShovedAtTwoHundredMetres()
    {
        const double massKg = 20_000.0;
        const double areaM2 = 30.0;

        double far = BlastShove.DragCoefficient * areaM2 * BlastWave.WindImpulse(3.0e5, 500.0) / massKg;
        double near = BlastShove.DragCoefficient * areaM2 * BlastWave.WindImpulse(3.0e5, 200.0) / massKg;

        Assert.InRange(far, 0.02, 1.0);
        Assert.InRange(near, 1.0, 30.0);
    }

    [Fact]
    public void ABoxShowsItsFaceToAWindAlongAnAxis()
    {
        double3 half = new(0.5, 1.5, 5.0);

        Assert.Equal(4.0 * 1.5 * 5.0, BlastShove.ProjectedArea(half, new double3(1, 0, 0)), 9);
        Assert.Equal(4.0 * 0.5 * 1.5, BlastShove.ProjectedArea(half, new double3(0, 0, -3)), 9);
    }

    /// <summary>
    /// A push through the centre of mass only moves the craft; one above it tips the craft the way
    /// the wind is blowing.
    /// </summary>
    [Fact]
    public void APushAboveTheCentreOfMassTipsItDownwind()
    {
        double3 com = new(0, 0, 0);
        double3 wind = new(1, 0, 0);

        (double3 through, double3 noTurn) = BlastShove.OnPart(com, wind, 10.0, 100.0, com);
        Assert.Equal(BlastShove.DragCoefficient * 1000.0, through.X, 9);
        Assert.Equal(0.0, Vec.Len(noTurn), 9);

        // Up is +Z: a push along +X above the centre turns the top toward +X, about +Y.
        (_, double3 turn) = BlastShove.OnPart(new double3(0, 0, 4), wind, 10.0, 100.0, com);
        Assert.True(turn.Y > 0.0);
        Assert.Equal(4.0 * BlastShove.DragCoefficient * 1000.0, turn.Y, 9);
    }

    /// <summary>A weak front reflects at twice itself, a strong one toward eight times.</summary>
    [Fact]
    public void AWallDoublesAWeakFrontAndOctuplesAStrongOne()
    {
        Assert.Equal(2.0, BlastWave.ReflectedPascals(10.0) / 10.0, 3);
        Assert.InRange(BlastWave.ReflectedPascals(1.0e9) / 1.0e9, 7.9, 8.0);
    }

    [Fact]
    public void AFrontDecaysOverItsPushAndIsGoneAfter()
    {
        Assert.Equal(1.0, BlastWave.Remaining(0.0, 0.3));
        Assert.Equal(0.5 * Math.Exp(-0.5), BlastWave.Remaining(0.15, 0.3), 12);
        Assert.Equal(0.0, BlastWave.Remaining(0.3, 0.3));
        Assert.Equal(0.0, BlastWave.Remaining(2.0, 0.3));
    }

    /// <summary>
    /// The air behind a front moves slowly behind a weak one and faster than sound behind a strong
    /// one: about 70 m/s behind 5 psi, and 640 m/s behind the 700 kPa a 340 kt burst puts 1 km out.
    /// </summary>
    [Fact]
    public void TheWindBehindAFrontHasGlasstonesSpeeds()
    {
        Assert.InRange(BlastWave.WindSpeed(34_474.0), 65.0, 80.0);
        Assert.InRange(BlastWave.WindSpeed(706_000.0), 600.0, 680.0);
        Assert.Equal(0.0, BlastWave.WindSpeed(0.0));
    }

    /// <summary>A small push is what it was; a huge one approaches the wind's speed and never passes it.</summary>
    [Fact]
    public void APushNeverOutrunsTheWind()
    {
        Assert.Equal(0.1, BlastShove.Saturate(0.1, 640.0), 3);
        Assert.InRange(BlastShove.Saturate(5517.0, 2570.0), 1500.0, 2570.0);
        Assert.True(BlastShove.Saturate(1.0e9, 640.0) < 640.0);
        Assert.Equal(0.5 * 640.0, BlastShove.Saturate(640.0, 640.0), 9);
    }
}
