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
    /// The up to build a view basis from: last frame's, corrected towards the one wanted by at
    /// most <paramref name="maxStepRad"/>, and by less than that the worse a reference it is.
    ///
    /// <para><b>Corrected rather than chosen.</b> Two earlier versions picked between the wanted
    /// up and the carried one at a threshold, and both flipped the picture — because switching
    /// rule is discontinuous wherever the switch is, and the two rules disagree by however far the
    /// carried one has drifted. Moving the threshold moves the flip; it does not remove it.
    /// Measured at 89° of roll for 1.3° of aim, with the view sitting exactly on the cutoff.</para>
    ///
    /// <para>So there is no cutoff. The reference is always the carried one, and the wanted one
    /// only ever pulls it — quickly where it is a good reference, not at all where the view lies
    /// along it and it says nothing. A stabilised head is a control loop, not a lookup.</para>
    ///
    /// <para><paramref name="lastUp"/> zero seeds it: with nothing carried there is nothing to
    /// correct, so the wanted one is taken outright. False only when neither is usable, which is a
    /// view along its own up on the very frame it was taken.</para>
    /// </summary>
    /// <param name="up">
    /// Orthogonal to <paramref name="forwardEcl"/>, so the caller hands it straight back next
    /// frame without it drifting into the view.
    /// </param>
    public static bool TryStableUp(double3 forwardEcl, double3 preferredUp, double3 lastUp,
                                   double maxStepRad, out double3 up)
    {
        up = Vec.Zero;

        double3 forward = Vec.Unit(forwardEcl);
        if (Vec.Len2(forward) < 0.5) return false;

        bool wanted = Across(forward, preferredUp, out double3 target, out double authority);
        bool carried = Across(forward, lastUp, out double3 held, out _);

        // Nothing carried yet, or nothing to carry towards: whichever exists is the answer.
        if (!carried) { up = target; return wanted; }
        if (!wanted) { up = held; return true; }

        // How far the wanted up may pull this frame. Scaled by how much of it lies across the
        // view: none of it does when the view is along it, and that is exactly when its direction
        // is meaningless — so it stops pulling instead of being switched away from.
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

    // The part of a candidate lying across the view, and how much of it there is. The length is
    // the sine of the angle between them, which is precisely how good a roll reference it is.
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
