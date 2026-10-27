using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The part of the ascent that is flown open loop, and the limiter that stops the closed loop
/// tearing the vehicle apart once it takes over.
///
/// <para>Closed-loop guidance near the pad says "point downrange", which through thick air flies the
/// stack sideways into its slipstream, so the first part is a schedule. The handover is by dynamic
/// pressure, so an airless launch hands over at once with nothing knowing there is no air.</para>
/// </summary>
internal static class AscentProfile
{
    /// <summary>Straight up until clear of whatever it was standing on.</summary>
    public const double VerticalRiseMetres = 250.0;

    /// <summary>Or until it is moving fast enough to steer, whichever comes first.</summary>
    public const double VerticalRiseSpeed = 70.0;

    /// <summary>
    /// The pitch schedule, as an angle above the horizon.
    ///
    /// <para>A square root against altitude, turning hardest while slow and low; a linear programme leaves
    /// the upper stage nearly vertical and guidance an enormous correction.</para>
    /// </summary>
    public static double PitchDegreesAt(double altitude, double turnStart, double turnEnd)
    {
        if (!(turnEnd > turnStart)) return 90.0;
        double fraction = Math.Clamp((altitude - turnStart) / (turnEnd - turnStart), 0.0, 1.0);
        return 90.0 * (1.0 - Math.Sqrt(fraction));
    }

    /// <summary>
    /// The commanded direction for a pitch angle above the horizon, on a stated heading.
    /// </summary>
    public static double3 Aim(double3 upCci, double3 downrangeCci, double pitchDegrees)
    {
        double3 up = Vec.Unit(upCci);
        double3 downrange = Vec.Unit(Vec.RejectFrom(downrangeCci, up));

        if (up.Equals(Vec.Zero)) return Vec.Unit(downrangeCci);
        if (downrange.Equals(Vec.Zero)) return up;

        double pitch = pitchDegrees * Math.PI / 180.0;
        return Vec.Unit(up * Math.Sin(pitch) + downrange * Math.Cos(pitch));
    }

    /// <summary>
    /// Which way downrange is: the horizontal part of the velocity the shot needs.
    ///
    /// <para>Off the trajectory solution rather than a great-circle bearing, so it carries the planet's
    /// turn under the flight.</para>
    /// </summary>
    public static double3 Downrange(double3 upCci, double3 requiredVelocityCci, double3 groundVelocityCci)
        => Vec.Unit(Vec.RejectFrom(requiredVelocityCci - groundVelocityCci, upCci));

    /// <summary>
    /// The commanded direction, held to within a stated angle of the airflow.
    ///
    /// <para>Against <em>dynamic pressure</em>, not density: at 35 km a stack doing 2 km/s has a hundredth
    /// of sea-level density and several kilopascals on it. It opens gradually, since a stack that snaps
    /// twenty degrees tumbles.</para>
    /// </summary>
    public static double3 HoldIntoTheAirflow(double3 wantedCci, double3 airflowCci,
                                             double dynamicPressurePa, double maxAngleDegrees,
                                             double freePressurePa = 200.0)
    {
        double3 wanted = Vec.Unit(wantedCci);
        double3 flow = Vec.Unit(airflowCci);

        if (wanted.Equals(Vec.Zero) || flow.Equals(Vec.Zero)) return wanted;
        if (!(dynamicPressurePa > 0.0) || !(maxAngleDegrees >= 0.0)) return wanted;

        double severity = Math.Clamp(dynamicPressurePa / Math.Max(freePressurePa, 1e-9), 0.0, 1.0);
        if (severity <= 0.0) return wanted;

        double allowed = (maxAngleDegrees + (180.0 - maxAngleDegrees) * (1.0 - severity)) * Math.PI / 180.0;
        double angle = Vec.AngleBetween(flow, wanted);
        if (angle <= allowed) return wanted;

        double3 axis = Vec.Cross(flow, wanted);
        if (Vec.Len2(axis) < 1e-18) return wanted;

        return Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.Unit(axis), allowed) * flow);
    }

    public static double DynamicPressure(double densityRatio, double airspeed, double seaLevelDensity = Medium.ReferenceDensityKgPerM3)
        => 0.5 * Math.Max(densityRatio, 0.0) * seaLevelDensity * airspeed * airspeed;
}
