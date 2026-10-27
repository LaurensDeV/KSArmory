using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Guards that the coast step does not decide the impact. <see cref="WarpPolicy"/> does nothing
/// while the step is inside <see cref="MunitionProfile.PreferredStep"/> — 225 ms on this specimen —
/// and the first frame over <c>PreferredStep / 8</c> = 28.1 ms at the scenario's 8x holds the world
/// down for the rest of the flight, so a shot coasts at ~190 ms or ~105 ms on one frame's length.
/// Gravity aimed per sub-step makes both land in the same place.
///
/// <para>Every figure is printed; the assertions pin the shape. <c>docs/MIRV-NEXT.md</c> item 2 has
/// the history and prices the step against the impact.</para>
/// </summary>
public class WarpLatchScatterTests(ITestOutputHelper Out)
{
    private static MunitionProfile Warhead => LatchProneWarhead.Profile;

    /// <summary>
    /// The wall-clock frame at which the world is slowed, at the speed the scenario asks for.
    ///
    /// <para>Measured against 38 flown shots: every shot whose first full 8x frame was longer than
    /// this had the world held down on it, and every shot below it did not. The nearest pair either
    /// side is 27.94 ms and 28.18 ms.</para>
    /// </summary>
    private static double TripFrameSeconds => Warhead.PreferredStep / DeorbitShot.ScenarioWarp;

    /// <summary>
    /// The steady frame the traced flights actually ran at during the coast — median 23.1-24.5 ms
    /// over four arms. The trip threshold is 1.2x it, which is what puts this inside the noise.
    /// </summary>
    private const double SteadyFrame = 0.0235;

    /// <summary>A frame stream that is steady but for one overrun, the first time <paramref name="after"/> passes.</summary>
    private static Func<double, double> OneLongFrame(double after, double longFrame,
                                                     double steady = SteadyFrame)
    {
        bool spent = false;
        return elapsed =>
        {
            if (spent || elapsed < after) return steady;
            spent = true;
            return longFrame;
        };
    }

    /// <summary>The release state the whole budget is flown from, as in <c>ProbeGapTests</c>.</summary>
    private static void ReleaseState(out double3 fromCci, out double3 velocityCci)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out fromCci, out double3 _);
        velocityCci = arc.RequiredVelocityCci
                      + Vec.Unit(arc.RequiredVelocityCci) * Warhead.LaunchSpeed;
    }

    /// <summary>
    /// Where the release probe says it comes down, and which way it is travelling there.
    ///
    /// <para>The point is the <b>body-fixed</b> one, because that is what a flown round is compared
    /// against; taking the inertial one instead measures the planet's own turn over the flight and
    /// calls it miss — 230 km on this arc.</para>
    /// </summary>
    private static (double3 Probe, double3 Along) ProbeAndTrack(double3 fromCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(DeorbitShot.Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

        double3 up = Vec.Unit(hit.PointCci);
        return (hit.GroundFixedPointCci, Vec.Unit(hit.VelocityCci - up * Vec.Dot(hit.VelocityCci, up)));
    }

    /// <summary>How far past a reference a point lies along the track, signed.</summary>
    private static double Downrange(double3 referenceCci, double3 pointCci, double3 alongTrackCci)
    {
        double metres = DeorbitShot.GroundMetres(referenceCci, pointCci);
        return Vec.Dot(pointCci - referenceCci, alongTrackCci) >= 0.0 ? metres : -metres;
    }

    /// <summary>
    /// The policy on its own: one frame 0.3 ms either side of the limit, and the world runs the next
    /// four hundred seconds at two different speeds.
    ///
    /// <para>This is the whole discreteness. Nothing downstream is nonlinear — the step-to-impact
    /// relation below is a straight line — but the step itself is decided by a threshold crossing,
    /// so a flight lands in one of two places rather than anywhere between them.</para>
    /// </summary>
    [Fact]
    public void OneFrameEitherSideOfTheLimitSetsTheCoastsSpeedForTheWholeFlight()
    {
        Out.WriteLine($"the round asks the world for {Warhead.PreferredStep * 1000:F0} ms, "
                      + $"which at {DeorbitShot.ScenarioWarp:F0}x is a frame of "
                      + $"{TripFrameSeconds * 1000:F2} ms");

        foreach (double frame in new[] { 0.02794, 0.02818, 0.0333 })
        {
            WarpPolicy policy = new();
            double speed = DeorbitShot.ScenarioWarp;
            double held = double.NaN;

            for (int f = 0; f < 2000; f++)
            {
                double wall = f == 1 ? frame : SteadyFrame;
                WarpDecision d = policy.Decide(speed * wall, speed, true, true, Warhead.PreferredStep);
                if (d.Action == WarpAction.Slow)
                {
                    if (double.IsNaN(held)) held = d.Speed;
                    speed = d.Speed;
                }
            }

            Out.WriteLine($"  a {frame * 1000:F2} ms frame at f=1: "
                          + (double.IsNaN(held)
                                 ? $"never held, coast step {speed * SteadyFrame * 1000:F0} ms"
                                 : $"held to {held:F2}x, coast step {speed * SteadyFrame * 1000:F0} ms"));
        }

        // 0.24 ms of one frame, and the world runs the rest of the flight at half the speed.
        Assert.True(Slowed(0.02818), "a frame just over the limit must slow the world");
        Assert.False(Slowed(0.02794), "a frame just under it must not");
        return;

        static bool Slowed(double frame)
        {
            WarpPolicy policy = new();
            WarpDecision d = policy.Decide(DeorbitShot.ScenarioWarp * frame, DeorbitShot.ScenarioWarp,
                                           true, true, Warhead.PreferredStep);
            return d.Action == WarpAction.Slow;
        }
    }

    /// <summary>
    /// Once it has acted it never gives the speed back, however quiet the rest of the flight is.
    ///
    /// <para>That is deliberate in <see cref="WarpPolicy"/> — only <c>Release</c> clears the hold and
    /// it needs the air to be clear — and it is what turns a single slow frame into a property of the
    /// whole flight rather than of one frame.</para>
    /// </summary>
    [Fact]
    public void TheHoldNeverLiftsWhileTheRoundIsStillFlying()
    {
        WarpPolicy policy = new();
        double speed = DeorbitShot.ScenarioWarp;

        // One overrun, then two thousand frames well inside the limit.
        for (int f = 0; f < 2000; f++)
        {
            double wall = f == 0 ? 0.0333 : SteadyFrame;
            WarpDecision d = policy.Decide(speed * wall, speed, true, true, Warhead.PreferredStep);
            if (d.Action == WarpAction.Slow) speed = d.Speed;

            Assert.NotEqual(WarpAction.Restore, d.Action);
        }

        Assert.True(policy.Holding);
        Assert.True(speed < DeorbitShot.ScenarioWarp * 0.7,
                    "the speed the flight is left with is the one the overrunning frame computed");

        Out.WriteLine($"one 33.3 ms frame, then 2000 quiet ones: still held at {speed:F2}x "
                      + $"({speed * SteadyFrame * 1000:F0} ms) rather than "
                      + $"{DeorbitShot.ScenarioWarp * SteadyFrame * 1000:F0} ms");
    }

    /// <summary>
    /// One release state, one frame stream, and a <em>single</em> frame's length moved by a quarter
    /// of a millisecond: the latch changes the coast step, and the impact must not follow.
    /// </summary>
    [Fact]
    public void TheSameReleaseStateLandsFarApartOnOneFramesLength()
    {
        ReleaseState(out double3 from, out double3 v);
        (double3 probe, double3 along) = ProbeAndTrack(from, v);

        DeorbitShot.WarpedFlight under = DeorbitShot.FlyTheRoundUnderTheWarpPolicy(
            from, v, DeorbitShot.ScenarioWarp, OneLongFrame(0.0, 0.02794), warhead: Warhead);

        DeorbitShot.WarpedFlight over = DeorbitShot.FlyTheRoundUnderTheWarpPolicy(
            from, v, DeorbitShot.ScenarioWarp, OneLongFrame(0.0, 0.02818), warhead: Warhead);

        Out.WriteLine($"one frame of 27.94 ms: never held, coast {under.MeanStep * 1000:F0} ms, "
                      + $"lands {Downrange(probe, under.GroundFixed, along):F0} m downrange of its probe");
        Out.WriteLine($"one frame of 28.18 ms: held to {over.HeldSpeed:F2}x at t={over.HeldAt:F1} s, "
                      + $"coast {over.MeanStep * 1000:F0} ms, "
                      + $"lands {Downrange(probe, over.GroundFixed, along):F0} m downrange of its probe");
        Out.WriteLine($"the two land {DeorbitShot.GroundMetres(under.GroundFixed, over.GroundFixed):F0} m apart");

        Assert.True(double.IsNaN(under.HeldAt));
        Assert.False(double.IsNaN(over.HeldAt));

        // The latch still fires: the two flights genuinely coast at different steps.
        Assert.True(over.MeanStep < 0.75 * under.MeanStep,
                    $"the latch did not change the coast step; {under.MeanStep * 1000:F0} ms "
                    + $"against {over.MeanStep * 1000:F0} ms");

        // Per-sub-step gravity holds the round's gap to its predictor flat, -149 to -153 m from a
        // 25 ms frame to a 320 ms one, so the step the latch decides does not decide the impact.
        Assert.True(DeorbitShot.GroundMetres(under.GroundFixed, over.GroundFixed) < 20.0,
                    "the warp latch has started scattering the impact again; the round's gap has "
                    + "gone back to being frame-dependent");
    }

    /// <summary>
    /// The trip time is what flown logs vary over — 0.25 s in most shots, up to 330 s in the rest —
    /// and a night of shots cannot hold it constant, so the sweep across it must stay flat.
    /// </summary>
    [Fact]
    public void WhereTheImpactGoesAgainstWhenTheWorldWasSlowed()
    {
        ReleaseState(out double3 from, out double3 v);
        (double3 probe, double3 along) = ProbeAndTrack(from, v);

        Out.WriteLine("held at   coast step   downrange of the probe   from the earliest hold");

        double first = double.NaN;
        double previous = double.NegativeInfinity;
        double last = double.NaN;

        foreach (double at in new[] { 0.0, 30.0, 100.0, 200.0, 300.0, 1e9 })
        {
            DeorbitShot.WarpedFlight f = DeorbitShot.FlyTheRoundUnderTheWarpPolicy(
                from, v, DeorbitShot.ScenarioWarp, OneLongFrame(at, 0.0333), warhead: Warhead);

            double walk = Downrange(probe, f.GroundFixed, along);
            if (double.IsNaN(first)) first = walk;

            Out.WriteLine($"{(at > 1e8 ? "never" : $"{at:F0} s"),8}"
                          + $"{f.MeanStep * 1000,12:F0} ms{walk,20:F0} m"
                          + $"{walk - first,22:F0} m");

            previous = walk;
            last = walk;
        }

        // Inside the rig's own repeatability.
        Assert.True(Math.Abs(last - first) < 20.0,
                    $"when the world was slowed has started mattering again: {last - first:F0} m "
                    + "across the sweep");

        // Named so the compiler does not report it unused, and because a future re-measurement
        // wants the first reading rather than only the span.
        Assert.True(double.IsFinite(previous));
    }

    /// <summary>
    /// The same curve read the way the logs report it: the mean coast step against the impact.
    ///
    /// <para>Flown, this is a straight line with an R² of 0.94-1.00 within an arm — 53.4 m per
    /// millisecond at a 7.1 degree arrival, 13.9 at 15.2 degrees and 9.1 at 20.3, which is the
    /// arrival angle's own leverage on a fixed error rather than four different mechanisms.</para>
    /// </summary>
    [Fact]
    public void HowMuchGroundOneMillisecondOfCoastStepIsWorth()
    {
        ReleaseState(out double3 from, out double3 v);
        (double3 probe, double3 along) = ProbeAndTrack(from, v);

        (double Step, double Walk)[] points =
        [
            .. new[] { 4.0, 5.0, 6.0, 7.0, 8.0 }.Select(warp =>
            {
                DeorbitShot.WarpedFlight f = DeorbitShot.FlyTheRoundUnderTheWarpPolicy(
                    from, v, warp, _ => SteadyFrame, warhead: Warhead);

                return (f.MeanStep * 1000.0, Downrange(probe, f.GroundFixed, along));
            }),
        ];

        foreach ((double step, double walk) in points)
        {
            Out.WriteLine($"  {step,4:F0} ms coast: {walk,7:F0} m downrange of the probe");
        }

        double slope = (points[^1].Walk - points[0].Walk) / (points[^1].Step - points[0].Step);
        Out.WriteLine($"{slope:F1} m of ground per millisecond of coast step");

        Assert.True(slope > 0.0, "a coarser coast must land the round further downrange, not nearer");
    }
}
