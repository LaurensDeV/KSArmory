using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// The chase transition's two ends have to be measured at one instant.
///
/// <para>A from-end built from a platform sample taken before the round was stepped, against a
/// to-end from the round's position after it, leaks one step of the planet's motion — 715 m on a
/// 24 ms frame against 286 m on a 9 ms one, alternating with the display's frame pacing. The camera
/// then reverses its vertical direction every frame by ±270 m while the intended path climbs
/// steadily.</para>
/// </summary>
public class ChaseBlendFrameTests
{
    // 29.8 km/s of ecliptic motion, oblique to the site's local vertical — which is the general
    // case and the one that matters. Perpendicular to it the leak has no vertical component at
    // all, and a test written that way measures nothing while looking like it passes.
    private static readonly double3 Carrier = new(29_800 * 0.6, 29_800 * 0.8, 0);
    private static readonly double3 Up = new(0, 1, 0);

    /// <summary>
    /// The blend as <c>ChaseCamera</c> runs it: every input an offset measured against the same
    /// platform sample as the round.
    /// </summary>
    private static double3 BlendedOffset(double3 fromOffset, double3 offsetFromPlatform,
                                         double3 eye, double blend)
    {
        Assert.True(ChaseView.TryBlend(fromOffset - offsetFromPlatform,
                                       fromOffset - offsetFromPlatform + Up * 8000.0,
                                       eye, eye + Up * 8000.0,
                                       Up, blend,
                                       out double3 blended, out _));
        return blended;
    }

    /// <summary>
    /// The cross-instant form: absolute positions, with the platform read a step earlier than the
    /// round. Written out so the two can be run on identical inputs.
    /// </summary>
    private static double3 BlendedAcrossInstants(double3 platformBeforeStep, double3 fromOffset,
                                                 double3 roundAfterStep, double3 eye, double blend)
    {
        Assert.True(ChaseView.TryBlend(platformBeforeStep + fromOffset,
                                       platformBeforeStep + fromOffset + Up * 8000.0,
                                       roundAfterStep + eye, roundAfterStep + eye + Up * 8000.0,
                                       Up, blend,
                                       out double3 blended, out _));
        return blended - roundAfterStep;
    }

    /// <summary>
    /// A round climbing away from a launcher, flown with the frame pacing that was measured: the
    /// step alternates between one and three intervals of a 120 Hz display.
    /// </summary>
    [Fact]
    public void TheCameraDoesNotReverseWhenTheStepAlternates()
    {
        const double shortStep = 1.0 / 120.0;
        const double longStep = 3.0 / 120.0;

        double3 platformEcl = new(1.5e11, 6.371e6, 0);
        double3 roundEcl = platformEcl + Up * 100.0;
        double3 climb = Up * 700.0;

        double3 fromOffset = new(-30.0, 12.0, 4.0);

        double worstFixed = 0.0;
        double worstShipped = 0.0;

        double3 lastFixed = default;
        double3 lastShipped = default;

        for (int frame = 0; frame < 40; frame++)
        {
            double dt = frame % 2 == 0 ? longStep : shortStep;
            double blend = Math.Min(1.0, 0.02 + frame * 0.012);

            // The platform as SampleWorld read it: before this frame's step.
            double3 platformBefore = platformEcl;

            // Then the world moves and the round is integrated.
            platformEcl += Carrier * dt;
            roundEcl += (Carrier + climb) * dt;

            // What Interceptor publishes: the round against THIS frame's platform sample.
            double3 offsetFromPlatform = roundEcl - platformEcl;

            // The settled pose the blend is heading for.
            double3 eye = Up * -26.0;

            double3 nowFixed = BlendedOffset(fromOffset, offsetFromPlatform, eye, blend);
            double3 nowShipped = BlendedAcrossInstants(platformBefore, fromOffset, roundEcl, eye, blend);

            if (frame > 2)
            {
                worstFixed = Math.Max(worstFixed, Math.Abs(Vec.Dot(nowFixed - lastFixed, Up)));
                worstShipped = Math.Max(worstShipped, Math.Abs(Vec.Dot(nowShipped - lastShipped, Up)));
            }

            lastFixed = nowFixed;
            lastShipped = nowShipped;
        }

        // The camera's offset from the round should creep, not lurch: the whole transition is a
        // few hundred metres and it has forty frames to cover it.
        Assert.True(worstFixed < 20.0,
                    $"the fixed blend still jumps {worstFixed:F1} m in one frame");

        // And the cross-instant form must be shown to fail on the same inputs, or this proves
        // nothing.
        Assert.True(worstShipped > 100.0,
                    $"the cross-instant blend only moved {worstShipped:F1} m — the test is not "
                    + "reproducing the bug it was written for");
    }

    /// <summary>
    /// One frame of a round climbing away from a launcher, with <paramref name="carrier"/> added to
    /// everything in the scene: the platform as <c>SampleWorld</c> read it before the step, the
    /// round's position after it, and what <c>Interceptor</c> publishes — the round against the
    /// platform sample from its own frame.
    /// </summary>
    private static (double3 PlatformBefore, double3 RoundAfter, double3 OffsetFromPlatform)
        Frame(double3 carrier)
    {
        const double dt = 1.0 / 60.0;

        double3 platformEcl = new(1.5e11, 6.371e6, 0);
        double3 roundEcl = platformEcl + Up * 400.0;

        double3 platformBefore = platformEcl;

        platformEcl += carrier * dt;
        roundEcl += (carrier + (Up * 700.0)) * dt;

        return (platformBefore, roundEcl, roundEcl - platformEcl);
    }

    /// <summary>
    /// The frame contract, stated directly: add any common velocity to the launcher, the round and
    /// the whole scene, and the camera's offset from the round must not move.
    /// </summary>
    [Fact]
    public void SharedMotionDoesNotReachTheBlend()
    {
        double3 fromOffset = new(-30.0, 12.0, 4.0);
        double3 eye = Up * -26.0;

        var still = Frame(default);
        var carried = Frame(Carrier);

        double3 stillOffset = BlendedOffset(fromOffset, still.OffsetFromPlatform, eye, 0.4);
        double3 carriedOffset = BlendedOffset(fromOffset, carried.OffsetFromPlatform, eye, 0.4);

        // Not exactly zero, and it cannot be: the carrier goes onto an ecliptic position and comes
        // off again, and a double holds 1.5e11 m to about 30 µm. What must not survive is metres.
        double leaked = Vec.Len(stillOffset - carriedOffset);

        Assert.True(leaked < 1e-3, $"the carrier reached the blend: {leaked:F4} m");

        // And it has to be shown leaking through the cross-instant form on the same scene. Without
        // this the assertion above also passes for a calculation that never had a carrier in it to
        // cancel, which is what it was doing when this test was written.
        double3 stillAcross =
            BlendedAcrossInstants(still.PlatformBefore, fromOffset, still.RoundAfter, eye, 0.4);
        double3 carriedAcross =
            BlendedAcrossInstants(carried.PlatformBefore, fromOffset, carried.RoundAfter, eye, 0.4);

        double across = Vec.Len(stillAcross - carriedAcross);

        Assert.True(across > 100.0,
                    $"the cross-instant form only moved {across:F1} m — the test is not "
                    + "reproducing the leak it was written for");
    }
}
