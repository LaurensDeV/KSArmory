using Brutal.Numerics;

namespace KSArmory;

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
    /// <param name="aimEcl">
    /// What the round is flying at, in <paramref name="roundEcl"/>'s frame, or null when it is
    /// flying at nothing. Both are positions in one frame and this differences them itself, so a
    /// translated frame — the round-relative one the caller works in — gives the same answer.
    /// </param>
    /// <param name="upHint">Away from the planet's centre. A hint; a parallel one is ignored.</param>
    /// <param name="engineAxisEcl">
    /// The axis the engine's camera controller cannot cross — <em>not</em>
    /// <paramref name="upHint"/>, which stays the local vertical and decides the lift. See
    /// <see cref="LeanOffAxis"/> for why the two are different directions.
    /// </param>
    public static bool TryPose(double3 roundEcl, double3 velocityLocal, double3? aimEcl,
                               double3 upHint, double3 engineAxisEcl,
                               double distanceBehind, double heightAbove, double lookAhead,
                               out double3 eyeEcl, out double3 forwardEcl, out double3 upEcl)
    {
        eyeEcl = roundEcl;
        forwardEcl = Vec.Zero;
        upEcl = upHint;

        if (!Vec.IsFinite(roundEcl) || !Vec.IsFinite(velocityLocal)) return false;

        double3 along = Vec.Unit(velocityLocal);
        if (Vec.Len2(along) < 0.5) return false;

        double ahead = Math.Max(0.0, lookAhead);
        double3 axis = along;
        double lookRange = ahead;

        // The rig stands behind the round on the line to what it is flying at, not on the round's
        // own flight path. For anything steering onto a target the two are a lead angle apart and
        // this barely moves; a bomb's target sits tens of degrees below the path it is falling
        // along, and a camera looking along that path never has it in frame at all.
        if (aimEcl is { } aim && Vec.IsFinite(aim))
        {
            double3 toAim = aim - roundEcl;
            double range = Vec.Len(toAim);

            // Inside the look-ahead the two answers agree — a round arriving is pointing at what
            // it is arriving at — so handing back to the flight path there costs nothing and keeps
            // the aim out of the reversal as the round goes past.
            if (range > ahead)
            {
                axis = HeldNearFlightPath(Vec.Unit(toAim), along);
                lookRange = range;
            }
        }

        // A round straight up the hint leaves no sideways reference, so the lift has nowhere to
        // go. Falling back to any perpendicular keeps the view usable instead of degenerate.
        double3 up = Vec.Unit(upHint);
        if (Vec.Len2(up) < 0.5 || Math.Abs(Vec.Dot(up, axis)) > 0.999) up = AnyPerpendicular(axis);

        // Lift perpendicular to the axis the eye stands off along, not along the hint: at a steep
        // climb angle the two are nearly the same direction and the camera would sit in front of
        // the round.
        double3 lift = Vec.Unit(up - axis * Vec.Dot(up, axis));
        if (Vec.Len2(lift) < 0.5) lift = AnyPerpendicular(axis);

        eyeEcl = roundEcl - axis * Math.Max(0.0, distanceBehind) + lift * heightAbove;

        // Along the axis rather than at the aim itself, so a target held out at the cone edge
        // leaves the round centred: the ride is of the round, and what it is flying at is what
        // stands behind it.
        double3 lookAt = roundEcl + axis * lookRange;
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

    // Holds a direction within MaxOffFlightPathDeg of the flight path, keeping the plane the two
    // lie in. What it bounds is a round that has gone past what it was aimed at: the line to the
    // target then swings through abeam and reverses, and a rig built on it whips round to face
    // backwards over a frame or two. Clamping is continuous where refusing is not -- at the bound
    // the held direction is the wanted one -- so a shot that misses slides to the edge and stays.
    private static double3 HeldNearFlightPath(double3 direction, double3 along)
    {
        double off = Math.Acos(Math.Clamp(Vec.Dot(direction, along), -1.0, 1.0));
        if (off <= MaxOffFlightPath) return direction;

        double3 across = Vec.RejectFrom(direction, along);
        if (Vec.Len2(across) < 1e-12) return along;

        return Vec.Unit(Vec.Unit(across) * Math.Sin(MaxOffFlightPath)
                        + along * Math.Cos(MaxOffFlightPath));
    }

    // Generous, because the angle a bomb's target sits below its flight path grows with release
    // height and shrinks with speed and there is no bound on either: 27 degrees from two
    // kilometres at 200 m/s, 48 from ten. What this is for is the reversal, not a framing rule.
    private const double MaxOffFlightPathDeg = 80.0;

    private static readonly double MaxOffFlightPath = MaxOffFlightPathDeg * Math.PI / 180.0;

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
    /// <para>Both ends are given at the range of what the round is flying at, so the aim turns by
    /// however far the player's view was off that and no further — <b>only the position really
    /// travels</b>. It is why the aim is given as two <em>points</em> rather than two directions:
    /// interpolating directions turns at a wildly uneven rate and collapses to zero length when
    /// they oppose, where two points at one depth stay a bounded turn apart.</para>
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
