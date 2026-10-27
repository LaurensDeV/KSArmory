using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The built-in-test sweep a tail kit runs on the rack, so the blades can be seen moving without
/// dropping the round.
///
/// <para><b>It produces a steering command, not deflections</b>, put through <see cref="FinMixer"/>, so
/// the blades only ever sit in a pose a real demand would give. One axis at a time, pitch then yaw.
/// Drawn only.</para>
/// </summary>
public static class FinTest
{
    /// <summary>Seconds for the full check — pitch through its travel, then yaw through its.</summary>
    public const double PeriodSeconds = 4.0;

    /// <summary>How much of each axis's slot is spent sweeping; the rest is rest at neutral.</summary>
    public const double SweepFraction = 0.78;

    /// <summary>
    /// The commanded lateral acceleration to show at <paramref name="seconds"/>, in the round's
    /// own frame with the nose along +X. Feed it to <see cref="FinMixer.DeflectionRad"/>.
    ///
    /// <para>Zero and stationary at the start and end of each half, so the blades come to rest before
    /// the other axis takes over.</para>
    /// </summary>
    /// <param name="authority">
    /// The round's own maximum lateral acceleration — the sweep reaches exactly this, so the
    /// blades use their full declared travel and no more.
    /// </param>
    public static double3 CommandBodyFrame(double seconds, double authority)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(authority) || authority <= 0.0)
            return Vec.Zero;

        double half = PeriodSeconds / 2.0;
        double t = seconds % PeriodSeconds;
        if (t < 0.0) t += PeriodSeconds;

        bool pitch = t < half;
        double local = pitch ? t : t - half;

        // Handing over from pitch to yaw while moving reverses one pair and not the other, which
        // reads as a fault; at rest there is no reversal.
        double sweep = half * SweepFraction;
        if (local >= sweep) return Vec.Zero;

        // Smoothstep inside the sine, so each axis starts and ends at rest.
        double u = local / sweep;
        double eased = u * u * (3.0 - 2.0 * u);
        double amplitude = authority * Math.Sin(2.0 * Math.PI * eased);

        return pitch ? new double3(0.0, amplitude, 0.0)
                     : new double3(0.0, 0.0, amplitude);
    }
}
