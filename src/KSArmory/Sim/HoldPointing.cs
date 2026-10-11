namespace KSArmory;

/// <summary>
/// When a stack holding for its burn window turns to face the burn: just long enough before ignition to make the turn.
///
/// <para>Before cutoff the whole stack is pointed on the bus's own jets, and held pointed they fire all the time -- flown,
/// a minute of it at 1x was affordable and five emptied them, leaving the trim nothing to pay with. Under a warp the stack
/// is on rails and burns nothing, so what costs is real time spent pointed, and the turn itself is the only part that has
/// to be paid for.</para>
/// </summary>
internal static class HoldPointing
{
    /// <summary>Added to the turn, for the controller settling on the line and the frame the engine lights on.</summary>
    public const double MarginSeconds = 30.0;

    /// <summary>The most a turn is allowed, when the stack's jets cannot be read.</summary>
    public const double MaxLeadSeconds = 300.0;

    /// <summary>
    /// A stack already pointing goes back to drifting only once ignition is this much further off than its lead, so a
    /// window that moves by seconds does not start and stop the turn.
    /// </summary>
    public const double RedriftSeconds = 60.0;

    // KSA's attitude controller is not an ideal bang-bang turn. A guess, not measured: the first holds flown on it say.
    private const double ControllerSlack = 1.5;

    /// <summary>
    /// How long an ideal turn through <paramref name="angleRad"/> takes from rest to rest: accelerate, coast at the rate
    /// limit if it is reached, decelerate.
    /// </summary>
    public static double TurnSeconds(double angleRad, double accelRadPerS2, double rateLimitRadPerS)
    {
        double angle = Math.Abs(angleRad);
        if (angle == 0.0) return 0.0;
        if (!(accelRadPerS2 > 0.0) || !double.IsFinite(accelRadPerS2)) return double.NaN;

        if (rateLimitRadPerS > 0.0 && double.IsFinite(rateLimitRadPerS)
            && angle >= rateLimitRadPerS * rateLimitRadPerS / accelRadPerS2)
        {
            return angle / rateLimitRadPerS + rateLimitRadPerS / accelRadPerS2;
        }

        return 2.0 * Math.Sqrt(angle / accelRadPerS2);
    }

    /// <summary>How long before ignition to start pointing, between the margin and <see cref="MaxLeadSeconds"/>.</summary>
    public static double LeadSeconds(double angleRad, double accelRadPerS2, double rateLimitRadPerS)
    {
        double turn = TurnSeconds(angleRad, accelRadPerS2, rateLimitRadPerS);
        if (!double.IsFinite(turn)) return MaxLeadSeconds;

        return Math.Clamp(ControllerSlack * turn + MarginSeconds, MarginSeconds, MaxLeadSeconds);
    }

    /// <summary>Whether to be pointing now, given whether it already is.</summary>
    public static bool Points(double secondsToIgnition, double leadSeconds, bool pointingNow)
    {
        if (!double.IsFinite(secondsToIgnition)) return true;

        return pointingNow ? secondsToIgnition <= leadSeconds + RedriftSeconds : secondsToIgnition <= leadSeconds;
    }
}
