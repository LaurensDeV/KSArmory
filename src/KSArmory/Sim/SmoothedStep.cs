namespace KSArmory;

/// <summary>
/// The simulated step, evened out — for the one job that wants a smooth clock rather than a
/// faithful one.
///
/// <para>KSA's step is not a clock, it is a report: <c>dtPlayer × achievedFraction × simSpeed</c>,
/// and <c>dtPlayer</c> carries the display's frame pacing. On a 120 Hz screen at a nominal 60 fps
/// that beats 1-3-1-3 — measured in flight as an alternation between <b>8.33 ms and 25.0 ms</b>,
/// exactly one and three vsync intervals. Anything that integrates the world must use it as it
/// comes, and every other consumer in this mod does.</para>
///
/// <para>A cosmetic ease is the exception. Advancing one by the raw step moves it three times as
/// far on alternate frames, which is a camera lurching along its path at 60 Hz — and because a
/// camera translation displaces an object by roughly <c>1/range</c>, it lands on whatever is
/// nearest and moving slowest.</para>
///
/// <para>Both properties that make the step the right input survive the smoothing, because it is
/// still the step being averaged: a paused world contributes nothing and the ease holds still, and
/// a warped one scales the whole average so slow motion is still slow.</para>
/// </summary>
internal sealed class SmoothedStep
{
    /// <summary>
    /// How much of each new step to take. A 3:1 alternation comes out at about ±4% of its mean —
    /// <c>w / (2 - w)</c> of the input swing — which is 12 times steadier and still follows a real
    /// change in frame rate within a few frames.
    /// </summary>
    public const double Weight = 0.15;

    private double _seconds;

    /// <summary>The evened-out step to advance by, given the one the engine just reported.</summary>
    public double Next(double stepSeconds)
    {
        // A paused or unusable step advances nothing and leaves the average alone, so what resumes
        // is what was running before rather than a decay towards zero.
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0.0) return 0.0;

        // Seeded rather than eased into from zero: a transition that started at a third of its
        // rate would crawl for its first few frames, which is the artefact this exists to remove.
        _seconds = _seconds > 0.0 ? _seconds + (stepSeconds - _seconds) * Weight : stepSeconds;

        return _seconds;
    }

    /// <summary>Forgets the running average, for a fresh transition.</summary>
    public void Reset() => _seconds = 0.0;
}
