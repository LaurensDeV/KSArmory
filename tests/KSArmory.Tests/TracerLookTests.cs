using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class TracerLookTests
{
    [Theory]
    [InlineData(4, 4)]   // the Pantsir's two twin guns at one in four
    [InlineData(2, 4)]
    [InlineData(6, 4)]   // the Phalanx
    [InlineData(3, 5)]
    public void EveryBarrelCarriesTracersAndTheBeltKeepsItsRatio(int barrels, int every)
    {
        int[] tracers = new int[barrels];
        long[] fed = new long[barrels];
        const int Shots = 1200;

        for (int shot = 0; shot < Shots; shot++)
        {
            int barrel = shot % barrels;
            if (TracerLook.IsTracer(fed[barrel]++, barrel, every)) tracers[barrel]++;
        }

        Assert.All(tracers, t => Assert.Equal(Shots / barrels / every, t));
    }

    [Fact]
    public void AtOneTimesTheStreakIsWhatTheEyeBlendsOfTheFlight()
        => Assert.Equal(1100.0 * TracerLook.PersistenceSeconds, TracerLook.LengthMetres(1100.0, 1.0, 1000.0), 9);

    [Fact]
    public void PausedTheStreakIsTheBurningBaseAlone()
        => Assert.Equal(TracerLook.ShortestMetres, TracerLook.LengthMetres(1100.0, 0.0, 1000.0), 9);

    [Fact]
    public void SlowMotionShortensTheStreakWithTheWorld()
        => Assert.Equal(1100.0 * TracerLook.PersistenceSeconds * 0.1,
                        TracerLook.LengthMetres(1100.0, 0.1, 1000.0), 9);

    [Fact]
    public void AtWarpTheStreakStopsShortOfALaser()
        => Assert.Equal(TracerLook.LongestMetres, TracerLook.LengthMetres(1100.0, 10.0, 1000.0), 9);

    [Fact]
    public void TheStreakNeverReachesBackPastTheMuzzle()
        => Assert.Equal(6.0, TracerLook.LengthMetres(1100.0, 1.0, 6.0), 9);

    [Fact]
    public void ItLightsOutOfTheMuzzleAndBurnsOut()
    {
        Assert.Equal(0.0, TracerLook.Burn(0.0, 0.0, 3.0));
        Assert.True(TracerLook.Burn(0.01, 11.0, 3.0) is > 0.0 and < 1.0);
        Assert.Equal(1.0, TracerLook.Burn(1.0, 1100.0, 3.0), 9);
        Assert.True(TracerLook.Burn(2.9, 2500.0, 3.0) is > 0.0 and < 1.0);
        Assert.Equal(0.0, TracerLook.Burn(3.0, 2600.0, 3.0));
    }

    [Fact]
    public void ARoundWithNoBurnIsNeverLit()
        => Assert.Equal(0.0, TracerLook.Burn(1.0, 1100.0, 0.0));

    [Fact]
    public void ADistantTracerThinsAndDimsRatherThanBloating()
    {
        // 0.12 m is 12 px wide at 100 px a metre: drawn that wide, at full brightness.
        (double nearWidth, double nearShare) = TracerLook.Core(100.0);
        Assert.Equal(12.0, nearWidth, 9);
        Assert.Equal(1.0, nearShare, 9);

        // A kilometre out on a 1440p screen it is a quarter of a pixel.
        (double farWidth, double farShare) = TracerLook.Core(2.745);
        Assert.Equal(TracerLook.MinCorePixels, farWidth, 9);
        Assert.Equal(0.12 * 2.745 / TracerLook.MinCorePixels, farShare, 9);
    }

    [Fact]
    public void ARoundWithNoTracerIsItsOwnWidthUpCloseAndStaysVisibleFarOff()
    {
        // 20 mm at 100 px a metre is 2 px: drawn that wide, at full brightness.
        (double nearWidth, double nearShare) = TracerLook.Ball(100.0, 0.02);
        Assert.Equal(2.0, nearWidth, 9);
        Assert.Equal(1.0, nearShare, 9);

        // A kilometre out it is a twentieth of a pixel, drawn a pixel wide at the floor.
        (double farWidth, double farShare) = TracerLook.Ball(2.745, 0.02);
        Assert.Equal(TracerLook.BallMinPixels, farWidth, 9);
        Assert.Equal(TracerLook.BallFloorShare, farShare, 9);
    }

    [Fact]
    public void ARoundWithNoTracerNeverBurnsOut()
        => Assert.Equal(1.0, TracerLook.Burn(40.0, 15_000.0, double.PositiveInfinity), 9);

    [Fact]
    public void AStreakPassingBesideTheEyeKeepsTheHalfInFront()
    {
        double4 head = new(1.0, 2.0, 0.5, 10.0);
        double4 tail = new(3.0, 4.0, 0.1, -10.0);

        Assert.True(TracerLook.TryClipToFront(ref head, ref tail, 0.05));
        Assert.Equal(10.0, head.W, 9);
        Assert.Equal(0.05, tail.W, 9);
        Assert.Equal(1.0 + (2.0 * (9.95 / 20.0)), tail.X, 9);
    }

    [Fact]
    public void AStreakWhollyBehindTheEyeIsNotDrawn()
    {
        double4 head = new(1.0, 2.0, 0.5, -1.0);
        double4 tail = new(3.0, 4.0, 0.1, -2.0);

        Assert.False(TracerLook.TryClipToFront(ref head, ref tail, 0.05));
    }
}
