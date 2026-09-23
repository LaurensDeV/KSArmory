using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Which side a craft is on when it has been <em>told</em>, which is the half a display name
/// cannot carry.
///
/// <para>The flag on a switcher row wrote <c>IffPolicy.OwnTeam</c> — the side an installation
/// fights for — and nothing read it back, so a contact's side came off its craft name alone. Two
/// sites whose flags both said Blue therefore classified each other as <see cref="Allegiance.Unknown"/>,
/// which <see cref="IffPolicy.EngageUnknown"/> engages by default. It showed on the rounds rather
/// than the mounts because two craft standing on the same ground are below
/// <c>SensorProfile.MinTargetSpeed</c> and never enter each other's track lists at all, while
/// their shells at a kilometre a second do.</para>
/// </summary>
public class TeamRosterTests
{
    private static readonly List<string> Sides = ["Red", "Blue"];

    /// <summary>A craft nothing has spoken for falls through to its name, as it always did.</summary>
    [Fact]
    public void ACraftNobodyHasPlacedIsPlacedByItsName()
    {
        var roster = new TeamRoster();
        var craft = new object();

        Assert.Null(roster.For(craft));
        Assert.Equal("Red", roster.TeamFor(craft, "Red Leader", Sides));
        Assert.Null(roster.TeamFor(craft, "Kerbal X", Sides));
    }

    /// <summary>
    /// The bug, in one assertion. Two craft named after what they are rather than whose they are,
    /// both put on Blue: each has to read the other as friendly.
    /// </summary>
    [Fact]
    public void TwoCraftOnOneTeamReadAsFriendlyWhateverTheyAreCalled()
    {
        var roster = new TeamRoster();
        object pantsir = new(), phalanx = new();

        roster.Declare(pantsir, "Blue");
        roster.Declare(phalanx, "Blue");

        var iff = new IffPolicy { OwnTeam = "Blue" };

        Assert.Equal(Allegiance.Friendly,
                     iff.Classify(roster.TeamFor(phalanx, "Mk 15 Phalanx", Sides)));
        Assert.False(iff.MayEngageTeam(roster.TeamFor(phalanx, "Mk 15 Phalanx", Sides)));
    }

    /// <summary>
    /// And the state it was in before, which is what that assertion has to fail against: without
    /// the declaration the same pair is Unknown, and Unknown is engaged by default.
    /// </summary>
    [Fact]
    public void WithoutTheDeclarationThatPairIsEngageable()
    {
        var roster = new TeamRoster();
        var iff = new IffPolicy { OwnTeam = "Blue" };

        Assert.Equal(Allegiance.Unknown, iff.Classify(roster.TeamFor(new object(), "Mk 15 Phalanx", Sides)));
        Assert.True(iff.MayEngageTeam(roster.TeamFor(new object(), "Mk 15 Phalanx", Sides)));
    }

    /// <summary>
    /// The flag beats the name, because it is the one somebody set on purpose — and because the
    /// substring rule has no way to tell "Redstone" from a craft on Red.
    /// </summary>
    [Fact]
    public void WhatTheFlagSaysBeatsWhatTheNameSays()
    {
        var roster = new TeamRoster();
        var craft = new object();

        roster.Declare(craft, "Blue");

        Assert.Equal("Blue", roster.TeamFor(craft, "Red Leader", Sides));
    }

    /// <summary>
    /// Declaring nothing is not declaring "no team": a craft whose flag is clear is still placed
    /// by its name, which is the only thing left that can place it.
    /// </summary>
    [Fact]
    public void ABlankDeclarationLeavesTheNameToAnswer()
    {
        var roster = new TeamRoster();
        var craft = new object();

        roster.Declare(craft, null);
        roster.Declare(craft, "   ");

        Assert.Null(roster.For(craft));
        Assert.Equal("Red", roster.TeamFor(craft, "Red Leader", Sides));
    }

    /// <summary>
    /// Keyed on the craft, never on its name: two craft off one blueprint share a display name,
    /// which <c>WeaponSystems.WarnIfNameIsTaken</c> already warns about. Sharing a team entry
    /// would put one side's launcher on the other's side.
    /// </summary>
    [Fact]
    public void TwoCraftSharingANameAreStillTwoCraft()
    {
        var roster = new TeamRoster();
        object mine = new(), theirs = new();

        roster.Declare(mine, "Red");
        roster.Declare(theirs, "Blue");

        Assert.Equal("Red", roster.For(mine));
        Assert.Equal("Blue", roster.For(theirs));
    }

    /// <summary>
    /// A craft whose installations disagree resolves the same way every frame. The roster
    /// enumerates in a dictionary's order, so without the rank the answer would depend on it —
    /// an allegiance that changes between sessions and cannot be reproduced.
    /// </summary>
    [Fact]
    public void TheLowestRankWinsWhateverOrderTheyAreDeclaredIn()
    {
        var craft = new object();

        var first = new TeamRoster();
        first.Declare(craft, "Red", 0);
        first.Declare(craft, "Blue", 1);

        var second = new TeamRoster();
        second.Declare(craft, "Blue", 1);
        second.Declare(craft, "Red", 0);

        Assert.Equal("Red", first.For(craft));
        Assert.Equal("Red", second.For(craft));
    }

    /// <summary>A director never outranks a weapon, whichever order they are declared in.</summary>
    [Fact]
    public void AWeaponOutranksADirector()
    {
        var roster = new TeamRoster();
        var craft = new object();

        roster.Declare(craft, "Blue", TeamRoster.DirectorRank);
        roster.Declare(craft, "Red", 0);

        Assert.Equal("Red", roster.For(craft));
    }

    /// <summary>
    /// Rebuilt rather than kept, because the entries key on live craft and a roster holding a
    /// destroyed one is what KsaWorld's census note forbids. A clear has to actually forget.
    /// </summary>
    [Fact]
    public void ClearingForgetsEveryDeclaration()
    {
        var roster = new TeamRoster();
        var craft = new object();

        roster.Declare(craft, "Red");
        roster.Clear();

        Assert.Null(roster.For(craft));
    }
}
