namespace KSArmory;

/// <summary>
/// The simulated step, evened out — for the one job that wants a smooth clock rather than a
/// faithful one.
///
/// <para>KSA's step is <c>dtPlayer × achievedFraction × simSpeed</c>, and on a 120 Hz screen at a
/// nominal 60 fps <c>dtPlayer</c> alternates 8.33 ms and 25.0 ms. Anything integrating the world
/// takes it as it comes; a cosmetic ease on it lurches three times as far on alternate frames.
/// CLAUDE.md, "The transition runs on an evened-out step".</para>
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

        // Seeded rather than eased in from zero, or a transition crawls for its first few frames.
        _seconds = _seconds > 0.0 ? _seconds + (stepSeconds - _seconds) * Weight : stepSeconds;

        return _seconds;
    }

    /// <summary>Forgets the running average, for a fresh transition.</summary>
    public void Reset() => _seconds = 0.0;
}
