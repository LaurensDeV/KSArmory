using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The roll a pointing command leaves undecided, and the one direction where the usual answer does
/// not merely fail but reverses.
/// </summary>
public class AimFrameTests
{
    private static readonly double3 Up = new(0, 0, 1);
    private static readonly double3 East = new(1, 0, 0);

    [Fact]
    public void StartingFreshItClocksToThePlanet()
    {
        double3 aim = Vec.Unit(new double3(1, 0, 0.2));
        double3 reference = AimFrame.Advance(Vec.Zero, aim, -Up, East);

        Assert.Equal(0.0, Vec.Dot(reference, aim), 9);
        Assert.True(Vec.Dot(reference, -Up) > 0.0, "the belly should be toward the planet");
    }

    /// <summary>
    /// Straight up is the singularity: nothing about "belly toward the planet" picks a roll when
    /// the nose points away from it. A vertical rise sits here for its whole duration.
    /// </summary>
    [Fact]
    public void PointingStraightUpFallsBackRatherThanPickingAnything()
    {
        double3 reference = AimFrame.Advance(Vec.Zero, Up, -Up, East);

        Assert.Equal(0.0, Vec.Dot(reference, Up), 9);
        Assert.True(Vec.Len(reference - East) < 1e-9, "it should have clocked to downrange");
    }

    /// <summary>
    /// The one that matters. Sweeping the nose up through the vertical, "belly down" reverses —
    /// so a reference re-derived each frame swings through half a turn and the vehicle rolls hard
    /// for no reason. Carrying it forward cannot do that, because it never asks again.
    /// </summary>
    [Fact]
    public void TheReferenceIsSteadyAsTheAimSweepsThroughTheVertical()
    {
        double3 carried = Vec.Zero;
        double3 previous = Vec.Zero;
        double worst = 0.0;

        for (int i = 0; i <= 2000; i++)
        {
            double angle = (i / 2000.0 - 0.5) * 1.2;
            double3 aim = Vec.Unit(new double3(Math.Sin(angle), 0, Math.Cos(angle)));

            carried = AimFrame.Advance(carried, aim, -Up, East);

            Assert.Equal(0.0, Vec.Dot(carried, aim), 6);

            if (!previous.Equals(Vec.Zero)) worst = Math.Max(worst, Vec.AngleBetween(previous, carried));
            previous = carried;
        }

        Assert.True(worst < 0.01,
                    $"the reference jumped {worst * 180.0 / Math.PI:F1} degrees between frames");
    }

    /// <summary>
    /// And re-deriving it does jump, which is what the carry is for. Without this the test above
    /// only says the arithmetic is smooth, not that anything was at stake.
    /// </summary>
    [Fact]
    public void RederivingItEachFrameSwingsThroughHalfATurn()
    {
        double3 previous = Vec.Zero;
        double worst = 0.0;

        for (int i = 0; i <= 2000; i++)
        {
            double angle = (i / 2000.0 - 0.5) * 1.2;
            double3 aim = Vec.Unit(new double3(Math.Sin(angle), 0, Math.Cos(angle)));

            double3 fresh = AimFrame.Advance(Vec.Zero, aim, -Up, East);

            if (!previous.Equals(Vec.Zero)) worst = Math.Max(worst, Vec.AngleBetween(previous, fresh));
            previous = fresh;
        }

        Assert.True(worst > 2.0,
                    $"re-deriving should swing through half a turn; it only moved {worst * 180.0 / Math.PI:F1} degrees");
    }

    [Fact]
    public void AnAimThatReversesOutrightIsReseededRatherThanLost()
    {
        double3 carried = AimFrame.Advance(Vec.Zero, East, -Up, Up);

        // Now point the nose along the old reference, which leaves it useless.
        double3 next = AimFrame.Advance(carried, carried, -Up, East);

        Assert.Equal(1.0, Vec.Len(next), 9);
        Assert.Equal(0.0, Vec.Dot(next, carried), 6);
    }
}
