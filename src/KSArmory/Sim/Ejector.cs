namespace KSArmory;

/// <summary>
/// An ejector's push as a stroke rather than a kick: the arms accelerate the store evenly over their
/// travel and it leaves at the ejection speed as they reach the end, so the arms are on the store the
/// whole way. The round and the drawn arms read the same motion.
/// </summary>
internal static class Ejector
{
    /// <summary>Seconds the arms hold at full travel before they come back.</summary>
    public const double HoldSeconds = 0.3;

    /// <summary>The even acceleration that reaches <paramref name="speed"/> over <paramref name="stroke"/>.</summary>
    public static double Acceleration(double speed, double stroke)
        => speed > 0.0 && stroke > 0.0 ? speed * speed / (2.0 * stroke) : 0.0;

    /// <summary>How long the stroke takes; zero where there is none, which is a push all at once.</summary>
    public static double StrokeSeconds(double speed, double stroke)
        => speed > 0.0 && stroke > 0.0 ? 2.0 * stroke / speed : 0.0;

    /// <summary>
    /// The share of a sub-step from <paramref name="from"/> lasting <paramref name="h"/> seconds that
    /// falls inside the stroke, so a push is neither lost nor doubled where a sub-step straddles its end.
    /// </summary>
    public static double ShareOfStep(double from, double h, double strokeSeconds)
    {
        if (!(h > 0.0) || !(strokeSeconds > 0.0)) return 0.0;
        return Math.Clamp((Math.Min(from + h, strokeSeconds) - Math.Max(from, 0.0)) / h, 0.0, 1.0);
    }

    /// <summary>
    /// How far the arms have run out <paramref name="t"/> seconds after release: with the store along the
    /// stroke, held at full travel, then back over <paramref name="returnSeconds"/>. Drawn only.
    /// </summary>
    public static double ArmOffset(double t, double speed, double stroke, double returnSeconds)
    {
        double strokeSeconds = StrokeSeconds(speed, stroke);
        if (!(strokeSeconds > 0.0) || !double.IsFinite(t) || t < 0.0) return 0.0;

        if (t < strokeSeconds) return 0.5 * Acceleration(speed, stroke) * t * t;

        double back = t - strokeSeconds - HoldSeconds;
        if (back < 0.0) return stroke;
        if (!(returnSeconds > 0.0) || back >= returnSeconds) return 0.0;

        double g = back / returnSeconds;
        return stroke * (1.0 - g * g * (3.0 - 2.0 * g));
    }
}
