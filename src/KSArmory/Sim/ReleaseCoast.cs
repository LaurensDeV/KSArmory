using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a prediction's departure will be when the warheads actually leave, so the separation kick is added there
/// and not hours early.
///
/// <para>The release is a fixed time before arrival, so a kick added at cutoff is given the whole coast to grow in:
/// 0.5 m/s at 668 km on a four-hour coast lands 2.8 km from the same kick at the release, and 3.8 km added at
/// apogee. <c>docs/ICBM-OUTSTANDING.md</c> 1.14.</para>
/// </summary>
internal static class ReleaseCoast
{
    /// <summary>
    /// The state at the release, coasted in vacuum from the departure, with how long that took. No coast -- the
    /// state unchanged and zero seconds -- when the release is already due, when the flight time is not known, or
    /// when the coast would end in air, which Kepler does not fly.
    /// </summary>
    public static double3 ToRelease(BallisticBody body, double3 positionCci, double3 velocityCci,
                                    double flightSeconds, double gateSeconds,
                                    Func<double3, double>? densityRatioAt,
                                    out double3 velocityAtReleaseCci, out double coastSeconds)
    {
        velocityAtReleaseCci = velocityCci;
        coastSeconds = 0.0;

        double coast = flightSeconds - gateSeconds;
        if (!double.IsFinite(coast) || !(coast > 0.0) || !(gateSeconds >= 0.0)) return positionCci;

        if (!Kepler.TryCoast(body.Mu, positionCci, velocityCci, coast, out double3 p, out double3 v)) return positionCci;
        if (densityRatioAt is not null && !(densityRatioAt(p) < Medium.NoticeableDensity)) return positionCci;

        velocityAtReleaseCci = v;
        coastSeconds = coast;
        return p;
    }
}
