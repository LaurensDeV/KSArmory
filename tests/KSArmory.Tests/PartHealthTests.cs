using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Parts with health: a burst costs its share times the scale, and at a scale of one a fresh part
/// breaks exactly where the sweep alone broke it.
/// </summary>
public class PartHealthTests
{
    private static MunitionProfile Warhead(float chargeKg) => new()
    {
        Name = "test",
        DisplayName = "test",
        ChargeKg = chargeKg,
    };

    [Fact]
    public void AtScaleOneAWholeShareBreaksAFreshPartAndLessDoesNot()
    {
        PartHealth ledger = new();
        object broken = new(), held = new();

        Assert.True(ledger.Hit(broken, 1.0, 1.0));
        Assert.False(ledger.Hit(held, 0.999, 1.0));
        Assert.Equal(0.0, ledger.Of(broken));
        Assert.Equal(0.001, ledger.Of(held), 9);
    }

    [Fact]
    public void LoadsShortOfBreakingAddUp()
    {
        PartHealth ledger = new();
        object part = new();

        Assert.False(ledger.Hit(part, 0.6, 1.0));
        Assert.True(ledger.Hit(part, 0.6, 1.0));
    }

    [Fact]
    public void ATenthScaleTakesTenWholeShares()
    {
        PartHealth ledger = new();
        object part = new();

        for (int i = 0; i < 9; i++) Assert.False(ledger.Hit(part, 1.0, 0.1));
        Assert.True(ledger.Hit(part, 1.0, 0.1));
        Assert.Equal(0.0, ledger.Of(part));
    }

    /// <summary>
    /// A burst against the skin is hundreds of shares; at a tenth it must still cost a tenth, or the
    /// scale does nothing against a gun.
    /// </summary>
    [Fact]
    public void NoOneHitCostsMoreThanAWholeShare()
    {
        PartHealth ledger = new();
        object part = new();

        Assert.False(ledger.Hit(part, 500.0, 0.1));
        Assert.Equal(0.9, ledger.Of(part), 9);
        Assert.True(new PartHealth().Hit(new object(), 500.0, 1.0));
    }

    [Fact]
    public void AnUntouchedPartIsWholeAndNothingIsNotAHit()
    {
        PartHealth ledger = new();
        object part = new();

        Assert.Equal(1.0, ledger.Of(part));
        Assert.False(ledger.Hit(part, 0.0, 1.0));
        Assert.False(ledger.Hit(part, double.NaN, 1.0));
        Assert.False(ledger.Hit(part, -5.0, 1.0));
        Assert.Equal(1.0, ledger.Of(part));
    }

    /// <summary>
    /// The calibration guard: over a sweep of gaps and strengths, a share of one or more is exactly
    /// what <see cref="BlastDamage.FailureRadius"/> breaks, so a fresh part at scale one breaks
    /// where it did before it had health.
    /// </summary>
    [Theory]
    [InlineData(0.02)]
    [InlineData(20.0)]
    [InlineData(20_000.0)]
    public void AShareOfOneIsExactlyTheFailureRadius(double chargeKg)
    {
        foreach (double tolerance in new[] { 1.0e5, 1.0e6, BlastDamage.ReferencePascals, 1.0e8 })
        {
            double failure = BlastDamage.FailureRadius(chargeKg, tolerance);
            double blast = KSArmory.Warhead.BlastRadius(chargeKg);

            for (double gap = 0.0; gap < blast * 1.5; gap += blast / 97.0)
            {
                double share = BlastDamage.Share(chargeKg, tolerance, gap);
                Assert.Equal(gap <= failure, share >= 1.0);
            }
        }
    }

    [Fact]
    public void AShareFallsWithDistanceAndIsNothingPastTheBlastRadius()
    {
        double blast = KSArmory.Warhead.BlastRadius(20.0);
        double previous = double.PositiveInfinity;

        for (double gap = 1.0; gap <= blast; gap += blast / 50.0)
        {
            double share = BlastDamage.Share(20.0, BlastDamage.ReferencePascals, gap);
            Assert.True(share <= previous);
            Assert.True(share > 0.0);
            previous = share;
        }

        Assert.Equal(0.0, BlastDamage.Share(20.0, BlastDamage.ReferencePascals, blast * 1.01));
    }

    /// <summary>The sweep over a craft and the per-part rule agree part for part.</summary>
    [Fact]
    public void SharesBreakWhatTheSweepBreaks()
    {
        MunitionProfile munition = Warhead(20f);
        double3 carrier = new(29_800 * 0.6, 29_800 * 0.8, 0);
        double3 burst = new(1.0e8, 2.0e8, 3.0e5);

        List<DamageablePart> parts = [];
        for (int i = 0; i < 40; i++)
        {
            double3 at = burst + (carrier * 0.004) + new double3(i * 2.0, 1.0, 0.5);
            parts.Add(new DamageablePart(i, at, 1.0, BlastDamage.ReferencePascals / (1 + (i % 5))));
        }

        List<int> swept = [];
        BlastDamage.Sweep(burst, -0.004, carrier, parts.ToArray(), munition, swept);

        List<(int Index, double Share)> shares = [];
        BlastDamage.Shares(burst, -0.004, carrier, parts.ToArray(), munition, shares);

        PartHealth ledger = new();
        object[] keys = parts.Select(_ => new object()).ToArray();
        int[] broken = shares.Where(s => ledger.Hit(keys[s.Index], s.Share, 1.0)).Select(s => s.Index).ToArray();

        Assert.NotEmpty(swept);
        Assert.True(swept.Count < parts.Count);
        Assert.Equal(swept.OrderBy(i => i), broken.OrderBy(i => i));
    }

    [Fact]
    public void FrontsMeetingAddOnlyTheirReflectionAndOnlyHeadOn()
    {
        (double real, double breaking) = BlastDamage.RealLoad(20.0, BlastDamage.ReferencePascals, 40.0);
        FrontLoad east = new(0.3, real, breaking, new double3(1, 0, 0), 0.0, 0.05);
        FrontLoad west = east with { Direction = new double3(-1, 0, 0) };

        Assert.Equal(0.0, BlastDamage.MeetingShare([east]));
        Assert.Equal(0.0, BlastDamage.MeetingShare([east, east]));
        Assert.True(BlastDamage.MeetingShare([east, west]) > 0.0);

        // Meeting never counts either front again: the whole combined share less both alone.
        (_, double combined) = BlastDamage.Combine([east, west]);
        Assert.Equal(combined - (2.0 * real / breaking), BlastDamage.MeetingShare([east, west]), 9);
    }
}
