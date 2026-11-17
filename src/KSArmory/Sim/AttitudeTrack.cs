using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a craft's body points over the next seconds, flown under its aerodynamic moment from an
/// <see cref="AeroTable"/> and its inertia -- because since 2026.11.16.5901 that moment turns a craft,
/// and holding its rates put a tumbling drone's lift along the wrong axis.
///
/// <para>Flown on demand, a step at a time, as far ahead as it is asked. The body's axes are kept as
/// vectors in the ecliptic, turned by Rodrigues' formula, so no quaternion convention can slip.</para>
/// </summary>
public sealed class AttitudeTrack
{
    /// <summary>What the craft is doing at a moment, for the track to fly its rotation against.</summary>
    /// <param name="Air">Its velocity against the air (m/s).</param>
    /// <param name="DensityRatio">The air against <see cref="Medium.ReferenceDensityKgPerM3"/>.</param>
    public readonly record struct Flow(double3 Air, double DensityRatio);

    private const double Step = 0.01;

    private readonly AeroTable _table;
    private readonly double3 _inertiaDiagonal, _inertiaOff;
    private readonly double3 _inverseDiagonal, _inverseOff;
    private readonly double _referenceDensity;
    private readonly Func<double, Flow> _flowAt;

    private readonly List<(double3 X, double3 Y, double3 Z, double3 Rates)> _samples = [];

    /// <param name="inertiaDiagonal">XX, YY, ZZ of the body-frame inertia tensor about the centre of mass (kg m²).</param>
    /// <param name="inertiaOff">Its YX, ZX, ZY.</param>
    /// <param name="flowAt">The craft's airflow at a time from now, which the caller's own flight answers.</param>
    public AttitudeTrack(AeroTable table, doubleQuat body2Ecl, double3 bodyRates, double3 inertiaDiagonal,
                         double3 inertiaOff, double3 inverseDiagonal, double3 inverseOff, double referenceDensity,
                         Func<double, Flow> flowAt)
    {
        _table = table;
        _inertiaDiagonal = inertiaDiagonal;
        _inertiaOff = inertiaOff;
        _inverseDiagonal = inverseDiagonal;
        _inverseOff = inverseOff;
        _referenceDensity = referenceDensity;
        _flowAt = flowAt;
        _samples.Add((body2Ecl * new double3(1, 0, 0), body2Ecl * new double3(0, 1, 0), body2Ecl * new double3(0, 0, 1),
                      bodyRates));
    }

    /// <summary>A body-frame vector in the ecliptic, <paramref name="seconds"/> from now.</summary>
    public double3 ToEcl(double3 body, double seconds)
    {
        (double3 x, double3 y, double3 z, _) = At(seconds);
        return (x * body.X) + (y * body.Y) + (z * body.Z);
    }

    /// <summary>An ecliptic vector in the body frame, <paramref name="seconds"/> from now.</summary>
    public double3 ToBody(double3 ecl, double seconds)
    {
        (double3 x, double3 y, double3 z, _) = At(seconds);
        return new double3(Vec.Dot(x, ecl), Vec.Dot(y, ecl), Vec.Dot(z, ecl));
    }

    /// <summary>The body rates (rad/s, body axes) <paramref name="seconds"/> from now.</summary>
    public double3 RatesAt(double seconds) => At(seconds).Rates;

    private (double3 X, double3 Y, double3 Z, double3 Rates) At(double seconds)
    {
        if (!(seconds > 0.0)) return _samples[0];

        double index = seconds / Step;
        int i = (int)index;
        while (_samples.Count <= i + 1) Advance();

        var (x0, y0, z0, w0) = _samples[i];
        var (x1, y1, z1, w1) = _samples[i + 1];
        double f = index - i;
        return Orthonormal(x0 + ((x1 - x0) * f), y0 + ((y1 - y0) * f), w0 + ((w1 - w0) * f));
    }

    private void Advance()
    {
        var (x, y, z, rates) = _samples[^1];
        double t = (_samples.Count - 1) * Step;

        Flow flow = _flowAt(t);
        double airspeed = Vec.Len(flow.Air);
        double3 torque = Vec.Zero;
        if (airspeed > 1.0 && flow.DensityRatio > 0.0 && _table.IsComplete)
        {
            double3 flowBody = new double3(-Vec.Dot(x, flow.Air), -Vec.Dot(y, flow.Air), -Vec.Dot(z, flow.Air)) * (1.0 / airspeed);
            double q = 0.5 * _referenceDensity * flow.DensityRatio * airspeed * airspeed;
            torque = _table.Loads(flowBody, rates * (1.0 / airspeed)).Moment * q;
        }

        // Euler's equations in the body: I dω/dt = M - ω × Iω.
        double3 accel = Apply(_inverseDiagonal, _inverseOff, torque - Vec.Cross(rates, Apply(_inertiaDiagonal, _inertiaOff, rates)));
        double3 next = rates + (accel * Step);
        if (!Vec.IsFinite(next)) next = rates;

        // Turned by the step's mean rate, carried into the ecliptic.
        double3 mean = (rates + next) * 0.5;
        double3 axis = (x * mean.X) + (y * mean.Y) + (z * mean.Z);
        double angle = Vec.Len(axis) * Step;
        if (angle > 0.0)
        {
            double3 k = Vec.Unit(axis);
            x = Rotate(x, k, angle);
            y = Rotate(y, k, angle);
        }

        _samples.Add(Orthonormal(x, y, next));
    }

    private static (double3, double3, double3, double3) Orthonormal(double3 x, double3 y, double3 rates)
    {
        double3 ux = Vec.Unit(x);
        double3 uz = Vec.Unit(Vec.Cross(ux, y));
        return (ux, Vec.Cross(uz, ux), uz, rates);
    }

    private static double3 Rotate(double3 v, double3 k, double angle)
        => (v * Math.Cos(angle)) + (Vec.Cross(k, v) * Math.Sin(angle)) + (k * (Vec.Dot(k, v) * (1.0 - Math.Cos(angle))));

    // A symmetric tensor from its diagonal and its YX, ZX, ZY.
    private static double3 Apply(double3 d, double3 off, double3 v)
        => new(d.X * v.X + off.X * v.Y + off.Y * v.Z,
               off.X * v.X + d.Y * v.Y + off.Z * v.Z,
               off.Y * v.X + off.Z * v.Y + d.Z * v.Z);
}
