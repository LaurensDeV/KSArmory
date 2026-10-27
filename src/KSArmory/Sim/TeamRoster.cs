namespace KSArmory;

/// <summary>
/// Which craft is on which side, as <em>declared</em> rather than as named — the half of IFF the
/// panel's flag writes.
///
/// <para>An installation's <see cref="IffPolicy.OwnTeam"/> is both the side it fights for and the
/// side everything else reads it and its rounds on; without this, two Blue sites read each other as
/// <see cref="Allegiance.Unknown"/>. A craft with no flag is on no team.</para>
///
/// <para>Keyed on the craft by reference, never by name: craft built from one blueprint share a
/// display name. Rebuilt once a frame from the live policies.</para>
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
    /// team declares nothing, which leaves the craft on no team.
    ///
    /// <para><paramref name="rank"/> settles a craft whose installations disagree, as after a split.
    /// Lowest wins, the first launcher, because the roster enumerates in a dictionary's order.</para>
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
}
