namespace KSArmory;

/// <summary>
/// Hands out a simulation step once and only once.
///
/// <para>The engine reports the last step applied, so asking twice without a step between returns
/// the same one. Deduplicated on the step's end time, so the value is still the engine's own step.</para>
///
/// <para>Generic over the timestamp so it can be tested without KSA's <c>UniverseTime</c>.</para>
/// </summary>
public sealed class StepGate<T> where T : struct, IEquatable<T>
{
    /// <summary>
    /// How far a span may fall short of the reported step and still be taken: KSA rounds its clock
    /// to the nearest nanosecond, so the two differ by up to half of one on every frame.
    /// </summary>
    public const double RoundingSeconds = 1e-9;

    private T _integratedThrough;
    private bool _hasIntegrated;

    /// <summary>
    /// <paramref name="delta"/> the first time <paramref name="nextTime"/> is seen, zero on any
    /// repeat of it.
    /// </summary>
    /// <remarks>
    /// Does not filter non-finite or negative deltas. Whether a step is one the simulation can
    /// faithfully integrate is a separate question with a separate answer — see
    /// <see cref="SimClock"/> — and merging the two would hide a bad step behind a duplicate one.
    /// </remarks>
    public double Consume(T nextTime, double delta) => Consume(nextTime, delta, null);

    /// <summary>
    /// As <see cref="Consume(T, double)"/>, but closing any gap the caller was not running across.
    ///
    /// <para><paramref name="spanSeconds"/> runs from the step last integrated through to
    /// <paramref name="nextTime"/>. When the caller misses a frame the engine reports only the last
    /// step, so <paramref name="delta"/> alone loses the skipped one: 656 m for a missed 22 ms frame
    /// at 29.8 km/s, measured in flight.</para>
    ///
    /// <para><b>The span wins within rounding</b>, because KSA places the planets on its clock rounded
    /// to whole nanoseconds while <paramref name="delta"/> is unrounded; taking the longer ran a round
    /// 0.125 ns a frame ahead of the ground, ~20 mm over a warped coast (<c>docs/ACCURACY-PLAN.md</c> 3el).</para>
    /// </summary>
    public double Consume(T nextTime, double delta, Func<T, T, double>? spanSeconds)
    {
        if (_hasIntegrated && nextTime.Equals(_integratedThrough)) return 0.0;

        double taken = delta;

        // A span shorter than the step by more than rounding means the clock disagrees with the
        // step it just handed over -- a save loaded mid-session jumps it backwards -- and the step
        // is then the thing that moved the world.
        if (_hasIntegrated && spanSeconds is not null)
        {
            double span = spanSeconds(_integratedThrough, nextTime);
            if (double.IsFinite(span) && span > taken - RoundingSeconds) taken = span;
        }

        _integratedThrough = nextTime;
        _hasIntegrated = true;
        return taken;
    }

    /// <summary>
    /// Forgets which step was last integrated, so the next one is taken whatever its timestamp.
    /// For unload and scene changes, where the clock itself may restart.
    /// </summary>
    public void Reset() => _hasIntegrated = false;

    public bool HasIntegrated => _hasIntegrated;
}
