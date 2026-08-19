namespace KSArmory;

/// <summary>One craft that might now be carrying a launcher that has gone missing.</summary>
/// <param name="CraftIndex">Opaque to this file: the caller's own handle on the craft.</param>
/// <param name="AlreadyCrewed">Another system already runs this launcher, so it is not ours.</param>
internal readonly record struct HandoverCandidate(
    int CraftIndex, int Ordinal, double MetresFromPlatform, bool AlreadyCrewed);

internal enum HandoverVerdict
{
    /// <summary>Nothing carries it. The weapon stays where it is and looks again next frame.</summary>
    NotFound,

    Move,

    /// <summary>Two of them, too close to tell apart. Refused, because guessing is worse.</summary>
    Ambiguous,
}

internal readonly record struct Handover(HandoverVerdict Verdict, int CraftIndex, int Ordinal, string Why);

/// <summary>
/// Which craft a launcher went to, when it is no longer on the one that was carrying it.
///
/// <para>A decoupler splits a stack in two and the launcher leaves on one half — so a weapon that
/// was pinned to the whole stack has to follow it. The roster is the only thing that knows what
/// else is crewed, so it makes this decision; the arithmetic is here because the roster is
/// KSA-facing and unreachable from the tests.</para>
///
/// <para><b>Absence never moves anything.</b> A part tree read mid-rebuild returns nothing, which
/// is indistinguishable from a launcher that has genuinely gone — so a launcher going missing only
/// ever starts a search, and nothing changes unless one is positively found somewhere else.</para>
/// </summary>
internal static class PlatformHandover
{
    /// <summary>
    /// Beyond this, it is somebody else's launcher.
    ///
    /// <para>The real separation a frame after a split is a stack length plus a decoupler shove —
    /// metres. This is loose because timewarp is not held down during a coast with nothing in the
    /// air, so a long step between the split and the notice can put the pair a good way apart.</para>
    /// </summary>
    public const double MaxMetres = 10_000.0;

    /// <summary>
    /// Two candidates closer together than this cannot be told apart, so neither is chosen.
    ///
    /// <para>Picking the wrong one is worse than picking none: it strands the operator's settings
    /// on the wrong craft <em>and</em> leaves the right one to be crewed fresh with defaults.</para>
    /// </summary>
    public const double AmbiguousWithinMetres = 50.0;

    public static Handover Choose(IReadOnlyList<HandoverCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        HandoverCandidate best = default;
        HandoverCandidate runnerUp = default;
        bool haveBest = false;
        bool haveRunnerUp = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            HandoverCandidate c = candidates[i];

            if (c.AlreadyCrewed) continue;
            if (!double.IsFinite(c.MetresFromPlatform) || c.MetresFromPlatform > MaxMetres) continue;

            if (!haveBest || c.MetresFromPlatform < best.MetresFromPlatform)
            {
                runnerUp = best;
                haveRunnerUp = haveBest;
                best = c;
                haveBest = true;
            }
            else if (!haveRunnerUp || c.MetresFromPlatform < runnerUp.MetresFromPlatform)
            {
                runnerUp = c;
                haveRunnerUp = true;
            }
        }

        if (!haveBest) return new Handover(HandoverVerdict.NotFound, -1, -1, "nothing carries it");

        if (haveRunnerUp
            && runnerUp.MetresFromPlatform - best.MetresFromPlatform < AmbiguousWithinMetres)
        {
            return new Handover(HandoverVerdict.Ambiguous, -1, -1,
                                $"two craft carry it, {best.MetresFromPlatform:F0} m and "
                                + $"{runnerUp.MetresFromPlatform:F0} m away");
        }

        return new Handover(HandoverVerdict.Move, best.CraftIndex, best.Ordinal,
                            $"{best.MetresFromPlatform:F0} m away");
    }
}
