using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The release probe's own miss, cancelled by a separation velocity along the ground.
///
/// <para>The aim loop stops on its payback rule with the release state still predicted to miss, about a
/// metre short on a 32° arrival. <see cref="ReleaseFocus.TryMissKick"/> is the least velocity that moves
/// that prediction onto the target along the ground, and <see cref="ReleaseFocus.Kick"/> refuses it past
/// <see cref="ReleaseFocus.MaxMissKickMetresPerSecond"/>.</para>
///
/// <para>The probe is flown as the computer flies it, with the warhead's drag, and with its crossing on
/// the surface unless a test says otherwise. Landings are read to the sphere as
/// <see cref="ReleaseFocusTests"/> reads them.</para>
/// </summary>
public class ReleaseMissTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_375_278.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    // One warhead away on 2026-09-12-query, shot 001 seat 8: 852 km up, 5.0 km/s, 340 s to go.
    private static readonly double3 TracedPositionCci = new(3_326_222.3, -6_201_274.6, -1_632_562.5);
    private static readonly double3 TracedVelocityCci = new(-146.5688, 2_952.0146, -4_034.5339);

    // The bar a cancelled miss is held to, against a miss of a metre or more.
    private const double OnePoint = 0.01;

    public enum Arc { Traced, Long }

    private readonly record struct Probe(double3 PositionCci, double3 VelocityCci, ImpactPredictor.Impact Hit,
                                         double3 LandedCci, ArrivalFrame Frame);

    private static Probe ProbeFor(Arc arc, bool onTheSurface = true)
    {
        double3 position = TracedPositionCci, velocity = TracedVelocityCci;

        if (arc == Arc.Long)
        {
            // A lofted arc from the same release point, flown for five times as long.
            double3 normal = Vec.Unit(Vec.Cross(TracedPositionCci, TracedVelocityCci));
            double3 aim = doubleQuat.CreateFromAxisAngle(normal, 40.0 * Math.PI / 180.0)
                          * Vec.Unit(TracedPositionCci) * R;

            Assert.True(Lambert.TrySolve(position, aim, 1_500.0, Mu, out Lambert.Transfer transfer));
            velocity = transfer.DepartureVelocityCci;
        }

        Assert.True(TryPredict(position, velocity, onTheSurface, out ImpactPredictor.Impact hit),
                    "the release state never came down");
        Assert.True(ArrivalFrame.TryAt(hit.PointCci, hit.VelocityCci, out ArrivalFrame frame));

        Probe probe = new(position, velocity, hit, Landed(position, velocity), frame);

        if (arc == Arc.Traced)
        {
            Assert.InRange(hit.Seconds, 320.0, 360.0);
            Assert.InRange(frame.BelowHorizontalDegrees(hit.VelocityCci), 28.0, 36.0);
        }
        else
        {
            Assert.InRange(hit.Seconds, 1_400.0, 1_600.0);
        }

        return probe;
    }

    // As IcbmComputer.ProbeRelease flies it, onTheSurface being its PredictionStopsOnTheSurface.
    private static bool TryPredict(double3 positionCci, double3 velocityCci, bool onTheSurface,
                                   out ImpactPredictor.Impact hit)
        => ImpactPredictor.TryPredict(Earth, positionCci, velocityCci, 2.0, 20_000.0, out hit, null, null,
                                      new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21),
                                      stopOnTheSurface: onTheSurface);

    // Where a round from this state stops, carried up its own velocity to the sphere and fixed to the
    // ground at the release.
    private static double3 Landed(double3 positionCci, double3 velocityCci)
    {
        Assert.True(TryPredict(positionCci, velocityCci, onTheSurface: false, out ImpactPredictor.Impact hit),
                    "a warhead never came down");

        double3 up = Vec.Unit(hit.PointCci);
        double sinking = -Vec.Dot(hit.VelocityCci, up);
        Assert.True(sinking > 0.0, "a warhead came down climbing");

        double back = (R - Vec.Len(hit.PointCci)) / sinking;
        return Earth.UncarryCci(hit.PointCci - hit.VelocityCci * back, hit.Seconds - back);
    }

    // A target the probe's landing is off by these many metres along the ground, in its arrival frame.
    private static double3 TargetOff(in Probe probe, double downrange, double cross)
    {
        double3 offAtArrival = probe.Frame.Downrange * downrange + probe.Frame.Cross * cross;
        return Vec.Unit(probe.LandedCci - Earth.UncarryCci(offAtArrival, probe.Hit.Seconds)) * R;
    }

    private static double3 OnTheGround(in Probe probe, double3 landedCci, double3 targetCci)
        => probe.Frame.Resolve(Earth.CarryCci(landedCci - targetCci, probe.Hit.Seconds));

    private static double3 Horizontal(double3 vectorCci, double3 atCci)
    {
        double3 up = Vec.Unit(atCci);
        return vectorCci - up * Vec.Dot(vectorCci, up);
    }

    private void Say(string what, in Probe probe, double3 kick, double3 landedCci, double3 targetCci)
    {
        double3 parts = OnTheGround(probe, landedCci, targetCci);
        Out.WriteLine($"  {what}: kick {Vec.Len(kick) * 1000.0:F3} mm/s, lands {parts.Y:+0.0000;-0.0000} downrange "
                      + $"{parts.Z:+0.0000;-0.0000} cross of the target, {Vec.Len(landedCci - targetCci) * 100.0:F3} cm "
                      + $"({probe.Hit.Seconds:F0} s)");
    }

    /// <summary>The probe lands on its target once the kick is given, whichever way along the ground it missed.</summary>
    [Theory]
    [InlineData(Arc.Traced, -1.0, +0.3)]
    [InlineData(Arc.Traced, 0.0, +1.0)]
    [InlineData(Arc.Traced, +0.8, -0.5)]
    [InlineData(Arc.Long, -1.0, +0.3)]
    public void TheKickLandsTheProbeOnItsTarget(Arc arc, double downrange, double cross)
    {
        Probe probe = ProbeFor(arc);
        double3 target = TargetOff(probe, downrange, cross);

        double3 before = OnTheGround(probe, probe.LandedCci, target);
        Assert.InRange(before.Y, downrange - 0.001, downrange + 0.001);
        Assert.InRange(before.Z, cross - 0.001, cross + 0.001);
        Assert.True(Vec.Len(Horizontal(probe.Hit.GroundFixedPointCci - probe.LandedCci, target)) < 0.001,
                    "the probe's crossing is not where the round stops, so it is not the miss being tested");

        Assert.True(ReleaseFocus.TryMissKick(Earth, probe.PositionCci, probe.VelocityCci, probe.Hit.Seconds,
                                             probe.Hit.GroundFixedPointCci, target, out double3 kick));

        double3 landed = Landed(probe.PositionCci, probe.VelocityCci + kick);
        Say($"{arc} ({downrange:+0.0;-0.0;0.0}, {cross:+0.0;-0.0;0.0})", probe, kick, landed, target);

        Assert.InRange(Vec.Len(kick), arc == Arc.Traced ? 0.0015 : 0.0002, ReleaseFocus.MaxMissKickMetresPerSecond);
        Assert.True(Vec.Len(landed - target) < OnePoint,
                    $"the kicked round lands {Vec.Len(landed - target) * 100.0:F1} cm from its target");
    }

    /// <summary>
    /// A crossing found under the surface keeps its depth: the kick moves the prediction along the ground,
    /// and the ground image of that depth is <see cref="IcbmConfig.PredictionStopsOnTheSurface"/>'s to remove.
    /// </summary>
    [Fact]
    public void TheDepthOfACrossingIsLeftToThePredictionsOwnSwitch()
    {
        Probe surface = ProbeFor(Arc.Traced);
        Probe deep = ProbeFor(Arc.Traced, onTheSurface: false);

        double3 depthOnGround = Horizontal(deep.Hit.GroundFixedPointCci - surface.Hit.GroundFixedPointCci,
                                           surface.LandedCci);
        Assert.True(Vec.Len(depthOnGround) > 0.05,
                    $"the crossing is found only {Vec.Len(depthOnGround) * 100.0:F1} cm of ground past the surface, "
                    + "which cannot tell the two apart");

        double3 target = TargetOff(surface, -1.0, +0.3);

        Assert.True(ReleaseFocus.TryMissKick(Earth, deep.PositionCci, deep.VelocityCci, deep.Hit.Seconds,
                                             deep.Hit.GroundFixedPointCci, target, out double3 kick));

        double3 landed = Landed(deep.PositionCci, deep.VelocityCci + kick);
        Say("deep crossing", surface, kick, landed, target);
        Out.WriteLine($"  the depth lands {Vec.Len(depthOnGround) * 100.0:F1} cm along the ground");

        Assert.True(Vec.Len(landed - target + depthOnGround) < OnePoint,
                    $"the round lands {Vec.Len(landed - target + depthOnGround) * 100.0:F1} cm from the depth's own image");
    }

    /// <summary>
    /// A miss the payback rule leaves is given back; one the loop failed to close is refused whole, and the
    /// round leaves on nothing of it.
    /// </summary>
    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void AMissTheLoopFailedToCloseIsRefusedRatherThanFlown(Arc arc)
    {
        Probe probe = ProbeFor(arc);

        double3 near = TargetOff(probe, -2.0, 0.0);
        ReleaseFocus.Separation within = KickFor(probe, near);

        Assert.Equal(ReleaseFocus.MissOutcome.Cancelled, within.Miss);
        Assert.Equal(within.MissKickCci, within.KickCci);
        Assert.True(Vec.Len(Landed(probe.PositionCci, probe.VelocityCci + within.KickCci) - near) < OnePoint,
                    "the two metres the payback rule leaves were not cancelled");

        double3 far = TargetOff(probe, -40.0, +12.0);
        ReleaseFocus.Separation beyond = KickFor(probe, far);
        Out.WriteLine($"  {arc}: 2 m wants {Vec.Len(within.MissKickCci) * 1000.0:F3} mm/s, "
                      + $"42 m wants {Vec.Len(beyond.MissKickCci) * 1000.0:F3} mm/s");

        Assert.True(Vec.Len(beyond.MissKickCci) > ReleaseFocus.MaxMissKickMetresPerSecond,
                    "this miss fits under the cap, so nothing here tests the cap");
        Assert.Equal(ReleaseFocus.MissOutcome.OverTheCap, beyond.Miss);
        Assert.Equal(Vec.Zero, beyond.KickCci);
    }

    [Fact]
    public void AMissThatWillNotSolveGivesNothing()
    {
        Probe probe = ProbeFor(Arc.Traced);
        double3 nowhere = new(double.NaN, 0.0, 0.0);

        ReleaseFocus.Separation kick = KickFor(probe, nowhere);

        Assert.Equal(ReleaseFocus.MissOutcome.Unsolved, kick.Miss);
        Assert.Equal(Vec.Zero, kick.MissKickCci);
        Assert.Equal(Vec.Zero, kick.KickCci);
    }

    private static ReleaseFocus.Separation KickFor(in Probe probe, double3 targetCci)
        => ReleaseFocus.Kick(Earth, probe.PositionCci, probe.VelocityCci, probe.Hit.Seconds, Vec.Zero, Vec.Zero,
                             focusRing: false, cancelSpin: false,
                             new ReleaseFocus.ProbeMiss(probe.Hit.GroundFixedPointCci, targetCci));
}
