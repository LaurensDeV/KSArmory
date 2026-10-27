using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The parts of the gunner's sight that are geometry rather than strokes: where the horizontal
/// reference lies, how far the head is looking above it, and where a target that has left the
/// picture went.
///
/// <para>These answer in the world; <see cref="Reticle"/> answers in pixels.</para>
/// </summary>
public static class SightPicture
{
    /// <summary>
    /// How far a look direction is above the local horizontal (rad), positive up.
    ///
    /// <para>Against the launcher's own up, so it reads as elevation at the site, not latitude.</para>
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
    /// <para>World points rather than a screen-space line, so projecting them carries the camera's
    /// tilt and roll. An arc rather than two ends: level places lie on a circle round the eye.</para>
    ///
    /// <para>Zero when the head looks along its own up, where the plane projects to a point.</para>
    /// </summary>
    /// <param name="distance">How far out to place them, in metres.</param>
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

        // From the cross product rather than by rejection, so the axes are exactly orthogonal and
        // every point lands at the same elevation.
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
    /// <para>Fed the already-clamped edge position: <c>KsaWorld.TryProjectOrClamp</c> is what handles
    /// a contact behind the camera.</para>
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
    /// The up to build a view basis from: last frame's, corrected towards the one wanted by at
    /// most <paramref name="maxStepRad"/>, and by less than that the worse a reference it is.
    ///
    /// <para>Corrected rather than chosen: switching between the two at a threshold flips the picture
    /// there, measured at 89° of roll for 1.3° of aim. So there is no cutoff.</para>
    ///
    /// <para><paramref name="lastUp"/> zero seeds it with the wanted one. False only when neither is
    /// usable.</para>
    /// </summary>
    /// <param name="up">Orthogonal to <paramref name="forwardEcl"/>, to be handed back next frame.</param>
    public static bool TryStableUp(double3 forwardEcl, double3 preferredUp, double3 lastUp,
                                   double maxStepRad, out double3 up)
    {
        up = Vec.Zero;

        double3 forward = Vec.Unit(forwardEcl);
        if (Vec.Len2(forward) < 0.5) return false;

        bool wanted = Across(forward, preferredUp, out double3 target, out double authority);
        bool carried = Across(forward, lastUp, out double3 held, out _);

        if (!carried) { up = target; return wanted; }
        if (!wanted) { up = held; return true; }

        // Scaled by how much of the wanted up lies across the view, so along it, where it says
        // nothing, it stops pulling instead of being switched away from.
        double step = Math.Max(0.0, maxStepRad) * authority;
        double apart = Vec.AngleBetween(held, target);

        if (apart <= step || step <= 0.0)
        {
            up = apart <= step ? target : held;
            return true;
        }

        double3 axis = Vec.Cross(held, target);
        if (Vec.Len2(axis) < 1e-18) { up = held; return true; }

        up = Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.Unit(axis), step) * held);
        return true;
    }

    // The length is the sine of the angle to the view, which is how good a roll reference it is.
    private static bool Across(double3 forward, double3 candidate, out double3 across,
                               out double authority)
    {
        across = Vec.Zero;
        authority = 0.0;

        double3 unit = Vec.Unit(candidate);
        if (Vec.Len2(unit) < 0.5) return false;

        double3 rejected = Vec.RejectFrom(unit, forward);
        double length = Vec.Len(rejected);
        if (length < 1e-6) return false;

        across = rejected / length;
        authority = length;
        return true;
    }
}
