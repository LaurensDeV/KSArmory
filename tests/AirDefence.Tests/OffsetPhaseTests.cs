using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// The drawn offset, tested against the phase relationship the <em>engine</em> actually has —
/// which is not the one the older tests in this directory assume.
///
/// <para><b>What was measured.</b> A probe in the frame hook, where both values are produced,
/// compared the round's integration against the platform's sample every update for thousands of
/// frames. Writing the update index as k, with Q the platform sample and P the round's position
/// after its step, it violated</para>
///
/// <code>
/// ( P(k) - P(k-1) ) - ( Q(k) - Q(k-1) )  ==  localVelocity * dt(k)
/// </code>
///
/// <para>by more than 5 m on <b>2 frames out of several thousand</b>. So the platform sample
/// arriving at frame k has advanced by <c>v * dt(k)</c> — the step used by <em>that same</em>
/// update, not the one before it. That is also what <c>Universe.GetLastSimStep()</c> means: at
/// frame k it reports the step the engine has just finished applying, which is precisely the
/// interval the platform moved across since the previous sample.</para>
///
/// <para><b>Why this file exists separately.</b> <see cref="RoundOffsetStabilityTests"/> advances
/// the platform <em>after</em> the update, encoding <c>Q(k+1) - Q(k) == v * dt(k)</c> — the
/// opposite phase. With a constant step the two are identical, which is why that suite passed
/// against implementations that visibly jumped in game: its own frame times were the only thing
/// varying, and it advanced the platform by exactly the <c>v*dt</c> it passed in, so the error
/// cancelled. These tests vary the step the way changing simulation speed does, which is when the
/// two phases separate.</para>
///
/// <para>Every assertion here was checked to <b>fail</b> against the two previous implementations
/// before being kept. A regression test that passes against the old code is worth nothing, and
/// this repository has already produced one.</para>
/// </summary>
public class OffsetPhaseTests
{
    // Earth-like: an enormous ecliptic position with the ~29.8 km/s of common motion that turns
    // sub-millisecond bookkeeping errors into hundreds of metres on screen.
    private static readonly double3 PlatformStart = new(1.4959e11, 0, 0);
    private static readonly double3 OrbitalVelocity = new(0, 29800, 0);

    private static MunitionProfile Munition() => Arsenal.MunitionNamed(Arsenal.PantsirS1.Munition);

    /// <summary>
    /// Flies a round across the given steps, advancing the platform to its sample for each frame
    /// <em>before</em> the update that uses it — the phase measured in game — and returns the
    /// offset the renderer would have used on each frame.
    /// </summary>
    private static List<double3> Fly(IReadOnlyList<double> steps)
    {
        MunitionProfile munition = Munition();

        double3 platform = PlatformStart;
        double3 up = new(1, 0, 0);

        var round = new Interceptor(
            positionEcl: platform + up * 3.0,
            velocityEcl: OrbitalVelocity + up * munition.LaunchSpeed,
            target: null!,
            tube: 1,
            platformEcl: platform,
            frameVelocityEcl: OrbitalVelocity);

        var offsets = new List<double3>();

        foreach (double dt in steps)
        {
            // The platform sample for frame k, having advanced by exactly the step this frame's
            // update is about to be given. Reversing these two lines reproduces the older suite's
            // assumption, and every assertion below then passes against code that jumps in game.
            platform += OrbitalVelocity * dt;

            round.Update(dt, target: null, gravity: double3.Zero,
                         frameVelocityEcl: OrbitalVelocity, platformEcl: platform,
                         munition: munition);

            offsets.Add(round.OffsetFromPlatform);
        }

        return offsets;
    }

    /// <summary>Largest single-frame movement of the drawn offset, in metres.</summary>
    private static double LargestStep(List<double3> offsets)
    {
        double worst = 0.0;
        for (int i = 1; i < offsets.Count; i++)
            worst = Math.Max(worst, Vec.Len(offsets[i] - offsets[i - 1]));
        return worst;
    }

    [Fact]
    public void AnOrdinaryFrameTimeWobbleDoesNotMoveTheDrawnOffset()
    {
        // Real frames are not evenly spaced: 16/20 ms alternation is unremarkable. A form that
        // pairs the round's displacement from one frame with the platform's from another leaks
        // 29800 * 0.004 = ~119 m every frame, which is the fast zigzag reported from play.
        //
        // The round climbs at a few hundred m/s, so a legitimate frame moves it well under 20 m.
        var steps = new List<double>();
        for (int i = 0; i < 120; i++) steps.Add(i % 2 == 0 ? 0.016 : 0.020);

        double worst = LargestStep(Fly(steps));

        Assert.True(worst < 50.0,
            $"the drawn offset moved {worst:F0} m in a single frame on a 4 ms wobble; "
            + "at 29.8 km/s that is ecliptic motion leaking in, not the round flying");
    }

    [Fact]
    public void ChangingSimulationSpeedDoesNotThrowTheRoundAcrossTheSky()
    {
        // The reported symptom: "the rockets jump around if i change the time step". Dropping
        // from 1x to 0.25x takes the engine's step from ~22 ms to ~5.6 ms in one frame, and any
        // form carrying a `v * dstep` term displaces the round by 29800 * 0.0169 = ~500 m on that
        // single frame. Measured in game at 507.37 m before this was fixed.
        var steps = new List<double>();
        for (int i = 0; i < 40; i++) steps.Add(0.0225);   // 1x
        for (int i = 0; i < 40; i++) steps.Add(0.0056);   // 0.25x
        for (int i = 0; i < 40; i++) steps.Add(0.0002);   // 0.01x
        for (int i = 0; i < 40; i++) steps.Add(0.0225);   // and back

        double worst = LargestStep(Fly(steps));

        Assert.True(worst < 50.0,
            $"changing the simulation speed moved the drawn offset {worst:F0} m in one frame");
    }

    [Fact]
    public void TheOffsetStaysOnTheLaunchAxis()
    {
        // Fired straight up with nothing pushing it sideways, so any component along the orbital
        // direction is leaked common motion rather than flight. This is the steady-state check
        // that the two above cannot make: they would both pass on a form that is smoothly and
        // consistently wrong.
        var steps = new List<double>();
        for (int i = 0; i < 120; i++) steps.Add(i % 2 == 0 ? 0.016 : 0.020);

        foreach (double3 offset in Fly(steps))
            Assert.True(Math.Abs(offset.Y) < 1.0,
                $"the offset drifted {offset.Y:F1} m along the orbital direction");
    }
}
