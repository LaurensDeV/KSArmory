using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// A round's drawn offset must be smooth from frame to frame, whatever the frame times.
///
/// <para>Everything visible about a round — the tracer and the subpart body — is placed from
/// <see cref="Interceptor.OffsetFromPlatform"/>, so if that quantity is not a smooth function of
/// time the round jitters however correct its trajectory is.</para>
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
            platformEcl: platform,
            frameVelocityEcl: OrbitalVelocity);

        var offsets = new List<double3>();

        foreach (double dt in frameTimes)
        {
            // The platform sample for this frame, advanced BEFORE the update that uses it - the
            // phase measured in the game's frame hook. See the note in OffsetPhaseTests.
            platform += OrbitalVelocity * dt;

            // No target and no gravity: the round's motion in the local frame is a clean climb,
            // so any wobble in the reported offset comes from the bookkeeping, not the physics.
            round.Update(dt, target: null, gravity: double3.Zero,
                         frameVelocityEcl: OrbitalVelocity, platformEcl: platform,
                         munition: munition);

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

        Interceptor Fresh() => new(PlatformStart + up * 500.0, velocity, null!, 1, PlatformStart,
                                   OrbitalVelocity);

        Interceptor shortFrame = Fresh();
        Interceptor longFrame = Fresh();

        // Each is given the platform sample belonging to ITS OWN frame - the platform has moved
        // by v * dt by the time an update of length dt runs. Handing both the same sample is the
        // fiction that made the older forms look dt-independent: it holds the platform still
        // while the round flies, so the ecliptic motion has nothing to cancel against.
        shortFrame.Update(0.016, null, double3.Zero, OrbitalVelocity,
                          PlatformStart + OrbitalVelocity * 0.016, munition);
        longFrame.Update(0.020, null, double3.Zero, OrbitalVelocity,
                         PlatformStart + OrbitalVelocity * 0.020, munition);

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

    // Removed: TheDrawnOffsetDoesNotDependOnWhenThePlatformWasSampled.
    //
    // It asserted that the drawn offset must be independent of which instant the platform was
    // sampled at, which is true only of an accumulated offset. The offset is derived from the
    // round's own position instead, and deliberately *is* paired with a platform sample — that
    // pairing is what DrawAnchor exists to reconcile, and the build that draws dead centre in
    // game is the one that does it this way.
    //
    // The test was written the same day as the accumulation it was defending, and both were
    // wrong. Keeping it would have forced the design the game rejects.

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
            target: null!, tube: 1, platformEcl: platform, frameVelocityEcl: OrbitalVelocity);

        Assert.Equal(0.0, Vec.Len(round.TravelSinceLaunch), 9);

        double previous = 0.0;
        foreach (double dt in frameTimes)
        {
            platform += OrbitalVelocity * dt;
            round.Update(dt, null, double3.Zero, OrbitalVelocity, platform, munition);

            double travelled = Vec.Len(round.TravelSinceLaunch);
            Assert.True(travelled >= previous - 1e-6,
                $"travel went backwards: {previous:F1} m then {travelled:F1} m");
            Assert.True(travelled - previous < 50.0,
                $"travel jumped {travelled - previous:F0} m in one frame");
            previous = travelled;
        }
    }
}
