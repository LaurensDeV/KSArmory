namespace KSArmory;

/// <summary>
/// How far a recoiling barrel has run back, given the time since the gun last fired.
///
/// <para>A sharp run to full travel and a slower counter-recoil back into battery: that asymmetry is
/// what reads as a gun firing rather than a barrel sliding. Both ends are eased, because a barrel
/// that starts or stops dead reads as a dropped frame.</para>
/// </summary>
internal static class GunRecoil
{
    /// <summary>
    /// Metres rearward of battery. Zero before any shot, and zero again once it is back.
    /// </summary>
    public static double Offset(double secondsSinceShot, double travelMetres,
                                double recoilSeconds, double returnSeconds)
    {
        if (!(travelMetres > 0.0) || !double.IsFinite(secondsSinceShot) || secondsSinceShot < 0.0)
        {
            return 0.0;
        }

        if (recoilSeconds > 0.0 && secondsSinceShot < recoilSeconds)
        {
            double f = secondsSinceShot / recoilSeconds;
            return travelMetres * (1.0 - (1.0 - f) * (1.0 - f));
        }

        double back = secondsSinceShot - Math.Max(0.0, recoilSeconds);
        if (!(returnSeconds > 0.0) || back >= returnSeconds) return 0.0;

        double g = back / returnSeconds;
        return travelMetres * (1.0 - g * g * (3.0 - 2.0 * g));
    }
}
