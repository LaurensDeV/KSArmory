using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The airflow a coasting body will meet where the air first turns it, solved on the conic it is on.
/// </summary>
/// <remarks>
/// Closed form because the coast above the air is a conic: energy and angular momentum are the same at every
/// point of it, so any coast state gives the same entry, within what the trim moves. All in Cci, so the
/// planet's ecliptic motion never enters; <c>docs/RV-POINTING.md</c>.
/// </remarks>
internal static class EntryDirection
{
    /// <summary>
    /// The altitude at which <see cref="BodyAttitude.Authority"/> first becomes non-zero for a body
    /// arriving at <paramref name="speed"/>, capped at where the air begins; NaN on a body with none.
    /// </summary>
    public static double AuthorityAltitude(in BodyAir air, double speed)
    {
        if (!air.HasAir || !(speed > 0.0)) return double.NaN;

        // ratio(h) * v^2 = NoAuthoritySpeed^2, ratio(h) = (rho0 / reference) * exp(-h / H)
        double atSea = air.SeaLevelDensity / Medium.ReferenceDensityKgPerM3 * speed * speed;
        double threshold = BodyAttitude.NoAuthoritySpeed * BodyAttitude.NoAuthoritySpeed;
        if (!(atSea > threshold)) return 0.0;

        double h = air.ScaleHeightMetres * Math.Log(atSea / threshold);
        return air.BoundaryMetres > 0.0 ? Math.Min(h, air.BoundaryMetres) : h;
    }

    /// <summary>
    /// The velocity relative to the ground at the first descending crossing of <paramref name="entryRadius"/>,
    /// and where that is. False on a conic that is open, radial, circular, never reaches the radius, or is
    /// already inside it -- the caller keeps the line it holds.
    /// </summary>
    public static bool TryAt(in BallisticBody body, double3 positionCci, double3 velocityCci, double entryRadius,
                             out double3 airflowCci, out double3 entryPositionCci)
    {
        airflowCci = Vec.Zero;
        entryPositionCci = Vec.Zero;

        double mu = body.Mu;
        double r = Vec.Len(positionCci);
        if (!(mu > 0.0) || !(entryRadius > 0.0) || !(r > entryRadius) || !Vec.IsFinite(velocityCci)) return false;

        double3 h = Vec.Cross(positionCci, velocityCci);
        double hLen = Vec.Len(h);
        if (!(hLen > 1e-6 * r * Vec.Len(velocityCci))) return false;

        double3 eVec = (Vec.Cross(velocityCci, h) / mu) - (positionCci / r);
        double e = Vec.Len(eVec);
        if (!(e > 1e-9) || !(e < 1.0)) return false;

        double p = hLen * hLen / mu;
        if (!(p / (1.0 + e) < entryRadius)) return false;

        double cosNu = Math.Clamp(((p / entryRadius) - 1.0) / e, -1.0, 1.0);
        double sinNu = Math.Sqrt(Math.Max(0.0, 1.0 - (cosNu * cosNu)));

        // The descending crossing is at -nu: on the way in to periapsis, radial velocity negative.
        double3 pHat = eVec / e;
        double3 qHat = Vec.Cross(h / hLen, pHat);

        entryPositionCci = ((pHat * cosNu) - (qHat * sinNu)) * entryRadius;
        double3 velocity = ((pHat * sinNu) + (qHat * (e + cosNu))) * Math.Sqrt(mu / p);

        airflowCci = velocity - body.GroundVelocityCci(entryPositionCci);
        return Vec.IsFinite(airflowCci) && !airflowCci.Equals(Vec.Zero);
    }
}
