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

    /// <summary>Stop competing for the speed control: something else is driving it.</summary>
    Yield,

    /// <summary>The world will not run slower at all. Give up on what is in the air.</summary>
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
/// Clamping the step instead leaves rounds advancing 0.32 s while the world advances seconds, so
/// they trail further behind every frame and the salvo reads as a guidance failure. Measured at
/// 600x: closest approach 124 km, against 15-20 m unwarped.</para>
///
/// <para>This is a control loop against an actuator that answers late and is shared with the
/// player. See the constants for what each one is holding shut.</para>
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
    /// The slowest this will ever ask the world to run. One, and not by coincidence — below it the
    /// player is waiting on the mod rather than the other way round.
    /// </summary>
    public const double RealTime = 1.0;

    /// <summary>
    /// Steps to let pass after a request lands before judging it.
    ///
    /// <para>The step arriving on the frame a write takes effect still measures the interval
    /// *before* it. Dividing by that again reduces on top of a reduction already in flight: 30x
    /// becomes 9.9x and then straight on to 3.2x, and the pair repeats for as long as the salvo
    /// lasts.</para>
    /// </summary>
    public const int SettleSteps = 1;

    /// <summary>
    /// Frames to wait for a request to be observed before calling it refused. KSA rejects a speed
    /// change outright while its own auto-warp is running, and that is indistinguishable from a
    /// slow write until enough frames have passed.
    /// </summary>
    public const int FramesAwaitingWrite = 4;

    /// <summary>
    /// Times something else may raise the speed while it is held before the mod stops competing.
    ///
    /// <para>The player's warp control and KSA's auto-warp both write the same field this does.
    /// Fighting them frame by frame is a loop neither side wins, and the mod is the one that
    /// should stand down — it is the guest.</para>
    /// </summary>
    public const int OverridesBeforeYielding = 2;

    // A speed is never observed exactly: SimSpeed rounds what it stores.
    private const double SpeedTolerance = 0.05;

    private double _restoreTo;
    private double _requested;
    private bool _awaitingWrite;
    private int _framesAwaiting;
    private int _settle;
    private int _overrides;
    private bool _yielded;

    /// <summary>True while the player's chosen speed is being held down.</summary>
    public bool Holding => _restoreTo > 0.0;

    /// <summary>The speed that will be given back, or zero when not holding.</summary>
    public double HeldSpeed => _restoreTo;

    /// <summary>True once this salvo has given up competing for the speed control.</summary>
    public bool Yielded => _yielded;

    /// <summary>
    /// Decides what the frame hook should do, given the step the engine just reported.
    /// </summary>
    /// <param name="dtSim">Simulated seconds in the step just taken.</param>
    /// <param name="currentSpeed">The world's timewarp factor right now.</param>
    /// <param name="roundsInFlight">Whether anything is airborne that has to be integrated.</param>
    /// <param name="enabled">The player's setting; false disables the whole mechanism.</param>
    /// <param name="faithfulStep">
    /// Longest step the rounds in the air can be integrated across, which is the smallest such
    /// step among them. Defaults to the interceptor's, which is what a hard-manoeuvring endgame
    /// round needs; a ballistic weapon can take far longer ones and holding the world down to
    /// this for a flight lasting minutes is what trips the abandon guard below.
    /// </param>
    public WarpDecision Decide(double dtSim, double currentSpeed, bool roundsInFlight, bool enabled,
                               double faithfulStep = Interceptor.MaxFaithfulStep)
    {
        if (!enabled) return Release(currentSpeed, "warp limiting turned off");
        if (!roundsInFlight) return Release(currentSpeed, "nothing in the air");

        if (!double.IsFinite(dtSim) || !double.IsFinite(currentSpeed) || currentSpeed <= 0.0)
        {
            return WarpDecision.Nothing;
        }

        // Once stood down, stay down until the air is clear. Otherwise the next overrunning frame
        // simply restarts the fight.
        if (_yielded) return WarpDecision.Nothing;

        if (Holding)
        {
            if (_awaitingWrite)
            {
                if (Near(currentSpeed, _requested))
                {
                    _awaitingWrite = false;
                    _settle = SettleSteps;
                }
                else if (++_framesAwaiting > FramesAwaitingWrite)
                {
                    double was = _restoreTo;
                    Clear();
                    return new WarpDecision(WarpAction.Abandon, was,
                                            "the world will not run slow enough to simulate them");
                }

                return WarpDecision.Nothing;
            }

            if (_settle > 0)
            {
                _settle--;
                return WarpDecision.Nothing;
            }

            // Raised by something else. Stand down rather than trade writes with it.
            if (currentSpeed > _requested * (1.0 + SpeedTolerance) && ++_overrides > OverridesBeforeYielding)
            {
                _yielded = true;
                _restoreTo = 0.0;
                return new WarpDecision(WarpAction.Yield, 0.0,
                                        "something else is driving the speed; rounds will lag");
            }
        }

        // Note what is *not* reset here. A step inside the limit is exactly what the fight against
        // another writer produces -- the requested value lands, one good frame passes, the speed
        // goes back up -- so clearing the override count on it means the count never reaches its
        // threshold. The budget is per salvo and only Release clears it.
        if (dtSim <= faithfulStep) return WarpDecision.Nothing;

        // Self-calibrating: the frame time is dtSim/currentSpeed, so the speed that lands on the
        // target step needs no knowledge of the frame rate. That also makes a slow frame and a
        // high warp the same problem, which to a round they are.
        double target = currentSpeed * (faithfulStep * Margin) / dtSim;

        // Never below real time. A round that would rather the world ran slower than the player's
        // own clock does not get it: the mod is a guest, and a game that crawls is a worse thing to
        // hand somebody than a round integrated on a longer step. Where the two conflict, the round
        // takes the coarser step and the accuracy that comes with it.
        target = Math.Max(target, RealTime);

        if (!double.IsFinite(target) || target <= 0.0 || target >= currentSpeed)
        {
            return WarpDecision.Nothing;
        }

        bool first = !Holding;
        if (first) _restoreTo = currentSpeed;

        _requested = target;
        _awaitingWrite = true;
        _framesAwaiting = 0;

        return new WarpDecision(WarpAction.Slow, target,
                                first
                                    ? $"holding {currentSpeed:F0}x down to {target:F1}x while rounds fly"
                                    : $"{currentSpeed:F1}x still overruns; asking for {target:F1}x");
    }

    // Gives the speed back, if it is still the mod's to give. A player who moved the speed while it
    // was held has overridden the policy, and restoring would undo a deliberate choice.
    private WarpDecision Release(double currentSpeed, string why)
    {
        bool held = Holding;
        double to = _restoreTo;
        double requested = _requested;
        Clear();
        _yielded = false;

        if (!held) return WarpDecision.Nothing;

        bool stillOurs = requested > 0.0 && double.IsFinite(currentSpeed)
                         && Near(currentSpeed, requested);

        return stillOurs ? new WarpDecision(WarpAction.Restore, to, why) : WarpDecision.Nothing;
    }

    private static bool Near(double a, double b) => System.Math.Abs(a - b) <= b * SpeedTolerance;

    /// <summary>Forgets any held speed, without restoring it.</summary>
    public void Clear()
    {
        _restoreTo = 0.0;
        _requested = 0.0;
        _awaitingWrite = false;
        _framesAwaiting = 0;
        _settle = 0;
        _overrides = 0;
    }
}
