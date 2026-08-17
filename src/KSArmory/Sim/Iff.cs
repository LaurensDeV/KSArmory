namespace KSArmory;

/// <summary>Which side a contact is on, as far as this battery knows.</summary>
public enum Allegiance
{
    /// <summary>No team assigned, or none recognised.</summary>
    Unknown,

    /// <summary>Same team as the battery.</summary>
    Friendly,

    /// <summary>A team the battery is at war with.</summary>
    Hostile,

    /// <summary>A team that is neither, and is not to be shot at.</summary>
    Neutral,
}

/// <summary>
/// Which team a craft's name puts it on, and the half of IFF that runs before
/// <see cref="IffPolicy.Classify"/> gets a string to compare.
/// </summary>
public static class Teams
{
    /// <summary>
    /// The team whose name appears in <paramref name="craftName"/>, or null if none does.
    ///
    /// <para>A <b>substring</b> match, because KSA has no team field and a craft's display name is
    /// the only assignment available without asking the player to fill in a second one. That is
    /// also its trap, and it is not fixable from here: a craft called "Redstone" lands on team
    /// "Red" without anyone having said so. Longest match wins, so listing "Red Team" alongside
    /// "Red" resolves the pair that is actually ambiguous; nothing resolves the pair that merely
    /// shares a prefix.</para>
    /// </summary>
    public static string? TeamFor(string? craftName, IReadOnlyList<string> teamNames)
    {
        if (string.IsNullOrEmpty(craftName) || teamNames.Count == 0) return null;

        string? best = null;

        for (int i = 0; i < teamNames.Count; i++)
        {
            string team = teamNames[i];

            if (string.IsNullOrWhiteSpace(team)) continue;
            if (craftName.Contains(team, StringComparison.OrdinalIgnoreCase)
                && (best is null || team.Length > best.Length))
            {
                best = team;
            }
        }

        return best;
    }
}

/// <summary>
/// Decides which contacts a battery may engage. <b>IFF is Identification Friend or Foe</b> — the
/// radar-transponder scheme real air defence uses to avoid shooting its own side.
///
/// <para>KSA has no concept of sides, so teams are the mod's own: a contact carries whatever team
/// name it was assigned, and this compares it to the battery's. Team names are compared
/// case-insensitively and a null or empty name is <see cref="Allegiance.Unknown"/>, which is
/// deliberately not the same as hostile.</para>
///
/// <para>The defaults are permissive on purpose — <see cref="EngageUnknown"/> is true — so a world
/// with no teams assigned engages everything. Turning it off is what makes a hostiles-only
/// engagement possible.</para>
/// </summary>
public sealed class IffPolicy
{
    /// <summary>This battery's own team. Null or empty means it has not picked a side.</summary>
    public string? OwnTeam { get; set; }

    /// <summary>Engage contacts with no recognised team. On by default.</summary>
    public bool EngageUnknown { get; set; } = true;

    /// <summary>Engage contacts on a declared-neutral team. Off by default.</summary>
    public bool EngageNeutral { get; set; }

    /// <summary>
    /// Never engage a friendly, whatever else says otherwise. Settable so a test range can be set
    /// up, but it should stay on.
    /// </summary>
    public bool ProtectFriendly { get; set; } = true;

    /// <summary>
    /// Teams counted as friendly alongside <see cref="OwnTeam"/>. Case-insensitive.
    ///
    /// <para>Any number of teams can exist — a name is just a string — and without this every
    /// team but one is hostile, which is a free-for-all. A coalition needs each member to list
    /// the others.</para>
    /// </summary>
    public HashSet<string> AlliedTeams { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Teams treated as neutral rather than hostile. Case-insensitive.</summary>
    public HashSet<string> NeutralTeams { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a contact stands relative to <see cref="OwnTeam"/>.</summary>
    public Allegiance Classify(string? contactTeam)
    {
        if (string.IsNullOrWhiteSpace(contactTeam)) return Allegiance.Unknown;
        if (NeutralTeams.Contains(contactTeam)) return Allegiance.Neutral;
        if (AlliedTeams.Contains(contactTeam)) return Allegiance.Friendly;

        if (string.IsNullOrWhiteSpace(OwnTeam)) return Allegiance.Unknown;

        return string.Equals(contactTeam, OwnTeam, StringComparison.OrdinalIgnoreCase)
            ? Allegiance.Friendly
            : Allegiance.Hostile;
    }

    /// <summary>Whether a contact of this allegiance may be fired on.</summary>
    public bool MayEngage(Allegiance allegiance) => allegiance switch
    {
        Allegiance.Friendly => !ProtectFriendly,
        Allegiance.Hostile => true,
        Allegiance.Neutral => EngageNeutral,
        _ => EngageUnknown,
    };

    /// <summary>Classify and decide in one step.</summary>
    public bool MayEngageTeam(string? contactTeam) => MayEngage(Classify(contactTeam));
}
