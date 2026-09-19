using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The split the lead check logs. A miss read in the wrong axes points at the wrong fault: a burst
/// ahead of a slowing target and one beside a steady one are different bugs.
/// </summary>
public class LeadErrorTests
{
    private static readonly double3 Up = new(0, 0, 1);

    [Fact]
    public void AMissIsSplitIntoItsThreeDirectionsExactly()
    {
        // along +X with +Z up, right is X cross Z, which is -Y.
        double3 displacement = new double3(1, 0, 0) * 3 + Up * 4 + new double3(0, -1, 0) * 5;

        LeadError.Split split = LeadError.Resolve(displacement, new double3(250, 0, 0), Up);

        Assert.Equal(3.0, split.Along, 9);
        Assert.Equal(4.0, split.Up, 9);
        Assert.Equal(5.0, split.Right, 9);
    }

    [Fact]
    public void AClimbingTrackIsTakenAlongItsHorizontal()
    {
        LeadError.Split split = LeadError.Resolve(new double3(10, 0, 0), new double3(100, 0, 500), Up);

        Assert.Equal(10.0, split.Along, 9);
        Assert.Equal(0.0, split.Up, 9);
    }

    [Fact]
    public void AVerticalTrackStillSplitsIntoThree()
    {
        LeadError.Split split = LeadError.Resolve(new double3(3, 4, 5), Up, Up);

        Assert.True(double.IsFinite(split.Along) && double.IsFinite(split.Right));
        Assert.Equal(5.0, split.Up, 9);
        Assert.Equal(25.0, (split.Along * split.Along) + (split.Right * split.Right), 9);
    }

    [Fact]
    public void AFallingTargetLeavesTheBurstAboveItByHalfGTSquared()
    {
        const double t = 8.0;
        double3 atFire = new(300, 0, 0);
        double3 atBurst = atFire + new double3(0, 0, -9.80665 * t);

        LeadError.Split split = LeadError.Resolve(LeadError.SteadyTargetMiss(atFire, atBurst, t), atFire, Up);

        Assert.Equal(0.5 * 9.80665 * t * t, split.Up, 9);
        Assert.Equal(0.0, split.Along, 9);
    }

    [Fact]
    public void ASlowingTargetLeavesTheBurstAheadOfIt()
    {
        const double t = 8.0;
        double3 atFire = new(300, 0, 0);
        double3 atBurst = new(220, 0, 0);

        LeadError.Split split = LeadError.Resolve(LeadError.SteadyTargetMiss(atFire, atBurst, t), atFire, Up);

        Assert.Equal(320.0, split.Along, 9);
    }
}
