namespace KSArmory;

/// <summary>One craft that might now be carrying a part that has gone missing.</summary>
/// <param name="CraftIndex">Opaque to this file: the caller's own handle on the craft.</param>
/// <param name="AlreadyCrewed">Another entry already runs this one, so it is not ours.</param>
internal readonly record struct HandoverCandidate(
    int CraftIndex, int Ordinal, double MetresFromPlatform, bool AlreadyCrewed);

internal enum HandoverVerdict
{
    /// <summary>Nothing carries it. The entry stays where it is and looks again next frame.</summary>
    NotFound,

    Move,

    /// <summary>Two craft, too close to tell apart. Refused, because guessing is worse.</summary>
    Ambiguous,
}

internal readonly record struct Handover(HandoverVerdict Verdict, int CraftIndex, int Ordinal, string Why);

/// <summary>
/// Which craft a part went to, when it is no longer on the one that was carrying it.
///
/// <para>A decoupler splits a stack in two and the part leaves on one half — so anything that was
/// pinned to the whole stack has to follow it. A roster is the only thing that knows what else is
/// crewed, so it makes this decision; the arithmetic is here because the rosters are KSA-facing
/// and unreachable from the tests.</para>
///
/// <para>One decision for every roster that has to follow a part: launchers and optical directors
/// are separate parts on possibly separate halves of a split, so each asks this for itself. What
/// must not vary between them is where the line is drawn, which is what this file is.</para>
///
/// <para><b>Absence never moves anything.</b> A part tree read mid-rebuild returns nothing, which
/// is indistinguishable from a part that has genuinely gone — so a part going missing only ever
/// starts a search, and nothing changes unless one is positively found somewhere else.</para>
/// </summary>
internal static class PlatformHandover
{
    /// <summary>
    /// Beyond this, it is somebody else's part.
    ///
    /// <para>The real separation a frame after a split is a stack length plus a decoupler shove —
    /// metres. This is loose because timewarp is not held down during a coast with nothing in the
    /// air, so a long step between the split and the notice can put the pair a good way apart.</para>
    /// </summary>
    public const double MaxMetres = 10_000.0;

    /// <summary>
    /// Two <em>craft</em> closer together than this cannot be told apart, so neither is chosen.
    ///
    /// <para>Picking the wrong one is worse than picking none: it strands the operator's settings
    /// on the wrong craft <em>and</em> leaves the right one to be crewed fresh with defaults.</para>
    ///
    /// <para>Between craft, never between sightings. Two of the part on one craft say the same
    /// thing about where it went, and the ordinal is which of them — so refusing there strands
    /// both halves of a craft that carried a pair, which is the case a roster keyed on an ordinal
    /// exists for.</para>
    /// </summary>
    public const double AmbiguousWithinMetres = 50.0;

    public static Handover Choose(IReadOnlyList<HandoverCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        HandoverCandidate best = default;
        bool haveBest = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            HandoverCandidate c = candidates[i];
            if (!Eligible(c)) continue;

            // Two on one craft are the same distance away, so part order decides between them
            // rather than whichever the caller happened to walk first.
            bool better = !haveBest
                          || c.MetresFromPlatform < best.MetresFromPlatform
                          || (c.CraftIndex == best.CraftIndex && c.Ordinal < best.Ordinal);

            if (better)
            {
                best = c;
                haveBest = true;
            }
        }

        if (!haveBest) return new Handover(HandoverVerdict.NotFound, -1, -1, "nothing carries it");

        // The nearest sighting on some *other* craft, which is the only thing that can make this
        // a guess: see AmbiguousWithinMetres.
        HandoverCandidate rival = default;
        bool haveRival = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            HandoverCandidate c = candidates[i];
            if (!Eligible(c) || c.CraftIndex == best.CraftIndex) continue;

            if (!haveRival || c.MetresFromPlatform < rival.MetresFromPlatform)
            {
                rival = c;
                haveRival = true;
            }
        }

        if (haveRival && rival.MetresFromPlatform - best.MetresFromPlatform < AmbiguousWithinMetres)
        {
            return new Handover(HandoverVerdict.Ambiguous, -1, -1,
                                $"two craft carry it, {best.MetresFromPlatform:F0} m and "
                                + $"{rival.MetresFromPlatform:F0} m away");
        }

        return new Handover(HandoverVerdict.Move, best.CraftIndex, best.Ordinal,
                            $"{best.MetresFromPlatform:F0} m away");
    }

    private static bool Eligible(HandoverCandidate c)
        => !c.AlreadyCrewed
           && double.IsFinite(c.MetresFromPlatform)
           && c.MetresFromPlatform <= MaxMetres;
}
