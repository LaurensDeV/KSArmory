using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Works out the orbit-camera angles that would point the view at something.
///
/// <para>KSA's orbit camera is driven by an azimuth and an elevation about a reference frame, and
/// its controller rewrites the camera from those every frame. So the way to aim that camera is to
/// set the angles it is already reading — not to write the camera, which is overwritten before it
/// renders, and not to switch the viewport to a fixed mode, which takes the view away from the
/// player entirely. Which of the two copies of those angles is the writable one is a KSA fact, and
/// lives with the caller.</para>
///
/// <para>The frame those angles are measured in is private. It does not have to be: the
/// controller builds the camera's own basis out of it, so the frame can be recovered <em>from</em>
/// that basis. Given the camera's forward and right and the angles that produced them, the
/// frame's vertical is <c>normalise(right × horizontal)</c>, and that is all the aiming needs.</para>
///
/// <para>No KSA types — the caller reads the camera and applies the answer, which is what makes
/// the geometry testable.</para>
/// </summary>
public static class OrbitAim
{
    /// <summary>
    /// One frame's view of the orbit camera. The two angle pairs are not the same numbers: the
    /// shown pair is what built the camera basis this frame, and the stored pair is the one a
    /// mouse drag moves and the only one worth writing.
    /// </summary>
    public readonly record struct Reading(
        double3 Forward, double3 Right,
        double ShownAzimuth, double ShownElevation,
        double StoredAzimuth, double StoredElevation);

    /// <summary>
    /// Whether two angle pairs mean the same aim, allowing for a whole turn between them.
    ///
    /// <para>Used to notice the player taking the view back: the stored angles are exactly what
    /// was last written unless something else moved them, so any difference is somebody else's.
    /// </para>
    /// </summary>
    public static bool SameAim(double azimuth, double elevation,
                               double otherAzimuth, double otherElevation, double toleranceRad)
        => Math.Abs(WrapPi(azimuth - otherAzimuth)) <= toleranceRad
           && Math.Abs(elevation - otherElevation) <= toleranceRad;

    /// <summary>
    /// The azimuth and elevation that would point the camera along <paramref name="desired"/>.
    /// </summary>
    /// <param name="forward">Where the camera looks now.</param>
    /// <param name="right">The camera's right, which is the axis its elevation turns about.</param>
    /// <param name="azimuth">The controller's azimuth now (rad).</param>
    /// <param name="elevation">The controller's elevation now (rad).</param>
    /// <param name="desired">Where the camera should look.</param>
    public static bool TrySolve(double3 forward, double3 right, double azimuth, double elevation,
                                double3 desired, out double toAzimuth, out double toElevation)
    {
        toAzimuth = azimuth;
        toElevation = elevation;

        if (!Vec.IsFinite(forward) || !Vec.IsFinite(right) || !Vec.IsFinite(desired)) return false;
        if (!double.IsFinite(azimuth) || !double.IsFinite(elevation)) return false;
        if (Vec.Len(forward) < 1e-9 || Vec.Len(right) < 1e-9 || Vec.Len(desired) < 1e-9) return false;

        double3 f = Vec.Unit(forward);
        double3 r = Vec.Unit(right);
        double3 d = Vec.Unit(desired);

        // Undo the elevation to recover the horizontal the azimuth was measured along, then the
        // frame's vertical follows from it and the camera's right.
        double3 horizontal = Rotate(f, r, -elevation);
        double3 up = Vec.Unit(Vec.Cross(r, horizontal));
        if (!Vec.IsFinite(up) || Vec.Len(up) < 0.5) return false;

        // Elevation is the rise out of that plane. The controller builds forward by rotating the
        // horizontal about the right by the elevation, and that rotation moves it exactly towards
        // the frame's vertical, so the component along it is the sine of the angle.
        double rise = Math.Clamp(Vec.Dot(d, up), -1.0, 1.0);
        toElevation = Math.Asin(rise);

        // Azimuth as a *delta* from where it is, which sidesteps needing the frame's horizontal
        // reference direction at all — only the turn between two directions about the vertical.
        double3 flat = Vec.Unit(d - up * Vec.Dot(d, up));
        if (!Vec.IsFinite(flat) || Vec.Len(flat) < 1e-6)
        {
            // Straight up or straight down: the azimuth is undefined, so leave it be and let the
            // elevation do the work.
            toAzimuth = azimuth;
            return true;
        }

        double turn = Math.Atan2(Vec.Dot(Vec.Cross(horizontal, flat), up),
                                 Vec.Dot(horizontal, flat));
        toAzimuth = azimuth + turn;
        return true;
    }

    /// <summary>
    /// Eases a pair of angles towards a target, framerate independently.
    ///
    /// <para>Exponential rather than linear: it starts at once and slows as it arrives, which
    /// reads as being nudged rather than snapped. <paramref name="rate"/> is the fraction of the
    /// remaining angle covered per second.</para>
    /// </summary>
    public static void Ease(ref double azimuth, ref double elevation,
                            double toAzimuth, double toElevation, double rate, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0.0) return;
        if (!double.IsFinite(toAzimuth) || !double.IsFinite(toElevation)) return;

        double k = 1.0 - Math.Exp(-Math.Max(rate, 0.0) * dt);
        azimuth += WrapPi(toAzimuth - azimuth) * k;
        elevation += (toElevation - elevation) * k;
    }

    /// <summary>True once the angles are close enough that nudging further would not show.</summary>
    public static bool Arrived(double azimuth, double elevation,
                               double toAzimuth, double toElevation, double toleranceRad)
        => Math.Abs(WrapPi(toAzimuth - azimuth)) <= toleranceRad
           && Math.Abs(toElevation - elevation) <= toleranceRad;

    /// <summary>
    /// The shortest way round, so a turn never takes the long way about. Half open: the result
    /// is in [-pi, pi), so exactly half a turn comes back as -pi rather than +pi.
    /// </summary>
    public static double WrapPi(double radians)
    {
        if (!double.IsFinite(radians)) return 0.0;

        double x = (radians + Math.PI) % (2.0 * Math.PI);
        if (x < 0.0) x += 2.0 * Math.PI;
        return x - Math.PI;
    }

    // Rodrigues, for a unit axis. Only ever used here to undo one rotation.
    private static double3 Rotate(double3 v, double3 axis, double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return v * c + Vec.Cross(axis, v) * s + axis * (Vec.Dot(axis, v) * (1.0 - c));
    }
}
