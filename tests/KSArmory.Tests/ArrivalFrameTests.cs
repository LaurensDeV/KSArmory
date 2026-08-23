using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The axes an arrival is measured in, and the one geometry that has none.
///
/// <para>What makes this worth its own suite is the <c>cot γ</c> multiplier: the same drift resolved
/// up rather than across the track costs eight times as much ground at a 7° arrival, so a budget
/// that reports lengths instead of components is not a budget at all.</para>
/// </summary>
public class ArrivalFrameTests(ITestOutputHelper Out)
{
    private const double R = 6_371_000.0;

    /// <summary>An arrival a stated number of degrees below the horizontal, coming in due east.</summary>
    private static void Shallow(double belowDegrees, out double3 point, out double3 velocity)
    {
        point = new double3(R, 0, 0);

        double rad = belowDegrees * Math.PI / 180.0;
        velocity = new double3(-Math.Sin(rad), Math.Cos(rad), 0) * 2740.0;
    }

    [Fact]
    public void TheAxesAreRightHandedAndOrthonormal()
    {
        Shallow(7.1, out double3 point, out double3 velocity);
        Assert.True(ArrivalFrame.TryAt(point, velocity, out ArrivalFrame frame));

        foreach (double3 axis in new[] { frame.Up, frame.Downrange, frame.Cross })
        {
            Assert.Equal(1.0, Vec.Len(axis), 9);
        }

        Assert.Equal(0.0, Vec.Dot(frame.Up, frame.Downrange), 9);
        Assert.Equal(0.0, Vec.Dot(frame.Up, frame.Cross), 9);
        Assert.Equal(0.0, Vec.Dot(frame.Downrange, frame.Cross), 9);

        // Right-handed: up x downrange is the cross axis, not its negative.
        double3 built = Vec.Cross(frame.Up, frame.Downrange);
        Assert.Equal(0.0, Vec.Len(built - frame.Cross), 9);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(7.1)]
    [InlineData(20.0)]
    [InlineData(45.0)]
    public void TheArrivalAngleComesBackOutOfTheFrame(double below)
    {
        Shallow(below, out double3 point, out double3 velocity);
        Assert.True(ArrivalFrame.TryAt(point, velocity, out ArrivalFrame frame));

        Assert.Equal(below, frame.BelowHorizontalDegrees(velocity), 6);
    }

    /// <summary>
    /// The whole reason the components are kept apart: a drift up costs <c>cot γ</c> times as much
    /// ground as the same drift across the track, and at the arrival this mod flies that is eight.
    /// </summary>
    [Fact]
    public void ADriftUpIsWorthCotGammaTimesTheSameDriftAcross()
    {
        Shallow(7.1, out double3 point, out double3 velocity);
        Assert.True(ArrivalFrame.TryAt(point, velocity, out ArrivalFrame frame));

        double3 up = frame.Resolve(frame.Up);
        double3 across = frame.Resolve(frame.Cross);

        Assert.Equal(1.0, up.X, 9);
        Assert.Equal(0.0, up.Y, 9);
        Assert.Equal(1.0, across.Z, 9);

        double cot = 1.0 / Math.Tan(7.1 * Math.PI / 180.0);
        Out.WriteLine($"at 7.1 deg, one metre up is {cot:F1} m of ground");

        Assert.InRange(cot, 7.5, 8.5);
    }

    /// <summary>
    /// A vertical arrival has no ground track, and this refuses rather than inventing one.
    ///
    /// <para>Inventing it is the failure this shares with <c>Vec.PerpendicularTo</c> and with the
    /// map frame at the poles: the invented axis is stable nowhere, so it flips as the geometry
    /// creeps past and takes every component reported against it with it.</para>
    /// </summary>
    [Fact]
    public void AVerticalArrivalHasNoTrackAndIsRefused()
    {
        double3 point = new(R, 0, 0);

        Assert.False(ArrivalFrame.TryAt(point, new double3(-2740, 0, 0), out _),
                     "straight down was given a ground track");

        Assert.False(ArrivalFrame.TryAt(point, Vec.Zero, out _), "a standing round was given one");
        Assert.False(ArrivalFrame.TryAt(Vec.Zero, new double3(0, 1, 0), out _),
                     "the body's own centre was given an up");

        // A hair off vertical is still refused; a degree is not.
        Shallow(89.99, out double3 p1, out double3 v1);
        Assert.False(ArrivalFrame.TryAt(p1, v1, out _));

        Shallow(89.0, out double3 p2, out double3 v2);
        Assert.True(ArrivalFrame.TryAt(p2, v2, out _));
    }

    [Fact]
    public void NothingFiniteComesOutOfSomethingThatIsNot()
    {
        double3 point = new(R, 0, 0);
        Assert.False(ArrivalFrame.TryAt(point, new double3(double.NaN, 1, 0), out _));
        Assert.False(ArrivalFrame.TryAt(new double3(double.PositiveInfinity, 0, 0),
                                        new double3(0, 1, 0), out _));
    }
}
