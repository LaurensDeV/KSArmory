using Xunit;

namespace KSArmory.Tests;

public class GuardStateTests
{
    [Fact]
    public void ASystemGuardsExactlyWhenAutoEngageIsOn()
    {
        Assert.Equal(Guard.Guarding, GuardState.Of(autoEngage: true, autoEngages: true));
        Assert.Equal(Guard.Off, GuardState.Of(autoEngage: false, autoEngages: true));
    }

    /// <summary>
    /// A bomb rack has no auto-engage to turn on. Counting it as off would leave a craft carrying one
    /// amber for ever; counting it as guarding would call a craft guarding that engages nothing.
    /// </summary>
    [Fact]
    public void ASystemThatCannotEngageOnItsOwnHasNoSay()
    {
        Assert.Null(GuardState.Of(autoEngage: false, autoEngages: false));
        Assert.Null(GuardState.Of(autoEngage: true, autoEngages: false));
    }

    [Fact]
    public void ACraftGuardsOnlyWhenEverySystemOnItDoes()
    {
        Assert.Equal(Guard.Guarding, GuardState.Combine(Guard.Guarding, Guard.Guarding));
        Assert.Equal(Guard.Off, GuardState.Combine(Guard.Off, Guard.Off));

        // Anything mixed is partly guarding, which is what amber says.
        Assert.Equal(Guard.Partly, GuardState.Combine(Guard.Guarding, Guard.Off));
        Assert.Equal(Guard.Partly, GuardState.Combine(Guard.Off, Guard.Guarding));
        Assert.Equal(Guard.Partly, GuardState.Combine(Guard.Partly, Guard.Guarding));
    }

    /// <summary>
    /// A partly guarding craft is one click from guarding, not one click from off: the click that
    /// stops it is the one made on a craft that is fully guarding.
    /// </summary>
    [Fact]
    public void AnythingShortOfGuardingTurnsGuardOn()
    {
        Assert.True(GuardState.TurnsOn(Guard.Off));
        Assert.True(GuardState.TurnsOn(Guard.Partly));
        Assert.False(GuardState.TurnsOn(Guard.Guarding));
    }
}
