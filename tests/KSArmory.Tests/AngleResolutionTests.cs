using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The resolution of <see cref="Vec.AngleBetween"/> at the two endpoints, which is what decides
/// whether a miss measured across a planet's surface can be read at all.
/// </summary>
/// <remarks>
/// Every ground distance in this mod is an angle times a radius — the release probe's miss, the
/// warhead trace's walk, <c>IcbmComputer.PredictedMissMetres</c>. The shot they are measuring is a
/// few centimetres, so a ruler with a floor above that reads every one of them as zero.
/// </remarks>
public class AngleResolutionTests
{
    private const double EarthishRadius = 6371000.0;

    private static double3 OnSphereAt(double angle)
        => new(EarthishRadius * Math.Cos(angle), EarthishRadius * Math.Sin(angle), 0.0);

    /// <summary>
    /// The one that fails against <c>acos</c>: a centimetre of ground is a 1.6e-9 rad angle, whose
    /// cosine is 1.0 to the last bit of a double, so the old form answered exactly 0.0.
    /// </summary>
    [Theory]
    [InlineData(0.001)]
    [InlineData(0.01)]
    [InlineData(0.035)]
    [InlineData(0.1)]
    [InlineData(0.134)]
    public void AGroundDistanceUnderTheOldRulersFloorIsStillMeasured(double metres)
    {
        double3 a = OnSphereAt(0.0);
        double3 b = OnSphereAt(metres / EarthishRadius);

        double measured = EarthishRadius * Vec.AngleBetween(a, b);

        Assert.Equal(metres, measured, 6);
    }

    /// <summary>
    /// Stated as the floor itself, so the number in <see cref="Vec.AngleBetween"/>'s remarks is
    /// pinned rather than asserted in prose: <c>acos</c> cannot resolve below sqrt(2*eps).
    /// </summary>
    [Fact]
    public void TheFloorTheOldFormCarriedIsGone()
    {
        double floorRad = Math.Sqrt(2.0 * 2.220446049250313e-16);
        double3 a = OnSphereAt(0.0);
        double3 b = OnSphereAt(floorRad / 100.0);

        Assert.True(Vec.AngleBetween(a, b) > 0.0,
                    "a hundredth of the old floor should be a positive angle, not nothing");
        Assert.Equal(floorRad / 100.0, Vec.AngleBetween(a, b), 15);
    }

    /// <summary>
    /// The other endpoint. A chord form would do for small angles and lose precision here instead;
    /// this is what makes the replacement safe for the thirty call sites that ask about degrees.
    /// </summary>
    [Theory]
    [InlineData(1e-7)]
    [InlineData(1e-4)]
    [InlineData(1e-2)]
    public void NearlyOpposedVectorsResolveJustAsWell(double fromPi)
    {
        double3 a = OnSphereAt(0.0);
        double3 b = OnSphereAt(Math.PI - fromPi);

        Assert.Equal(Math.PI - fromPi, Vec.AngleBetween(a, b), 12);
    }

    /// <summary>The ordinary range, which never depended on the form and must not move.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void TheOrdinaryRangeIsUnchanged(double angle)
    {
        Assert.Equal(angle, Vec.AngleBetween(OnSphereAt(0.0), OnSphereAt(angle)), 12);
    }

    /// <summary>A degenerate input still answers zero rather than NaN, as every caller assumes.</summary>
    [Fact]
    public void ADegenerateInputIsStillZeroRatherThanNaN()
    {
        Assert.Equal(0.0, Vec.AngleBetween(Vec.Zero, new double3(1, 0, 0)));
        Assert.Equal(0.0, Vec.AngleBetween(new double3(1, 0, 0), Vec.Zero));
    }
}
