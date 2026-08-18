using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The schedule flown while there is still air, and the limiter that decides how far guidance may
/// pull the stack off its own slipstream.
/// </summary>
public class AscentProfileTests
{
    private static readonly double3 Up = new(0, 0, 1);
    private static readonly double3 East = new(1, 0, 0);

    [Fact]
    public void ThePitchProgrammeLeavesVerticalAndReachesTheHorizon()
    {
        Assert.Equal(90.0, AscentProfile.PitchDegreesAt(0.0, 1000.0, 50_000.0), 3);
        Assert.Equal(90.0, AscentProfile.PitchDegreesAt(1000.0, 1000.0, 50_000.0), 3);
        Assert.Equal(0.0, AscentProfile.PitchDegreesAt(50_000.0, 1000.0, 50_000.0), 3);
        Assert.Equal(0.0, AscentProfile.PitchDegreesAt(90_000.0, 1000.0, 50_000.0), 3);
    }

    /// <summary>
    /// It has to turn hardest while it is slow and lowest. A schedule that is linear in altitude
    /// spends the whole upper stage near vertical and then hands guidance an enormous correction
    /// at exactly the point where correcting it is most expensive.
    /// </summary>
    [Fact]
    public void MostOfTheTurnHappensInTheFirstPartOfTheClimb()
    {
        double atHalf = AscentProfile.PitchDegreesAt(25_500.0, 1000.0, 50_000.0);
        Assert.True(atHalf < 45.0, $"halfway up it was still at {atHalf:F1} deg above the horizon");
    }

    [Fact]
    public void TheCommandedDirectionMatchesThePitchItWasGiven()
    {
        Assert.True(Vec.AngleBetween(AscentProfile.Aim(Up, East, 90.0), Up) < 1e-9);
        Assert.True(Vec.AngleBetween(AscentProfile.Aim(Up, East, 0.0), East) < 1e-9);

        double3 half = AscentProfile.Aim(Up, East, 45.0);
        Assert.Equal(45.0, Vec.AngleBetween(half, Up) * 180.0 / Math.PI, 6);
    }

    /// <summary>
    /// Thin air is not no load. At 35 km a rising stack has a hundredth of sea-level density on it
    /// and several kilopascals, because by then it is doing two kilometres a second — so a limiter
    /// that opens on density lets go exactly where the vehicle is going fastest.
    /// </summary>
    [Fact]
    public void TheLimiterHoldsWhileThereIsPressureAndOpensWhenThereIsNot()
    {
        double3 wanted = new(1, 0, 0);
        double3 flow = new(0, 0, 1);

        double3 loaded = AscentProfile.HoldIntoTheAirflow(wanted, flow, 30_000.0, 8.0);
        Assert.Equal(8.0, Vec.AngleBetween(loaded, flow) * 180.0 / Math.PI, 3);

        // Thin air, but fast: still thousands of pascals, still held.
        double thinButFast = AscentProfile.DynamicPressure(0.01, 2000.0);
        Assert.True(thinButFast > 20_000.0, $"{thinButFast:F0} Pa");
        Assert.Equal(8.0, Vec.AngleBetween(AscentProfile.HoldIntoTheAirflow(wanted, flow, thinButFast, 8.0), flow)
                          * 180.0 / Math.PI, 3);

        // Out of the air entirely: guidance may point wherever it likes.
        Assert.True(Vec.AngleBetween(AscentProfile.HoldIntoTheAirflow(wanted, flow, 0.0, 8.0), wanted) < 1e-9);
    }

    [Fact]
    public void ACommandAlreadyInsideTheLimitIsLeftAlone()
    {
        double3 flow = Vec.Unit(new double3(0, 0, 1));
        double3 wanted = Vec.Unit(new double3(0.05, 0, 1));

        Assert.True(Vec.AngleBetween(AscentProfile.HoldIntoTheAirflow(wanted, flow, 30_000.0, 8.0), wanted) < 1e-9);
    }

    /// <summary>
    /// Downrange is taken off the trajectory rather than from a great-circle bearing, so it already
    /// carries the correction for the planet turning under the flight. It is also the heading the
    /// closed loop will want, which is what makes the handover invisible.
    /// </summary>
    [Fact]
    public void DownrangeIsTheHorizontalPartOfTheVelocityTheShotNeeds()
    {
        double3 required = new(3000, 0, 4000);
        double3 frame = new(400, 0, 0);

        double3 downrange = AscentProfile.Downrange(Up, required, frame);

        Assert.Equal(0.0, Vec.Dot(downrange, Up), 9);
        Assert.Equal(1.0, Vec.Len(downrange), 9);
        Assert.True(downrange.X > 0.0);
    }
}
