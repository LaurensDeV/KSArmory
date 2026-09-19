using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The sight's ground test skips lookups it cannot need, and takes every one it can.
///
/// <para>The bomb's own flight samples once a frame and is unaffected. The sight flies a whole
/// trajectory inside one frame, so without this one pipper costs hundreds of terrain lookups,
/// which is the difference between solving it continuously and solving it a few times a second.</para>
/// </summary>
public class CoarseGroundTestTests
{
    // A flat world of a given radius about the origin, counting how often it is asked.
    private sealed class FlatGround(double radius) : IGroundTest
    {
        public int Calls { get; private set; }

        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double groundRadius)
        {
            Calls++;
            centreEcl = Vec.Zero;
            groundRadius = radius;
            return true;
        }
    }

    private const double R = 6_371_000.0;

    /// <summary>
    /// A fall from height costs a handful of lookups rather than one per step, and the saving is
    /// what pays for solving every frame.
    /// </summary>
    [Fact]
    public void AFallFromAltitudeCostsFarFewerLookupsThanSteps()
    {
        var inner = new FlatGround(R);
        var coarse = new CoarseGroundTest(inner);

        // Straight down from 10 km, a step at a time, as the sight flies it.
        int steps = 0;
        for (double h = 10_000.0; h > 0.0; h -= 10.0, steps++)
        {
            coarse.TryGround(new double3(R + h, 0, 0), out _, out _);
        }

        Assert.True(inner.Calls < steps / 5,
                    $"expected far fewer lookups than {steps} steps, took {inner.Calls}");
    }

    /// <summary>
    /// Near the ground it samples every step. That is the part that decides where the bomb lands,
    /// so it must be no coarser than it was before the cache existed.
    /// </summary>
    [Fact]
    public void ItSamplesEveryStepOnceItIsNearTheGround()
    {
        var inner = new FlatGround(R);
        var coarse = new CoarseGroundTest(inner);

        coarse.TryGround(new double3(R + CoarseGroundTest.NearMetres * 4, 0, 0), out _, out _);
        int primed = inner.Calls;

        for (int i = 0; i < 20; i++)
        {
            coarse.TryGround(new double3(R + 100.0 - (i * 5.0), 0, 0), out _, out _);
        }

        Assert.Equal(primed + 20, inner.Calls);
    }

    /// <summary>
    /// And it re-samples on travel, whatever the height: a sample taken over the sea says nothing
    /// about the hill the bomb is now above, and trusting it would report clear air inside a ridge.
    /// </summary>
    [Fact]
    public void ItResamplesAfterTravellingEvenWhileHigh()
    {
        var inner = new FlatGround(R);
        var coarse = new CoarseGroundTest(inner);

        coarse.TryGround(new double3(R + 8_000.0, 0, 0), out _, out _);
        int primed = inner.Calls;

        // Sideways, staying high: the height gate alone would never ask again.
        coarse.TryGround(new double3(R + 8_000.0, CoarseGroundTest.ResampleMetres * 1.5, 0),
                         out _, out _);

        Assert.Equal(primed + 1, inner.Calls);
    }
}
