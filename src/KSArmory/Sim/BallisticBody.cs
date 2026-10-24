using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The planet a ballistic arc is flown around, as the four numbers an arc actually needs.
///
/// <para>Everything here is body-centred inertial (<c>Cci</c>), so the planet's ~29.8 km/s of ecliptic
/// motion is out of every term; <c>docs/FRAMES-AND-EPOCHS.md</c>.</para>
///
/// <para><see cref="SurfaceRadius"/> is the mean sphere, so an arc solved against it is a first answer;
/// the terminal correction is flown against the real height field.</para>
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
    /// Where a point fixed to the ground has been carried to after <paramref name="seconds"/>: over a
    /// half-hour flight Earth turns 7.5 degrees, 830 km at the equator.
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
    /// The same point as if the planet had not turned since the epoch: the inverse of
    /// <see cref="CarryCci"/>, which turns an inertial impact point back into a place on the map.
    /// </summary>
    public double3 UncarryCci(double3 positionCci, double seconds) => CarryCci(positionCci, -seconds);

    /// <summary>Height above the mean sphere.</summary>
    public double AltitudeOf(double3 positionCci) => positionCci.Length() - SurfaceRadius;

    public bool IsUsable => Mu > 0.0 && SurfaceRadius > 0.0;
}
