using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The within-run split: which rocket flies which variant, and what a bad spec does about it.
/// </summary>
public class ShotArmsTests
{
    private static ShotArms Parse(string spec)
    {
        Assert.True(ShotArms.TryParse(spec, out ShotArms arms, out string fault), fault);
        return arms;
    }

    [Fact]
    public void ABareNameIsAnArmThatChangesNothing()
    {
        ShotArms arms = Parse("base");

        Assert.Equal(1, arms.Count);
        Assert.Equal("base", arms.For(0).Name);
        Assert.Empty(arms.For(0).Settings);
    }

    [Fact]
    public void AnArmCarriesTheSettingsItVaries()
    {
        ShotArms arms = Parse("base|trim:TrimCeilingFromBudget=true,TrimBudgetMetresPerSecond=90");

        Assert.Equal(2, arms.Count);

        ShotArms.Arm trim = arms.For(1);
        Assert.Equal("trim", trim.Name);
        Assert.Equal(2, trim.Settings.Count);
        Assert.Equal("TrimCeilingFromBudget", trim.Settings[0].Field);
        Assert.Equal("90", trim.Settings[1].Value);
    }

    /// <summary>
    /// The roster gradient is 175x, so the arms have to alternate down it rather than split it.
    /// </summary>
    [Fact]
    public void ArmsAlternateDownTheRosterRatherThanBlocking()
    {
        ShotArms arms = Parse("a|b");

        string[] flown = [.. Enumerable.Range(0, 8).Select(i => arms.For(i).Name)];

        Assert.Equal(["a", "b", "a", "b", "a", "b", "a", "b"], flown);
        Assert.Equal(4, flown.Count(n => n == "a"));
    }

    /// <summary>
    /// Alternation balances the gradient in expectation; the phase is what removes what is left,
    /// by giving each arm every position across a batch.
    /// </summary>
    [Fact]
    public void ThePhaseRotatesWhichArmDrawsTheFirstRocket()
    {
        ShotArms arms = Parse("a|b|c");

        Assert.Equal("a", arms.For(0, phase: 0).Name);
        Assert.Equal("b", arms.For(0, phase: 1).Name);
        Assert.Equal("c", arms.For(0, phase: 2).Name);
        Assert.Equal("a", arms.For(0, phase: 3).Name);
    }

    [Fact]
    public void EveryArmDrawsEveryPositionOverAFullCycleOfPhases()
    {
        ShotArms arms = Parse("a|b|c");

        foreach (int position in Enumerable.Range(0, 3))
        {
            string[] drawn = [.. Enumerable.Range(0, 3).Select(p => arms.For(position, p).Name)];
            Assert.Equal(3, drawn.Distinct().Count());
        }
    }

    [Fact]
    public void SettingsAreWrittenOntoTheCraftsOwnConfiguration()
    {
        ShotArms arms = Parse("base|steep:MinArrivalAngleDeg=15.5");
        IcbmConfig config = new();

        Assert.True(ShotArms.TryApply(arms.For(1), config, out string fault), fault);
        Assert.Equal(15.5, config.MinArrivalAngleDeg);
    }

    [Fact]
    public void TheBaselineArmLeavesTheSettingsAlone()
    {
        ShotArms arms = Parse("base|steep:MinArrivalAngleDeg=15.5");
        IcbmConfig config = new() { MinArrivalAngleDeg = 7.0 };

        Assert.True(ShotArms.TryApply(arms.For(0), config, out _));
        Assert.Equal(7.0, config.MinArrivalAngleDeg);
    }

    /// <summary>
    /// A shot flown on the settings of the arm it was meant to differ from is worse than no shot,
    /// so a field that will not resolve is a refusal rather than a skip.
    /// </summary>
    [Fact]
    public void ASettingThatIsNotOnTheConfigurationIsRefusedByName()
    {
        ShotArms arms = Parse("base|typo:TrimCielingFromBudget=true");

        Assert.False(ShotArms.TryApply(arms.For(1), new IcbmConfig(), out string fault));
        Assert.Contains("TrimCielingFromBudget", fault);
    }

    [Fact]
    public void AValueOfTheWrongTypeIsRefusedByName()
    {
        ShotArms arms = Parse("base|bad:MinArrivalAngleDeg=steeper");

        Assert.False(ShotArms.TryApply(arms.For(1), new IcbmConfig(), out string fault));
        Assert.Contains("MinArrivalAngleDeg", fault);
    }

    /// <summary>The spec is written in a script and read back wherever it flies.</summary>
    [Fact]
    public void ADecimalIsReadTheSameWayOnEveryMachine()
    {
        ShotArms arms = Parse("x:MinArrivalAngleDeg=15.5");
        IcbmConfig config = new();

        Assert.True(ShotArms.TryApply(arms.For(0), config, out _));
        Assert.Equal(15.5, config.MinArrivalAngleDeg);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoSpecIsNoArms(string? spec)
    {
        Assert.False(ShotArms.TryParse(spec, out _, out string fault));
        Assert.NotEmpty(fault);
    }

    [Fact]
    public void TwoArmsOfOneNameAreRefused()
    {
        Assert.False(ShotArms.TryParse("base|base:MinArrivalAngleDeg=15", out _, out string fault));
        Assert.Contains("base", fault);
    }

    [Theory]
    [InlineData("base|:MinArrivalAngleDeg=15")]
    [InlineData("base|x:MinArrivalAngleDeg")]
    [InlineData("base|x:=15")]
    public void ASpecThatCannotBeReadIsRefusedRatherThanGuessedAt(string spec)
    {
        Assert.False(ShotArms.TryParse(spec, out _, out string fault));
        Assert.NotEmpty(fault);
    }

    /// <summary>
    /// The arms waiting to be flown are settings, and every one of them is off by default.
    ///
    /// <para>That is what makes a paired night honest: the baseline arm names no settings at all,
    /// so it is the shipped code rather than a second variant. A default flipped here would make
    /// every "base" in every batch since silently mean something else.</para>
    /// </summary>
    [Fact]
    public void TheBaselineArmIsTheShippedBehaviour()
    {
        IcbmConfig shipped = new();

        Assert.False(shipped.TrimCeilingFromBudget);

        ShotArms arms = Parse("base|ceiling:TrimCeilingFromBudget=true");
        Assert.True(ShotArms.TryApply(arms.For(0), shipped, out _));
        Assert.False(shipped.TrimCeilingFromBudget);

        Assert.True(ShotArms.TryApply(arms.For(1), shipped, out _));
        Assert.True(shipped.TrimCeilingFromBudget);
    }

    [Fact]
    public void AnArmSaysWhatItVariesSoTheLogCanAttributeAShot()
    {
        ShotArms arms = Parse("base|trim:TrimCeilingFromBudget=true");

        Assert.Equal("base", arms.For(0).Describe());
        Assert.Contains("TrimCeilingFromBudget=true", arms.For(1).Describe());
    }
}
