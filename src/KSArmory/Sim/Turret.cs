using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The turret's azimuth drive: where it is pointing, where it has been told to point, and how
/// fast it is allowed to get there.
///
/// <para>Deliberately free of KSA types, like <see cref="Interceptor"/> and <see cref="Vec"/> —
/// the test project links this file directly so the slewing can be exercised without the game.
/// Adding a <c>using KSA;</c> here breaks the tests.</para>
///
/// <para>Angles are radians about the part's X axis, which is the vehicle's up. Zero points
/// along +Y, the direction the vehicle drives and the way the tracking array faces at rest.</para>
/// </summary>
public sealed class Turret
{
    /// <summary>Where the turret is actually pointing. Wrapped to (-pi, pi].</summary>
    public double BearingRad { get; private set; }

    /// <summary>Where it has been told to point, or null if it has no order.</summary>
    public double? CommandRad { get; private set; }

    /// <summary>How far above the horizon the pods are pitched.</summary>
    public double ElevationRad { get; private set; } = DefaultRestElevation;

    /// <summary>Elevation it has been told to hold, or null if it has no order.</summary>
    public double? CommandElevationRad { get; private set; }

    /// <summary>Elevation the pods sit at with nothing to look at.</summary>
    public const double DefaultRestElevation = 0.9599; // 55 degrees, the modelled pose

    /// <summary>
    /// Travel limits on the elevation drive. The floor is level, not slightly below it: a real
    /// launcher does not depress past horizontal, and there is nothing worth shooting at down
    /// there anyway — the battery defends the sky above itself.
    /// </summary>
    public double MinElevationRad { get; set; }
    public double MaxElevationRad { get; set; } = double.DegreesToRadians(82);

    /// <summary>
    /// Depression limit over the vehicle's own bodywork, and how wide that arc is.
    ///
    /// <para>Pointing forward, the pods would swing down through the auxiliary power unit
    /// behind the cab. A real launcher has a mechanical cutout stopping exactly that; without
    /// one the tubes just pass through the hull, which looks like the bug it is.</para>
    ///
    /// <para>A flat depression limit everywhere would be simpler and worse: the pods can
    /// legitimately come right down to level once traversed off the beam, and that is the shot
    /// against something skimming the horizon.</para>
    /// </summary>
    public double ForwardMinElevationRad { get; set; } = double.DegreesToRadians(15);
    public double ForwardArcRad { get; set; } = double.DegreesToRadians(50);

    /// <summary>Lowest elevation the pods may take at a given bearing, easing across the arc
    /// edge so traversing into the forward sector lifts them rather than snapping them up.</summary>
    public double DepressionFloorAt(double bearingRad)
    {
        double offAxis = Math.Abs(WrapPi(bearingRad));
        if (offAxis >= ForwardArcRad || ForwardArcRad <= 0.0) return MinElevationRad;

        double t = offAxis / ForwardArcRad;
        return Math.Max(MinElevationRad, ForwardMinElevationRad * (1.0 - t * t));
    }

    /// <summary>Angle still to cover in traverse, signed. Zero when there is no command.</summary>
    public double ErrorRad => CommandRad is { } command ? WrapPi(command - BearingRad) : 0.0;

    /// <summary>Angle still to cover in elevation, signed.</summary>
    public double ElevationErrorRad
        => CommandElevationRad is { } command ? command - ElevationRad : 0.0;

    /// <summary>True once *both* axes are within a few degrees of their order.</summary>
    public bool OnTarget => CommandRad is not null
                            && Math.Abs(ErrorRad) < 0.05
                            && Math.Abs(ElevationErrorRad) < 0.05;

    /// <summary>
    /// Unbroken seconds spent on target. Fire control waits on this rather than on
    /// <see cref="OnTarget"/> alone, so a round is never released during the instant the
    /// launcher happens to sweep across the aim point on its way somewhere else.
    /// </summary>
    public double SecondsOnTarget { get; private set; }

    /// <summary>True once the launcher has been steady on the aim point for long enough.</summary>
    public bool IsLaid(double settleSeconds) => OnTarget && SecondsOnTarget >= settleSeconds;

    /// <summary>
    /// Bearing that points the turret along <paramref name="directionPartFrame"/>.
    ///
    /// Rotating by <c>a</c> about +X carries +Y to <c>(0, cos a, sin a)</c>, so the bearing of a
    /// direction is just the angle of its (Y, Z) components. The X component — how far above or
    /// below the horizon the target sits — is deliberately dropped: this is an azimuth drive,
    /// and the missile pods are at a fixed elevation.
    /// </summary>
    public static double BearingTo(double3 directionPartFrame)
        => Math.Atan2(directionPartFrame.Z, directionPartFrame.Y);

    /// <summary>
    /// Elevation that points the pods at a direction: its angle above the plane the turret
    /// traverses in. X is up, so that is X against the length of the (Y, Z) part.
    /// </summary>
    public static double ElevationTo(double3 directionPartFrame)
        => Math.Atan2(directionPartFrame.X,
                      Math.Sqrt(directionPartFrame.Y * directionPartFrame.Y
                                + directionPartFrame.Z * directionPartFrame.Z));

    /// <summary>Orders the launcher onto a direction given in the part's own frame.</summary>
    public void Track(double3 directionPartFrame)
    {
        if (!Vec.IsFinite(directionPartFrame)) return;
        if (Vec.Len2(directionPartFrame) < 1e-12) return;

        CommandRad = BearingTo(directionPartFrame);
        CommandElevationRad = ClampElevation(ElevationTo(directionPartFrame), CommandRad.Value);
    }

    /// <summary>Orders both axes directly, bypassing the radar. Used by the manual override.</summary>
    public void Point(double bearingRad, double? elevationRad = null)
    {
        if (double.IsFinite(bearingRad)) CommandRad = WrapPi(bearingRad);
        if (elevationRad is { } elevation && double.IsFinite(elevation))
        {
            CommandElevationRad = ClampElevation(elevation, CommandRad ?? BearingRad);
        }
    }

    /// <summary>Sends the launcher back to rest: facing forward, pods at their modelled pose.</summary>
    public void Stow(double restElevationRad = DefaultRestElevation)
    {
        CommandRad = 0.0;
        CommandElevationRad = ClampElevation(restElevationRad, 0.0);
    }

    /// <summary>Drops the order and leaves the launcher where it is.</summary>
    public void Hold()
    {
        CommandRad = null;
        CommandElevationRad = null;
    }

    private double ClampElevation(double elevation, double atBearingRad)
        => !double.IsFinite(elevation)
            ? ElevationRad
            : Math.Clamp(elevation, DepressionFloorAt(atBearingRad), MaxElevationRad);

    /// <summary>
    /// Advances the drive. Turns the short way round and never faster than
    /// <paramref name="slewRateRadPerSec"/>, so the turret sweeps rather than snapping — which
    /// is both what the real thing does and the only way the motion reads as motion on screen.
    /// </summary>
    public void Update(double dt, double slewRateRadPerSec, double elevationRateRadPerSec)
    {
        if (!(dt > 0.0)) return;

        if (CommandRad is { } command && slewRateRadPerSec > 0.0)
        {
            BearingRad = StepToward(BearingRad, command, slewRateRadPerSec * dt);
        }

        if (CommandElevationRad is { } elevation && elevationRateRadPerSec > 0.0)
        {
            // No wrapping here: elevation is a limited arc, not a circle, so stepping is a
            // plain clamped move. Wrapping it would let the pods take the "short way" through
            // the deck to reach a high angle.
            double step = elevationRateRadPerSec * dt;
            double error = elevation - ElevationRad;
            ElevationRad += Math.Abs(error) <= step ? error : Math.Sign(error) * step;
        }

        // The interlock, enforced against where the turret *is* rather than where it was told
        // to go. Traversing into the forward arc with the pods low has to lift them out of the
        // bodywork on the way round, not once it arrives.
        ElevationRad = Math.Clamp(ElevationRad, DepressionFloorAt(BearingRad), MaxElevationRad);

        SecondsOnTarget = OnTarget ? SecondsOnTarget + dt : 0.0;
    }

    /// <summary>Moves <paramref name="from"/> toward <paramref name="to"/> the short way, by at
    /// most <paramref name="maxStep"/>.</summary>
    public static double StepToward(double from, double to, double maxStep)
    {
        double error = WrapPi(to - from);
        if (Math.Abs(error) <= maxStep) return WrapPi(to);
        return WrapPi(from + Math.Sign(error) * maxStep);
    }

    /// <summary>Folds an angle into (-pi, pi].</summary>
    public static double WrapPi(double angle)
    {
        if (!double.IsFinite(angle)) return 0.0;

        angle %= Math.Tau;
        if (angle > Math.PI) angle -= Math.Tau;
        else if (angle <= -Math.PI) angle += Math.Tau;
        return angle;
    }

    /// <summary>Forgets everything. Used when the battery changes platform.</summary>
    public void Reset()
    {
        BearingRad = 0.0;
        ElevationRad = DefaultRestElevation;
        CommandRad = null;
        CommandElevationRad = null;
        SecondsOnTarget = 0.0;
    }
}
