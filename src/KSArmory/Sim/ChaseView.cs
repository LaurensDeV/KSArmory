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
        upEcl = lift;

        return Vec.IsFinite(eyeEcl) && Vec.IsFinite(forwardEcl);
    }

    private static double3 AnyPerpendicular(double3 axis)
    {
        double3 candidate = Math.Abs(axis.X) < 0.9 ? new double3(1, 0, 0) : new double3(0, 1, 0);

        return Vec.Unit(Vec.Cross(axis, candidate));
    }
}
