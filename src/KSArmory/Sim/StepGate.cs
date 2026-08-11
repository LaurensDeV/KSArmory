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
    public double Consume(T nextTime, double delta)
    {
        if (_hasIntegrated && nextTime.Equals(_integratedThrough)) return 0.0;

        _integratedThrough = nextTime;
        _hasIntegrated = true;
        return delta;
    }

    /// <summary>
    /// Forgets which step was last integrated, so the next one is taken whatever its timestamp.
    /// For unload and scene changes, where the clock itself may restart.
    /// </summary>
    public void Reset() => _hasIntegrated = false;

    /// <summary>True once a step has been consumed and not reset.</summary>
    public bool HasIntegrated => _hasIntegrated;
}
