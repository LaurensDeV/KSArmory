using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The parts of the gunner's sight that are geometry rather than strokes: where the horizontal
/// reference lies, how far the head is looking above it, and where a target that has left the
/// picture went.
///
/// <para>Separate from <see cref="Reticle"/> because these answer in the world and it answers in
/// pixels. The split matters once the sight zooms: at 16× a target moves off screen in a fraction
/// of a second, and everything here exists to say where it went.</para>
/// </summary>
public static class SightPicture
{
    /// <summary>
    /// How far a look direction is above the local horizontal (rad), positive up.
    ///
    /// <para>Against the launcher's own up rather than any celestial axis, so it reads as elevation
    /// at the site rather than as latitude.</para>
    /// </summary>
    public static double ElevationRad(double3 forwardEcl, double3 upEcl)
    {
        if (!Vec.IsFinite(forwardEcl) || !Vec.IsFinite(upEcl)) return 0.0;
        if (Vec.Len2(forwardEcl) < 1e-18 || Vec.Len2(upEcl) < 1e-18) return 0.0;

        double sine = Math.Clamp(Vec.Dot(Vec.Unit(forwardEcl), Vec.Unit(upEcl)), -1.0, 1.0);

        return Math.Asin(sine);
    }

    /// <summary>
    /// Points at zero elevation spanning <paramref name="halfAngleRad"/> either side of where the
    /// head is looking. Their projection is the sight's horizontal reference.
    ///
    /// <para>Points rather than a screen-space line: a line drawn flat across the view is only
    /// right where the camera happens to be level, and the whole reason to draw one is the case
    /// where it is not. Projecting places that genuinely sit on the horizontal plane gets the tilt
    /// for free, including whatever the engine's own roll is doing.</para>
    ///
    /// <para><b>An arc rather than two ends.</b> Level places lie on a circle around the eye, and
    /// the straight chord between two of them dips below level in the middle — by a quarter of the
    /// distance across a wide span, which is kilometres. Several short segments follow the circle
    /// instead, and the span is the caller's to match to the field of view.</para>
    ///
    /// <para>Zero when the head is looking along its own up, where the horizontal plane projects
    /// to a point and there is no line to draw.</para>
    /// </summary>
    /// <param name="distance">
    /// How far out to place them. Far enough that they read as direction rather than as objects,
    /// and near enough to stay in front of the camera.
    /// </param>
    /// <returns>How many points were written, in order across the picture.</returns>
    public static int ReferenceArc(double3 eyeEcl, double3 forwardEcl, double3 upEcl,
                                   double halfAngleRad, double distance, Span<double3> into)
    {
        if (into.Length < 2) return 0;
        if (!Vec.IsFinite(eyeEcl) || !Vec.IsFinite(forwardEcl) || !Vec.IsFinite(upEcl)) return 0;
        if (!double.IsFinite(halfAngleRad) || !double.IsFinite(distance) || distance <= 0.0) return 0;
        if (Vec.Len2(forwardEcl) < 1e-18 || Vec.Len2(upEcl) < 1e-18) return 0;

        double3 up = Vec.Unit(upEcl);
        double3 forward = Vec.Unit(forwardEcl);

        double3 right = Vec.Cross(forward, up);
        if (Vec.Len2(right) < 1e-12) return 0;
        right = Vec.Unit(right);

        // Forward with the vertical taken out. Deriving it from the cross product rather than by
        // rejecting `up` out of `forward` keeps the three axes exactly orthogonal, which is what
        // makes every point land at the same elevation.
        double3 flat = Vec.Unit(Vec.Cross(up, right));

        double half = Math.Clamp(halfAngleRad, 1e-4, 1.4);

        for (int i = 0; i < into.Length; i++)
        {
            double angle = -half + 2.0 * half * i / (into.Length - 1);

            into[i] = eyeEcl + (flat * Math.Cos(angle) + right * Math.Sin(angle)) * distance;
            if (!Vec.IsFinite(into[i])) return 0;
        }

        return into.Length;
    }

    /// <summary>
    /// Which way an off-screen contact lies, as a unit vector in screen space.
    ///
    /// <para>Necessary rather than decorative once the sight magnifies: at 16× the field is three
    /// degrees, so a target that breaks lock or a head still slewing leaves nothing on screen at
    /// all and no cue about which way to look. Fed the already-clamped edge position — putting a
    /// point on the edge is <c>KsaWorld.TryProjectOrClamp</c>'s job, and it is the only one of the
    /// two that can handle a contact behind the camera.</para>
    /// </summary>
    public static bool TryPointing(float2 from, float2 to, out float2 towards)
    {
        towards = default;

        if (!float.IsFinite(from.X) || !float.IsFinite(from.Y)) return false;
        if (!float.IsFinite(to.X) || !float.IsFinite(to.Y)) return false;

        float dx = to.X - from.X;
        float dy = to.Y - from.Y;

        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 1e-3f)) return false;

        towards = new float2(dx / length, dy / length);

        return true;
    }

    /// <summary>
    /// How near a view may come to its own up before that up is useless for building a basis.
    /// About 2.6°, which is far enough out that the cross product still has usable length.
    /// </summary>
    public const double UpUnusableAbove = 0.999;

    /// <summary>
    /// The up to build a view basis from: the one wanted, or the one used last frame when the
    /// view has swung too near it.
    ///
    /// <para><b>Continuity is the whole job.</b> A view looking along its own up has no roll — any
    /// perpendicular is equally correct — so anything that *switches rule* at that point flips the
    /// picture through half a turn as the view creeps past. Carrying the previous frame's answer
    /// through the singularity is what makes it pass rather than snap, and it works because the
    /// view can only creep: it is a rate-limited head.</para>
    ///
    /// <para><b>Sweeping through the up direction genuinely reverses which way world-up points in
    /// the picture</b>, and taking that literally is the flip. So the answer nearer last frame's is
    /// the one kept, even where that means the horizon reads inverted afterwards: a stabilised
    /// camera holds its roll through the pole and comes out upside down, rather than snapping
    /// half a turn on one frame in the middle. The reference line stays a true horizontal either
    /// way — it is drawn from places that sit on it, not from the up vector.</para>
    ///
    /// <para>False only when nothing is usable, which is a view along its own up on the very frame
    /// it was taken. There is nothing continuous to be had there — nothing to be continuous
    /// with.</para>
    /// </summary>
    /// <param name="up">
    /// Orthogonal to <paramref name="forwardEcl"/>, so the caller can hand it straight back as
    /// <paramref name="lastUp"/> next frame without it drifting into the view.
    /// </param>
    public static bool TryStableUp(double3 forwardEcl, double3 preferredUp, double3 lastUp,
                                   out double3 up)
    {
        up = Vec.Zero;

        double3 forward = Vec.Unit(forwardEcl);
        if (Vec.Len2(forward) < 0.5) return false;

        if (!Usable(forward, preferredUp, out up) && !Usable(forward, lastUp, out up)) return false;

        // The nearer of the two ways round. Both are the same line and the picture cares which end
        // of it is up, so choosing the one last frame used is what carries the roll through.
        double3 previous = Vec.Unit(lastUp);
        if (Vec.Len2(previous) > 0.5 && Vec.Dot(up, previous) < 0.0) up = -up;

        return true;
    }

    private static bool Usable(double3 forward, double3 candidate, out double3 up)
    {
        up = Vec.Zero;

        double3 unit = Vec.Unit(candidate);
        if (Vec.Len2(unit) < 0.5) return false;
        if (Math.Abs(Vec.Dot(forward, unit)) > UpUnusableAbove) return false;

        double3 across = Vec.RejectFrom(unit, forward);
        if (Vec.Len2(across) < 1e-12) return false;

        up = Vec.Unit(across);
        return true;
    }
}
