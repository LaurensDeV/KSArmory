using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// When a vehicle may start manoeuvring after dropping a stage.
///
/// <para>Small, and load-bearing for a reason that is not obvious from the arithmetic: every branch
/// here is one that reads the same from outside and behaves oppositely. A wait that never ends
/// holds the whole salvo; a wait that ends too early nulls the separation at the moment the two
/// halves are closest.</para>
/// </summary>
public class SeparationClearanceTests
{
    [Fact]
    public void FarEnoughApartIsClear()
    {
        Clearance c = SeparationClearance.Check(SeparationClearance.Metres + 1.0, 10.0);

        Assert.True(c.IsClear);
        Assert.False(c.OnTheClock);
        Assert.Contains("clear of the spent stack", c.Said);
    }

    [Fact]
    public void StillCloseIsNotClear()
    {
        Clearance c = SeparationClearance.Check(SeparationClearance.Metres - 1.0, 10.0);

        Assert.False(c.IsClear);
        Assert.Contains("waiting", c.Said);
    }

    /// <summary>
    /// A stage that cannot be read must not count as gone.
    ///
    /// <para>A part tree mid-rebuild answers with nothing, which is indistinguishable from a stage
    /// that has genuinely separated — and it is at its most likely in the first frames after a
    /// split, which is exactly when the two are closest. Reading absence as clearance therefore
    /// fires the manoeuvre at the worst possible instant, and looks like it worked.</para>
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1.0)]
    public void AnUnreadableStageWaitsOutTheClockRatherThanCountingAsGone(double reading)
    {
        Assert.False(SeparationClearance.Check(reading, 0.0).IsClear);
        Assert.False(SeparationClearance.Check(reading, SeparationClearance.TimeoutSeconds - 0.1).IsClear);

        Clearance late = SeparationClearance.Check(reading, SeparationClearance.TimeoutSeconds);

        Assert.True(late.IsClear);
        Assert.True(late.OnTheClock);
        Assert.Contains("no clearance reading", late.Said);
    }

    /// <summary>
    /// The wait always ends. A stack that barely moved is a worse shot; warheads still aboard when
    /// the release altitude closes are no shot at all.
    /// </summary>
    [Fact]
    public void ItGivesUpWaitingRatherThanHoldingForEver()
    {
        Clearance c = SeparationClearance.Check(2.0, SeparationClearance.TimeoutSeconds);

        Assert.True(c.IsClear);
        Assert.True(c.OnTheClock);

        // And it says the distance it settled for, because that is the number that explains a
        // salvo released closer than intended.
        Assert.Contains("2 m", c.Said);
        Assert.Contains("short", c.Said);
    }

    /// <summary>
    /// Distance beats the clock, so a vehicle that gets clear early is not made to wait it out.
    /// </summary>
    [Fact]
    public void ClearanceIsReachedOnDistanceRatherThanOnTime()
    {
        Clearance c = SeparationClearance.Check(SeparationClearance.Metres, 0.5);

        Assert.True(c.IsClear);
        Assert.False(c.OnTheClock);
    }
}
