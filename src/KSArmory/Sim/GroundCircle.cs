using System;

namespace KSArmory;

/// <summary>
/// A ring of ground a fixed distance from a place — what a blast radius is, drawn on a map.
///
/// <para>Not a circle on an equirectangular map: it widens in longitude by 1/cos(latitude) and wraps
/// whole at a pole. Each point is walked on the sphere with the great-circle destination formula,
/// which holds over a pole and at any radius.</para>
/// </summary>
internal static class GroundCircle
{
    /// <summary>
    /// The place <paramref name="metres"/> away from another along <paramref name="bearingRad"/>,
    /// clockwise from north.
    /// </summary>
    public static void At(double latitudeDeg, double longitudeDeg, double metres,
                          double bodyRadiusMetres, double bearingRad,
                          out double outLatitudeDeg, out double outLongitudeDeg)
    {
        outLatitudeDeg = latitudeDeg;
        outLongitudeDeg = longitudeDeg;

        if (!(bodyRadiusMetres > 0.0) || !double.IsFinite(metres) || metres == 0.0) return;

        double delta = metres / bodyRadiusMetres;
        double lat = double.DegreesToRadians(latitudeDeg);
        double lon = double.DegreesToRadians(longitudeDeg);

        double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
        double sinDelta = Math.Sin(delta), cosDelta = Math.Cos(delta);

        double to = Math.Asin(Math.Clamp((sinLat * cosDelta)
                                         + (cosLat * sinDelta * Math.Cos(bearingRad)), -1.0, 1.0));

        double across = Math.Atan2(Math.Sin(bearingRad) * sinDelta * cosLat,
                                   cosDelta - (sinLat * Math.Sin(to)));

        outLatitudeDeg = double.RadiansToDegrees(to);
        outLongitudeDeg = double.RadiansToDegrees(lon + across);
    }

    /// <summary>
    /// How tall the ring is in degrees of latitude — the cheap bound, for deciding whether it is
    /// worth drawing at all before any of it is walked.
    /// </summary>
    public static double HeightDeg(double metres, double bodyRadiusMetres)
        => bodyRadiusMetres > 0.0
               ? double.RadiansToDegrees(metres / bodyRadiusMetres)
               : 0.0;
}
