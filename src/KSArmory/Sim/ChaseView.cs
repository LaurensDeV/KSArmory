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
    /// <param name="engineAxisEcl">
    /// The axis the engine's camera controller cannot cross — <em>not</em>
    /// <paramref name="upHint"/>, which stays the local vertical and decides the lift. See
    /// <see cref="LeanOffAxis"/> for why the two are different directions.
    /// </param>
    public static bool TryPose(double3 roundEcl, double3 velocityLocal, double3 upHint,
                               double3 engineAxisEcl,
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

        forwardEcl = LeanOffAxis(Vec.Unit(forwardEcl), engineAxisEcl);
        upEcl = lift;

        return Vec.IsFinite(eyeEcl) && Vec.IsFinite(forwardEcl);
    }

    /// <summary>
    /// Tilts a view direction away from the axis the engine's camera cannot cross.
    ///
    /// <para>KSA's fixed camera builds its basis by crossing the view with that axis and
    /// normalising, so a parallel pair divides by zero — and a vertically launched round points
    /// very near it. Every direction handed to the engine goes through here.</para>
    ///
    /// <para><b>The axis is ecliptic +Z, not the local vertical.</b> The controller crosses against
    /// the camera reference frame's +Z, and a followable that is not a vehicle or a celestial gets
    /// the Identity frame with its declared reference frame ignored entirely. Leaning off local up
    /// instead guards a singularity that is not there and leaves the real one open, and
    /// <c>KsaWorld.TryLookFromMainViewport</c> then refuses the write and the chase drops the view
    /// in mid-flight. See <c>docs/KSA-CAMERAS.md</c>.</para>
    /// </summary>
    public static double3 LeanOffAxis(double3 forward, double3 axisHint)
    {
        double3 axis = Vec.Unit(axisHint);
        if (Vec.Len2(axis) < 0.5) return forward;

        double alongAxis = Vec.Dot(forward, axis);
        if (Math.Abs(alongAxis) <= MaxAlongAxis) return forward;

        double3 sideways = forward - axis * alongAxis;
        sideways = Vec.Len2(sideways) < 1e-12 ? AnyPerpendicular(axis) : Vec.Unit(sideways);

        double lean = alongAxis < 0.0 ? -MaxAlongAxis : MaxAlongAxis;

        return Vec.Unit(axis * lean + sideways * Math.Sqrt(1.0 - (MaxAlongAxis * MaxAlongAxis)));
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

    /// <summary>
    /// Eases a camera from where the player had it onto the chase pose, without cutting.
    ///
    /// <para>Both ends are looking at much the same thing — the player at the target, the chase
    /// along a round that is flying at it — so <b>only the position really travels</b> and the aim
    /// barely moves. That is what makes this calm, and it is why the aim is given as two
    /// <em>points</em> rather than two directions: interpolating directions turns at a wildly
    /// uneven rate and collapses to zero length when they oppose, where two points near the same
    /// target are nearly the same point.</para>
    ///
    /// <para><b>Both ends must be positions sampled this frame</b>, not stored ones. They are
    /// anchored to different moving things, and the ecliptic is inertial — a point captured at the
    /// start and held still falls half a kilometre behind per frame.</para>
    /// </summary>
    /// <param name="t">Progress, 0 at the player's pose and 1 at the chase. Clamped.</param>
    public static bool TryBlend(double3 fromEcl, double3 fromLookAtEcl,
                                double3 toEcl, double3 toLookAtEcl,
                                double3 engineAxisEcl, double t,
                                out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = toEcl;
        forwardEcl = Vec.Unit(toLookAtEcl - toEcl);

        if (!Vec.IsFinite(fromEcl) || !Vec.IsFinite(toEcl)) return false;
        if (!Vec.IsFinite(fromLookAtEcl) || !Vec.IsFinite(toLookAtEcl) || !double.IsFinite(t))
        {
            return false;
        }

        double e = Smoothstep(Math.Clamp(t, 0.0, 1.0));

        eyeEcl = fromEcl + ((toEcl - fromEcl) * e);

        double3 lookAt = fromLookAtEcl + ((toLookAtEcl - fromLookAtEcl) * e);
        double3 forward = lookAt - eyeEcl;

        if (Vec.Len2(forward) < 1e-6) return false;

        // The same tilt the settled pose gets, for the same reason: a view along the axis KSA's
        // fixed camera crosses against divides by zero, and a transition can sweep through it.
        forwardEcl = LeanOffAxis(Vec.Unit(forward), engineAxisEcl);

        return Vec.IsFinite(eyeEcl) && Vec.Len2(forwardEcl) > 0.5;
    }

    // Flat at both ends, so the camera leaves and arrives without a kick at either.
    private static double Smoothstep(double t) => t * t * (3.0 - (2.0 * t));

    private static double3 AnyPerpendicular(double3 axis)
    {
        double3 candidate = Math.Abs(axis.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);

        return Vec.Unit(Vec.Cross(axis, candidate));
    }
}
