using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The claims each turret rung makes on the ring. A lead claim that outlives the tracking rung
/// that made it holds the missiles with "the cannon has the bearing" while the ring is driven by
/// hand, spun or stowed.
/// </summary>
public class TurretModeTests
{
    private static readonly TurretRung[] NotTracking =
        [TurretRung.Spin, TurretRung.Manual, TurretRung.Cursor, TurretRung.Designated, TurretRung.Stowed];

    [Fact]
    public void NoRungButTrackingClaimsTheGunLeadEvenWhenOneSolved()
    {
        foreach (TurretRung rung in NotTracking)
        {
            Assert.False(TurretMode.Claims(rung, leadSolved: true).OnGunLead, rung.ToString());
        }
    }

    [Fact]
    public void TrackingClaimsTheGunLeadOnlyWhenItSolved()
    {
        Assert.True(TurretMode.Claims(TurretRung.Tracking, leadSolved: true).OnGunLead);
        Assert.False(TurretMode.Claims(TurretRung.Tracking, leadSolved: false).OnGunLead);
    }

    [Fact]
    public void OnlyTheCursorRungClaimsTheCursor()
    {
        Assert.True(TurretMode.Claims(TurretRung.Cursor, leadSolved: false).OnCursor);
        Assert.False(TurretMode.Claims(TurretRung.Tracking, leadSolved: true).OnCursor);

        foreach (TurretRung rung in NotTracking)
        {
            if (rung != TurretRung.Cursor) Assert.False(TurretMode.Claims(rung, leadSolved: true).OnCursor);
        }
    }
}
