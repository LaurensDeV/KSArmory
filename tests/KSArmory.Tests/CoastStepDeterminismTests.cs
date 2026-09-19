using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The warhead's coast step must be decided by its profile, not by whether one frame overran.
///
/// <para><see cref="WarpPolicy"/> acts only once the step exceeds what the round asked for, so a
/// preferred step above the coast's own step is never reached: the world is left alone and the
/// round coasts at whatever the frame rate gives. <see cref="WarpLatchScatterTests"/> prices what
/// that costs when a stray frame does trip it and the decision then latches for the whole flight.
/// This file pins the other half — that on a steady frame stream the hold engages at all.</para>
///
/// <para>The arithmetic both rest on: when the policy acts it asks for
/// <c>currentSpeed * PreferredStep * Margin / dtSim</c>, which lands the world on a step of exactly
/// <c>Margin * PreferredStep</c> however fast the frames are. So the step a round receives is
/// <b>0.6 times the one it names</b>, and naming one the coast never exceeds receives nothing.</para>
/// </summary>
public class CoastStepDeterminismTests(ITestOutputHelper Out)
{
    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    /// <summary>
    /// The steady coast frame the traced flights ran at — median 23.1-24.5 ms over four arms.
    /// The fastest of them is what a preferred step has to get under to engage every time.
    /// </summary>
    private const double FastestSteadyFrame = 0.0231;

    /// <summary>
    /// On a steady coast the hold engages, so the step is the profile's choice and not an accident.
    ///
    /// <para>Fails against a preferred step at or above the coast's own step, which is the state
    /// <c>docs/MIRV-NEXT.md</c> item 7e measured: 38 flown shots split by whether a single frame
    /// crossed the threshold, and the whole rest of each coast calibrated off that one frame.</para>
    /// </summary>
    [Fact]
    public void TheHoldEngagesOnACoastThatNeverOverruns()
    {
        double steadyStep = FastestSteadyFrame * DeorbitShot.ScenarioWarp;

        WarpPolicy policy = new();
        WarpDecision d = policy.Decide(steadyStep, DeorbitShot.ScenarioWarp,
                                       roundsInFlight: true, enabled: true, Warhead.PreferredStep);

        Out.WriteLine($"steady coast step {steadyStep * 1000.0:F1} ms at {DeorbitShot.ScenarioWarp:F0}x, "
                      + $"preferred {Warhead.PreferredStep * 1000.0:F0} ms -> {d.Action} {d.Speed:F2}x");

        Assert.True(d.Action == WarpAction.Slow,
                    $"the coast runs at {steadyStep * 1000.0:F1} ms and the warhead asks for "
                    + $"{Warhead.PreferredStep * 1000.0:F0} ms, so nothing is held and the step is "
                    + "whatever the frame rate gave -- see docs/MIRV-NEXT.md item 7e");
    }

    /// <summary>
    /// And it settles at the step the profile names, rather than hunting.
    ///
    /// <para>Once held, the step is inside the preferred one, so the policy stops asking and the
    /// speed stays where it was put. That is what makes the received step a property of the profile
    /// rather than of the frame that happened to trip it.</para>
    /// </summary>
    [Fact]
    public void TheHeldCoastIsSixTenthsOfWhatTheRoundAsksFor()
    {
        double steadyStep = FastestSteadyFrame * DeorbitShot.ScenarioWarp;

        WarpPolicy policy = new();
        WarpDecision first = policy.Decide(steadyStep, DeorbitShot.ScenarioWarp,
                                           roundsInFlight: true, enabled: true, Warhead.PreferredStep);
        Assert.Equal(WarpAction.Slow, first.Action);

        double held = first.Speed * FastestSteadyFrame;
        Out.WriteLine($"held {first.Speed:F2}x -> coast step {held * 1000.0:F1} ms");

        Assert.Equal(WarpPolicy.Margin * Warhead.PreferredStep, held, 4);
    }
}
