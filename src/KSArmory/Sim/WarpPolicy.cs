namespace KSArmory;

/// <summary>What the frame hook should do about the world running faster than a round can fly.</summary>
internal enum WarpAction
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>Ask the world to run slower, at <see cref="WarpDecision.Speed"/>.</summary>
    Slow,

    /// <summary>Give the player their speed back — the air is clear.</summary>
    Restore,

    /// <summary>Slowing the world did not take. Give up on what is in the air.</summary>
    Abandon,
}

/// <summary>The action, the speed it needs, and something to say about it.</summary>
internal readonly record struct WarpDecision(WarpAction Action, double Speed, string Why)
{
    public static readonly WarpDecision Nothing = new(WarpAction.None, 0.0, "");
}

/// <summary>
/// Keeps the world slow enough that rounds in the air can actually be simulated.
///
/// <para>Past <see cref="Interceptor.MaxFaithfulStep"/> a round moves further per step than its
/// own fuse radius, so it can pass through a target without ever measuring a close approach.
/// Clamping the step instead — which is what shipped — leaves rounds advancing 0.32 s while the
/// world advances seconds, so they trail further behind every frame and the salvo reads as a
/// guidance failure. Measured at 600x: closest approach 124 km against 15-20 m unwarped.</para>
///
/// <para>So the world is slowed rather than the round being lied to, and only while something is
/// in the air. If the engine will not take the speed, there is nothing honest left to do and the
/// salvo is abandoned — a lost salvo the player is told about beats a silent 124 km miss.</para>
///
/// <para>No KSA types: the caller supplies the speed and applies the answer.</para>
/// </summary>
internal sealed class WarpPolicy
{
    /// <summary>
    /// Fraction of <see cref="Interceptor.MaxFaithfulStep"/> aimed at. The step is the frame time
    /// times the speed, and frame time moves, so asking for exactly the limit sits on the edge and
    /// overruns on the next slow frame.
    /// </summary>
    public const double Margin = 0.6;

    /// <summary>
    /// Consecutive overrunning frames, while already holding the speed down, before the salvo is
    /// abandoned. More than one because the speed write lands a frame later than the read.
    /// </summary>
    public const int AttemptsBeforeAbandon = 3;

    private double _restoreTo;
    private double _lastRequested;
    private int _attempts;

    /// <summary>True while the player's chosen speed is being held down.</summary>
    public bool Holding => _restoreTo > 0.0;

    /// <summary>The speed that will be given back, or zero when not holding.</summary>
    public double HeldSpeed => _restoreTo;

    /// <summary>
    /// Decides what the frame hook should do, given the step the engine just reported.
    /// </summary>
    /// <param name="dtSim">Simulated seconds in the step just taken.</param>
    /// <param name="currentSpeed">The world's timewarp factor right now.</param>
    /// <param name="roundsInFlight">Whether anything is airborne that has to be integrated.</param>
    /// <param name="enabled">The player's setting; false disables the whole mechanism.</param>
    public WarpDecision Decide(double dtSim, double currentSpeed, bool roundsInFlight, bool enabled)
    {
        if (!enabled) return Release(currentSpeed, "warp limiting turned off");
        if (!roundsInFlight) return Release(currentSpeed, "nothing in the air");

        if (!double.IsFinite(dtSim) || !double.IsFinite(currentSpeed) || currentSpeed <= 0.0)
        {
            return WarpDecision.Nothing;
        }

        if (dtSim <= Interceptor.MaxFaithfulStep)
        {
            _attempts = 0;
            return WarpDecision.Nothing;
        }

        // Self-calibrating: the frame time is dtSim/currentSpeed, so the speed that lands on the
        // target step needs no knowledge of the frame rate. Doing it this way also means a slow
        // frame is handled the same as a high warp, because to a round they are the same thing.
        double target = currentSpeed * (Interceptor.MaxFaithfulStep * Margin) / dtSim;
        if (!double.IsFinite(target) || target <= 0.0) return WarpDecision.Nothing;

        if (!Holding)
        {
            _restoreTo = currentSpeed;
            _lastRequested = target;
            _attempts = 1;
            return new WarpDecision(WarpAction.Slow, target,
                                    $"holding {currentSpeed:F0}x down to {target:F1}x while rounds fly");
        }

        // Still overrunning while already holding: the write is not taking effect.
        _attempts++;
        if (_attempts > AttemptsBeforeAbandon)
        {
            double was = _restoreTo;
            Clear();
            return new WarpDecision(WarpAction.Abandon, was,
                                    "the world will not run slow enough to simulate them");
        }

        _lastRequested = target;
        return new WarpDecision(WarpAction.Slow, target, "still overrunning; asking again");
    }

    // Gives the speed back, if it is still ours to give. A player who moved the speed themselves
    // while we held it has overridden us, and restoring would undo a deliberate choice.
    private WarpDecision Release(double currentSpeed, string why)
    {
        if (!Holding) return WarpDecision.Nothing;

        double to = _restoreTo;
        double requested = _lastRequested;
        Clear();

        // Only if the world is still sitting at what we asked for. A player who moved the speed
        // while we held it has overridden us, and restoring would undo a deliberate choice.
        // Compared as a ratio: SimSpeed rounds what it stores, so an exact match is not reachable.
        bool stillOurs = requested > 0.0
                         && double.IsFinite(currentSpeed)
                         && System.Math.Abs(currentSpeed - requested) <= requested * 0.05;

        return stillOurs ? new WarpDecision(WarpAction.Restore, to, why) : WarpDecision.Nothing;
    }

    /// <summary>Forgets any held speed, without restoring it.</summary>
    public void Clear()
    {
        _restoreTo = 0.0;
        _lastRequested = 0.0;
        _attempts = 0;
    }
}
