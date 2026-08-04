namespace AirDefence;

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
/// Decides which contacts a battery may engage. <b>IFF is Identification Friend or Foe</b> — the
/// radar-transponder scheme real air defence uses to avoid shooting its own side.
///
/// <para>KSA has no concept of sides, so teams are the mod's own: a contact carries whatever team
/// name it was assigned, and this compares it to the battery's. Team names are compared
/// case-insensitively and a null or empty name is <see cref="Allegiance.Unknown"/>, which is
/// deliberately not the same as hostile.</para>
///
/// <para>The defaults are permissive on purpose — <see cref="EngageUnknown"/> is true — so a world
/// where nobody has assigned teams behaves exactly as it did before teams existed. Turning it off
/// is what makes a hostiles-only engagement possible.</para>
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

    /// <summary>Teams treated as neutral rather than hostile. Case-insensitive.</summary>
    public HashSet<string> NeutralTeams { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a contact stands relative to <see cref="OwnTeam"/>.</summary>
    public Allegiance Classify(string? contactTeam)
    {
        if (string.IsNullOrWhiteSpace(contactTeam)) return Allegiance.Unknown;
        if (NeutralTeams.Contains(contactTeam)) return Allegiance.Neutral;

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
