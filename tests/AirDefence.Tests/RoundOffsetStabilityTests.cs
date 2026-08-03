using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// A round's drawn offset must be smooth from frame to frame, whatever the frame times.
///
/// <para>Reported from play: rounds leaving the tubes "teleport around" and climb in a "super
/// quick zigzag pattern (slightly randomized)". Everything visible about a round — the tracer
/// and the subpart body — is placed from <see cref="Interceptor.OffsetFromPlatform"/>, so if
/// that quantity is not a smooth function of time the round visibly jitters however correct its
/// trajectory is.</para>
///
/// <para>The trap is that offsets are differences of ecliptic positions, which near Earth are
/// ~1.5e11 m apart and closing at ~29.8 km/s. At 60 fps the platform moves ~500 m per frame, so
/// anything that measures the two ends of the subtraction at even slightly different instants
/// produces an error of hundreds of metres — and if the discrepancy scales with the frame time,
/// it changes every frame and reads as a zigzag.</para>
/// </summary>
public class RoundOffsetStabilityTests
{
    // Earth-like: an enormous ecliptic position with a large common velocity, which is the
    // regime every one of this repository's frame-of-reference bugs has lived in.
    private static readonly double3 PlatformStart = new(1.4959e11, 0, 0);
    private static readonly double3 OrbitalVelocity = new(0, 29800, 0);

    private static MunitionProfile Munition() => Arsenal.MunitionNamed(Arsenal.PantsirS1.Munition);

    /// <summary>
    /// Flies a round for a number of frames with the given frame times, moving the platform
    /// exactly as its velocity says, and returns the offset seen each frame.
    /// </summary>
    private static List<double3> FlyWithFrameTimes(IReadOnlyList<double> frameTimes)
    {
        MunitionProfile munition = Munition();

        double3 platform = PlatformStart;
        double3 up = new(1, 0, 0);

        // Straight up out of the tube, carrying the platform's motion with it.
        var round = new Interceptor(
            positionEcl: platform + up * 3.0,
            velocityEcl: OrbitalVelocity + up * munition.LaunchSpeed,
            target: null!,
            tube: 1,
            platformEcl: platform);

        var offsets = new List<double3>();

        foreach (double dt in frameTimes)
        {
            // No target and no gravity: the round's motion in the local frame is a clean climb,
            // so any wobble in the reported offset comes from the bookkeeping, not the physics.
            round.Update(dt, target: null, gravity: double3.Zero,
                         frameVelocityEcl: OrbitalVelocity, platformEcl: platform,
                         munition: munition);

            platform += OrbitalVelocity * dt;
            offsets.Add(round.OffsetFromPlatform);
        }

        return offsets;
    }

    [Fact]
    public void TheOffsetDoesNotDriftAlongTheOrbitalDirection()
    {
        // The round was fired straight up and nothing pushes it sideways, so its offset must
        // stay on the launch axis. Any component along the orbital direction is leaked common
        // motion.
        List<double3> offsets = FlyWithFrameTimes(Enumerable.Repeat(1.0 / 60.0, 120).ToList());

        foreach (double3 o in offsets)
            Assert.True(Math.Abs(o.Y) < 1.0,
                $"offset drifted {o.Y:F1} m along the orbital direction; it should stay on the launch axis");
    }

    [Fact]
    public void TheOffsetIsSmoothWhenFrameTimesJitter()
    {
        // Real frames are not evenly spaced. If the offset depends on the frame time, an
        // ordinary 16/20 ms alternation makes the round jump back and forth by
        // 29800 * 0.004 = ~120 m every frame - which is exactly what "super quick zigzag,
        // slightly randomized" looks like.
        var frameTimes = new List<double>();
        for (int i = 0; i < 120; i++) frameTimes.Add(i % 2 == 0 ? 0.016 : 0.020);

        List<double3> offsets = FlyWithFrameTimes(frameTimes);

        // Compare like with like: the climb is steady, so consecutive offsets should differ by
        // roughly the distance flown in that frame, never by hundreds of metres.
        for (int i = 1; i < offsets.Count; i++)
        {
            double step = Vec.Len(offsets[i] - offsets[i - 1]);
            Assert.True(step < 50.0,
                $"frame {i}: offset moved {step:F0} m in one frame; the round is not flying that fast");
        }
    }

    [Fact]
    public void TheDrawnOffsetDoesNotDependOnTheFrameTime()
    {
        // The property that makes a zigzag impossible, stated directly rather than inferred from
        // a simulated flight.
        //
        // Two rounds in identical states, told the same platform position, stepped by different
        // frame times. Whatever else differs afterwards, the offset the renderer uses must not:
        // it is a statement about where the round is relative to the platform *now*, and now is
        // the same for both.
        //
        // Any version that measures the offset after stepping fails this, because it then has to
        // reconcile a round at the end of the step with a platform at the start - and every way
        // of doing that multiplies dt by a ~29.8 km/s ecliptic velocity. A 4 ms difference is
        // 119 m. That is the zigzag, and no simulated flight in this file caught it, because
        // over successive frames the error telescopes and cancels.
        MunitionProfile munition = Munition();
        double3 up = new(1, 0, 0);
        double3 velocity = OrbitalVelocity + up * 300.0;

        Interceptor Fresh() => new(PlatformStart + up * 500.0, velocity, null!, 1, PlatformStart);

        Interceptor shortFrame = Fresh();
        Interceptor longFrame = Fresh();

        shortFrame.Update(0.016, null, double3.Zero, OrbitalVelocity, PlatformStart, munition);
        longFrame.Update(0.020, null, double3.Zero, OrbitalVelocity, PlatformStart, munition);

        // A longer frame does mean the round genuinely flew further, so the two are not expected
        // to match exactly: at ~300 m/s of local speed, 4 ms is 1.2 m of real travel. What must
        // not appear is the *ecliptic* scale. The same 4 ms against 29.8 km/s is 119 m, and any
        // expression that subtracts ecliptic positions a frame apart leaks exactly that.
        //
        // So the threshold sits deliberately between the two: loose enough for real local
        // motion, an order of magnitude below anything carrying the platform's orbital velocity.
        double difference = Vec.Len(shortFrame.OffsetFromPlatform - longFrame.OffsetFromPlatform);
        Assert.True(difference < 10.0,
            $"a 4 ms difference in frame time moved the drawn offset by {difference:F1} m — "
            + "that is ecliptic motion leaking in, not the round flying");
    }

    [Fact]
    public void TheDrawnOffsetDoesNotDependOnWhenThePlatformWasSampled()
    {
        // The property that ends this whole class of bug, and the only one that discriminates.
        //
        // Whether the platform position handed to Update is from the start of the step or the
        // end of it is a question about KSA's frame ordering that took four attempts and three
        // broken builds to not answer. Every test in this file assumes one of the two, so none
        // of them can catch a version that assumes the other — which is how a 500 m displacement
        // and two zigzags each got in front of a player.
        //
        // Accumulating travel through the local frame makes the question irrelevant: the drawn
        // offset is the launch point plus an integral of local velocity, and the platform
        // position passed each frame does not enter into it. So feed one round a platform
        // position that is wrong by half a kilometre every frame, and nothing should move.
        MunitionProfile munition = Munition();
        double3 up = new(1, 0, 0);
        double3 velocity = OrbitalVelocity + up * 300.0;

        var honest = new Interceptor(PlatformStart + up * 3.0, velocity, null!, 1, PlatformStart);
        var misled = new Interceptor(PlatformStart + up * 3.0, velocity, null!, 1, PlatformStart);

        double3 platform = PlatformStart;
        for (int frame = 0; frame < 60; frame++)
        {
            double dt = frame % 2 == 0 ? 0.016 : 0.020;

            honest.Update(dt, null, double3.Zero, OrbitalVelocity, platform, munition);

            // A whole step of ecliptic motion out, alternating - the exact error every previous
            // arrangement of this could be wrong by.
            double3 wrong = platform + OrbitalVelocity * (frame % 2 == 0 ? dt : -dt);
            misled.Update(dt, null, double3.Zero, OrbitalVelocity, wrong, munition);

            platform += OrbitalVelocity * dt;
        }

        double difference = Vec.Len(honest.OffsetFromPlatform - misled.OffsetFromPlatform);
        Assert.Equal(0.0, difference, 6);
    }

    [Fact]
    public void TravelSinceLaunchStartsAtZeroAndGrowsSmoothly()
    {
        // TravelSinceLaunch is what places the subpart body. It is measured from LaunchOffset,
        // so the two must use the same convention - if the constructor and Update disagree
        // about which instant the platform is sampled at, every round begins life displaced by
        // a frame of orbital motion.
        var frameTimes = new List<double>();
        for (int i = 0; i < 60; i++) frameTimes.Add(i % 2 == 0 ? 0.016 : 0.020);

        MunitionProfile munition = Munition();
        double3 platform = PlatformStart;
        double3 up = new(1, 0, 0);

        var round = new Interceptor(
            positionEcl: platform + up * 3.0,
            velocityEcl: OrbitalVelocity + up * munition.LaunchSpeed,
            target: null!, tube: 1, platformEcl: platform);

        Assert.Equal(0.0, Vec.Len(round.TravelSinceLaunch), 9);

        double previous = 0.0;
        foreach (double dt in frameTimes)
        {
            round.Update(dt, null, double3.Zero, OrbitalVelocity, platform, munition);
            platform += OrbitalVelocity * dt;

            double travelled = Vec.Len(round.TravelSinceLaunch);
            Assert.True(travelled >= previous - 1e-6,
                $"travel went backwards: {previous:F1} m then {travelled:F1} m");
            Assert.True(travelled - previous < 50.0,
                $"travel jumped {travelled - previous:F0} m in one frame");
            previous = travelled;
        }
    }
}
