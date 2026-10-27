namespace KSArmory;

/// <summary>What the frame hook should do about the world running faster than a round can fly.</summary>
internal enum WarpAction
{
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
/// <para>A control loop against an actuator that answers late and is shared with the player; each
/// constant below says what it holds shut.</para>
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
    /// Longest wall-clock frame a frame <em>rate</em> may be inferred from.
    ///
    /// <para>The frame a speed change lands on is a hitch, and deciding on it pins the world near real
    /// time for the whole flight: flown, jumps to 100x and to 800x both measured the same 3.2 s hitch
    /// and both asked for 1.5x. A step implying a second or more is skipped; the next frame is ordinary.</para>
    /// </summary>
    public const double MaxPlausibleFrameSeconds = 1.0;


    /// <summary>
    /// Steps to let pass after a request lands before judging it.
    ///
    /// <para>The step arriving on the frame a write takes effect still measures the interval before
    /// it; judged on that, 30x went to 9.9x then 3.2x and oscillated for the whole salvo.</para>
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
    /// <para>The player's warp control and KSA's auto-warp write the same field; trading writes
    /// frame by frame is a loop neither side wins, so the mod stands down.</para>
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

    /// <param name="dtSim">Simulated seconds in the step just taken.</param>
    /// <param name="faithfulStep">
    /// Longest step the rounds in the air can be integrated across, the smallest among them.
    /// Defaults to the interceptor's; a ballistic weapon can take far longer ones.
    /// </param>
    /// <param name="autoWarpRunning">
    /// Whether KSA is running its own warp-to-a-time, which refuses any speed change. Nothing is then
    /// asked or counted and the rounds take the warp's step, as <c>Ksa/Icbm/IcbmComputers.cs</c> does
    /// for a holding burn.
    /// </param>
    public WarpDecision Decide(double dtSim, double currentSpeed, bool roundsInFlight, bool enabled,
                               double faithfulStep = Interceptor.MaxFaithfulStep,
                               bool autoWarpRunning = false)
    {
        if (!enabled) return Release(currentSpeed, "warp limiting turned off");
        if (!roundsInFlight) return Release(currentSpeed, "nothing in the air");

        if (!double.IsFinite(dtSim) || !double.IsFinite(currentSpeed) || currentSpeed <= 0.0)
        {
            return WarpDecision.Nothing;
        }

        // The refusal cannot be told from a slow write, so counting it would abandon a store released
        // high and warped to. A pending request is dropped, or the count resumes when the warp ends.
        if (autoWarpRunning)
        {
            _awaitingWrite = false;
            _framesAwaiting = 0;
            return WarpDecision.Nothing;
        }

        // Stay down until the air is clear, or the next overrunning frame restarts the fight.
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

        // The override count is not cleared on a step inside the limit: the fight against another
        // writer produces exactly those, so it would never reach its threshold. Only Release clears it.
        if (dtSim <= faithfulStep) return WarpDecision.Nothing;

        // See MaxPlausibleFrameSeconds.
        if (dtSim / currentSpeed > MaxPlausibleFrameSeconds) return WarpDecision.Nothing;

        // The frame time is dtSim/currentSpeed, so this needs no knowledge of the frame rate.
        double target = currentSpeed * (faithfulStep * Margin) / dtSim;

        // Never below real time: where they conflict the round takes the coarser step. Not rate-limited
        // either: a cap breaks the contract that the speed asked for lands inside the limit, and an
        // interceptor converging over several decisions steps past its fuse on each.
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

    /// <summary>
    /// Tells the policy that <em>this mod</em> just set the world speed, so it is not counted as an
    /// override. A yield lasts until the sky is empty, which a staggered salvo never gives it: over
    /// 12 shots, 9 yielded and landed warheads on 117-267 ms frames against 18-33 ms for the 3 that held.
    /// </summary>
    public void NoteOurOwnRequest(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0.0) return;

        _requested = speed;
        _overrides = 0;
        _settle = SettleSteps;
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
