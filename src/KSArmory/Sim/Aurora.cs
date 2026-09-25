using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The aurora a burst above the atmosphere makes where the planet's field brings its charged debris
/// back down into the air: near the burst, and at the other end of the same field line in the other
/// hemisphere.
///
/// <para>A burst in near-vacuum throws out charged particles -- the fission debris, ionised, and the
/// electrons it decays with -- which cannot cross the magnetic field and spiral along it. Where a field
/// line comes back down to about 100 km they strike the air and light it as the solar wind does.
/// Teak, 3.8 Mt at 77 km over Johnston Island in 1958, lit an aurora over Samoa 3,200 km away: the
/// magnetic conjugate point. Starfish Prime did the same, and its trapped electrons made a radiation
/// belt that lasted years.</para>
///
/// <para><b>The field is a dipole on the spin axis</b>, because KSA has none: a field line is
/// <c>r = L R cos²λ</c> in magnetic latitude λ, so a burst sets its line's <c>L</c> and the line meets
/// the aurora's altitude at the latitude that solves it, north and south, on the burst's own meridian.
/// The brightness is drawn, not measured.</para>
/// </summary>
internal static class Aurora
{
    /// <summary>
    /// The air, against sea level, above which a burst's debris is free to run along the field rather
    /// than being stopped in the fireball: about 70 km, which takes in Teak's 77.
    /// </summary>
    public const double AirRatio = 1.0e-4;

    /// <summary>Whether a burst in air this thin makes an aurora.</summary>
    public static bool Lights(double airRatio) => double.IsFinite(airRatio) && airRatio < AirRatio;

    /// <summary>
    /// The curtain's sharp lower border over the mean sphere (m). The green of oxygen peaks a little above
    /// it, at 110-115 km, and in a bright display nitrogen fringes it pink just below.
    /// </summary>
    public const double BottomAltitude = 100_000.0;

    /// <summary>
    /// Where it is gone above (m): the red of oxygen peaks at 200-250 km and fades out through this.
    /// </summary>
    public const double TopAltitude = 400_000.0;

    /// <summary>How long it glows at all (s), and the e-folding time it fades on.</summary>
    public const double LifeSeconds = 900.0;

    /// <inheritdoc cref="LifeSeconds"/>
    public const double FadeSeconds = 240.0;

    /// <summary>How long after the burst it lights: the particles are seconds along the field line.</summary>
    public const double ArrivesSeconds = 3.0;

    /// <summary>Its brightness at a megatonne, looking through the curtain edge-on from under it.</summary>
    public const double PeakNits = 0.25;

    /// <summary>
    /// Where the burst's field line comes down to the aurora's altitude, as unit directions from the
    /// planet's centre: the end in the burst's own hemisphere first, then its conjugate. None when the
    /// line never climbs that high -- a burst under the aurora's altitude on the magnetic equator -- and
    /// one where the burst sits on the equator line itself.
    /// </summary>
    /// <param name="burstFromCentre">The burst, from the planet's centre, in any frame the axis is in.</param>
    /// <param name="axis">The dipole's axis, the spin axis.</param>
    public static int Footpoints(double3 burstFromCentre, double3 axis, double planetRadius,
                                 Span<double3> into)
    {
        double r = Vec.Len(burstFromCentre);
        if (!(r > 0.0) || !(planetRadius > 0.0) || into.Length < 2) return 0;

        double3 up = burstFromCentre / r;
        double3 pole = Vec.Unit(axis);
        double sinLat = Math.Clamp(Vec.Dot(up, pole), -1.0, 1.0);
        double cos2 = 1.0 - (sinLat * sinLat);
        if (!(cos2 > 1.0e-9)) return 0;

        double shell = r / (planetRadius * cos2);
        double foot = (planetRadius + BottomAltitude) / planetRadius;
        double cos2Foot = foot / shell;
        if (cos2Foot > 1.0) return 0;

        double footLat = Math.Acos(Math.Sqrt(cos2Foot));

        // The burst's meridian: along the equator, at its magnetic longitude.
        double3 outward = Vec.Unit(up - (pole * sinLat));
        double3 north = (outward * Math.Cos(footLat)) + (pole * Math.Sin(footLat));
        double3 south = (outward * Math.Cos(footLat)) - (pole * Math.Sin(footLat));

        bool northFirst = sinLat >= 0.0;
        into[0] = northFirst ? north : south;
        if (footLat < 1.0e-6) return 1;

        into[1] = northFirst ? south : north;
        return 2;
    }

    /// <summary>Magnetic east at a footpoint: the way an auroral arc runs.</summary>
    public static double3 EastAt(double3 footDirection, double3 axis)
    {
        double3 east = Vec.Cross(Vec.Unit(axis), footDirection);
        return Vec.Len2(east) > 1.0e-12 ? Vec.Unit(east) : Vec.Zero;
    }

    /// <summary>
    /// How bright the curtain is at an age, for a yield in kilotonnes: arriving over its first seconds,
    /// fading over minutes, and gone at <see cref="LifeSeconds"/>. As the square root of the yield,
    /// since the brighter aurora is the wider one as much as the more intense.
    /// </summary>
    public static double Strength(double yieldKt, double age)
    {
        if (!(yieldKt > 0.0) || !(age >= 0.0) || age >= LifeSeconds) return 0.0;

        double arriving = Math.Clamp(age / ArrivesSeconds, 0.0, 1.0);
        double fading = Math.Exp(-Math.Max(age - ArrivesSeconds, 0.0) / FadeSeconds);
        double ending = 1.0 - Math.Clamp((age - (LifeSeconds - 60.0)) / 60.0, 0.0, 1.0);

        return PeakNits * Math.Sqrt(yieldKt / 1000.0) * arriving * fading * ending;
    }
}
