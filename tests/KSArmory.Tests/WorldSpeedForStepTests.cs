using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Asking the world for a step rather than a speed, which is what makes two machines the same
/// experiment. <c>docs/MIRV-NEXT.md</c> <b>8ac</b>.
/// </summary>
public class WorldSpeedForStepTests(ITestOutputHelper Out)
{
    private const double Ceiling = 100.0;
    private const double Wanted = 0.066;

    /// <summary>
    /// The whole point: the same wanted step gives a different speed on a different machine, and
    /// the product — the step actually flown — comes out the same.
    /// </summary>
    [Theory]
    [InlineData(0.021)]
    [InlineData(0.0298)]
    [InlineData(0.0355)]
    public void TheStepFlownIsTheSameOnEveryMachine(double frame)
    {
        double speed = WorldSpeed.ForStep(Wanted, frame, Ceiling);
        double flown = speed * frame;

        Out.WriteLine($"{frame * 1000:F1} ms frame -> {speed:F2}x -> {flown * 1000:F1} ms a step");

        Assert.Equal(Wanted, flown, 6);
    }

    /// <summary>A slower machine pays in wall clock, which is the trade being made on purpose.</summary>
    [Fact]
    public void ASlowerMachineIsAskedForLessSpeed()
    {
        Assert.True(WorldSpeed.ForStep(Wanted, 0.0355, Ceiling)
                    < WorldSpeed.ForStep(Wanted, 0.021, Ceiling));
    }

    /// <summary>
    /// Never past the ceiling. A machine fast enough to want a thousand times is one the rest of
    /// the mod has not agreed to.
    /// </summary>
    [Fact]
    public void TheCeilingStillBinds()
    {
        Assert.Equal(Ceiling, WorldSpeed.ForStep(1.0, 0.001, Ceiling));
    }

    /// <summary>
    /// And never below real time: this exists to spend a coast faster, and a frame slow enough to
    /// want less than 1x has a problem no warp setting fixes.
    /// </summary>
    [Fact]
    public void ItNeverAsksForSlowMotion()
    {
        Assert.Equal(1.0, WorldSpeed.ForStep(0.010, 0.5, Ceiling));
    }

    /// <summary>
    /// A frame time nobody can read falls back to the ceiling. A pace that cannot be measured is
    /// not a reason to crawl.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnreadableFrameTimeFallsBackToTheCeiling(double frame)
    {
        Assert.Equal(Ceiling, WorldSpeed.ForStep(Wanted, frame, Ceiling));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void AnUnreadableWantedStepAsksForNothing(double wanted)
    {
        Assert.Equal(1.0, WorldSpeed.ForStep(wanted, 0.021, Ceiling));
    }

    /// <summary>
    /// The two regimes 8ac observed, and what this would have asked for on each. That difference
    /// is the whole of what made those two nights incomparable.
    /// </summary>
    [Fact]
    public void ItWouldHaveHeldTheTwoObservedRegimesToOneStep()
    {
        double fast = WorldSpeed.ForStep(Wanted, 0.021, Ceiling);
        double slow = WorldSpeed.ForStep(Wanted, 0.0298, Ceiling);

        Out.WriteLine($"the fast night would have run {fast:F2}x, the slow one {slow:F2}x, "
                      + "both at 66 ms a step");

        Assert.Equal(fast * 0.021, slow * 0.0298, 6);
    }
}
