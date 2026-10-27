namespace KSArmory;

/// <summary>
/// Where each rocket of a group aims, so that the first warhead down does not destroy the rest.
///
/// <para><b>A warhead's lethal radius reaches other warheads</b>, since the splash sweep runs over
/// rounds in the air: flown at 2,000 km with 20 kt Mk 21s, eight groups on one point measured three of
/// eight, and the more accurate the shot the worse it gets.</para>
///
/// <para>Spread along one bearing shared by the group, anchored on the operator's aim; each flight is
/// scored against its own point.</para>
/// </summary>
internal static class AimSpread
{
    /// <summary>
    /// How far apart the aim points go, in lethal radii.
    ///
    /// <para><c>spacing &gt; lethal + 2 x miss</c>; at 2,000 km the misses were 0.2-0.4 km against a 2.0 km
    /// lethal radius, so six is margin. Bounded above by wanting the group to stay one shot.</para>
    /// </summary>
    public const double SpacingInLethalRadii = 6.0;

    /// <summary>
    /// The gap between two adjacent aim points, for a round with the given lethal radius.
    ///
    /// <para>Derived, because the yield is a knob: 20 kt is 2.0 km of lethal radius, 300 kt 4.9.</para>
    /// </summary>
    public static double SpacingMetres(double lethalRadiusMetres)
        => double.IsFinite(lethalRadiusMetres) && lethalRadiusMetres > 0.0
               ? SpacingInLethalRadii * lethalRadiusMetres
               : 0.0;

    /// <summary>
    /// The bearing to spread along: square to the shot, so the rockets differ across the range
    /// rather than along it.
    ///
    /// <para>Taken once from the <i>group's</i> reference shot: per rocket, pads spread east against one
    /// target fan inward toward each other.</para>
    ///
    /// <para>NaN when the two points are the same or antipodal, where no bearing exists.</para>
    /// </summary>
    public static double CrossRangeBearingDeg(double fromLatDeg, double fromLonDeg,
                                              double toLatDeg, double toLonDeg)
    {
        double bearing = BearingDeg(fromLatDeg, fromLonDeg, toLatDeg, toLonDeg);

        return double.IsFinite(bearing) ? Wrap360(bearing + 90.0) : double.NaN;
    }

    /// <summary>
    /// Where rocket <paramref name="index"/> of <paramref name="count"/> aims.
    ///
    /// <para>Anchored rather than centred: index 0 lands on the base point, where the scenario's camera
    /// is. A single rocket, a zero spacing or an unusable bearing give the base point back.</para>
    /// </summary>
    public static (double LatitudeDeg, double LongitudeDeg) For(
        double baseLatDeg, double baseLonDeg, int index, int count,
        double spacingMetres, double bearingDeg, double bodyRadiusMetres)
    {
        if (count <= 1 || index <= 0) return (baseLatDeg, baseLonDeg);
        if (!(spacingMetres > 0.0) || !double.IsFinite(bearingDeg)) return (baseLatDeg, baseLonDeg);
        if (!(bodyRadiusMetres > 0.0)) return (baseLatDeg, baseLonDeg);

        return Along(baseLatDeg, baseLonDeg, bearingDeg, index * spacingMetres, bodyRadiusMetres);
    }

    /// <summary>
    /// The point <paramref name="distanceMetres"/> along a great circle from a start, on the given
    /// bearing. Signed: a negative distance walks the reciprocal bearing.
    ///
    /// <para>Great-circle rather than <c>metres / (R cos(lat))</c>, which diverges at the poles.</para>
    /// </summary>
    public static (double LatitudeDeg, double LongitudeDeg) Along(
        double latDeg, double lonDeg, double bearingDeg, double distanceMetres,
        double bodyRadiusMetres)
    {
        if (!(bodyRadiusMetres > 0.0) || !double.IsFinite(distanceMetres)
            || !double.IsFinite(bearingDeg) || !double.IsFinite(latDeg) || !double.IsFinite(lonDeg))
        {
            return (latDeg, lonDeg);
        }

        double lat = latDeg * Rad;
        double lon = lonDeg * Rad;
        double bearing = bearingDeg * Rad;
        double delta = distanceMetres / bodyRadiusMetres;

        double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
        double sinDelta = Math.Sin(delta), cosDelta = Math.Cos(delta);

        double sinLat2 = Math.Clamp(sinLat * cosDelta + cosLat * sinDelta * Math.Cos(bearing),
                                    -1.0, 1.0);
        double lat2 = Math.Asin(sinLat2);

        double lon2 = lon + Math.Atan2(Math.Sin(bearing) * sinDelta * cosLat,
                                       cosDelta - sinLat * sinLat2);

        return (lat2 / Rad, Wrap180(lon2 / Rad));
    }

    /// <summary>
    /// Great-circle distance between two points on a sphere, by the haversine.
    /// </summary>
    public static double GroundMetresBetween(double aLatDeg, double aLonDeg,
                                             double bLatDeg, double bLonDeg,
                                             double bodyRadiusMetres)
    {
        double sinHalfLat = Math.Sin((bLatDeg - aLatDeg) * Rad * 0.5);
        double sinHalfLon = Math.Sin((bLonDeg - aLonDeg) * Rad * 0.5);

        double h = sinHalfLat * sinHalfLat
                   + Math.Cos(aLatDeg * Rad) * Math.Cos(bLatDeg * Rad) * sinHalfLon * sinHalfLon;

        return 2.0 * bodyRadiusMetres * Math.Asin(Math.Sqrt(Math.Clamp(h, 0.0, 1.0)));
    }

    /// <summary>
    /// Initial great-circle bearing from one point to another, degrees clockwise from north. NaN
    /// where there is no such bearing — the same point, or its antipode.
    /// </summary>
    public static double BearingDeg(double fromLatDeg, double fromLonDeg,
                                    double toLatDeg, double toLonDeg)
    {
        double fromLat = fromLatDeg * Rad, toLat = toLatDeg * Rad;
        double dLon = (toLonDeg - fromLonDeg) * Rad;

        double y = Math.Sin(dLon) * Math.Cos(toLat);
        double x = Math.Cos(fromLat) * Math.Sin(toLat)
                   - Math.Sin(fromLat) * Math.Cos(toLat) * Math.Cos(dLon);

        // Both vanish where no bearing exists; due north has y = 0 alone and must survive.
        if (Math.Abs(x) < 1e-12 && Math.Abs(y) < 1e-12) return double.NaN;

        return Wrap360(Math.Atan2(y, x) / Rad);
    }

    private const double Rad = Math.PI / 180.0;

    private static double Wrap360(double deg) => ((deg % 360.0) + 360.0) % 360.0;

    private static double Wrap180(double deg)
    {
        double wrapped = Wrap360(deg);
        return wrapped > 180.0 ? wrapped - 360.0 : wrapped;
    }
}
