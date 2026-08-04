namespace AirDefence;

/// <summary>
/// Hands out a simulation step once and only once.
///
/// <para>The engine answers "the last step applied", not "a step has happened since you last
/// asked". Ask twice without it having stepped and it reports the same step twice — and
/// integrating a step twice puts real, permanent motion into a round that the world never made.
/// Reported from play as: pause, select 0.05x, pause again, repeat, and the round walks further off
/// with every cycle.</para>
///
/// <para><b>Accumulation is the tell.</b> A mismatched epoch produces a fixed offset; only
/// re-integrating a consumed step compounds, because it lands in an integrated quantity rather than
/// a derived one.</para>
///
/// <para>Deduplicated on the step's own end time rather than by differencing a clock, which keeps
/// the property <see cref="SimClock"/> exists to protect: the value returned is still the step the
/// engine actually applied, so it cannot be a phase out from the world. It just cannot now be
/// applied twice.</para>
///
/// <para>Generic over the timestamp so it can be tested without KSA's <c>SimTime</c>. The only
/// thing it needs from one is equality.</para>
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
    /// Deliberately does not filter non-finite or negative deltas. Whether a step is one the
    /// simulation can faithfully integrate is a different question with a different answer — see
    /// <see cref="SimClock"/> — and conflating the two here would hide a bad step behind a
    /// duplicate one.
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
