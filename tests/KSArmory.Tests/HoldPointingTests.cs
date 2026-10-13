using Xunit;

namespace KSArmory.Tests;

public class HoldPointingTests
{
    [Fact]
    public void AShortTurnIsAccelerateThenDecelerate()
    {
        // 90 deg at 0.01 rad/s^2, well under a 30 deg/s limit: 2 * sqrt(1.5708 / 0.01) = 25.07 s.
        Assert.Equal(25.07, HoldPointing.TurnSeconds(Math.PI / 2.0, 0.01, Math.PI / 6.0), 2);
    }

    [Fact]
    public void ALongTurnCoastsAtTheRateLimit()
    {
        // 180 deg at 1 rad/s^2 against 5 deg/s: pi / 0.0873 + 0.0873 = 36.09 s.
        double limit = 5.0 * Math.PI / 180.0;
        Assert.Equal(Math.PI / limit + limit, HoldPointing.TurnSeconds(Math.PI, 1.0, limit), 6);
    }

    [Fact]
    public void TheLeadIsTheTurnWithSlackAndAMarginCappedAtFiveMinutes()
    {
        Assert.Equal(HoldPointing.MarginSeconds, HoldPointing.LeadSeconds(0.0, 0.01, 0.5));
        Assert.Equal(1.5 * 25.066 + 30.0, HoldPointing.LeadSeconds(Math.PI / 2.0, 0.01, Math.PI / 6.0), 1);
        Assert.Equal(HoldPointing.MaxLeadSeconds, HoldPointing.LeadSeconds(Math.PI, 1e-6, 0.5));
        Assert.Equal(HoldPointing.MaxLeadSeconds, HoldPointing.LeadSeconds(Math.PI / 2.0, double.NaN, 0.5));
    }

    [Fact]
    public void AStackStartsTurningAtItsLeadAndDriftsAgainOnlyWellPastIt()
    {
        Assert.False(HoldPointing.Points(91.0, 90.0, pointingNow: false));
        Assert.True(HoldPointing.Points(90.0, 90.0, pointingNow: false));

        // A window that moves a few seconds later keeps it pointing; one that moves minutes later lets it drift.
        Assert.True(HoldPointing.Points(140.0, 90.0, pointingNow: true));
        Assert.False(HoldPointing.Points(151.0, 90.0, pointingNow: true));
    }
}
