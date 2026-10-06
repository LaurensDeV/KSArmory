using Xunit;

namespace KSArmory.Tests;

public class TeamRulesTests
{
    [Fact]
    public void ARelationIsOneOfTheThreeAndNeverTwo()
    {
        var rules = new TeamRules();
        Assert.Equal(Allegiance.Hostile, rules.Towards("Red"));

        rules.Set("Red", Allegiance.Friendly);
        Assert.Equal(Allegiance.Friendly, rules.Towards("Red"));

        rules.Set("red", Allegiance.Neutral);
        Assert.Equal(Allegiance.Neutral, rules.Towards("Red"));
        Assert.DoesNotContain("Red", rules.Allies);

        rules.Set("Red", Allegiance.Hostile);
        Assert.Empty(rules.Allies);
        Assert.Empty(rules.Neutrals);
    }

    [Fact]
    public void ASystemTakesItsTeamsRulesAndKeepsItsOwnTeam()
    {
        var rules = new TeamRules { EngageUnknown = false, EngageNeutral = true };
        rules.Set("Green", Allegiance.Friendly);

        var iff = new IffPolicy { OwnTeam = "Blue" };
        iff.NeutralTeams.Add("Stale");
        rules.ApplyTo(iff);

        Assert.Equal("Blue", iff.OwnTeam);
        Assert.False(iff.EngageUnknown);
        Assert.True(iff.EngageNeutral);
        Assert.Equal(Allegiance.Friendly, iff.Classify("Green"));
        Assert.Empty(iff.NeutralTeams);
    }

    [Fact]
    public void ATeamTakesItsRulesBackFromTheFirstSystemRestoredOnly()
    {
        var config = new Config();

        var first = new IffPolicy { OwnTeam = "Blue", EngageUnknown = false };
        first.AlliedTeams.Add("Green");
        var second = new IffPolicy { OwnTeam = "blue", EngageUnknown = true };

        config.AdoptRules(first);
        config.AdoptRules(second);

        TeamRules blue = config.RulesFor("Blue");
        Assert.False(blue.EngageUnknown);
        Assert.Equal(Allegiance.Friendly, blue.Towards("Green"));
    }

    [Fact]
    public void ForgettingATeamTakesItOutOfEveryOthersRelations()
    {
        var config = new Config();
        config.RulesFor("Blue").Set("Red", Allegiance.Friendly);
        config.NoTeamRules.Set("Red", Allegiance.Neutral);
        config.RulesFor("Red");

        config.ForgetRules("Red");

        Assert.False(config.TeamRules.ContainsKey("Red"));
        Assert.Equal(Allegiance.Hostile, config.RulesFor("Blue").Towards("Red"));
        Assert.Equal(Allegiance.Hostile, config.NoTeamRules.Towards("Red"));
    }
}
