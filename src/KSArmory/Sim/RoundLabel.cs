namespace KSArmory;

/// <summary>
/// What to call a round in a line somebody reads.
///
/// <para><see cref="IProjectile.Tube"/> is a tube number from one for a round from a tube, and the
/// negative of the barrel number for a shell, which keeps a shell out of <see cref="Magazine"/>'s
/// range. So it must never be printed raw.</para>
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
