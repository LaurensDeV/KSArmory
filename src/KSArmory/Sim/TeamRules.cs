namespace KSArmory;

/// <summary>
/// One team's rules of engagement: which other teams it counts as allied or neutral, and what it
/// may fire on. A team's, not a weapon's: every system and director on the team takes these each
/// frame (<see cref="ApplyTo"/>), so the Teams window is the one place they are set.
/// </summary>
public sealed class TeamRules
{
    public HashSet<string> Allies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Neutrals { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool EngageUnknown = true;
    public bool EngageNeutral;
    public bool ProtectFriendly = true;

    /// <summary>What it makes of another team: allied, neutral or, by default, hostile.</summary>
    public Allegiance Towards(string team)
        => Neutrals.Contains(team) ? Allegiance.Neutral
         : Allies.Contains(team) ? Allegiance.Friendly
         : Allegiance.Hostile;

    /// <summary>Sets what it makes of another team, which is one of the three and never two.</summary>
    public void Set(string team, Allegiance allegiance)
    {
        Allies.Remove(team);
        Neutrals.Remove(team);

        if (allegiance == Allegiance.Friendly) Allies.Add(team);
        else if (allegiance == Allegiance.Neutral) Neutrals.Add(team);
    }

    /// <summary>Copies these onto one system's policy, leaving its own team as it is.</summary>
    public void ApplyTo(IffPolicy iff)
    {
        iff.EngageUnknown = EngageUnknown;
        iff.EngageNeutral = EngageNeutral;
        iff.ProtectFriendly = ProtectFriendly;

        iff.AlliedTeams.Clear();
        iff.AlliedTeams.UnionWith(Allies);
        iff.NeutralTeams.Clear();
        iff.NeutralTeams.UnionWith(Neutrals);
    }

    /// <summary>Takes a saved system's settings as the team's, which is how a team's rules come back with a save.</summary>
    public void TakeFrom(IffPolicy iff)
    {
        EngageUnknown = iff.EngageUnknown;
        EngageNeutral = iff.EngageNeutral;
        ProtectFriendly = iff.ProtectFriendly;

        Allies.Clear();
        Allies.UnionWith(iff.AlliedTeams);
        Neutrals.Clear();
        Neutrals.UnionWith(iff.NeutralTeams);
    }

    /// <summary>Forgets a team no longer declared.</summary>
    public void Forget(string team)
    {
        Allies.Remove(team);
        Neutrals.Remove(team);
    }
}
