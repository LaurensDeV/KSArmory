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
