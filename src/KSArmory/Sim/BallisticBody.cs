using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The planet a ballistic arc is flown around, as the four numbers an arc actually needs.
///
/// <para>Everything here is body-centred inertial — the frame KSA calls <c>Cci</c>, not the
/// ecliptic. A trajectory that leaves the ground and comes back to it is a two-body problem about
/// one planet, and flying it in <c>Ecl</c> would carry ~29.8 km/s of the planet's own motion
/// through every term of a solve that lasts half an hour. The carrier is already subtracted in
/// Cci, which is the whole reason to work there; <c>docs/FRAMES-AND-EPOCHS.md</c> has what
/// happens when it is not.</para>
///
/// <para><see cref="SurfaceRadius"/> is the mean sphere. Real ground stands above and below it by
/// kilometres, so an arc solved against it is a first answer rather than the last one — the
/// terminal correction is flown against the actual height field, not against this.</para>
/// </summary>
internal readonly record struct BallisticBody(
    double Mu,
    double SurfaceRadius,
    double3 SpinAxisCci,
    double SpinRateRadPerSec)
{
    /// <summary>Gravitational acceleration at a point, as the inverse-square law and nothing else.</summary>
    public double3 GravityCci(double3 positionCci)
    {
        double r = positionCci.Length();
        if (!(r > 1.0)) return Vec.Zero;
        return positionCci * (-Mu / (r * r * r));
    }

    /// <summary>
    /// Where a point fixed to the ground has been carried to after <paramref name="seconds"/>.
    ///
    /// <para>This is what makes a target on a spinning planet a moving target. Over a half-hour
    /// flight Earth turns 7.5 degrees, which at the equator is 830 km — so a solve that treats the
    /// aim point as stationary misses by most of a continent.</para>
    /// </summary>
    public double3 CarryCci(double3 positionCci, double seconds)
    {
        double3 axis = Vec.Unit(SpinAxisCci);
        if (axis.Equals(Vec.Zero) || SpinRateRadPerSec == 0.0) return positionCci;
        return doubleQuat.CreateFromAxisAngle(axis, SpinRateRadPerSec * seconds) * positionCci;
    }

    /// <summary>The inertial velocity a point on the ground has purely from the spin.</summary>
    public double3 GroundVelocityCci(double3 positionCci)
        => Vec.Cross(Vec.Unit(SpinAxisCci) * SpinRateRadPerSec, positionCci);

    /// <summary>
    /// The same point expressed as if the planet had not turned since the epoch.
    ///
    /// <para>The inverse of <see cref="CarryCci"/>, and the step that turns an impact point in
    /// inertial space back into a place on the map. Skipping it reports every impact displaced
    /// east by the whole flight time's worth of rotation.</para>
    /// </summary>
    public double3 UncarryCci(double3 positionCci, double seconds) => CarryCci(positionCci, -seconds);

    /// <summary>Height above the mean sphere.</summary>
    public double AltitudeOf(double3 positionCci) => positionCci.Length() - SurfaceRadius;

    public bool IsUsable => Mu > 0.0 && SurfaceRadius > 0.0;
}
