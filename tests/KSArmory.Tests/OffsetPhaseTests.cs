using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The drawn offset, tested against the phase relationship the engine actually has.
///
/// <para>The platform sample arriving at update k has advanced by <c>v * dt(k)</c> — the step used
/// by <em>that same</em> update, not the one before it. Over thousands of frames this holds to
/// within 5 m on all but two of them:</para>
///
/// <code>
/// ( P(k) - P(k-1) ) - ( Q(k) - Q(k-1) )  ==  localVelocity * dt(k)
/// </code>
///
/// <para>It is also what <c>Universe.GetLastSimStep()</c> means: at frame k it reports the step
/// just finished, which is the interval the platform moved across.</para>
///
/// <para>These tests <b>vary the step</b> the way changing simulation speed does. At a constant
/// step this phase and its opposite are indistinguishable, so a suite that never varies it cannot
/// see the difference at all.</para>
/// </summary>
public class OffsetPhaseTests
{
    // Earth-like: an enormous ecliptic position with the ~29.8 km/s of common motion that turns
    // sub-millisecond bookkeeping errors into hundreds of metres on screen.
    private static readonly double3 PlatformStart = new(1.4959e11, 0, 0);
    private static readonly double3 OrbitalVelocity = new(0, 29800, 0);

    private static MunitionProfile Munition() => Catalogue.MunitionNamed(BuiltIns.PantsirS1.Munition);

    /// <summary>
    /// Flies a round across the given steps, advancing the platform to its sample for each frame
    /// <em>before</em> the update that uses it — the phase the engine has — and returns the offset
    /// the renderer would have used on each frame.
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
            frameVelocityEcl: OrbitalVelocity) { Munition = BuiltIns.Missile57E6 };

        var offsets = new List<double3>();

        foreach (double dt in steps)
        {
            // The platform sample for frame k, having advanced by exactly the step this frame's
            // update is about to be given. Reversing these two lines encodes the opposite phase,
            // and every assertion below then passes against code that jumps in game.
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
        // 29800 * 0.004 = ~119 m every frame, alternating in sign: a fast lateral zigzag.
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
        // Dropping from 1x to 0.25x takes the engine's step from ~22 ms to ~5.6 ms in one frame,
        // and any form carrying a `v * dstep` term displaces the round by 29800 * 0.0169 = ~500 m
        // on that single frame -- measured at 507.37 m, which is a round jumping across the sky.
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
