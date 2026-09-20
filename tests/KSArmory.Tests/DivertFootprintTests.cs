using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The reach ellipse read off the columns a salvo already flies, against the numbers
/// <c>MirvDivertTests</c> priced it at.
/// </summary>
public class DivertFootprintTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // The flown release at 6,179 km: ~/shots/2026-09-17-threearm shot 001, GeoSat FAT_1 round 1.
    private static readonly double3 FlownPositionCci = new(3_835_254.4, -5_998_331.6, -1_302_454.7);
    private static readonly double3 FlownVelocityCci = new(279.7753, 2844.5295, -3967.0558);

    private static double DensityAt(double3 p)
    {
        double altitude = Math.Max(0.0, Vec.Len(p) - R);
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    private static DivertFootprint Flown()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, R, true);

        ReleaseFocus.FlownSensitivity flown =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, FlownPositionCci, FlownVelocityCci, air)
            ?? throw new InvalidOperationException("the columns did not come down");

        Assert.True(DivertFootprint.TryFrom(Earth, flown, fromTheRealState: true, out DivertFootprint f));
        return f;
    }

    /// <summary>
    /// The columns the kicks already fly give the same reach phase 0 measured a different way.
    /// </summary>
    /// <remarks>
    /// <c>MirvDivertTests</c> prices this geometry at <b>688 m along per m/s and 355 across</b> by
    /// differencing whole predicted landings. If this reproduces that off the sensitivity columns, the
    /// display needs no flying of its own — which is the claim the whole of phase 2 rests on.
    /// </remarks>
    [Fact]
    public void TheEllipseIsWhatPhaseZeroPricedByFlyingLandings()
    {
        DivertFootprint f = Flown();

        Out.WriteLine($"semi-major {f.SemiMajorMetresPerMetrePerSecond:F1} m per m/s, "
                      + $"semi-minor {f.SemiMinorMetresPerMetrePerSecond:F1}, "
                      + $"long axis {f.OrientationRad * 180.0 / Math.PI:+0.00;-0.00;0.00} deg off downrange, "
                      + $"{f.FlightSeconds:F0} s of flight");

        Assert.InRange(f.SemiMajorMetresPerMetrePerSecond, 620.0, 760.0);
        Assert.InRange(f.SemiMinorMetresPerMetrePerSecond, 320.0, 390.0);

        // Stretched along the ground track, which is what makes it an ellipse rather than a disc.
        Assert.True(f.SemiMajorMetresPerMetrePerSecond > f.SemiMinorMetresPerMetrePerSecond);
        Assert.InRange(Math.Abs(f.OrientationRad) * 180.0 / Math.PI, 0.0, 2.0);
    }

    /// <summary>The cost of a displacement is the ellipse read backwards, so the two must invert.</summary>
    [Fact]
    public void TheCostOfADisplacementInvertsTheReach()
    {
        DivertFootprint f = Flown();

        foreach ((double along, double cross) in new[] { (50_000.0, 0.0), (0.0, 20_000.0), (30_000.0, 10_000.0) })
        {
            double spent = f.CostMetresPerSecond(along, cross);

            Assert.True(f.Reaches(along, cross, spent + 1e-6), $"{along}/{cross} unreachable at its own cost");
            Assert.False(f.Reaches(along, cross, spent * 0.9), $"{along}/{cross} reachable at 90% of its cost");
        }

        // A move along the long axis is the cheap one, which is the whole point of the shape.
        Assert.True(f.CostMetresPerSecond(50_000.0, 0.0) < f.CostMetresPerSecond(0.0, 50_000.0));
    }

    /// <summary>
    /// Nothing is reachable on no budget, and the ellipse never claims otherwise — the failure a region
    /// display makes silently is drawing something and calling everything inside it reachable.
    /// </summary>
    [Fact]
    public void NoBudgetReachesNothingButWhereItAlreadyLands()
    {
        DivertFootprint f = Flown();

        Assert.True(f.Reaches(0.0, 0.0, 0.0));
        Assert.False(f.Reaches(1.0, 0.0, 0.0));
        Assert.False(f.Reaches(0.0, 1.0, 0.0));
    }

    /// <summary>
    /// The arrival frame is taken against the ground, not the inertial velocity. The axes cannot tell
    /// the difference — both frames span one horizontal plane — but everything angular can.
    /// </summary>
    [Fact]
    public void TheFrameIsTheGroundsAndOnlyTheAnglesKnow()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, R, true);

        ReleaseFocus.FlownSensitivity flown =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, FlownPositionCci, FlownVelocityCci, air)
            ?? throw new InvalidOperationException("the columns did not come down");

        Assert.True(DivertFootprint.TryFrom(Earth, flown, fromTheRealState: true, out DivertFootprint ground));

        Assert.True(ArrivalFrame.TryAt(flown.ArrivedCci, flown.ArrivalVelocityCci, out ArrivalFrame inertial));

        double turn = Math.Acos(Math.Clamp(Vec.Dot(ground.Frame.Downrange, inertial.Downrange), -1.0, 1.0));
        Out.WriteLine($"the two downranges are {turn * 180.0 / Math.PI:F2} deg apart");

        Assert.InRange(turn * 180.0 / Math.PI, 1.0, 20.0);
    }
}
