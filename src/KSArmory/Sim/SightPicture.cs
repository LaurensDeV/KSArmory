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
}
