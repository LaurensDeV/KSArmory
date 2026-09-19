using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// How a round names itself in a line a player reads.
///
/// <para><see cref="IProjectile.Tube"/> is a tube number for a round from a tube and a sentinel
/// for a shell, and the two ranges do not overlap by design — see <see cref="RoundLabel"/>. What
/// is pinned here is that the sentinel never reaches a reader: a negative tube number reads as a
/// broken launcher rather than as the cannon working exactly as built.</para>
/// </summary>
public class RoundLabelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(12)]
    public void ARoundFromATubeIsNamedByThatTube(int tube)
    {
        Assert.Equal($"round {tube}", RoundLabel.For(tube));
        Assert.False(RoundLabel.IsGunRound(tube));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(-4, 4)]
    [InlineData(-6, 6)]
    public void AShellIsNamedByItsBarrelAndNotByATube(int tube, int barrel)
    {
        Assert.True(RoundLabel.IsGunRound(tube));
        Assert.Equal(barrel, RoundLabel.Barrel(tube));
        Assert.Equal($"shell from barrel {barrel}", RoundLabel.For(tube));
    }

    /// <summary>
    /// The property that holds whatever the wording becomes: nothing a player reads may carry a
    /// negative number, because tubes are numbered from one everywhere else they appear.
    /// </summary>
    [Fact]
    public void NoLabelEverShowsANegativeNumber()
    {
        for (int tube = -32; tube <= 32; tube++)
        {
            if (tube == 0) continue;
            Assert.DoesNotContain("-", RoundLabel.For(tube));
        }
    }

    /// <summary>
    /// A shell says that it is one. Naming the barrel alone would be as opaque as the tube number
    /// was: a reader has to tell a shell from a missile without knowing the encoding.
    /// </summary>
    [Fact]
    public void AShellSaysItIsAShell()
    {
        Assert.Contains("shell", RoundLabel.For(-4));
        Assert.DoesNotContain("shell", RoundLabel.For(4));
    }
}
