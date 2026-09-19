using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The release probe's miss, cancelled over ground that is not level.
///
/// <para><see cref="ReleaseFocus.TryMissKick"/> moves the arc square to its arrival by the miss's own
/// component there, and the crossing slides back along the arrival onto whatever surface the miss was
/// measured on. Taken square to local up, a miss over ground falling away downrange at <c>g</c> is
/// cancelled wrong by <c>1 − tan γ / (tan γ − g)</c> of itself; taken along the chord on the ground, it is
/// not. <see cref="ReleaseMissTests"/> flies the mean sphere, where the two agree.</para>
/// </summary>
public class ReleaseMissSlopeTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_375_278.0;
    private const double ScaleHeight = 8_000.0;

    // Wide against a metre of miss, so the slope is linear across it; bounded, so the arc is not cut short
    // on the way in.
    private const double RampSpanMetres = 2_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    // The release ReleaseMissTests flies: 852 km up, 5.0 km/s, 340 s to go, arriving at 32 deg.
    private static readonly double3 TracedPositionCci = new(3_326_222.3, -6_201_274.6, -1_632_562.5);
    private static readonly double3 TracedVelocityCci = new(-146.5688, 2_952.0146, -4_034.5339);

    private readonly record struct Shot(ImpactPredictor.Impact Probe, ArrivalFrame Frame, double TanGamma,
                                        double3 TargetCci, Func<double3, double> Ground);

    // A constant gradient through where the level-ground probe comes down, along the ground track and positive
    // falling away downrange, or across it and positive falling away to the right.
    private static Func<double3, double> Ramp(double gradient, bool across = false)
    {
        Assert.True(Predict(TracedVelocityCci, null, out ImpactPredictor.Impact hit));

        double3 at = Vec.Unit(hit.GroundFixedPointCci);
        double3 overGround = Earth.UncarryCci(hit.VelocityCci - Earth.GroundVelocityCci(hit.PointCci), hit.Seconds);
        double3 along = Vec.Unit(overGround - at * Vec.Dot(overGround, at));
        double3 falling = across ? Vec.Unit(Vec.Cross(along, at)) : along;

        return bodyFixed =>
        {
            double s = R * Math.Asin(Math.Clamp(Vec.Dot(Vec.Unit(bodyFixed), falling), -1.0, 1.0));
            return R - gradient * RampSpanMetres * Math.Tanh(s / RampSpanMetres);
        };
    }

    // As IcbmComputer.ProbeRelease flies it, over the given ground.
    private static bool Predict(double3 velocityCci, Func<double3, double>? ground, out ImpactPredictor.Impact hit)
        => ImpactPredictor.TryPredict(Earth, TracedPositionCci, velocityCci, 2.0, 20_000.0, out hit, ground, null,
                                      new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                                      stopOnTheSurface: true);

    // The probe over this ground, with its target this far off the impact: by default a metre short and 0.3 m
    // right of it.
    private static Shot ShotOver(double gradient, bool across = false, double downrange = -1.0, double cross = 0.3)
    {
        Func<double3, double> ground = Ramp(gradient, across);

        Assert.True(Predict(TracedVelocityCci, ground, out ImpactPredictor.Impact probe));
        Assert.True(ArrivalFrame.TryAt(probe.PointCci, probe.VelocityCci, out ArrivalFrame frame));

        double3 offAtArrival = frame.Downrange * downrange + frame.Cross * cross;
        double3 target = Vec.Unit(probe.GroundFixedPointCci - Earth.UncarryCci(offAtArrival, probe.Seconds)) * R;

        double tanGamma = Math.Tan(frame.BelowHorizontalDegrees(probe.VelocityCci) * Math.PI / 180.0);
        return new Shot(probe, frame, tanGamma, target, ground);
    }

    // Where this velocity comes down against the target along the ground, in the probe's arrival frame.
    private static double3 MissOnTheGround(in Shot shot, double3 velocityCci)
    {
        Assert.True(Predict(velocityCci, shot.Ground, out ImpactPredictor.Impact hit));

        double3 up = Vec.Unit(hit.GroundFixedPointCci);
        double3 miss = hit.GroundFixedPointCci - shot.TargetCci;
        return shot.Frame.Resolve(Earth.CarryCci(miss - up * Vec.Dot(miss, up), shot.Probe.Seconds));
    }

    private static double3 KickFor(in Shot shot, Func<double3, double>? ground)
    {
        Assert.True(ReleaseFocus.TryMissKick(Earth, TracedPositionCci, TracedVelocityCci, shot.Probe.Seconds,
                                             shot.Probe.GroundFixedPointCci, shot.TargetCci, out double3 kick, ground));
        return kick;
    }

    /// <summary>
    /// Measured square to local up, the kick is off by the slope's own factor — the term a level-ground
    /// fixture cannot produce.
    /// </summary>
    [Theory]
    [InlineData(+0.10)]
    [InlineData(-0.10)]
    [InlineData(+0.15)]
    [InlineData(-0.15)]
    public void SquareToUpTheKickIsWrongByTheSlopesFactor(double gradient)
    {
        Shot shot = ShotOver(gradient);

        double3 before = MissOnTheGround(shot, TracedVelocityCci);
        double3 after = MissOnTheGround(shot, TracedVelocityCci + KickFor(shot, null));

        double left = after.Y / before.Y;
        double factor = 1.0 - shot.TanGamma / (shot.TanGamma - gradient);
        Out.WriteLine($"gradient {gradient:+0.00;-0.00}: {left:+0.000;-0.000} of the miss left downrange, "
                      + $"1 - tan g/(tan g - gradient) = {factor:+0.000;-0.000}");

        Assert.InRange(before.Y, -1.01, -0.99);
        Assert.InRange(left, factor - 0.02, factor + 0.02);
        Assert.True(Math.Abs(left) > 0.1, "the slope moves less than a tenth of the miss, so nothing here tests it");
    }

    /// <summary>
    /// Measured square to local up, a miss across the track over ground falling away to the side lands the round
    /// long or short by <c>c · cross · cot γ</c>: the arc moved sideways meets a surface at another height, and
    /// slides along its arrival to reach it.
    /// </summary>
    [Theory]
    [InlineData(+0.20)]
    [InlineData(-0.20)]
    public void SquareToUpASideSlopeTurnsACrossMissIntoARangeMiss(double gradient)
    {
        Shot shot = ShotOver(gradient, across: true, downrange: 0.0, cross: 1.0);

        double3 after = MissOnTheGround(shot, TracedVelocityCci + KickFor(shot, null));
        double expected = Math.Abs(gradient) / shot.TanGamma;
        Out.WriteLine($"side gradient {gradient:+0.00;-0.00}: lands ({after.Y:+0.000;-0.000}, {after.Z:+0.000;-0.000}) m, "
                      + $"c cot g = {expected:F3} m");

        Assert.InRange(Math.Abs(after.Y), 0.8 * expected, 1.2 * expected);
    }

    /// <summary>Measured along the chord on the ground, one kick lands on the target whatever the slope.</summary>
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(+0.10, false)]
    [InlineData(-0.10, false)]
    [InlineData(+0.15, false)]
    [InlineData(-0.15, false)]
    [InlineData(+0.20, true)]
    [InlineData(-0.20, true)]
    public void OverTheGroundAsItLiesOneKickLandsOnTheTarget(double gradient, bool across)
    {
        Shot shot = across ? ShotOver(gradient, across: true, downrange: 0.0, cross: 1.0) : ShotOver(gradient);

        double3 kick = KickFor(shot, shot.Ground);
        double3 after = MissOnTheGround(shot, TracedVelocityCci + kick);
        Out.WriteLine($"{(across ? "side " : "")}gradient {gradient:+0.00;-0.00;0.00}: kick {Vec.Len(kick) * 1000.0:F3} mm/s, "
                      + $"lands ({after.Y * 1000.0:+0.0;-0.0} mm, {after.Z * 1000.0:+0.0;-0.0} mm)");

        Assert.True(Vec.Len(after) < 0.005, $"the kicked round lands {Vec.Len(after) * 1000.0:F1} mm from its target");
    }

    /// <summary>
    /// A lookup that cannot be trusted measures the miss square to up rather than flying a chord through a
    /// mountain: one that answers nothing, and one reading the mean sphere at one end and high ground at the
    /// other.
    /// </summary>
    [Fact]
    public void AGroundThatCannotBeTrustedLeavesTheMissSquareToUp()
    {
        Shot shot = ShotOver(+0.10);
        double3 squareToUp = KickFor(shot, null);

        double3 target = shot.TargetCci;
        Func<double3, double> nothing = _ => double.NaN;
        Func<double3, double> mountain = p => p.Equals(target) ? R + 4_000.0 : R;

        Assert.False(ReleaseFocus.TryMissOnTheGround(shot.Probe.GroundFixedPointCci, target, nothing, out _));
        Assert.False(ReleaseFocus.TryMissOnTheGround(shot.Probe.GroundFixedPointCci, target, mountain, out _));
        Assert.Equal(squareToUp, KickFor(shot, nothing));
        Assert.Equal(squareToUp, KickFor(shot, mountain));
    }

    /// <summary>The separation carries the ground through, and the cap still reads the chord's kick.</summary>
    [Fact]
    public void TheSeparationKicksOverTheGroundItIsGiven()
    {
        Shot shot = ShotOver(+0.15);

        ReleaseFocus.Separation kick = ReleaseFocus.Kick(Earth, TracedPositionCci, TracedVelocityCci, shot.Probe.Seconds,
                                                         Vec.Zero, Vec.Zero, focusRing: false, cancelSpin: false,
                                                         new ReleaseFocus.ProbeMiss(shot.Probe.GroundFixedPointCci,
                                                                                    shot.TargetCci, shot.Ground));

        Assert.Equal(ReleaseFocus.MissOutcome.Cancelled, kick.Miss);
        Assert.Equal(KickFor(shot, shot.Ground), kick.MissKickCci);
        Assert.True(Vec.Len(MissOnTheGround(shot, TracedVelocityCci + kick.KickCci)) < 0.005);
    }
}
