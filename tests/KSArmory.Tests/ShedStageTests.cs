using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which of the vehicles that appeared during a split is this stack's own half.
///
/// <para>Flown 2026-09-03: a world of eight rockets on one profile had computers adopting each
/// other's spent stages at 20 and 40 km, because the census took the nearest new vehicle at any
/// distance. What that costs is the separation gate — it measures the adopted stack, so it reads
/// kilometres and passes at once.</para>
/// </summary>
public class ShedStageTests
{
    private static ShedChoice Choose(params (int Index, double Metres)[] seen)
    {
        ShedCandidate[] candidates = new ShedCandidate[seen.Length];
        for (int i = 0; i < seen.Length; i++) candidates[i] = new ShedCandidate(seen[i].Index, seen[i].Metres);
        return ShedStage.Choose(candidates);
    }

    [Fact]
    public void TheOwnStackIsMetresAwayAndIsTaken()
    {
        ShedChoice choice = Choose((7, 12.4));

        Assert.Equal(ShedVerdict.Take, choice.Verdict);
        Assert.Equal(7, choice.Index);
    }

    /// <summary>
    /// The flown fault. A decoupler parts two halves at about a metre a second, so nothing this
    /// vehicle dropped is tens of kilometres away — and taking the nearest anyway is what adopted
    /// another rocket's stage.
    /// </summary>
    [Theory]
    [InlineData(20_000.0)]
    [InlineData(40_000.0)]
    public void AnotherRocketsStageIsNotAdoptedHoweverNearItIsToTheOthers(double metres)
    {
        ShedChoice choice = Choose((3, metres), (4, metres * 2.0));

        Assert.Equal(ShedVerdict.NotFound, choice.Verdict);
    }

    /// <summary>
    /// Two plausible halves are refused rather than guessed between. Choosing wrong reports a
    /// stack that is already clear, which authorises the trim alongside the real one; refusing
    /// only costs the clearance its distance, and it has a clock to fall back on.
    /// </summary>
    [Fact]
    public void TwoInsideTheBoundAreRefusedRatherThanGuessedBetween()
    {
        ShedChoice choice = Choose((1, 8.0), (2, 140.0));

        Assert.Equal(ShedVerdict.Ambiguous, choice.Verdict);
    }

    [Fact]
    public void NothingNewIsNotFoundRatherThanAnIndexOfZero()
    {
        Assert.Equal(ShedVerdict.NotFound, ShedStage.Choose([]).Verdict);
        Assert.Equal(ShedVerdict.NotFound, Choose((5, double.NaN)).Verdict);
    }

    /// <summary>The bound is on distance alone, so ordering cannot change the verdict.</summary>
    [Fact]
    public void TheFarOneIsRejectedWhicheverOrderItIsSeenIn()
    {
        Assert.Equal(ShedVerdict.Take, Choose((1, 30.0), (2, 55_000.0)).Verdict);
        Assert.Equal(ShedVerdict.Take, Choose((2, 55_000.0), (1, 30.0)).Verdict);
        Assert.Equal(1, Choose((2, 55_000.0), (1, 30.0)).Index);
    }
}
