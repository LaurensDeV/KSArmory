using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Scoring a salvo that went to several places.
///
/// <para>The load-bearing one is <see cref="OneTargetIsWordForWordTheGroupsOwnVerdict"/>. Every
/// accuracy number this project owns is measured on the single-target shot, so if a board of one
/// ever stops reproducing a bare <see cref="ShotGroup"/> exactly, every historical comparison is
/// against a different ruler and nothing says so.</para>
/// </summary>
public class ShotBoardTests
{
    private const double Bar = 5_000.0;

    private static void Send(ShotGroup group, params double[] misses)
    {
        foreach (double miss in misses)
        {
            group.Release();
            group.Arrive(miss);
        }
    }

    /// <summary>
    /// A board of one produces the group's verdict character for character, pass included — for
    /// every shape of outcome the group has words for.
    /// </summary>
    [Theory]
    [InlineData(new double[] { 100.0, 200.0, 300.0 })]
    [InlineData(new double[] { 9_000.0 })]
    [InlineData(new double[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 })]
    [InlineData(new double[] { })]
    public void OneTargetIsWordForWordTheGroupsOwnVerdict(double[] misses)
    {
        ShotGroup alone = new();
        ShotBoard board = new(1);

        Send(alone, misses);
        Send(board.For(0), misses);

        ShotVerdict was = alone.Judge(Bar);
        ShotVerdict now = board.Judge(Bar);

        Assert.Equal(was.Said, now.Said, ignoreCase: false);
        Assert.Equal(was.Pass, now.Pass);
        Assert.Equal(alone.Released, board.Released);
        Assert.Equal(alone.Arrived, board.Arrived);
    }

    /// <summary>A request that named no aim at all is still one group, not none.</summary>
    [Fact]
    public void ARequestThatNamedNowhereStillHasOneGroup()
    {
        ShotBoard board = new(0);

        Assert.Equal(1, board.Targets);
        Assert.False(board.Split);
        Assert.Equal(new ShotGroup().Judge(Bar).Said, board.Judge(Bar).Said);
    }

    /// <summary>
    /// A warhead that went to target two is judged against target two, and cannot drag target one's
    /// group with it.
    /// </summary>
    [Fact]
    public void EachTargetIsJudgedOnItsOwnWarheads()
    {
        ShotBoard board = new(2);

        Send(board.For(0), 100.0, 150.0, 200.0);
        Send(board.For(1), 9_000.0, 9_100.0, 9_200.0);

        Assert.True(board.JudgeTarget(0, Bar).Pass);
        Assert.False(board.JudgeTarget(1, Bar).Pass);
        Assert.False(board.Judge(Bar).Pass);
        Assert.Contains("target 2", board.Judge(Bar).Said);
    }

    /// <summary>
    /// One target short is the whole flight short. A walk that only pays for five of six stops is a
    /// worse shot than one that reached them all, whatever the five that arrived measured.
    /// </summary>
    [Fact]
    public void ATargetThatGotNothingFailsTheFlight()
    {
        ShotBoard board = new(3);

        Send(board.For(0), 100.0, 110.0);
        Send(board.For(2), 120.0, 130.0);

        ShotVerdict verdict = board.Judge(Bar);

        Assert.False(verdict.Pass);
        Assert.Contains("target 2 failed", verdict.Said);
        Assert.Equal(4, board.Released);
        Assert.Equal(4, board.Arrived);
    }

    /// <summary>
    /// A split flight's verdict carries no group mean, best or spread — the four words
    /// <c>tools/shot-report.py</c>'s VERDICT pattern reads a single-target group off. Twenty
    /// kilometres of deliberate separation in that shape is a twenty-kilometre miss to every night
    /// already flown.
    /// </summary>
    [Fact]
    public void ASplitFlightReportsNoGroupStatistics()
    {
        ShotBoard board = new(2);

        Send(board.For(0), 100.0);
        Send(board.For(1), 20_000.0);

        string said = board.Judge(50_000.0).Said;

        Assert.DoesNotContain("mean ", said);
        Assert.DoesNotContain("best ", said);
        Assert.DoesNotContain("spread ", said);
        Assert.Contains("2 targets", said);
    }

    /// <summary>
    /// The worst is the worst anywhere, and it names where. A flight whose second stop scattered
    /// reads as a fault at that stop rather than as a wide group.
    /// </summary>
    [Fact]
    public void TheWorstNamesTheTargetItLandedNear()
    {
        ShotBoard board = new(3);

        Send(board.For(0), 100.0);
        Send(board.For(1), 400.0);
        Send(board.For(2), 250.0);

        Assert.Contains("worst 0.400 km on target 2", board.Judge(Bar).Said);
    }

    /// <summary>
    /// Nothing anywhere is said as such rather than as a NaN. A board every one of whose groups is
    /// empty is a flight that released nothing, and the words for that are the group's own.
    /// </summary>
    [Fact]
    public void ASplitFlightThatReleasedNothingSaysSo()
    {
        ShotBoard board = new(2);

        ShotVerdict verdict = board.Judge(Bar);

        Assert.False(verdict.Pass);
        Assert.Contains("nothing arrived anywhere", verdict.Said);
        Assert.DoesNotContain("NaN", verdict.Said);
    }

    /// <summary>
    /// A round nobody could attribute is scored somewhere rather than dropped. Losing it would make
    /// an attribution fault read as a shot with fewer, better warheads.
    /// </summary>
    [Fact]
    public void AnUnattributableRoundIsStillCounted()
    {
        ShotBoard board = new(2);

        Send(board.For(-1), 100.0);
        Send(board.For(97), 200.0);

        Assert.Equal(2, board.Released);
        Assert.Equal(2, board.Arrived);
    }
}
