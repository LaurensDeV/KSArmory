namespace KSArmory;

/// <summary>
/// Where each rocket of a group aims, so that the first warhead down does not destroy the rest.
///
/// <para><b>A warhead kills inside <see cref="Warhead.LethalRadius"/>, and that reaches other
/// warheads.</b> The splash sweep runs over rounds in the air as well as craft — which is how one
/// missile intercepts another — so eight rockets aimed at one point are eight groups inside each
/// other's kill radius. Flown at 2,000 km with 20 kt Mk 21s: the first group down at 229 m took
/// thirty warheads of five other rockets with it, in one frame, and those score as never having
/// arrived. Three of eight flights were measured in eighteen of twenty shots.</para>
///
/// <para><b>It gets worse as the shots get better.</b> At 12,902 km the misses are tens of
/// kilometres and no group is inside another's two, so nothing is lost; the failure appeared only
/// once the geometry was accurate enough to put every warhead within a kilometre of the same spot.
/// An instrument that discards its sample in proportion to how well the thing being measured works
/// is not one to leave standing.</para>
///
/// <para>Spread along one bearing shared by the whole group, anchored on the operator's own aim so
/// that the first rocket still lands where they asked and the camera is. Each flight is scored
/// against its own point, so nothing about the measurement changes except how much of it
/// survives.</para>
/// </summary>
internal static class AimSpread
{
    /// <summary>
    /// How far apart the aim points go, in lethal radii.
    ///
    /// <para>The requirement is that a burst at one point cannot reach a warhead arriving at the
    /// next, <i>including</i> both misses: <c>spacing &gt; lethal + 2 x miss</c>. At 2,000 km the
    /// misses are 0.2-0.4 km against a 2.0 km lethal radius, so three radii would do and six is the
    /// margin. It is bounded above by wanting the group to stay one shot: at 12 km a piece the
    /// outermost of eight sits 84 km across a 2,000 km shot, which is under two kilometres of
    /// downrange difference once it is spread square to the flight.</para>
    /// </summary>
    public const double SpacingInLethalRadii = 6.0;

    /// <summary>
    /// The gap between two adjacent aim points, for a round with the given lethal radius.
    ///
    /// <para>Derived rather than typed in, because the yield is a knob — <c>ReentryVehicleMk21</c>
    /// ships at 20 kt and says in as many words that the real 300 kt is one number away. That is
    /// 2.0 km of lethal radius against 4.9, and a constant chosen for the first is far too small
    /// for the second.</para>
    /// </summary>
    public static double SpacingMetres(double lethalRadiusMetres)
        => double.IsFinite(lethalRadiusMetres) && lethalRadiusMetres > 0.0
               ? SpacingInLethalRadii * lethalRadiusMetres
               : 0.0;

    /// <summary>
    /// The bearing to spread along: square to the shot, so the rockets differ across the range
    /// rather than along it.
    ///
    /// <para>Taken once from the <i>group's</i> reference shot rather than per rocket. Each rocket
    /// working out its own perpendicular points them in different directions — pads spread east
    /// against one target fan inward — and two rockets displaced towards each other are the
    /// fratricide this exists to stop.</para>
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
    /// <para>Anchored rather than centred: index 0 lands on <paramref name="baseLatDeg"/> /
    /// <paramref name="baseLonDeg"/> exactly. The operator named that point and the scenario has
    /// already moved the watched site to it, so shifting the whole group off it to keep the
    /// centroid tidy would move the shot away from its own camera.</para>
    ///
    /// <para>A single rocket, a zero spacing or an unusable bearing all give the base point back
    /// unchanged — a spread that cannot be computed must not silently become a displacement of
    /// nothing in particular.</para>
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
    /// <para>The great-circle form rather than <c>metres / (R cos(lat))</c> because the cheap one
    /// diverges at the poles and is wrong well before it — and a scenario is free to aim anywhere.
    /// </para>
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

        // Both components vanish at the two points where a bearing is not defined, and Atan2 answers
        // zero there rather than refusing. Due north is a real answer and must survive, so the test
        // is on the pair being degenerate rather than on y alone.
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
