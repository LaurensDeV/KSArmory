using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The part of the ascent that is flown open loop, and the limiter that stops the closed loop
/// tearing the vehicle apart once it takes over.
///
/// <para>Closed-loop guidance cannot fly the first minute. Its answer near the pad is roughly
/// "point downrange", which through thick air at increasing speed means flying the stack sideways
/// into its own slipstream. So the first part of the flight is a schedule — straight up, then a
/// pitch programme that turns gently enough for the vehicle to follow it — and guidance only takes
/// the wheel once there is not enough air left for the difference to matter.</para>
///
/// <para>The handover is by dynamic pressure rather than by altitude, because that is the thing
/// that actually does the damage and it is the same number on every body. A launch on the Moon
/// hands over immediately and correctly, with nothing having to know the Moon has no air.</para>
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
    /// <para>A square root against altitude rather than a straight line: the vehicle has to be
    /// turned hardest while it is slow and lowest, and a linear programme spends the whole upper
    /// stage nearly vertical and then asks guidance for an enormous correction.</para>
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
    /// <para>Taken off the trajectory solution rather than from a great-circle bearing to the
    /// target, so it already carries the correction for the planet turning under the flight. A
    /// heading computed on the map is out by the better part of a degree at intercontinental range,
    /// which is a hundred kilometres at the far end.</para>
    /// </summary>
    public static double3 Downrange(double3 upCci, double3 requiredVelocityCci, double3 groundVelocityCci)
        => Vec.Unit(Vec.RejectFrom(requiredVelocityCci - groundVelocityCci, upCci));

    /// <summary>
    /// The commanded direction, held to within a stated angle of the airflow.
    ///
    /// <para>Held against <em>dynamic pressure</em>, not against air density. Thin air is not the
    /// same as no load: at 35 km a rising stack has a hundredth of sea-level density and several
    /// kilopascals on it, because it is by then doing two kilometres a second. A limiter that opens
    /// on density alone lets go exactly where the vehicle is going fastest.</para>
    ///
    /// <para>And it opens gradually rather than at a threshold, because a step change in what
    /// guidance is allowed to ask for is a step change in what the vehicle is told to do, and a
    /// stack that snaps twenty degrees is a stack that tumbles.</para>
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

        // Full severity allows the stated angle; no severity allows anything. Between them the
        // allowance opens smoothly, so the command never jumps.
        double allowed = (maxAngleDegrees + (180.0 - maxAngleDegrees) * (1.0 - severity)) * Math.PI / 180.0;
        double angle = Vec.AngleBetween(flow, wanted);
        if (angle <= allowed) return wanted;

        double3 axis = Vec.Cross(flow, wanted);
        if (Vec.Len2(axis) < 1e-18) return wanted;

        return Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.Unit(axis), allowed) * flow);
    }

    /// <summary>Dynamic pressure, as the thing that decides when steering is free.</summary>
    public static double DynamicPressure(double densityRatio, double airspeed, double seaLevelDensity = 1.225)
        => 0.5 * Math.Max(densityRatio, 0.0) * seaLevelDensity * airspeed * airspeed;
}
