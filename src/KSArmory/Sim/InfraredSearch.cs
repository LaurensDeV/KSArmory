using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Whether a passive infrared set sees a contact: its heat from <see cref="Signature"/>, thinned by
/// the air on the path and spread over the range squared, against the set's threshold.
/// </summary>
public static class InfraredSearch
{
    /// <summary>
    /// What sea-level air takes out of the mid-wave window per metre: 0.4/km, humid air's order.
    /// </summary>
    public const double ExtinctionPerMetre = 4.0e-4;

    /// <summary>
    /// Water vapour carries the window's absorption, and its scale height is about a quarter of the
    /// air's, so the extinction goes as the density ratio to this power. Linear in density, no
    /// constant gives both a fighter at 60 km from 10 km up and an idling craft at sea level at a few.
    /// </summary>
    public const double VapourPower = 4.0;

    /// <summary>
    /// What reaches the set, 0 to 1: Beer-Lambert over a straight path whose vapour falls
    /// exponentially from one end to the other, which is the log-mean of the two ends.
    /// </summary>
    public static double Transmission(double rangeMetres, double airRatioAtEye, double airRatioAtTarget)
    {
        double a = Math.Pow(Math.Max(0.0, airRatioAtEye), VapourPower);
        double b = Math.Pow(Math.Max(0.0, airRatioAtTarget), VapourPower);

        double vapour = a == b || a <= 0.0 || b <= 0.0
                            ? 0.5 * (a + b)
                            : (a - b) / Math.Log(a / b);

        return Math.Exp(-ExtinctionPerMetre * vapour * Math.Max(rangeMetres, 0.0));
    }

    /// <summary>What a contact of this heat puts on the set's aperture (W/m²).</summary>
    public static double Irradiance(double intensityKwPerSr, double rangeMetres, double airRatioAtEye,
                                    double airRatioAtTarget)
    {
        if (!(intensityKwPerSr > 0.0) || !(rangeMetres > 0.0)) return 0.0;
        return intensityKwPerSr * 1000.0 * Transmission(rangeMetres, airRatioAtEye, airRatioAtTarget)
               / (rangeMetres * rangeMetres);
    }

    /// <summary>
    /// Whether the line from the eye, carried on past the contact, meets the body: the contact is
    /// then seen against warm ground rather than cold sky.
    /// </summary>
    public static bool SeenAgainstGround(double3 eyeEcl, double3 targetEcl, double3 bodyCentreEcl,
                                         double bodyRadius)
    {
        if (!(bodyRadius > 0.0)) return false;

        double3 line = targetEcl - eyeEcl;
        double length = Vec.Len(line);
        if (!(length > 0.0)) return false;

        double3 dir = line / length;
        double3 fromCentre = targetEcl - bodyCentreEcl;
        double along = Vec.Dot(fromCentre, dir);
        double square = Vec.Len2(fromCentre) - (bodyRadius * bodyRadius);

        // A contact under the mean sphere is on the ground already.
        if (square <= 0.0) return true;
        if (along >= 0.0) return false;

        return (along * along) - square >= 0.0;
    }

    /// <summary>Whether a set with this threshold sees a contact of this heat at this range.</summary>
    public static bool Sees(SensorProfile sensor, double intensityKwPerSr, double rangeMetres,
                            double airRatioAtEye, double airRatioAtTarget, bool againstGround)
    {
        double threshold = sensor.IrThresholdWPerM2 * (againstGround ? Math.Max(1f, sensor.LookDownPenalty) : 1f);
        return Irradiance(intensityKwPerSr, rangeMetres, airRatioAtEye, airRatioAtTarget) >= threshold;
    }

    /// <summary>
    /// <see cref="Sees(SensorProfile,double,double,double,double,bool)"/> from the eye's and the
    /// contact's positions, and the body under the eye.
    /// </summary>
    public static bool Sees(SensorProfile sensor, double intensityKwPerSr, double3 eyeEcl, double3 targetEcl,
                            double airRatioAtEye, double airRatioAtTarget, double3 bodyCentreEcl, double bodyRadius)
        => Sees(sensor, intensityKwPerSr, Vec.Len(targetEcl - eyeEcl), airRatioAtEye, airRatioAtTarget,
                SeenAgainstGround(eyeEcl, targetEcl, bodyCentreEcl, bodyRadius));
}
