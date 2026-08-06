using Brutal.Numerics;

namespace KSArmory.Sim;

/// <summary>Where to put a camera that rides behind a round in flight.</summary>
public static class ChaseView
{
    /// <summary>
    /// Eye and forward for a camera trailing a round, looking past it at what it is flying at.
    /// </summary>
    /// <param name="velocityLocal">
    /// <c>IProjectile.VelocityLocal</c>, never <c>VelocityEcl</c>: the ecliptic's ~29.8 km/s is
    /// shared, so an absolute velocity points every round the same way.
    /// </param>
    /// <param name="upHint">Away from the planet's centre. A hint; a parallel one is ignored.</param>
    public static bool TryPose(double3 roundEcl, double3 velocityLocal, double3 upHint,
                               double distanceBehind, double heightAbove, double lookAhead,
                               out double3 eyeEcl, out double3 forwardEcl, out double3 upEcl)
    {
        eyeEcl = roundEcl;
        forwardEcl = Vec.Zero;
        upEcl = upHint;

        if (!Vec.IsFinite(roundEcl) || !Vec.IsFinite(velocityLocal)) return false;

        double3 along = Vec.Unit(velocityLocal);
        if (Vec.Len2(along) < 0.5) return false;

        // A round straight up the hint leaves no sideways reference, so the lift has nowhere to
        // go. Falling back to any perpendicular keeps the view usable instead of degenerate.
        double3 up = Vec.Unit(upHint);
        if (Vec.Len2(up) < 0.5 || Math.Abs(Vec.Dot(up, along)) > 0.999) up = AnyPerpendicular(along);

        // Lift perpendicular to the flight path, not along the hint: at a steep climb angle the
        // two are nearly the same direction and the camera would sit in front of the round.
        double3 lift = Vec.Unit(up - along * Vec.Dot(up, along));
        if (Vec.Len2(lift) < 0.5) lift = AnyPerpendicular(along);

        eyeEcl = roundEcl - along * Math.Max(0.0, distanceBehind) + lift * heightAbove;

        double3 lookAt = roundEcl + along * Math.Max(0.0, lookAhead);
        forwardEcl = lookAt - eyeEcl;

        if (Vec.Len2(forwardEcl) < 1e-12) return false;

        forwardEcl = Vec.Unit(forwardEcl);

        // KSA's fixed camera crosses the view with the frame's axis and normalises, so a parallel
        // pair divides by zero -- and a vertically launched round is exactly that at first.
        double3 axis = Vec.Unit(upHint);
        double alongAxis = Vec.Dot(forwardEcl, axis);

        if (Math.Abs(alongAxis) > MaxAlongAxis)
        {
            double3 sideways = forwardEcl - axis * alongAxis;
            sideways = Vec.Len2(sideways) < 1e-12 ? AnyPerpendicular(axis) : Vec.Unit(sideways);

            double lean = alongAxis < 0.0 ? -MaxAlongAxis : MaxAlongAxis;
            forwardEcl = Vec.Unit(axis * lean + sideways * Math.Sqrt(1.0 - (MaxAlongAxis * MaxAlongAxis)));
        }

        upEcl = lift;

        return Vec.IsFinite(eyeEcl) && Vec.IsFinite(forwardEcl);
    }

    // About 2.6 degrees off the axis: enough for the cross product to have a length to normalise.
    private const double MaxAlongAxis = 0.999;

    /// <summary>
    /// How far back the camera sits, closing in as the round converges.
    ///
    /// <para>A fixed stand-off makes a missile appear to hang still, because everything in frame
    /// scales together. The easing accelerates into the impact: a symmetric one is flat at both
    /// ends, so it is slowest exactly where the arrival happens.</para>
    /// </summary>
    /// <param name="range">Distance from the round to what it is aimed at.</param>
    /// <param name="far">At or beyond this range, the full stand-off.</param>
    /// <param name="near">At or inside this range, the closest the camera comes.</param>
    public static double StandOff(double range, double far, double near,
                                  double farDistance, double nearDistance)
    {
        if (!double.IsFinite(range) || !(far > near)) return farDistance;

        double t = Math.Clamp((range - near) / (far - near), 0.0, 1.0);

        // A root curve: the slope grows as the range runs out, so it holds station then rushes in.
        t = Math.Pow(t, Sharpness);

        return nearDistance + ((farDistance - nearDistance) * t);
    }

    // Below one, so the closing accelerates rather than easing off. Lower closes later and harder.
    private const double Sharpness = 0.5;

    private static double3 AnyPerpendicular(double3 axis)
    {
        double3 candidate = Math.Abs(axis.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);

        return Vec.Unit(Vec.Cross(axis, candidate));
    }
}
