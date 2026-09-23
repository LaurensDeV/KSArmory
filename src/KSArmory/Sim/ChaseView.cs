using Brutal.Numerics;

namespace KSArmory;

/// <summary>Where to put a camera that rides behind a round in flight.</summary>
public static class ChaseView
{
    // Below this a line has no reliable direction left to take Unit() of. Metres, because it is
    // about the arithmetic rather than about framing.
    private const double MinAimRange = 1.0;

    /// <summary>The pose with its lift derived afresh, for a camera with no last frame to carry.</summary>
    public static bool TryPose(double3 roundEcl, double3 velocityLocal, double3? aimEcl,
                               double3 upHint, double3 engineAxisEcl,
                               double distanceBehind, double heightAbove, double lookAhead,
                               out double3 eyeEcl, out double3 forwardEcl, out double3 upEcl)
        => TryPose(roundEcl, velocityLocal, aimEcl, upHint, engineAxisEcl, distanceBehind,
                   heightAbove, lookAhead, Vec.Zero, out eyeEcl, out forwardEcl, out upEcl);

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
    /// <param name="liftReference">
    /// Last frame's lift — the side of the axis the eye stood on — or zero to derive it from
    /// <paramref name="upHint"/>. Comes back as <paramref name="upEcl"/>.
    /// </param>
    public static bool TryPose(double3 roundEcl, double3 velocityLocal, double3? aimEcl,
                               double3 upHint, double3 engineAxisEcl,
                               double distanceBehind, double heightAbove, double lookAhead,
                               double3 liftReference,
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

            // Held to the target all the way in. A round arriving is only pointing at what it
            // arrives at when the target is not moving: proportional navigation flies a collision
            // course, which holds a lead angle to impact by design, so handing back to the flight
            // path over the last stretch swings the view off the target by that whole angle at the
            // one moment anybody is watching. What that handback was guarding — the line reversing
            // as the round goes past — is HeldNearFlightPath's job, and it bounds the swing to 80°
            // whether the round is arriving or long past.
            //
            // The look-at stays at least a look-ahead out so the framing does not pitch up as the
            // range collapses, and MinAimRange is a divide-by-zero guard rather than a distance.
            if (range > MinAimRange)
            {
                axis = HeldNearFlightPath(Vec.Unit(toAim), along);
                lookRange = Math.Max(range, ahead);
            }
        }

        // Carried when there is one to carry. Derived from the hint, the lift is whatever part of
        // the hint lies across the axis, which near the vertical is a sliver pointing the way the
        // axis leans -- and past a threshold a fixed perpendicular instead. Either reverses the
        // lift as the axis passes the vertical, so the eye swaps sides and the round appears to turn
        // half a round: a bomb falling onto a point below it. The caller pulls the carried lift back
        // towards the hint once a frame, which is what keeps it from drifting.
        double3 carried = liftReference - axis * Vec.Dot(liftReference, axis);
        double3 lift;

        if (Vec.IsFinite(carried) && Vec.Len2(carried) > 1e-6)
        {
            lift = Vec.Unit(carried);
        }
        else
        {
            // A round straight up the hint leaves no sideways reference, so the lift has nowhere
            // to go. Falling back to any perpendicular keeps the view usable instead of degenerate.
            double3 up = Vec.Unit(upHint);
            if (Vec.Len2(up) < 0.5 || Math.Abs(Vec.Dot(up, axis)) > 0.999) up = AnyPerpendicular(axis);

            // Lift perpendicular to the axis the eye stands off along, not along the hint: at a
            // steep climb angle the two are nearly the same direction and the camera would sit in
            // front of the round.
            lift = Vec.Unit(up - axis * Vec.Dot(up, axis));
            if (Vec.Len2(lift) < 0.5) lift = AnyPerpendicular(axis);
        }

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
    /// The pose turned round the round by the player, and moved in or out along the line from it.
    ///
    /// <para>The whole rig turns — eye, view and up together — so the round stays exactly where it
    /// was in the picture and the view's roll is carried rather than re-derived. With nothing to
    /// apply it is the pose handed in to the bit, which is what lets a view the player has let go of
    /// settle onto the chase's own.</para>
    /// </summary>
    /// <param name="eyeFromRound">The eye, as a separation from the round.</param>
    /// <param name="localUp">What the yaw turns about, as KSA's orbit camera turns about the vertical.</param>
    /// <param name="yaw">See <see cref="ChaseOrbit.Yaw"/>.</param>
    /// <param name="pitch">See <see cref="ChaseOrbit.Pitch"/>; positive raises the eye.</param>
    /// <param name="lowestEye">
    /// How far along <paramref name="localUp"/> the eye may sit from the round, negative being below
    /// it. The pitch gives way to it and the yaw does not, so a view turned into the ground stops at
    /// the ground rather than refusing the turn.
    /// </param>
    public static void Orbit(double3 eyeFromRound, double3 forward, double3 up, double3 localUp,
                             double yaw, double pitch, double zoom, double lowestEye,
                             double3 engineAxisEcl,
                             out double3 eyeEcl, out double3 forwardEcl, out double3 upEcl)
    {
        eyeEcl = eyeFromRound;
        forwardEcl = forward;
        upEcl = up;

        if (yaw == 0.0 && pitch == 0.0 && zoom == 1.0) return;
        if (!Vec.IsFinite(eyeFromRound) || !Vec.IsFinite(forward) || !Vec.IsFinite(up)) return;
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch) || !(zoom > 0.0) || !double.IsFinite(zoom)) return;

        double3 vertical = Vec.Unit(localUp);
        if (Vec.Len2(vertical) < 0.5) return;

        // Square to the view and its own up, which has a length however steeply the view looks
        // down; a right taken off the vertical has none when a round falls straight onto its target.
        double3 right = Vec.Unit(Vec.Cross(up, forward));
        if (Vec.Len2(right) < 0.5) right = AnyPerpendicular(forward);

        double3 eye = Turned(pitch, out double3 turnedForward, out double3 turnedUp);

        if (Vec.Dot(eye, vertical) < lowestEye)
        {
            double allowed = 0.0;
            double refused = 1.0;

            // Nothing below the floor is used, and the full turn was: find how much of the pitch the
            // floor leaves. Falls back to none of it, which is the yaw and zoom alone.
            for (int i = 0; i < 24; i++)
            {
                double mid = 0.5 * (allowed + refused);
                if (Vec.Dot(Turned(pitch * mid, out _, out _), vertical) >= lowestEye) allowed = mid;
                else refused = mid;
            }

            eye = Turned(pitch * allowed, out turnedForward, out turnedUp);
        }

        forwardEcl = LeanOffAxis(Vec.Unit(turnedForward), engineAxisEcl);
        upEcl = turnedUp;
        eyeEcl = eye;

        if (Vec.IsFinite(eyeEcl) && Vec.IsFinite(forwardEcl) && Vec.IsFinite(upEcl)) return;

        eyeEcl = eyeFromRound;
        forwardEcl = forward;
        upEcl = up;

        double3 Turned(double withPitch, out double3 f, out double3 u)
        {
            doubleQuat q = doubleQuat.CreateFromAxisAngle(vertical, yaw)
                           * doubleQuat.CreateFromAxisAngle(right, withPitch);

            f = q * forward;
            u = q * up;
            return (q * eyeFromRound) * zoom;
        }
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
    /// How far short of its arrival a chase stops riding a round and watches it go in: clear of the
    /// fireball by a margin that grows with it, and never so close that a small one fills the view.
    /// </summary>
    public static double StopShortMetres(double chargeKg)
    {
        double shortOf = Math.Max(MinStopShortMetres, StopShortFireballs * Warhead.FireballRadius(chargeKg));

        // A charge that grows a cloud is watched from far enough to SEE the cloud, which is a
        // different and much larger number than clearing its fireball. Six fireball radii is 328 m
        // at 0.3 kt, and what stands there is 1.31 km tall -- so the camera ends up under it looking
        // up, and inside the 497 m this same charge is lethal to. Standing off the cloud's own
        // height puts it across the frame and outside what made it.
        return chargeKg >= MushroomCloud.ThresholdKg
                   ? Math.Max(shortOf, CloudsInFrame * MushroomCloud.DrawnCloudTop(MushroomCloud.KilotonsFor(chargeKg)))
                   : shortOf;
    }

    /// <summary>
    /// How long the view is held on a burst afterwards: as long as there is something still
    /// happening, which for everything but a nuclear charge is no time at all.
    ///
    /// <para><b>Sized by the burst rather than picked, the same way
    /// <see cref="StopShortMetres"/> is.</b> A conventional round is over when the flash is: three
    /// seconds is already generous. A nuclear one is not — <see cref="MushroomCloud"/> rises for
    /// <see cref="MushroomCloud.RiseSeconds"/> and stands for as long again, so a flat three
    /// seconds showed about a twenty-fifth of the thing the mod goes to the trouble of drawing,
    /// and took the camera away mid-event.</para>
    ///
    /// <para>The rise rather than the whole life. At the ceiling the cloud stops changing shape and
    /// the rest is it standing there — and it stands in the world, so a player who wants more can
    /// fly their own camera back to it. What cannot be recovered is the part they were taken away
    /// from.</para>
    ///
    /// <para><b>It is not always a cloud, so the body is asked as well as the charge.</b>
    /// <see cref="AirlessBurst.WatchSeconds"/> decides which of the three is happening and carries
    /// the charge threshold with it, so whether this burst made anything at all stays one question
    /// with one answer. Holding for the rise regardless is what left the camera on an empty sky for
    /// most of a minute over a body that grows no cloud.</para>
    ///
    /// <para>The world terms are required rather than defaulted, because the charge alone used to
    /// be the whole question: a default would let a caller written against the old shape keep
    /// compiling and quietly hold the view for a cloud that is not there.</para>
    /// </summary>
    public static double LingerSeconds(double chargeKg, bool hasAir,
                                       double gravityMetresPerSecond2, double burstAltitudeMetres)
        => Math.Max(MinLingerSeconds,
                    AirlessBurst.WatchSeconds(chargeKg, hasAir,
                                              gravityMetresPerSecond2, burstAltitudeMetres));

    /// <summary>
    /// Held on any burst, nuclear or not. Long enough to see what happened and short enough that
    /// a cannon putting one round into a drone does not take the view away for the next one.
    /// </summary>
    public const double MinLingerSeconds = 3.0;

    /// <summary>
    /// Whether a round is near enough its arrival to stop and watch: within
    /// <see cref="StopShortMetres"/> of where it arrives, and within <see cref="WatchSeconds"/> of
    /// getting there. With the time unknown the chase never stops.
    ///
    /// <para>The distance left is the straight line to where the fall ends, gravity included —
    /// never the time to go times the speed now, which a store thrown upwards reads as nothing at the
    /// top of its climb. And a round whose whole flight is inside the distance is ridden until its
    /// last few seconds rather than let go at release.</para>
    /// </summary>
    public static bool StopsShort(double timeToGo, double3 velocity, double3 gravity, double chargeKg)
        => double.IsFinite(timeToGo) && timeToGo >= 0.0 && timeToGo <= WatchSeconds(chargeKg)
           && Vec.Len(ArrivalFromRound(timeToGo, velocity, gravity)) <= StopShortMetres(chargeKg);

    /// <summary>
    /// How long before its arrival a chase may stop riding a round: the time its stop-short distance
    /// takes at <c>WatchMetresPerSecond</c>, so a bigger warhead is let go of earlier and further
    /// out — 4 s at the B61's 0.3 kt, 13 s at 10 kt and 44 s at 340 kt — and never under
    /// <see cref="MinWatchSeconds"/>, which leaves every conventional round to the distance alone.
    /// </summary>
    public static double WatchSeconds(double chargeKg)
        => Math.Max(MinWatchSeconds, StopShortMetres(chargeKg) / WatchMetresPerSecond);

    public const double MinWatchSeconds = 4.0;

    // Faster than a falling bomb arrives, so the time is the binding bound only for one thrown up or
    // released low, whose whole flight is inside the distance.
    private const double WatchMetresPerSecond = 500.0;

    /// <summary>Where a round arrives from where it is now, flown under gravity alone.</summary>
    public static double3 ArrivalFromRound(double timeToGo, double3 velocity, double3 gravity)
        => (velocity * timeToGo) + (gravity * (0.5 * timeToGo * timeToGo));



    /// <summary>
    /// How much of the chase's stand-off a round of this length gets, measured against the missile
    /// the stand-off was framed on — so a shell fills as much of the picture as a missile does.
    /// </summary>
    public static double StandOffScale(double bodyLength)
        => double.IsFinite(bodyLength) && bodyLength > 0.0
               ? Math.Clamp(bodyLength / FramedBodyLength, MinStandOffScale, MaxStandOffScale)
               : 1.0;

    // The 57E6's body, which the chase's stand-off distances were chosen around.
    private const double FramedBodyLength = 3.10;

    // A 5-inch shell is 0.14 of it. The floor keeps the closest approach -- 7 m, so 0.7 m at the floor --
    // clear of the engine's 0.1 m near plane; the ceiling keeps a pack's long store from being chased
    // from afar.
    private const double MinStandOffScale = 0.1;
    private const double MaxStandOffScale = 2.0;

    // A conventional round's fireball is under 11 m, so every one is watched from about this far.
    private const double MinStopShortMetres = 60.0;

    // Six radii out, so the whole ball and what it throws are in frame: a 300-tonne bomb is watched
    // from a kilometre and a 20-kiloton warhead from four.
    private const double StopShortFireballs = 6.0;

    /// <summary>
    /// Which way a held eye looks at a burst with a cloud to stand over it: at the middle of the
    /// column, but never so far above the burst that the burst leaves the frame. From the kilometres
    /// the column height was chosen at that tilt is gentle; from a few hundred metres the middle of
    /// the column is nearly straight up, and the view ends up on whatever is flying overhead.
    /// </summary>
    /// <param name="fovDeg">The vertical field of view, which is what KSA's projection takes.</param>
    public static double3 WatchBurstForward(double3 eyeToBurst, double3 eyeToColumn, double fovDeg)
    {
        double3 atBurst = Vec.Unit(eyeToBurst);
        double3 atColumn = Vec.Unit(eyeToColumn);
        if (Vec.Len2(atBurst) < 0.5) return atColumn;
        if (Vec.Len2(atColumn) < 0.5) return atBurst;

        double most = double.DegreesToRadians(BurstInFrameShare * 0.5 * fovDeg);
        if (!(most > 0.0) || Vec.AngleBetween(atBurst, atColumn) <= most) return atColumn;

        double3 toward = atColumn - (atBurst * Vec.Dot(atBurst, atColumn));
        if (Vec.Len2(toward) < 1e-12) return atBurst;

        return (atBurst * Math.Cos(most)) + (Vec.Unit(toward) * Math.Sin(most));
    }

    // How far from the centre towards the bottom edge the burst may sit.
    private const double BurstInFrameShare = 0.6;

    /// <summary>
    /// How far above the burst to look while a cloud stands, in metres.
    ///
    /// <para>The burst point is the bottom of what there is to see. Held on it, a cloud that grows
    /// a kilometre upward leaves the frame through the top — photographed at 0.60 of the rise, the
    /// cap was cut off and only the stem and skirt were in shot. Aiming at the middle of the column
    /// instead puts the whole of it across the frame.</para>
    ///
    /// <para>Below the middle rather than at it, because the cap is the wide part and wants the
    /// room: the eye is looking down the axis of something whose top half is nearly all of its
    /// volume. Zero for a charge that grows nothing, which is every conventional round.</para>
    /// </summary>
    public static double CloudAimHeightMetres(double chargeKg)
        => chargeKg < MushroomCloud.ThresholdKg
               ? 0.0
               : 0.45 * MushroomCloud.DrawnCloudTop(MushroomCloud.KilotonsFor(chargeKg));

    // How much room to leave around a cloud, in cloud heights. Standing off by exactly the height
    // puts the eye level with the crown and, flown, inside the smoke: the capture at 0.30 of the
    // rise came back a flat wall of brown. A cloud that height fills a 50 degree frame at about
    // 1.07 of it, so this is that with margin.
    private const double CloudsInFrame = 1.6;

    /// <summary>
    /// Eases a camera from where the player had it onto the chase pose, turning the look from the
    /// round onto what it is flying at.
    ///
    /// <para>The look starts on the round and is fully on <paramref name="toLookAtEcl"/> when the
    /// transition ends, on the same ease the eye travels by. In between it is a lerp of the two
    /// <b>as seen from wherever the eye has got to</b>, so the round slides from the middle of the
    /// frame towards where the settled pose puts it while the target comes in behind.</para>
    ///
    /// <para><b>Both ends of the turn are taken at one depth</b>: the round's direction is carried
    /// out to the range of the far end before the two are lerped. A round a hundred metres off
    /// lerped against a target five kilometres away is taken over by the far point almost at once —
    /// seven-eighths of a 60° turn in the first fifth of the transition. Two points at one depth
    /// lerp as their directions do, so the turn is spread over the ease; an exactly opposed pair
    /// lerps to nothing halfway, and is refused rather than normalised into NaN.</para>
    ///
    /// <para><b>Every point must be a position sampled this frame</b>, not a stored one. They are
    /// anchored to different moving things, and the ecliptic is inertial — a point captured at the
    /// start and held still falls half a kilometre behind per frame.</para>
    /// </summary>
    /// <param name="roundEcl">The round being chased, where the turn starts.</param>
    /// <param name="toLookAtEcl">
    /// Along the settled view at the range of what the round is flying at, where the turn ends.
    /// </param>
    /// <param name="t">Progress, 0 at the player's pose and 1 at the chase. Clamped.</param>
    public static bool TryBlend(double3 fromEcl, double3 toEcl,
                                double3 roundEcl, double3 toLookAtEcl,
                                double3 engineAxisEcl, double t,
                                out double3 eyeEcl, out double3 forwardEcl)
    {
        eyeEcl = toEcl;
        forwardEcl = Vec.Unit(toLookAtEcl - toEcl);

        if (!Vec.IsFinite(fromEcl) || !Vec.IsFinite(toEcl)) return false;
        if (!Vec.IsFinite(roundEcl) || !Vec.IsFinite(toLookAtEcl) || !double.IsFinite(t))
        {
            return false;
        }

        double e = Smoothstep(Math.Clamp(t, 0.0, 1.0));

        eyeEcl = fromEcl + ((toEcl - fromEcl) * e);

        double depth = Vec.Len(toLookAtEcl - eyeEcl);
        if (depth < MinAimRange) return false;

        // An eye on top of the round has no direction to it, so the far end is all there is.
        double3 toRound = roundEcl - eyeEcl;
        double3 onRound = Vec.Len(toRound) > MinAimRange
                          ? eyeEcl + (Vec.Unit(toRound) * depth)
                          : toLookAtEcl;

        double3 lookAt = onRound + ((toLookAtEcl - onRound) * e);
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
