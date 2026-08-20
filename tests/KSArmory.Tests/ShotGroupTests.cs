using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Whether a salvo counts as a shot that worked.
///
/// <para>Small, and every case here is one where the wrong rule reports a success. A harness whose
/// verdict is generous is worse than no harness: it is a green tick over a shot that scattered.</para>
/// </summary>
public class ShotGroupTests
{
    private static ShotGroup Salvo(params double[] misses)
    {
        ShotGroup group = new();

        foreach (double miss in misses)
        {
            group.Release();
            group.Arrive(miss);
        }

        return group;
    }

    /// <summary>
    /// The worst warhead decides, not the mean. Averaging is what turns a group straddling the
    /// target into a shot nobody fired.
    /// </summary>
    [Fact]
    public void TheWorstWarheadDecides()
    {
        ShotGroup group = Salvo(100.0, 200.0, 9_000.0);

        Assert.False(group.Judge(5_000.0).Pass);
        Assert.True(group.Judge(10_000.0).Pass);
    }

    /// <summary>A warhead that never arrived is a failure, not a warhead that does not count.</summary>
    [Fact]
    public void OneThatNeverArrivedFailsTheShot()
    {
        ShotGroup group = Salvo(100.0, 200.0);
        group.Release();

        ShotVerdict verdict = group.Judge(5_000.0);

        Assert.False(verdict.Pass);
        Assert.Contains("never did", verdict.Said);
    }

    /// <summary>
    /// An unmeasurable impact is one that never arrived. A miss that reads as zero and a direct hit
    /// are the two outcomes that must never be confused.
    /// </summary>
    [Fact]
    public void AnImpactNobodyCouldMeasureIsNotAHit()
    {
        ShotGroup group = new();
        group.Release();
        group.Arrive(double.NaN);

        Assert.Equal(0, group.Arrived);
        Assert.False(group.Judge(5_000.0).Pass);
    }

    /// <summary>A salvo that never left the tubes is not a pass on an empty group.</summary>
    [Fact]
    public void NothingReleasedIsNotAPass()
    {
        Assert.False(new ShotGroup().Judge(5_000.0).Pass);
    }

    /// <summary>
    /// The spread is the part no single aim can remove, so it is reported beside the miss rather
    /// than folded into it: a tight group far from the target and a scattered one centred on it
    /// have the same worst miss and want opposite things looked at.
    /// </summary>
    [Fact]
    public void TheSpreadIsReportedBesideTheMiss()
    {
        ShotGroup tight = Salvo(4_000.0, 4_100.0, 4_050.0);
        ShotGroup scattered = Salvo(50.0, 4_100.0, 2_000.0);

        Assert.Equal(tight.Worst, scattered.Worst, 6);
        Assert.True(scattered.Spread > tight.Spread * 10.0);
        Assert.Contains("spread", tight.Judge(5_000.0).Said);
    }

    /// <summary>The bar is in the verdict either way, because that is the thing to argue with.</summary>
    [Theory]
    [InlineData(1_000.0)]
    [InlineData(9_000.0)]
    public void TheBarIsAlwaysInTheVerdict(double bar)
    {
        Assert.Contains($"{bar / 1000.0:F1} km", Salvo(4_000.0).Judge(bar).Said);
    }
}
