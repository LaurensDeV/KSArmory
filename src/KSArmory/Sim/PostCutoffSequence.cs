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
/// <para>It is here rather than in <c>Ksa/Icbm/IcbmComputer.cs</c> so the largest term in the mod's
/// accuracy can be examined headlessly rather than only by flying a night.
/// <c>docs/MIRV-NEXT.md</c> <b>8ac</b>.</para>
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
    /// <para>Extending it to the first pass as well was flown and harmful: 0 of 32 corrections paid
    /// back against 12, and four shots 54-105x worse. <c>docs/MIRV-NEXT.md</c> 8ae.</para>
    /// </summary>
    public static double CeilingFor(int postBoostCycles, double budgetMetresPerSecond,
                                    double spentMetresPerSecond)
    {
        if (postBoostCycles <= 0) return double.NaN;

        // Never negative: a budget already overspent is no allowance rather than a debt, and a
        // negative ceiling reads to BusTrim as a refusal of every pass including the ones that
        // would have cost nothing.
        double left = budgetMetresPerSecond - spentMetresPerSecond;

        return double.IsFinite(left) ? Math.Max(0.0, left) : 0.0;
    }

    /// <summary>
    /// Whether the release waits on the trim. A precondition of being ready rather than a step inside the release
    /// sequence, because that sequence latches its reference on the first ready frame, and a reference latched before
    /// the decoupler's shove is out describes a line no warhead will leave on.
    /// </summary>
    public static bool TrimHoldsTheRelease(bool trimBeforeRelease, bool readyToDeploy, bool trimAbandoned,
                                           bool releasesAtCutoff, bool trimDone, bool correcting)
        => trimBeforeRelease && readyToDeploy && !trimAbandoned && !releasesAtCutoff && (!trimDone || correcting);

    /// <summary>
    /// One frame of the loop, from the clearance's verdict and what the trim has spent.
    /// </summary>
    /// <param name="keepOutCoversTheClearance">
    /// <see cref="IcbmConfig.KeepOutCoversTheClearance"/>. With it set, a clearance that runs out of
    /// time stops <em>waiting</em> rather than giving up: the trim fires on and the keep-out
    /// interlock answers the safety question a direction at a time.
    /// </param>
    public static Plan Decide(bool clearanceIsClear, bool clearanceAbandoned, int postBoostCycles,
                              double budgetMetresPerSecond, double spentMetresPerSecond,
                              bool keepOutCoversTheClearance = false)
    {
        double ceiling = CeilingFor(postBoostCycles, budgetMetresPerSecond, spentMetresPerSecond);

        // Abandoning is what the timeout does when nothing else can answer "is it safe to fire".
        // The interlock can: it is computed in the same pass, it already knows which way the stack
        // lies, and it withholds the directions that point at it while spending the frame on the
        // ones that do not. With every direction withheld the trim waits, which is the outcome the
        // timeout wanted, and MaxSeconds and the budget bound that wait.
        //
        // The cost of not having it is the whole correction: an abandoned trim returns before any
        // aim correction is applied, and the shot lands where the raw burn put it. Flown, that is
        // 87 of 144 flights (8y) and 8 of 8 on the night that measured it per craft, against 140 m
        // for a correction that runs to completion.
        if (clearanceAbandoned && !keepOutCoversTheClearance)
        {
            return new Plan(Abandon: true, MayTrim: false, double.NaN);
        }

        return new Plan(
            Abandon: false,

            // Fire on. The clearance said the gap will not open on its own, and the interlock is
            // now the thing keeping the bus off the stage rather than the clock.
            MayTrim: clearanceIsClear || clearanceAbandoned,
            ceiling);
    }
}
