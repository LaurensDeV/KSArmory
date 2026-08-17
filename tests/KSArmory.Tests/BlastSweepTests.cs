using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// How near a burst a body was, and what that does to it.
///
/// <para>The measurement is shared by the two sweeps a burst runs — over craft and over rounds in
/// the air — so that they cannot disagree about how near "near" is while differing in what they do
/// about it.</para>
/// </summary>
public class BlastSweepTests
{
    // 29.8 km/s of ecliptic motion, oblique to everything, which is the general case.
    private static readonly double3 Carrier = new(29_800 * 0.6, 29_800 * 0.8, 0);

    private static MunitionProfile Warhead20Kg() => new()
    {
        Name = "test",
        DisplayName = "test",
        ChargeKg = 20f,
    };

    [Fact]
    public void TheGapIsToTheSurfaceNotTheCentre()
    {
        double3 burst = new(0, 0, 0);
        double3 target = new(100, 0, 0);

        Assert.Equal(90.0, BlastSweep.SurfaceGap(target, Vec.Zero, 0.0, burst, 10.0), 9);
    }

    /// <summary>
    /// A body inside its own mean radius reads as negative rather than clamping. The callers
    /// compare against a radius, so any negative answer is lethal either way — and clamping would
    /// hide a burst that went off inside a craft.
    /// </summary>
    [Fact]
    public void ABurstInsideTheHullReadsNegative()
    {
        Assert.True(BlastSweep.SurfaceGap(new double3(3, 0, 0), Vec.Zero, 0.0, Vec.Zero, 10.0) < 0.0);
    }

    /// <summary>
    /// The sample is carried forward to the burst's own instant. Without it the gap is measured
    /// against where the target was before the round finished its step, which at closing speed is
    /// metres of error across a fuse radius.
    /// </summary>
    [Fact]
    public void TheSampleIsCarriedForwardToTheBurst()
    {
        double3 burst = new(0, 0, 0);
        double3 sampled = new(500, 0, 0);
        double3 velocity = new(-1000, 0, 0);

        // 20 ms of closing at 1000 m/s is 20 m nearer than the sample says.
        Assert.Equal(480.0, BlastSweep.SurfaceGap(sampled, velocity, 0.02, burst, 0.0), 9);
    }

    /// <summary>
    /// The frame rule: both terms are ecliptic, so the motion they share cancels. Add 29.8 km/s to
    /// the whole scene — the burst, the sample and the body's velocity — and the gap does not move.
    ///
    /// <para>This is what stops a burst reporting a 500 m miss on a craft it went off against,
    /// which is what one term carrying the carrier and the other not would produce.</para>
    /// </summary>
    [Fact]
    public void SharedMotionDoesNotReachTheGap()
    {
        const double since = 1.0 / 60.0;

        double3 burst = new(1.5e11, 6.371e6, 0);
        double3 sampled = burst + new double3(240, 30, -12);
        double3 velocity = new(-900, 40, 0);

        double still = BlastSweep.SurfaceGap(sampled, velocity, since, burst, 8.0);

        // The same scene with the planet's motion added to it. The sample is the instant everything
        // is measured from, so it is where it was; the body now carries the extra velocity, and the
        // burst — which happens `since` later — has been carried that far by it.
        double carried = BlastSweep.SurfaceGap(sampled, velocity + Carrier, since,
                                               burst + (Carrier * since), 8.0);

        // Not exactly equal, and it cannot be: the carrier goes onto a 1.5e11 m coordinate and
        // comes off again, and a double holds that to about 30 um. What must not survive is metres.
        Assert.True(Math.Abs(still - carried) < 1e-3,
                    $"the carrier reached the gap: {Math.Abs(still - carried):F4} m");
    }

    // ---- What a gap that size means --------------------------------------

    [Fact]
    public void InsideTheLethalRadiusIsLethal()
    {
        MunitionProfile m = Warhead20Kg();

        Assert.Equal(BlastEffect.Lethal, BlastSweep.Effect(m.LethalRadius - 0.1, m));
        Assert.Equal(BlastEffect.Lethal, BlastSweep.Effect(-5.0, m));
    }

    [Fact]
    public void BetweenTheRadiiIsANearMiss()
    {
        MunitionProfile m = Warhead20Kg();

        Assert.Equal(BlastEffect.NearMiss, BlastSweep.Effect(m.LethalRadius + 0.1, m));
        Assert.Equal(BlastEffect.NearMiss, BlastSweep.Effect(m.BlastRadius, m));
    }

    [Fact]
    public void PastTheBlastRadiusIsUntouched()
    {
        MunitionProfile m = Warhead20Kg();

        Assert.Equal(BlastEffect.Untouched, BlastSweep.Effect(m.BlastRadius + 0.1, m));
    }

    /// <summary>
    /// The boundaries are inclusive on the near side, so no gap falls between two categories.
    /// Both radii come off one charge, so a warhead cannot be configured with the lethal radius
    /// the larger and there is no ordering to get wrong here.
    /// </summary>
    [Fact]
    public void EveryGapHasExactlyOneAnswer()
    {
        MunitionProfile m = Warhead20Kg();

        Assert.True(m.LethalRadius < m.BlastRadius);

        for (double gap = -5.0; gap < m.BlastRadius + 10.0; gap += 0.25)
        {
            BlastEffect effect = BlastSweep.Effect(gap, m);

            Assert.Equal(gap <= m.LethalRadius, effect == BlastEffect.Lethal);
            Assert.Equal(gap > m.BlastRadius, effect == BlastEffect.Untouched);
        }
    }
}
