namespace KSArmory;

/// <summary>
/// What to call a round in a line somebody reads.
///
/// <para><see cref="IProjectile.Tube"/> carries two different things in one field. A round from a
/// tube gets that tube's number from one, which is the range <see cref="Magazine"/> owns and the
/// index its body subpart is selected by. A gun round has no tube at all — the cannon fire through
/// <see cref="LauncherProfile.GunMuzzles"/>, and a launcher with a cannon may declare no tubes
/// whatever — so a shell carries the negative of its barrel number instead. That sign is what
/// keeps a shell out of the magazine's range, so it can never be reclaimed as a tube or write
/// itself over a missile's body.</para>
///
/// <para>Which makes it a number that must not be printed raw: tubes are numbered from one
/// everywhere a player sees one, so a negative reads as a fault in the launcher rather than as the
/// shell it actually is.</para>
/// </summary>
internal static class RoundLabel
{
    /// <summary>Whether this index marks a gun round rather than one from a tube.</summary>
    public static bool IsGunRound(int tube) => tube < 0;

    /// <summary>Which barrel a gun round left, numbered from one like a tube.</summary>
    public static int Barrel(int tube) => -tube;

    /// <summary>
    /// The round as the subject of a sentence: <c>round 4</c>, or <c>shell from barrel 4</c>.
    /// </summary>
    public static string For(int tube) =>
        IsGunRound(tube) ? $"shell from barrel {Barrel(tube)}" : $"round {tube}";
}
