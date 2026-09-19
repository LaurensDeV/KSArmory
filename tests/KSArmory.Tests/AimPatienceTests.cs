using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What <see cref="AimCorrection.WorseBeforeStopping"/> is actually counting.
///
/// <para>The name, and the reasoning it was chosen for, are both about a <em>run</em>: the miss is
/// not monotonic in the aim, so a loop that stops the first time it stops improving stops inside a
/// patch it would have come out the far side of — measured at 7,645 km as a five-cycle excursion
/// with a better aim 38 km beyond it. Twelve is how long the loop sits through such a patch.</para>
///
/// <para><b>The counter was never reset by a pass that was neither better nor worse</b>, so it
/// accumulated across a whole flight and twelve scattered excursions stopped the loop as readily as
/// one run of twelve. It was unreachable while the band was a flat 250 m — nothing at this shot's
/// scale is 250 m worse than the best — and <c>AimThresholdTracksTheMiss</c> is what makes both
/// branches live: <c>docs/ACCURACY-PLAN.md</c> 3bz.</para>
/// </summary>
public class AimPatienceTests
{
    // A metre of aim per unit, so a miss can be dialled in directly.
    private static double3 Target => new(6_371_000.0, 0.0, 0.0);
    private static double3 LandedBy(double metres) => Target + new double3(0.0, metres, 0.0);

    private static AimCorrection Walked(params double[] misses)
    {
        AimCorrection aim = new();

        foreach (double m in misses) aim.Observe(LandedBy(m), Target, thresholdTracksTheMiss: true);

        return aim;
    }

    /// <summary>
    /// The patch it was built for still stops it — otherwise this is patience without a limit.
    /// </summary>
    [Fact]
    public void AnUnbrokenRunOfWorsePassesStillStopsIt()
    {
        double[] misses = new double[1 + AimCorrection.WorseBeforeStopping];
        misses[0] = 100.0;
        for (int i = 1; i < misses.Length; i++) misses[i] = 400.0;

        Assert.True(Walked(misses).Settled);
    }

    /// <summary>
    /// The same twelve excursions with a level pass between each. No run of twelve happened, so the
    /// loop has seen no reason to give up — and against the cumulative counter this settles.
    /// </summary>
    [Fact]
    public void ExcursionsSeparatedByLevelPassesDoNotStopIt()
    {
        List<double> misses = [100.0];
        for (int i = 0; i < AimCorrection.WorseBeforeStopping; i++)
        {
            misses.Add(400.0);
            // Inside the band about the best, so it is neither an improvement nor an excursion.
            misses.Add(100.0 + AimCorrection.ImprovedByFraction * 100.0 * 0.5);
        }

        Assert.False(Walked([.. misses]).Settled);
    }

    /// <summary>
    /// A pass that improves has always reset it, and that is not what changed.
    ///
    /// <para>Flat band, so every step down is a genuine improvement however far the walk goes — a
    /// proportional one stops registering once the improvements fall under its floor, which ends
    /// the loop for a reason that has nothing to do with patience.</para>
    /// </summary>
    [Fact]
    public void AnImprovementStillClearsTheCount()
    {
        const double Step = 1.5 * AimCorrection.ImprovedByMetres;

        double best = 100_000.0;
        List<double> misses = [best];
        for (int i = 0; i < 4 * AimCorrection.WorseBeforeStopping; i++)
        {
            misses.Add(best + Step);
            best -= Step;
            misses.Add(best);
        }

        AimCorrection aim = new();
        foreach (double m in misses) aim.Observe(LandedBy(m), Target, thresholdTracksTheMiss: false);

        Assert.False(aim.Settled);
    }
}
