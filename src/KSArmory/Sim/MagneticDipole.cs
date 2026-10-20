using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// A body's magnetic field as a dipole that may be tilted and off its centre, which is what KSA declares
/// (<c>MagnetosphereReference</c>): every vector here is in the body's fixed frame, from its centre.
/// <see cref="None"/> where the body has no field.
/// </summary>
/// <param name="Centre">The dipole's centre, from the body's centre (m).</param>
/// <param name="Axis">The dipole's axis, unit.</param>
/// <param name="SurfaceTesla">The field at the surface over its magnetic equator (T).</param>
/// <param name="RadiusMetres">The body's mean radius, which <c>L</c> is measured in.</param>
internal readonly record struct MagneticDipole(double3 Centre, double3 Axis, double SurfaceTesla, double RadiusMetres)
{
    public static readonly MagneticDipole None = new(Vec.Zero, Vec.Zero, 0.0, 0.0);

    public bool Exists => SurfaceTesla > 0.0 && RadiusMetres > 0.0 && Vec.Len2(Axis) > 0.0;

    public double SinLatitude(double3 point)
        => Math.Clamp(Vec.Dot(Vec.Unit(point - Centre), Vec.Unit(Axis)), -1.0, 1.0);

    /// <summary>The shell a point's field line belongs to, in body radii, as KSA's <c>Radiation.ComputeL</c> has it.</summary>
    public double L(double3 point)
    {
        double s = SinLatitude(point);
        double cos2 = 1.0 - (s * s);
        return cos2 > 1.0e-12 ? Vec.Len(point - Centre) / (RadiusMetres * cos2) : double.PositiveInfinity;
    }

    /// <summary>Which way the field runs at a point, along <c>3(a·u)u - a</c>; its sign says nothing.</summary>
    public double3 FieldDirection(double3 point)
    {
        double3 u = Vec.Unit(point - Centre);
        double3 a = Vec.Unit(Axis);
        double3 field = (u * (3.0 * Vec.Dot(a, u))) - a;
        return Vec.Len2(field) > 1.0e-12 ? Vec.Unit(field) : a;
    }

    /// <summary>The field's strength at a point (T): <c>B₀(R/r)³√(1+3sin²λ)</c>.</summary>
    public double Tesla(double3 point)
    {
        double r = Vec.Len(point - Centre);
        if (!Exists || !(r > 0.0)) return 0.0;

        double s = SinLatitude(point);
        double ratio = RadiusMetres / r;
        return SurfaceTesla * ratio * ratio * ratio * Math.Sqrt(1.0 + (3.0 * s * s));
    }

    /// <summary>Magnetic east at a point: the way an auroral arc runs there.</summary>
    public double3 EastAt(double3 point)
    {
        double3 east = Vec.Cross(Vec.Unit(Axis), point - Centre);
        return Vec.Len2(east) > 1.0e-12 ? Vec.Unit(east) : Vec.Zero;
    }

    private const int LineSamples = 720;

    /// <summary>
    /// Where a point's field line comes down to <paramref name="footRadius"/> from the body's centre, the
    /// end on the point's side of the line's apex first. None when the line never climbs that high, and one
    /// where both ends meet. Searched along the line rather than solved, because off the centre the
    /// sphere the feet lie on is not a sphere about the dipole.
    /// </summary>
    public int Footpoints(double3 point, double footRadius, Span<double3> into)
    {
        if (!Exists || into.Length < 2) return 0;

        double3 m = point - Centre;
        double3 a = Vec.Unit(Axis);
        double s = Vec.Dot(m, a);
        double3 equatorial = m - (a * s);
        if (!(Vec.Len2(equatorial) > 1.0e-6)) return 0;

        double3 e = Vec.Unit(equatorial);
        double r = Vec.Len(m);
        double sinPoint = s / r;
        double shell = r / (1.0 - (sinPoint * sinPoint));
        double pointLat = Math.Asin(sinPoint);
        double3 centre = Centre;

        double3 At(double lat)
        {
            double c = Math.Cos(lat);
            return centre + (((e * c) + (a * Math.Sin(lat))) * (shell * c * c));
        }

        double Above(double lat) => Vec.Len(At(lat)) - footRadius;

        const double Edge = (Math.PI / 2.0) - 1.0e-4;
        double step = 2.0 * Edge / LineSamples;
        int apex = 0;
        double highest = double.NegativeInfinity;
        for (int i = 0; i <= LineSamples; i++)
        {
            double h = Above(-Edge + (i * step));
            if (h > highest) { highest = h; apex = i; }
        }

        if (!(highest > 0.0)) return 0;

        double apexLat = -Edge + (apex * step);
        double? north = Descend(apexLat, Edge, step, Above);
        double? south = Descend(apexLat, -Edge, -step, Above);
        if (north is null || south is null) return 0;

        bool northFirst = pointLat >= apexLat;
        into[0] = At(northFirst ? north.Value : south.Value);
        if (Math.Abs(north.Value - south.Value) < 1.0e-6) return 1;

        into[1] = At(northFirst ? south.Value : north.Value);
        return 2;
    }

    private static double? Descend(double from, double to, double step, Func<double, double> above)
    {
        double outside = from;
        for (double lat = from + step; step > 0.0 ? lat <= to : lat >= to; lat += step)
        {
            if (above(lat) <= 0.0)
            {
                double inside = lat;
                for (int i = 0; i < 60; i++)
                {
                    double mid = 0.5 * (outside + inside);
                    if (above(mid) > 0.0) outside = mid;
                    else inside = mid;
                }

                return 0.5 * (outside + inside);
            }

            outside = lat;
        }

        return null;
    }
}
