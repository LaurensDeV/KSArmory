using Xunit;

namespace KSArmory.Tests;

public class GuardStateTests
{
    [Fact]
    public void AnUnarmedSystemIsSafeWhateverAutoEngageSays()
    {
        Assert.Equal(Guard.Safe, GuardState.Of(armed: false, autoEngage: true, autoEngages: true));
        Assert.Equal(Guard.Safe, GuardState.Of(armed: false, autoEngage: false, autoEngages: true));
    }

    [Fact]
    public void AnArmedSystemGuardsOnlyWithAutoEngageOn()
    {
        Assert.Equal(Guard.Guarding, GuardState.Of(armed: true, autoEngage: true, autoEngages: true));
        Assert.Equal(Guard.Armed, GuardState.Of(armed: true, autoEngage: false, autoEngages: true));
    }

    /// <summary>
    /// A bomb rack has no auto-engage to turn on. Counting that against it would leave a craft
    /// carrying one amber for ever, however it was armed.
    /// </summary>
    [Fact]
    public void ASystemThatCannotEngageOnItsOwnGuardsOnceArmed()
        => Assert.Equal(Guard.Guarding, GuardState.Of(armed: true, autoEngage: false, autoEngages: false));

    [Fact]
    public void ACraftGuardsOnlyWhenEverySystemOnItDoes()
    {
        Assert.Equal(Guard.Guarding, GuardState.Combine(Guard.Guarding, Guard.Guarding));
        Assert.Equal(Guard.Safe, GuardState.Combine(Guard.Safe, Guard.Safe));

        // Anything mixed is part-armed, which is what amber says.
        Assert.Equal(Guard.Armed, GuardState.Combine(Guard.Guarding, Guard.Safe));
        Assert.Equal(Guard.Armed, GuardState.Combine(Guard.Safe, Guard.Guarding));
        Assert.Equal(Guard.Armed, GuardState.Combine(Guard.Armed, Guard.Guarding));
    }

    /// <summary>
    /// A half-armed craft is one click from guarding, not one click from safe: the click that
    /// makes things safe is the one made on a craft that is fully guarding.
    /// </summary>
    [Fact]
    public void AnythingShortOfGuardingTurnsGuardOn()
    {
        Assert.True(GuardState.TurnsOn(Guard.Safe));
        Assert.True(GuardState.TurnsOn(Guard.Armed));
        Assert.False(GuardState.TurnsOn(Guard.Guarding));
    }
}
