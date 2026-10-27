namespace KSArmory;

/// <summary>One vehicle that appeared while a split was pending.</summary>
/// <param name="Index">Opaque to this file: the caller's own handle on the vehicle.</param>
internal readonly record struct ShedCandidate(int Index, double MetresFromCraft);

internal enum ShedVerdict
{
    /// <summary>Nothing plausible appeared. The clearance reads no distance and says so.</summary>
    NotFound,

    Take,

    /// <summary>Several could be it, so none is chosen — guessing authorises the trim early.</summary>
    Ambiguous,
}

internal readonly record struct ShedChoice(ShedVerdict Verdict, int Index, string Why);

/// <summary>
/// Which of the vehicles that just appeared is the half this stack let go of.
///
/// <para>Every vehicle new between asking for separation and the engine reporting it is a candidate,
/// including other rockets' stages. A foreign stage passes <see cref="SeparationClearance"/> at once and
/// fires the trim beside this stack, so the distance is bounded and two inside it are refused.</para>
/// </summary>
internal static class ShedStage
{
    /// <summary>
    /// Beyond this it belongs to somebody else. Loose against a separation of metres, since the notice can
    /// straddle a long step; another rocket in the world is kilometres away.
    /// </summary>
    public const double MaxMetres = 10_000.0;

    /// <summary>
    /// The one this vehicle dropped, or a refusal. Refusing leaves the clearance on its clock; choosing
    /// wrong reports a stack already clear, the failure the gate exists to prevent.
    /// </summary>
    public static ShedChoice Choose(ReadOnlySpan<ShedCandidate> candidates)
    {
        int found = 0;
        int index = 0;
        double nearest = double.PositiveInfinity;

        for (int i = 0; i < candidates.Length; i++)
        {
            double apart = candidates[i].MetresFromCraft;
            if (!double.IsFinite(apart) || apart < 0.0 || apart > MaxMetres) continue;

            found++;
            if (apart >= nearest) continue;

            nearest = apart;
            index = candidates[i].Index;
        }

        if (found == 0)
        {
            return new ShedChoice(ShedVerdict.NotFound, 0,
                                  $"nothing within {MaxMetres / 1000.0:F0} km of {candidates.Length} new vehicle(s)");
        }

        if (found > 1)
        {
            return new ShedChoice(ShedVerdict.Ambiguous, 0,
                                  $"{found} vehicles within {MaxMetres / 1000.0:F0} km, nearest {nearest:F1} m");
        }

        return new ShedChoice(ShedVerdict.Take, index, $"{nearest:F1} m away");
    }
}
