using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The built-in-test sweep a tail kit runs on the rack, so the blades can be seen moving without
/// dropping the round.
///
/// <para><b>It produces a steering command, not deflections.</b> That is the whole design: the
/// command goes through <see cref="FinMixer"/>, the same mixer the round uses in flight, so the
/// blades can only ever sit in a pose some real demand would have put them in. Driving each blade
/// independently — a wave chasing round the body, say — looks lively and is aerodynamic nonsense:
/// it puts opposite fins at angles that no pitch, yaw or roll input could ask for.</para>
///
/// <para>The sweep exercises one axis at a time, pitch then yaw, which is what a control check
/// actually is and what makes it readable as a test rather than as a steering bug.</para>
///
/// <para>Drawn only, exactly like <see cref="FinMixer"/>. Nothing here reaches the flight model:
/// a round on the rack is not steering.</para>
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
    /// <para>Zero at the start and end of each half, and <em>stationary</em> there too, so the
    /// blades ease to rest before the other axis takes over rather than reversing at speed.</para>
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

        // Rest at neutral between axes. Handing over from pitch to yaw turns the command through
        // a right angle, and half the blades are on the wrong side of that turn: with the set
        // still moving, exactly one pair reverses while the other carries on, which is the
        // asymmetry that reads as a fault. Stopped at neutral, there is no reversal to see.
        double sweep = half * SweepFraction;
        if (local >= sweep) return Vec.Zero;

        // Eased, not linear. A bare sine arrives at the stops and at neutral travelling at full
        // rate; smoothstep has zero slope at both ends, so each axis accelerates from rest and
        // comes back to rest.
        double u = local / sweep;
        double eased = u * u * (3.0 - 2.0 * u);
        double amplitude = authority * Math.Sin(2.0 * Math.PI * eased);

        // Pitch acts across the body's +Y, yaw across its +Z; the axial component never steers.
        return pitch ? new double3(0.0, amplitude, 0.0)
                     : new double3(0.0, 0.0, amplitude);
    }
}
