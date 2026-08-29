namespace KSArmory;

/// <summary>
/// What the loop after cutoff does next: whether the trim may fire, how much one pass may spend,
/// and whether the correction is over.
///
/// <para><b>This is the decision that dominates where the warheads land.</b> Over 64 flown
/// corrections, one that ran to completion landed at <b>140 m</b> and every other ending at 5 to
/// 45 km — 40x to 300x, which is not a distribution with a tail. Everything upstream of it, the
/// guidance and the burn and the arrival angle, is worth less than whether this loop finishes.</para>
///
/// <para>It lived in <c>Ksa/IcbmComputer.cs</c> until it was measured, which meant the largest term
/// in the mod's accuracy could only be examined by flying a night — and a night needs a working
/// game and a machine that happens to be in the fast regime, which turned out not to be
/// dependable. <c>docs/MIRV-NEXT.md</c> <b>8ac</b>. The pieces it orchestrates were always here;
/// only the order they run in was not.</para>
/// </summary>
internal static class PostCutoffSequence
{
    /// <summary>What one frame of the post-cutoff loop has decided.</summary>
    /// <param name="Abandon">
    /// Stop: the spent stack is too close to manoeuvre around, so the warheads go untrimmed. Not a
    /// wait — there is no manoeuvre here that does not fly into it.
    /// </param>
    /// <param name="MayTrim">Whether the thrusters may fire this frame.</param>
    /// <param name="CeilingMetresPerSecond">
    /// The most this pass may spend, or NaN for <see cref="BusTrim.MaxMetresPerSecond"/>.
    /// </param>
    internal readonly record struct Plan(bool Abandon, bool MayTrim, double CeilingMetresPerSecond);

    /// <summary>
    /// The ceiling one pass may spend.
    ///
    /// <para>Two different jobs wear the same number. Before any pass the trim is nulling a
    /// decoupler's shove — ones of metres a second, where an answer in the tens really is a bad
    /// solve, and the constant is the right bound. From the first pass on it is flying a deliberate
    /// aim correction, which grows with the trajectory: four of six shots at 12,902 km died on a
    /// fixed ten while asking for 11.5 to 13.4.</para>
    ///
    /// <para>Bounded by what is <em>left</em> of the budget rather than by a larger constant. The
    /// budget is the real limit on what the bus can spend, <c>BusTrim.Stalled</c> already ends a
    /// loop that is not closing, and a third bound above both would have nothing left to bound.</para>
    ///
    /// <para><paramref name="fromBudget"/> is <see cref="IcbmConfig.TrimCeilingFromBudget"/> and
    /// extends that to the first pass as well. The guard it lifts asks whether the <em>aim</em> has
    /// moved when the question is whether the <em>bus</em> has separated: 11 of 14 flown trims were
    /// already over the constant at the split, with no wait at all.</para>
    /// </summary>
    public static double CeilingFor(int postBoostCycles, double budgetMetresPerSecond,
                                    double spentMetresPerSecond, bool fromBudget)
    {
        if (postBoostCycles <= 0 && !fromBudget) return double.NaN;

        // Never negative: a budget already overspent is no allowance rather than a debt, and a
        // negative ceiling reads to BusTrim as a refusal of every pass including the ones that
        // would have cost nothing.
        double left = budgetMetresPerSecond - spentMetresPerSecond;

        return double.IsFinite(left) ? Math.Max(0.0, left) : 0.0;
    }

    /// <summary>
    /// How much larger a pass's demand may be than the one before it before it is a wind-up.
    ///
    /// <para>Half again, because the two cases are not close. A steep arrival asks for 7–11 m/s
    /// where a shallow one asks 2.45, and asks for it <em>once</em> — the geometry does not change
    /// between passes. A runaway is the aim correction and the trim driving one vehicle through one
    /// prediction, and it grows by an order of magnitude a pass: 0.02 m/s trimmed, 12.63 asked
    /// next.</para>
    /// </summary>
    public const double RunawayGrowth = 1.5;

    /// <summary>
    /// Whether a demand is winding up rather than merely being large.
    ///
    /// <para><b>Size is the wrong question and always was.</b> What the per-pass ceiling guards
    /// against is the reference <em>moving</em> — the correction re-solving under the trim's own
    /// thrust — and a magnitude cannot tell that from a large correction the geometry genuinely
    /// needs. It is why a 15° arrival flew a group six warheads wide on one point and then had the
    /// whole correction declined: <c>docs/METRE-LEVEL.md</c> B1.</para>
    ///
    /// <para>Answers false until there is a previous pass to compare against, so the first demand
    /// is never a runaway on its own evidence. The budget and <c>BusTrim.Stalled</c> are what bound
    /// a loop that is wrong about this.</para>
    /// </summary>
    public static bool IsRunaway(double demandNow, double demandLastPass,
                                 double growth = RunawayGrowth)
        => double.IsFinite(demandNow) && double.IsFinite(demandLastPass)
           && demandLastPass > 0.0 && growth > 0.0
           && demandNow > demandLastPass * growth;

    /// <summary>One frame of the loop, from the clearance's verdict and what the trim has spent.</summary>
    public static Plan Decide(bool clearanceIsClear, bool clearanceAbandoned, int postBoostCycles,
                              double budgetMetresPerSecond, double spentMetresPerSecond,
                              bool ceilingFromBudget)
    {
        if (clearanceAbandoned) return new Plan(Abandon: true, MayTrim: false, double.NaN);

        return new Plan(
            Abandon: false,
            MayTrim: clearanceIsClear,
            CeilingFor(postBoostCycles, budgetMetresPerSecond, spentMetresPerSecond, ceilingFromBudget));
    }
}
