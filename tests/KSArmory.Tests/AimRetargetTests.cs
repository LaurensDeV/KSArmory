using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What a bus's aim correction keeps when it hops to the next target.
///
/// <para>A walked stop freezes before its warheads leave, and the freeze reverts the bias to the best
/// the loop banked — which on an inner stop is the zero it started from, because a ~220 m first
/// reading banks and nothing under the flat 250 m band can beat it. The trim flew the bias the loop
/// had walked to, so that is the one a carried bias has to be.</para>
/// </summary>
public class AimRetargetTests
{
    private static double3 Target => new(6_371_000.0, 0.0, 0.0);

    // The arc falls `needed` short of wherever it is aimed: the coast plant at a response of one.
    private static void Walk(AimCorrection aim, double needed, int cycles)
    {
        for (int i = 0; i < cycles; i++)
        {
            double3 landed = Target + aim.BiasCci - new double3(0.0, needed, 0.0);
            aim.Observe(landed, Target);
        }
    }

    private static AimCorrection FrozenAfterAStop(double needed)
    {
        AimCorrection aim = new();
        aim.Retarget();
        Walk(aim, needed, 4);
        aim.Freeze();
        return aim;
    }

    [Fact]
    public void TheFreezeOnAnInnerStopRevertsToNoBiasAtAll()
    {
        AimCorrection aim = FrozenAfterAStop(220.0);

        Assert.Equal(0.0, Vec.Len(aim.BiasCci), 6);
    }

    [Fact]
    public void CarriedTheNextStopStartsFromTheBiasTheLoopWalkedTo()
    {
        AimCorrection aim = FrozenAfterAStop(220.0);

        aim.Retarget(carryBias: true);

        Assert.Equal(220.0, Vec.Len(aim.BiasCci), 3);
        Assert.False(aim.Settled);
    }

    [Fact]
    public void NotCarriedTheNextStopStartsFromNone()
    {
        AimCorrection aim = FrozenAfterAStop(220.0);

        aim.Retarget();

        Assert.Equal(0.0, Vec.Len(aim.BiasCci), 6);
    }

    /// <summary>
    /// Carried, the next stop's first reading is the small one and banks as the best; from zero
    /// it is the bias itself, which the flat band then never lets the loop beat.
    /// </summary>
    [Fact]
    public void CarriedTheFirstReadingIsTheResidualNotTheBias()
    {
        AimCorrection carried = FrozenAfterAStop(220.0);
        carried.Retarget(carryBias: true);
        Walk(carried, 230.0, 1);

        AimCorrection fresh = FrozenAfterAStop(220.0);
        fresh.Retarget();
        Walk(fresh, 230.0, 1);

        Assert.True(carried.BestMissMetres < 15.0, $"carried banked {carried.BestMissMetres:F1} m");
        Assert.True(fresh.BestMissMetres > 200.0, $"fresh banked {fresh.BestMissMetres:F1} m");
    }
}
