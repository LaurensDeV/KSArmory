using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// That a cycle taking no reading reports none.
///
/// <para>The trap this pins is not hypothetical: a settled loop went on printing its final tuple
/// every frame, 463 times a flight, and the median of those echoes was read as a distribution over
/// 3,788 samples. A readout that freezes is indistinguishable from a live one unless it says so.</para>
/// </summary>
public class AimReadoutTests
{
    private static double3 Out(double m) => new(6_371_000.0 + 0.0, m, 0.0);

    private static AimCorrection Settled()
    {
        AimCorrection loop = new();
        double3 target = new(6_371_000.0, 0.0, 0.0);

        loop.Observe(target + new double3(0.0, 1_000.0, 0.0), target);

        for (int i = 0; i < AimCorrection.WorseBeforeStopping + 2; i++)
        {
            loop.Observe(target + new double3(0.0, 50_000.0 + i, 0.0), target);
        }

        return loop;
    }

    [Fact]
    public void ASettledLoopReportsNoReadingRatherThanItsLastOne()
    {
        AimCorrection loop = Settled();
        Assert.True(loop.Settled, "the loop under test has to have settled for this to mean anything");

        double3 target = new(6_371_000.0, 0.0, 0.0);
        loop.Observe(target + new double3(0.0, 9_999.0, 0.0), target);

        Assert.True(double.IsNaN(loop.LastAimMoveMetres));
        Assert.True(double.IsNaN(loop.LastImpactMoveMetres));
        Assert.True(double.IsNaN(loop.LastImpactAlongAimMetres));
    }

    [Fact]
    public void AndALiveCycleStillReportsOne()
    {
        AimCorrection loop = new();
        double3 target = new(6_371_000.0, 0.0, 0.0);

        loop.Observe(target + new double3(0.0, 5_000.0, 0.0), target);
        loop.Observe(target + new double3(0.0, 4_000.0, 0.0), target);

        Assert.False(double.IsNaN(loop.LastAimMoveMetres));
        Assert.False(double.IsNaN(loop.LastImpactMoveMetres));
    }

    [Fact]
    public void AnUnreadableObservationReportsNoReadingEither()
    {
        AimCorrection loop = new();
        double3 target = new(6_371_000.0, 0.0, 0.0);

        loop.Observe(target + new double3(0.0, 5_000.0, 0.0), target);
        loop.Observe(new double3(double.NaN, 0.0, 0.0), target);

        Assert.True(double.IsNaN(loop.LastImpactMoveMetres));
    }
}
