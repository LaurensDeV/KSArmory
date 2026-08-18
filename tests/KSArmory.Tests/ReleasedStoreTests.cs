using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A store is released by hand; a missile is launched at something. Everything that treats those
/// two differently asks <see cref="MunitionProfile.Powered"/>, and none of it may ask whether the
/// round is guided.
///
/// <para>The distinction is not academic. Keying these on "unguided" is what left a guided bomb
/// rack refusing its own trigger for "no lock" — on a rack carrying no radar to lock with — and
/// silently switched its bomb sight off at the same time. Both are invisible to every other check
/// in the repository: the part loads, the round seats, the log says nothing.</para>
/// </summary>
public class ReleasedStoreTests
{
    private static MunitionProfile With(GuidanceMode mode)
        => new() { Name = "t", DisplayName = "t", Guidance = mode };

    [Theory]
    [InlineData(GuidanceMode.None)]
    [InlineData(GuidanceMode.Inertial)]
    public void AStoreIsReleasedRatherThanLaunched(GuidanceMode mode)
        => Assert.False(With(mode).Powered);

    [Theory]
    [InlineData(GuidanceMode.Seeker)]
    [InlineData(GuidanceMode.AntiRadiation)]
    [InlineData(GuidanceMode.CommandLink)]
    public void EverythingElseLeavesUnderItsOwnPower(GuidanceMode mode)
        => Assert.True(With(mode).Powered);

    /// <summary>
    /// Every registered round the arsenal ships, so a new one declares which side it is on rather
    /// than inheriting an answer from whichever mode it happened to pick.
    /// </summary>
    [Fact]
    public void EveryRegisteredRoundAnswersTheQuestion()
    {
        foreach (MunitionProfile round in Arsenal.Munitions)
        {
            bool powered = round.Powered;
            Assert.Equal(round.Guidance is not (GuidanceMode.None or GuidanceMode.Inertial), powered);
        }
    }
}
