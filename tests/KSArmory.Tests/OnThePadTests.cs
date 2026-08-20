using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The test the phase machine picks a vehicle up by, asked directly.
///
/// <para>It is public because a scripted shot has to know a rocket is still on the pad before it
/// lights it, and the two questions have to be one function. A second copy of this rule drifts
/// silently: the failure is a launch sequence entered for a vehicle already in the air, which flies
/// a pitch programme from wherever it happens to be.</para>
/// </summary>
public class OnThePadTests
{
    private const double TurnStart = 800.0;

    [Fact]
    public void AVehicleSittingStillLowDownIsOnTheGround()
    {
        Assert.True(IcbmProgram.IsOnTheGround(0.0, 0.0, TurnStart));
    }

    /// <summary>
    /// Both halves bind, and both are needed. Height alone calls a low pass on the deck a launch;
    /// speed alone calls a coasting apogee one.
    /// </summary>
    [Theory]
    [InlineData(0.0, AscentProfile.VerticalRiseSpeed + 1.0)]
    [InlineData(TurnStart + 1.0, 0.0)]
    public void EitherHeightOrSpeedTakesItOffThePad(double altitude, double airspeed)
    {
        Assert.False(IcbmProgram.IsOnTheGround(altitude, airspeed, TurnStart));
    }

    /// <summary>
    /// The height it is measured against is the vehicle's own turn altitude, not a constant. A pad
    /// on a plateau and one at sea level are the same situation, and the profile is what says so.
    /// </summary>
    [Fact]
    public void TheHeightComesFromTheProfileRatherThanFromAConstant()
    {
        Assert.False(IcbmProgram.IsOnTheGround(1_000.0, 0.0, TurnStart));
        Assert.True(IcbmProgram.IsOnTheGround(1_000.0, 0.0, 5_000.0));
    }
}
