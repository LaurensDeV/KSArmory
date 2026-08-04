using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// Identification Friend or Foe: which contacts a battery is allowed to shoot at.
///
/// <para>KSA has no concept of sides, so every rule here is the mod's own. The one that matters
/// most is that <see cref="Allegiance.Unknown"/> is not a synonym for hostile — a contact with no
/// team is unidentified, and whether that may be engaged is a policy choice.</para>
/// </summary>
public class IffTests
{
    private static IffPolicy Blue() => new() { OwnTeam = "Blue" };

    // ---- Classification --------------------------------------------------

    [Fact]
    public void SameTeamIsFriendly()
    {
        Assert.Equal(Allegiance.Friendly, Blue().Classify("Blue"));
    }

    [Fact]
    public void TeamNamesAreCaseInsensitive()
    {
        Assert.Equal(Allegiance.Friendly, Blue().Classify("blue"));
        Assert.Equal(Allegiance.Friendly, Blue().Classify("BLUE"));
    }

    [Fact]
    public void AnyOtherTeamIsHostile()
    {
        Assert.Equal(Allegiance.Hostile, Blue().Classify("Red"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoTeamIsUnknownRatherThanHostile(string? team)
    {
        Assert.Equal(Allegiance.Unknown, Blue().Classify(team));
    }

    /// <summary>
    /// A battery that has not picked a side cannot call anything hostile. Without this, the first
    /// craft to be given a team name would become an enemy of every unaligned battery at once.
    /// </summary>
    [Fact]
    public void ABatteryWithNoTeamOfItsOwnClassifiesNothing()
    {
        var policy = new IffPolicy();

        Assert.Equal(Allegiance.Unknown, policy.Classify("Red"));
        Assert.Equal(Allegiance.Unknown, policy.Classify("Blue"));
    }

    [Fact]
    public void ADeclaredNeutralIsNeitherSide()
    {
        IffPolicy policy = Blue();
        policy.NeutralTeams.Add("Civilian");

        Assert.Equal(Allegiance.Neutral, policy.Classify("Civilian"));
        Assert.Equal(Allegiance.Neutral, policy.Classify("civilian"));
    }

    /// <summary>Neutral wins over own-team, so a side cannot mark itself hostile by accident.</summary>
    [Fact]
    public void NeutralIsCheckedBeforeOwnTeam()
    {
        IffPolicy policy = Blue();
        policy.NeutralTeams.Add("Blue");

        Assert.Equal(Allegiance.Neutral, policy.Classify("Blue"));
    }

    // ---- Engagement ------------------------------------------------------

    [Fact]
    public void AFriendlyIsNeverEngagedWhileProtected()
    {
        IffPolicy policy = Blue();

        Assert.False(policy.MayEngage(Allegiance.Friendly));
        Assert.False(policy.MayEngageTeam("Blue"));
    }

    [Fact]
    public void AHostileIsAlwaysEngageable()
    {
        Assert.True(Blue().MayEngageTeam("Red"));
    }

    /// <summary>
    /// The default is permissive, so a world where nobody has assigned teams behaves exactly as it
    /// did before teams existed.
    /// </summary>
    [Fact]
    public void UnknownIsEngagedByDefault()
    {
        Assert.True(new IffPolicy().MayEngage(Allegiance.Unknown));
        Assert.True(Blue().MayEngageTeam(null));
    }

    [Fact]
    public void UnknownCanBeRefused()
    {
        IffPolicy policy = Blue();
        policy.EngageUnknown = false;

        Assert.False(policy.MayEngageTeam(null));
        Assert.True(policy.MayEngageTeam("Red"));
    }

    [Fact]
    public void NeutralIsRefusedByDefaultAndCanBeAllowed()
    {
        IffPolicy policy = Blue();
        policy.NeutralTeams.Add("Civilian");
        Assert.False(policy.MayEngageTeam("Civilian"));

        policy.EngageNeutral = true;
        Assert.True(policy.MayEngageTeam("Civilian"));
    }

    [Fact]
    public void FriendlyFireIsPossibleOnlyByExplicitlyTurningProtectionOff()
    {
        IffPolicy policy = Blue();
        Assert.False(policy.MayEngageTeam("Blue"));

        policy.ProtectFriendly = false;
        Assert.True(policy.MayEngageTeam("Blue"));
    }

    // ---- Through the threat model ---------------------------------------

    /// <summary>
    /// The gate fire control actually calls. A friendly well inside the engagement envelope must
    /// still be refused, which is why this is a separate question from range.
    /// </summary>
    [Fact]
    public void AFriendlyInsideTheEnvelopeIsStillRefused()
    {
        IffPolicy policy = Blue();
        var sensor = new SensorProfile { Name = "s", DisplayName = "s" };

        var friendly = new TrackState
        {
            Range = 5000, IsThreat = true,
            Team = "Blue", Allegiance = policy.Classify("Blue"),
        };
        var hostile = new TrackState
        {
            Range = 5000, IsThreat = true,
            Team = "Red", Allegiance = policy.Classify("Red"),
        };

        Assert.True(ThreatModel.InEngagementEnvelope(friendly, sensor));
        Assert.True(ThreatModel.InEngagementEnvelope(hostile, sensor));

        Assert.False(ThreatModel.MayEngage(friendly, policy));
        Assert.True(ThreatModel.MayEngage(hostile, policy));
    }

    // ---- More than two sides ---------------------------------------------

    /// <summary>
    /// Team names are arbitrary strings, so any number of sides can exist. With no alliances
    /// declared, every other team is hostile — a free-for-all.
    /// </summary>
    [Fact]
    public void EveryOtherTeamIsHostileInAFreeForAll()
    {
        IffPolicy blue = Blue();

        Assert.Equal(Allegiance.Friendly, blue.Classify("Blue"));
        Assert.Equal(Allegiance.Hostile, blue.Classify("Red"));
        Assert.Equal(Allegiance.Hostile, blue.Classify("Green"));
        Assert.Equal(Allegiance.Hostile, blue.Classify("Yellow"));
    }

    [Fact]
    public void AnAlliedTeamIsFriendlyWithoutSharingAName()
    {
        IffPolicy blue = Blue();
        blue.AlliedTeams.Add("Green");

        Assert.Equal(Allegiance.Friendly, blue.Classify("Green"));
        Assert.False(blue.MayEngageTeam("Green"));
        Assert.True(blue.MayEngageTeam("Red"));
    }

    /// <summary>
    /// A coalition is per-battery: each side lists the others. Nothing infers that an ally's ally
    /// is a friend, which keeps a battery's view of the world its own.
    /// </summary>
    [Fact]
    public void AlliancesAreDeclaredFromEachSideSeparately()
    {
        IffPolicy blue = new() { OwnTeam = "Blue" };
        blue.AlliedTeams.Add("Green");

        IffPolicy green = new() { OwnTeam = "Green" };

        // Blue holds fire; Green has not been told, so it does not.
        Assert.False(blue.MayEngageTeam("Green"));
        Assert.True(green.MayEngageTeam("Blue"));

        green.AlliedTeams.Add("Blue");
        Assert.False(green.MayEngageTeam("Blue"));
    }

    [Fact]
    public void ThreeSidedWarWithOneCoalition()
    {
        IffPolicy blue = new() { OwnTeam = "Blue" };
        blue.AlliedTeams.Add("Green");
        blue.NeutralTeams.Add("Civilian");

        Assert.False(blue.MayEngageTeam("Blue"));       // itself
        Assert.False(blue.MayEngageTeam("Green"));      // ally
        Assert.True(blue.MayEngageTeam("Red"));         // enemy
        Assert.True(blue.MayEngageTeam("Yellow"));      // unaligned enemy
        Assert.False(blue.MayEngageTeam("Civilian"));   // neutral
    }

    /// <summary>Neutral is checked before allied, so a team can be held off without being a friend.</summary>
    [Fact]
    public void NeutralOutranksAllied()
    {
        IffPolicy blue = Blue();
        blue.AlliedTeams.Add("Green");
        blue.NeutralTeams.Add("Green");

        Assert.Equal(Allegiance.Neutral, blue.Classify("Green"));
    }
}
