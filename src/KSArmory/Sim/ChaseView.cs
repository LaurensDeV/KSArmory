using Brutal.Numerics;

namespace KSArmory.Sim;

/// <summary>
/// Where to put a camera that rides behind a round in flight.
///
/// <para>The point of it is answering "where did that actually go" — a question the log can only
/// answer in numbers, and which cost a session of guessing whether the cannon was doing anything
/// at all.</para>
/// </summary>
public static class ChaseView
{
    /// <summary>
    /// Eye and forward for a camera trailing a round.
    ///
    /// <para><paramref name="velocityLocal"/> is the round's velocity with the frame's motion
    /// already removed — <c>IProjectile.VelocityLocal</c>, never <c>VelocityEcl</c>. The ecliptic
    /// carries about 29.8 km/s that every round shares, so a camera placed against the absolute
    /// velocity points the same way for every round on every heading, which looks like the chase
    /// being broken rather than the frame being wrong.</para>
    ///
    /// <para>The look point is ahead of the round rather than at it, so the round sits low in
    /// frame and what it is flying at is visible. A camera aimed at the round shows a dot against
    /// the sky and nothing of the engagement.</para>
    /// </summary>
    /// <param name="upHint">
    /// Roughly "up" — away from the planet's centre. Only used to lift the eye and to keep the
    /// horizon level; a parallel hint is ignored rather than producing a rolled view.
    /// </param>
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

        // Never let the view point straight up or straight down the reference frame's axis. KSA's
        // fixed camera crosses the view direction with that axis and normalises the result, so a
        // parallel pair is a division by zero -- and a round launched vertically is exactly that
        // on its first frames. Tilted by the smallest angle that survives it.
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

    // How nearly the view may point along the reference axis. Cosine of about 2.6 degrees off it:
    // far enough that the cross product has a length to normalise, close enough that a vertical
    // climb still looks vertical.
    private const double MaxAlongAxis = 0.999;

    /// <summary>
    /// How far back the camera should sit, closing in as the round converges on what it is
    /// shooting at.
    ///
    /// <para>Distance is what conveys speed: a camera at a fixed stand-off shows a missile that
    /// appears to hang still, because everything in frame scales together. Drawing in as the
    /// range falls makes the last second read as an arrival rather than a cut.</para>
    ///
    /// <para>Eased so the closing <em>accelerates</em> into the impact: it hangs back for most of
    /// the flight and then comes in hard over the last moment. A symmetric ease is flat at both
    /// ends, which makes it slowest exactly where the engagement is decided — the opposite of
    /// what carries the arrival.</para>
    /// </summary>
    /// <param name="range">Distance from the round to what it is aimed at.</param>
    /// <param name="far">At or beyond this range, the full stand-off.</param>
    /// <param name="near">At or inside this range, the closest the camera comes.</param>
    public static double StandOff(double range, double far, double near,
                                  double farDistance, double nearDistance)
    {
        if (!double.IsFinite(range) || !(far > near)) return farDistance;

        double t = Math.Clamp((range - near) / (far - near), 0.0, 1.0);

        // A root curve: its slope grows without bound as the range runs out, so the camera holds
        // station and then rushes in. The exponent is the whole character of the move -- lower
        // closes later and harder.
        t = Math.Pow(t, Sharpness);

        return nearDistance + ((farDistance - nearDistance) * t);
    }

    // Below one, so the closing accelerates rather than easing off. Half is a square root: still
    // three-quarters of the way out at the midpoint, and inside a third of the stand-off with a
    // tenth of the flight left.
    private const double Sharpness = 0.5;

    private static double3 AnyPerpendicular(double3 axis)
    {
        double3 candidate = Math.Abs(axis.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);

        return Vec.Unit(Vec.Cross(axis, candidate));
    }
}
