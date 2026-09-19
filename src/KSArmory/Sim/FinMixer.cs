using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// How far each fin of a cruciform set is deflected to produce one lateral command.
///
/// <para>A fin's lift acts along its own normal — the direction square to the blade — so a fin
/// contributes to the command in proportion to how much of the command lies along that normal.
/// A fin edge-on to the demand does nothing and stays neutral, and the pair across the body from
/// each other deflect opposite ways, which is what makes the set turn rather than roll.</para>
///
/// <para><b>Presentation only.</b> The flight model steers through
/// <see cref="Interceptor.GuidanceAccel"/> and knows nothing about fins; this decides what the
/// blades are drawn doing about it. Nothing here feeds back, so a wrong answer is ugly rather
/// than wrong.</para>
///
/// <para>The command is normalised by the round's own authority, so full deflection means "as
/// hard as this airframe pulls" rather than a number of m/s². That keeps a 3 g bomb and a 35 g
/// missile using the same blade travel for their own respective limits.</para>
/// </summary>
internal static class FinMixer
{
    /// <summary>
    /// Deflection of one fin, in radians, signed about its own hinge.
    /// </summary>
    /// <param name="commandBodyFrame">
    /// The commanded lateral acceleration in the round's frame, nose along +X. Only the part
    /// square to the nose steers, so the axial component is ignored.
    /// </param>
    /// <param name="finRollRad">Where this blade sits around the body, measured from +Y about +X.</param>
    /// <param name="authority">
    /// The round's own maximum lateral acceleration. Zero or less leaves every fin neutral rather
    /// than dividing by it.
    /// </param>
    /// <param name="maxDeflectionRad">Blade travel at full demand.</param>
    public static double DeflectionRad(double3 commandBodyFrame, double finRollRad,
                                       double authority, double maxDeflectionRad)
    {
        if (!Vec.IsFinite(commandBodyFrame)) return 0.0;
        if (!double.IsFinite(authority) || authority <= 0.0) return 0.0;
        if (!double.IsFinite(maxDeflectionRad) || maxDeflectionRad <= 0.0) return 0.0;

        // The blade lies in the plane containing the nose and its own radial direction, so its
        // normal is that radial turned a quarter turn about the body axis.
        double3 normal = new(0.0, -Math.Sin(finRollRad), Math.Cos(finRollRad));

        double demand = Vec.Dot(commandBodyFrame, normal) / authority;
        return Math.Clamp(demand, -1.0, 1.0) * maxDeflectionRad;
    }

    /// <summary>
    /// Where each blade of an <paramref name="count"/>-fin set sits around the body, given the
    /// roll the set was modelled at. Even spacing, which is what a cruciform is.
    /// </summary>
    public static double FinRollRad(int index, int count, double firstRollRad)
        => count <= 0 ? firstRollRad : firstRollRad + index * (2.0 * Math.PI / count);
}
