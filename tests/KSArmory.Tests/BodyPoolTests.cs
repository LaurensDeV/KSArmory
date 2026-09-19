using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Bodies lent to rounds with no tube to key one to. A body drawn for two rounds at once flickers
/// between them; one never returned is a shell nothing can draw again.
/// </summary>
public class BodyPoolTests
{
    private sealed class Round;

    [Fact]
    public void ARoundKeepsItsBodyForAsLongAsItAsks()
    {
        var pool = new BodyPool<Round>(4);
        var r = new Round();
        int slot = pool.SlotFor(r);
        for (int frame = 0; frame < 10; frame++) Assert.Equal(slot, pool.SlotFor(r));
        Assert.Equal(1, pool.InUse);
    }

    [Fact]
    public void TwoRoundsNeverShareABody()
    {
        var pool = new BodyPool<Round>(4);
        Assert.NotEqual(pool.SlotFor(new Round()), pool.SlotFor(new Round()));
    }

    [Fact]
    public void ARoundArrivingWhenEveryBodyIsLentGetsNone()
    {
        var pool = new BodyPool<Round>(2);
        pool.SlotFor(new Round());
        pool.SlotFor(new Round());
        Assert.Equal(-1, pool.SlotFor(new Round()));
        Assert.Equal(2, pool.InUse);
    }

    [Fact]
    public void AReturnedBodyIsLentAgainAndReported()
    {
        var pool = new BodyPool<Round>(2);
        var gone = new Round();
        var stays = new Round();
        int goneSlot = pool.SlotFor(gone);
        int staysSlot = pool.SlotFor(stays);

        var freed = new List<int>();
        pool.ReleaseWhere(r => ReferenceEquals(r, gone), freed);

        Assert.Equal([goneSlot], freed);
        Assert.Equal(staysSlot, pool.SlotFor(stays));
        Assert.Equal(goneSlot, pool.SlotFor(new Round()));
        Assert.Null(pool.OwnerOf(-1));
    }

    [Fact]
    public void AskingWhetherARoundHoldsABodyLendsItNone()
    {
        var pool = new BodyPool<Round>(1);
        var r = new Round();
        Assert.False(pool.Holds(r));
        Assert.Equal(0, pool.InUse);

        pool.SlotFor(r);
        Assert.True(pool.Holds(r));

        pool.ReleaseWhere(_ => true, []);
        Assert.False(pool.Holds(r));
    }

    [Fact]
    public void AnEmptyPoolLendsNothing() => Assert.Equal(-1, new BodyPool<Round>(0).SlotFor(new Round()));

    [Fact]
    public void ClearingReturnsEverything()
    {
        var pool = new BodyPool<Round>(3);
        pool.SlotFor(new Round());
        pool.SlotFor(new Round());
        pool.Clear();
        Assert.Equal(0, pool.InUse);
        Assert.Equal(0, pool.SlotFor(new Round()));
    }
}
