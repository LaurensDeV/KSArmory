using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The loop that decides where the warheads land, now that it can be asked without a rocket.
///
/// <para>Flown, a correction that ran to completion landed at 140 m and every other ending at 5 to
/// 45 km. Until this moved out of <c>Ksa/IcbmComputer.cs</c> none of it could be examined except by
/// spending a night — and a night needs a machine in the fast regime, which is not dependable.</para>
/// </summary>
public class PostCutoffSequenceTests
{
    private const double Budget = 60.0;

    /// <summary>
    /// Before any pass the trim is nulling a decoupler's shove, where an answer in the tens is a
    /// bad solve rather than a correction. NaN hands BusTrim its own constant.
    /// </summary>
    [Fact]
    public void TheFirstPassIsHeldToTheTrimsOwnConstant()
    {
        double ceiling = PostCutoffSequence.CeilingFor(
            postBoostCycles: 0, Budget, spentMetresPerSecond: 0.0, fromBudget: false);

        Assert.True(double.IsNaN(ceiling));
    }

    /// <summary>
    /// From the first pass on it is flying a deliberate aim correction, which grows with the
    /// trajectory -- four of six shots at 12,902 km died on a fixed ten while asking for 11.5-13.4.
    /// </summary>
    [Fact]
    public void OnceAPassHasRunTheCeilingIsWhatIsLeftOfTheBudget()
    {
        Assert.Equal(60.0, PostCutoffSequence.CeilingFor(1, Budget, 0.0, fromBudget: false));
        Assert.Equal(42.0, PostCutoffSequence.CeilingFor(1, Budget, 18.0, fromBudget: false));
        Assert.Equal(26.0, PostCutoffSequence.CeilingFor(3, Budget, 34.0, fromBudget: false));
    }

    /// <summary>
    /// The setting under test: the guard asks whether the AIM has moved when the question is
    /// whether the BUS has separated, and 11 of 14 flown trims were over the constant at the split.
    /// </summary>
    [Fact]
    public void TheSettingExtendsTheBudgetCeilingToTheFirstPass()
    {
        Assert.Equal(60.0, PostCutoffSequence.CeilingFor(0, Budget, 0.0, fromBudget: true));
        Assert.Equal(50.0, PostCutoffSequence.CeilingFor(0, Budget, 10.0, fromBudget: true));
    }

    /// <summary>
    /// A budget already overspent is no allowance, not a debt. A negative ceiling reads to BusTrim
    /// as a refusal of every pass, including ones that would have cost nothing.
    /// </summary>
    [Theory]
    [InlineData(60.0, 61.0)]
    [InlineData(60.0, 1000.0)]
    [InlineData(0.0, 0.0)]
    public void TheCeilingIsNeverNegative(double budget, double spent)
    {
        Assert.Equal(0.0, PostCutoffSequence.CeilingFor(1, budget, spent, fromBudget: false));
    }

    [Fact]
    public void AnUnreadableBudgetIsNoAllowanceRatherThanAnInfiniteOne()
    {
        Assert.Equal(0.0, PostCutoffSequence.CeilingFor(1, double.NaN, 0.0, fromBudget: false));
        Assert.Equal(0.0, PostCutoffSequence.CeilingFor(1, double.PositiveInfinity,
                                                        double.PositiveInfinity, fromBudget: false));
    }

    /// <summary>
    /// Abandoning is not waiting: the stack is readable and still too close, so there is no
    /// manoeuvre that does not fly into it, and the warheads go untrimmed.
    /// </summary>
    [Fact]
    public void AnAbandonedClearanceStopsTheLoopRatherThanHoldingFire()
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearanceIsClear: false, clearanceAbandoned: true, postBoostCycles: 2,
            Budget, spentMetresPerSecond: 5.0, ceilingFromBudget: true);

        Assert.True(plan.Abandon);
        Assert.False(plan.MayTrim);
    }

    /// <summary>
    /// Abandoning beats every other input, including a clearance that also reads clear -- a state
    /// the gate can produce, because it is asked every frame and never latched.
    /// </summary>
    [Fact]
    public void AbandoningWinsOverAClearanceThatAlsoReadsClear()
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearanceIsClear: true, clearanceAbandoned: true, postBoostCycles: 0,
            Budget, 0.0, ceilingFromBudget: false);

        Assert.True(plan.Abandon);
        Assert.False(plan.MayTrim);
    }

    /// <summary>
    /// With the interlock covering it, an abandoned clearance stops WAITING rather than giving up:
    /// the trim fires on and the keep-out withholds the directions that point at the stack.
    ///
    /// <para>This is the whole of it. An abandoned trim returns before any aim correction is
    /// applied — 87 of 144 flights, and 8 of 8 on the night that attributed the ending per rocket,
    /// against 140 m for a correction that runs to completion.</para>
    /// </summary>
    [Fact]
    public void TheInterlockLetsAnAbandonedClearanceKeepTrimming()
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearanceIsClear: false, clearanceAbandoned: true, postBoostCycles: 0,
            Budget, spentMetresPerSecond: 0.0, ceilingFromBudget: false,
            keepOutCoversTheClearance: true);

        Assert.False(plan.Abandon);
        Assert.True(plan.MayTrim);
    }

    /// <summary>
    /// And it still gets a ceiling, because the pass it is now allowed to fly is a real one.
    /// </summary>
    [Fact]
    public void AnAbandonedClearanceThatKeepsTrimmingIsStillBounded()
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearanceIsClear: false, clearanceAbandoned: true, postBoostCycles: 2,
            Budget, spentMetresPerSecond: 18.0, ceilingFromBudget: false,
            keepOutCoversTheClearance: true);

        Assert.Equal(42.0, plan.CeilingMetresPerSecond);
    }

    /// <summary>Off, it abandons exactly as it always did. This is what ships.</summary>
    [Fact]
    public void WithoutTheSettingAnAbandonedClearanceStillGivesUp()
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearanceIsClear: false, clearanceAbandoned: true, postBoostCycles: 0,
            Budget, 0.0, ceilingFromBudget: false, keepOutCoversTheClearance: false);

        Assert.True(plan.Abandon);
        Assert.False(plan.MayTrim);
    }

    /// <summary>
    /// It changes nothing while the clearance is doing its job -- it is a rule about the timeout,
    /// not about the gate.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ItChangesNothingWhileTheClearanceHasNotTimedOut(bool clear)
    {
        PostCutoffSequence.Plan on = PostCutoffSequence.Decide(
            clear, clearanceAbandoned: false, 1, Budget, 5.0, false, keepOutCoversTheClearance: true);
        PostCutoffSequence.Plan off = PostCutoffSequence.Decide(
            clear, clearanceAbandoned: false, 1, Budget, 5.0, false, keepOutCoversTheClearance: false);

        Assert.Equal(off, on);
    }

    /// <summary>The trim fires only once the bus is clear of what it dropped.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTrimFiresOnlyWhenTheClearanceSaysSo(bool clear)
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clear, clearanceAbandoned: false, postBoostCycles: 0, Budget, 0.0,
            ceilingFromBudget: false);

        Assert.False(plan.Abandon);
        Assert.Equal(clear, plan.MayTrim);
    }

    /// <summary>
    /// The plan's ceiling is the same number CeilingFor gives, so there is one rule rather than
    /// two that agree today.
    /// </summary>
    [Theory]
    [InlineData(0, 0.0, false)]
    [InlineData(0, 0.0, true)]
    [InlineData(2, 18.0, false)]
    [InlineData(4, 61.0, true)]
    public void ThePlanCarriesTheSameCeilingTheRuleGives(int cycles, double spent, bool fromBudget)
    {
        PostCutoffSequence.Plan plan = PostCutoffSequence.Decide(
            clearanceIsClear: true, clearanceAbandoned: false, cycles, Budget, spent, fromBudget);

        double direct = PostCutoffSequence.CeilingFor(cycles, Budget, spent, fromBudget);

        Assert.Equal(double.IsNaN(direct), double.IsNaN(plan.CeilingMetresPerSecond));
        if (!double.IsNaN(direct)) Assert.Equal(direct, plan.CeilingMetresPerSecond);
    }

    /// <summary>
    /// A steep arrival's demand is large and <em>stationary</em>: the geometry asks for 7-11 m/s
    /// where a shallow one asks 2.45, and it asks once. That is not a runaway and the old rule
    /// could not tell the difference, which is what declined the whole correction on the shot that
    /// produced the tightest group ever measured here.
    /// </summary>
    [Theory]
    [InlineData(11.0, 11.0)]
    [InlineData(11.0, 10.5)]
    [InlineData(7.0, 9.0)]
    [InlineData(2.45, 2.40)]
    public void ALargeStationaryDemandIsNotARunaway(double now, double before)
    {
        Assert.False(PostCutoffSequence.IsRunaway(now, before));
    }

    /// <summary>
    /// A wind-up is the correction and the trim driving one vehicle through one prediction, and its
    /// signature is the first jump: 8h's trace nulls to 0.02 m/s and the next solve asks 12.63,
    /// which is 632x.
    /// </summary>
    [Theory]
    [InlineData(12.63, 0.02)]
    [InlineData(3.0, 1.0)]
    [InlineData(11.0, 2.45)]
    public void ADemandThatGrowsAcrossPassesIsARunaway(double now, double before)
    {
        Assert.True(PostCutoffSequence.IsRunaway(now, before));
    }

    /// <summary>
    /// And the step <em>after</em> that is not caught, which is correct rather than a gap.
    ///
    /// <para>8h's 12.63 to 15.61 is 1.24x, and a correction whose aim has genuinely moved varies by
    /// about that much between passes. The wind-up is stopped at the jump that made it one; asking
    /// this rule to catch its continuation as well would mean refusing legitimate passes, which is
    /// the magnitude rule's own failure wearing a smaller number.</para>
    /// </summary>
    [Fact]
    public void TheContinuationOfAWindUpIsNotCaughtAgain()
    {
        Assert.True(PostCutoffSequence.IsRunaway(12.63, 0.02));
        Assert.False(PostCutoffSequence.IsRunaway(15.61, 12.63));
    }

    /// <summary>
    /// The first demand is never a runaway on its own evidence -- there is nothing to compare it
    /// against, and refusing it would be the magnitude rule wearing a different name.
    /// </summary>
    [Theory]
    [InlineData(11.0, double.NaN)]
    [InlineData(11.0, 0.0)]
    [InlineData(double.NaN, 5.0)]
    [InlineData(double.PositiveInfinity, 5.0)]
    public void WithNothingToCompareAgainstNothingIsARunaway(double now, double before)
    {
        Assert.False(PostCutoffSequence.IsRunaway(now, before));
    }

    /// <summary>
    /// The shipped default, walked one pass at a time: the first is the constant and each pass
    /// after it is bounded by what the last one left. This is the sequence 8aa read as a diverging
    /// solve -- the demand exceeding whatever remains, every pass, until the budget is gone.
    /// </summary>
    [Fact]
    public void TheShippedSequenceNarrowsAsTheBudgetIsSpent()
    {
        double[] spent = [0.0, 18.0, 34.0, 47.0, 60.0];
        double[] ceilings = [.. spent.Select((v, i) => PostCutoffSequence.CeilingFor(i, Budget, v, false))];

        Assert.True(double.IsNaN(ceilings[0]));

        for (int i = 2; i < ceilings.Length; i++)
        {
            Assert.True(ceilings[i] < ceilings[i - 1],
                        $"pass {i} was allowed {ceilings[i]} after {ceilings[i - 1]}");
        }

        Assert.Equal(0.0, ceilings[^1]);
    }
}
