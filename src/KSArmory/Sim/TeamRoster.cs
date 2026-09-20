namespace KSArmory;

/// <summary>
/// Which craft is on which side, as <em>declared</em> rather than as named — the half of IFF the
/// panel's flag writes.
///
/// <para><b>An installation's <see cref="IffPolicy.OwnTeam"/> is both halves of its allegiance</b>:
/// the side it fights for, and the side everything else reads it and its rounds on. Without the
/// second half two sites whose flags both say Blue each classify the other as
/// <see cref="Allegiance.Unknown"/>, which <see cref="IffPolicy.EngageUnknown"/> engages by
/// default. <see cref="Teams.TeamFor"/> is the fallback underneath, for a craft nothing of this
/// mod's is fitted to.</para>
///
/// <para>Keyed on the craft by <b>reference</b>, never by name: craft built from one blueprint
/// share a display name, which <c>WeaponSystems.WarnIfNameIsTaken</c> already warns about.</para>
///
/// <para>Rebuilt once a frame from the live policies, in the same pass as the airborne census and
/// for the same reason: nothing about the answer is per system.</para>
/// </summary>
public sealed class TeamRoster
{
    private readonly Dictionary<object, Membership> _declared = new(ReferenceEqualityComparer.Instance);

    private readonly record struct Membership(string Team, int Rank);

    /// <summary>The rank a director declares at, so any weapon aboard outranks it.</summary>
    public const int DirectorRank = int.MaxValue;

    /// <summary>Starts a fresh census. Every member has to be re-declared after this.</summary>
    public void Clear() => _declared.Clear();

    /// <summary>
    /// Records that <paramref name="craft"/> fights for <paramref name="team"/>. A null or blank
    /// team declares nothing, which leaves the craft's name to answer for it.
    ///
    /// <para><paramref name="rank"/> settles a craft whose installations disagree — the panel's
    /// flag sets every one of them together, so that is the operator having edited a single
    /// system's team under Tuning. Lowest rank wins, which is the first launcher, because the roster
    /// enumerates in a dictionary's order and an allegiance decided by that is an unreproducible
    /// bug report.</para>
    /// </summary>
    public void Declare(object? craft, string? team, int rank = 0)
    {
        if (craft is null || string.IsNullOrWhiteSpace(team)) return;
        if (_declared.TryGetValue(craft, out Membership held) && held.Rank <= rank) return;

        _declared[craft] = new Membership(team, rank);
    }

    /// <summary>The side this craft was put on, or null when nothing has said.</summary>
    public string? For(object? craft)
        => craft is not null && _declared.TryGetValue(craft, out Membership m) ? m.Team : null;

    /// <summary>
    /// The side a contact is on: what it was declared to be, and failing that whatever its name
    /// resolves to. One call, so nothing can consult half of it.
    /// </summary>
    public string? TeamFor(object? craft, string? craftName, IReadOnlyList<string> teamNames)
        => For(craft) ?? Teams.TeamFor(craftName, teamNames);
}
