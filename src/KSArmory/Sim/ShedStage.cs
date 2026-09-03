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
/// <para>A decoupler is noticed by difference — the world is counted when separation is asked for
/// and again when the engine reports it done. Everything new in between is a candidate, and in a
/// world flying several rockets on one profile that includes <em>their</em> stages: they stage
/// within moments of each other, so the window catches all of it.</para>
///
/// <para><b>Nearest-of-them is not enough, and the distance is what settles it.</b> A decoupler
/// parts two halves at about a metre a second, so a stack dropped a few frames ago is metres away
/// and nothing else in the world is. Adopting a foreign stage is not a cosmetic error: it is what
/// <see cref="SeparationClearance"/> measures, so the gate reads kilometres, passes at once, and
/// the trim fires while this vehicle's own stack is still alongside.</para>
///
/// <para>Same shape and the same answer as <see cref="PlatformHandover"/>, which draws this line
/// for a part rather than for a vehicle — bound the distance, and refuse rather than guess when
/// more than one is inside it.</para>
/// </summary>
internal static class ShedStage
{
    /// <summary>
    /// Beyond this it belongs to somebody else.
    ///
    /// <para>Deliberately loose against a physical separation of metres: timewarp is held while a
    /// computer is flying, but the notice can still straddle a long step. What it has to exclude
    /// is another rocket in the same world, and those are kilometres away.</para>
    /// </summary>
    public const double MaxMetres = 10_000.0;

    /// <summary>
    /// The one this vehicle dropped, or a refusal.
    ///
    /// <para>Two candidates inside <see cref="MaxMetres"/> are not told apart by taking the nearer.
    /// Refusing costs a clearance that reads unknown and falls back to its clock; choosing wrong
    /// reports a stack that is already clear, which is the failure the gate exists to prevent.</para>
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
