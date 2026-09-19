namespace KSArmory;

/// <summary>
/// Hands out a simulation step once and only once.
///
/// <para>The engine reports "the last step applied", not "a step since you last asked", so asking
/// twice without it having stepped returns the same step. Integrating one twice adds motion the
/// world never made, and because it lands in an integrated quantity the error compounds rather
/// than staying fixed.</para>
///
/// <para>Deduplicated on the step's own end time rather than by differencing a clock, so the value
/// returned is still the step the engine applied and cannot be a phase out from the world.</para>
///
/// <para>Generic over the timestamp so it can be tested without KSA's <c>UniverseTime</c>. Equality
/// is the only thing it needs from one.</para>
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
    /// <para><paramref name="spanSeconds"/> measures from the step last integrated through to
    /// <paramref name="nextTime"/>. Normally that is the step itself and the answer is unchanged.
    /// When the caller misses a frame entirely the engine reports only the <em>last</em> step, so
    /// <paramref name="delta"/> alone leaves the skipped one unintegrated — and the world advanced
    /// across it regardless. The whole deficit lands in the drawn offset, at 29.8 km/s of ecliptic
    /// motion: one missed 22 ms frame is 656 m, measured in flight.</para>
    ///
    /// <para>Still the engine's own step boundaries rather than a clock measured around the
    /// caller, so it cannot be a phase out from the world. A span too long to integrate is not
    /// this type's problem — see <see cref="SimClock"/>.</para>
    ///
    /// <para><b>The span is also what the planets are placed on, so it wins within rounding.</b>
    /// KSA advances its clock by the step rounded to whole nanoseconds and evaluates every celestial
    /// there, while <paramref name="delta"/> is the step before rounding. Taking whichever is longer
    /// runs a round 0.125 ns a frame ahead of the ground at any warp whose step is not a whole
    /// nanosecond — 3.7 µm a frame at 29.8 km/s, and ~20 mm of miss over a warped coast
    /// (<c>docs/ACCURACY-PLAN.md</c> 3el).</para>
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

    /// <summary>True once a step has been consumed and not reset.</summary>
    public bool HasIntegrated => _hasIntegrated;
}
