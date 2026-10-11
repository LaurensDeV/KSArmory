namespace KSArmory.Tests;

/// <summary>
/// KSA's thrust-vector attitude law, ported from <c>FlightComputer</c> so the rig steers a stack the way the game does:
/// per control axis, angle, integral and rate gains from discrete Riccati solves, a parabolic phase line blended in as
/// the rate error grows, and a steering interval that stretches while the stack is settled. Constants and their
/// arithmetic are KSA's; only the 3x3 matrix is this file's, because the game's is in an assembly the tests do not load.
/// </summary>
internal sealed class KsaTvcLaw
{
    private const double MinInterval = 0.1;

    // States are angle, integral and rate, one period normalised to one.
    private static readonly M3 TrackPlant = new(1, 0, 1, 1, 1, 0.5, 0, 0, 1);
    private static readonly M3 TrackCost = new(50, 0, 0, 0, 0.05, 0, 0, 0, 9999.999999999998);

    public double SteeringInterval { get; private set; } = MinInterval;

    private double _steeringAuthority;
    private double _integralY, _integralZ;

    /// <summary>
    /// One control update: the commands in [-1, 1] about the two axes square to the nose.
    /// </summary>
    /// <param name="angleY">Current less target, about the first axis, in radians; likewise the rest.</param>
    /// <param name="angularAccelY">The stack's angular acceleration about each axis, which the interval is drifted on.</param>
    public (double Y, double Z) Update(double angleY, double angleZ, double rateY, double rateZ, double authority,
                                       double angularAccelY, double angularAccelZ, double controlDeltaTime)
    {
        UpdateSteeringInterval(angleY, angleZ, rateY, rateZ, angularAccelY, angularAccelZ, authority);
        double y = Axis(angleY, rateY, authority, SteeringInterval, controlDeltaTime, ref _integralY);
        double z = Axis(angleZ, rateZ, authority, SteeringInterval, controlDeltaTime, ref _integralZ);
        return (y, z);
    }

    private void UpdateSteeringInterval(double angleY, double angleZ, double rateY, double rateZ,
                                        double accelY, double accelZ, double authority)
    {
        if (!(authority > 0.0))
        {
            SteeringInterval = MinInterval;
            _steeringAuthority = 0.0;
            return;
        }

        double interval = double.PositiveInfinity;
        LimitBySteeringDrift((float)angleY, (float)rateY, accelY, ref interval);
        LimitBySteeringDrift((float)angleZ, (float)rateZ, accelZ, ref interval);
        interval = Math.Max(interval, MinInterval);

        double floor = authority * MinInterval * MinInterval;
        double wanted = authority * interval * interval;
        double held = Math.Max(_steeringAuthority * SteeringInterval * SteeringInterval, floor);
        double next = wanted;
        if (wanted > held)
        {
            double decay = TrackDecayPerCycle(SteeringInterval, authority);
            double growth = decay < 1.0 ? 1.0 + (1.0 - decay) / 5.0 : 1.0;
            next = Math.Min(wanted, held * growth);
        }

        SteeringInterval = Math.Max(Math.Sqrt(next / authority), MinInterval);
        _steeringAuthority = authority;
    }

    private static void LimitBySteeringDrift(float angleError, float rateError, double angularAcceleration, ref double interval)
    {
        float margin = 1.7453292E-05f - Math.Abs(angleError);
        if (margin <= 0.0)
        {
            interval = MinInterval;
            return;
        }

        float rate = Math.Abs(rateError);
        double accel = Math.Abs(angularAcceleration);
        double root = rate + Math.Sqrt((double)(rate * rate) + 2.0 * accel * margin);
        if (root > 0.0) interval = Math.Min(interval, 2.0 * margin / root);
    }

    private static double Axis(double angleError, double rateError, double authority, double interval,
                               double controlDeltaTime, ref double integral)
    {
        if (Math.Abs(authority) < 1e-12) return 0.0;

        float angle = (float)angleError, rate = (float)rateError;
        double rateGain = RateGain(interval, authority);
        double blend = Math.Min(Math.Max(Math.Abs(rate) - 0.0021816615480929613, 0.0) / 0.008726646192371845, 1.0);
        blend *= blend;

        (double kAngle, double kIntegral, double kRate) = TrackGains(interval, authority);
        double satAngle = Math.Min(Math.Abs(kAngle * angle), 1.0);
        double satRate = Math.Min(Math.Abs(kRate * rate), 1.0);
        integral += kIntegral * (1.0 - satAngle) * (1.0 - satRate) * angle * controlDeltaTime;
        integral = Math.Clamp(integral, -1.0, 1.0);
        double cmd = -kAngle * angle - integral - kRate * rate;

        if (blend > 0.0)
        {
            float half = (float)(0.5 * authority);
            float x = PhaseLineInverse(rate, half, 0f);
            float line = PhaseLine(angle, half, MathF.CopySign(x, angle));
            cmd += blend * -rateGain * (rate - line);
        }

        return (float)Math.Clamp(cmd, -1.0, 1.0);
    }

    private static float PhaseLine(float angle, float authority, float offset)
    {
        float d = angle - offset;
        return -MathF.CopySign(MathF.Sqrt(2f * Math.Abs(d) * authority), d);
    }

    private static float PhaseLineInverse(float rate, float authority, float offset)
        => -rate * Math.Abs(rate) / (2f * authority) + offset;

    private static double RateGain(double period, double authority)
    {
        double b = authority * period;
        double p = RateDare(1.0, b, 1000.0, 1.0);
        double bp = b * p;
        return 1.0 / (1.0 + bp * b) * bp;
    }

    private static (double Angle, double Integral, double Rate) TrackGains(double period, double authority)
    {
        V3 k = TrackScaled(period, authority, out _);
        return (k.X, k.Y / period, k.Z * period);
    }

    private static double TrackDecayPerCycle(double period, double authority)
    {
        V3 k = TrackScaled(period, authority, out V3 b);
        return TrackPlant.Minus(V3.Outer(b, k)).SpectralRadius();
    }

    private static V3 Control(double c) => new(0.5 * c, c / 6.0, c);

    private static V3 TrackScaled(double period, double authority, out V3 b)
    {
        double scale = authority * period * period;
        b = Control(scale);
        double tried = scale;
        V3 k;
        for (int n = 0; ; n++)
        {
            V3 v = Control(tried);
            M3 p = TrackDare(TrackPlant, v, TrackCost, 1.0);
            V3 row = v.TimesRows(p);
            k = row.TimesRows(TrackPlant) * (1.0 / (1.0 + row.Dot(v)));
            if (DareResidual(p, v) <= 1e-8 || n >= 16) break;
            tried *= 0.1;
        }

        return scale > tried ? k * (tried / scale) : k;
    }

    private static double DareResidual(M3 p, V3 b)
    {
        V3 bp = b.TimesRows(p);
        V3 col = bp.TimesRows(TrackPlant);
        double s = 1.0 / (1.0 + bp.Dot(b));
        M3 r = TrackPlant.Transpose().Times(p).Times(TrackPlant).Minus(V3.Outer(col, col).Scale(s)).Plus(TrackCost).Minus(p);
        double norm = p.Frobenius();
        return norm > 0.0 ? r.Frobenius() / norm : r.Frobenius();
    }

    private static M3 TrackDare(M3 a, V3 b, M3 q, double r)
    {
        M3 ak = a;
        M3 g = V3.Outer(b * (1.0 / r), b);
        M3 h = q;
        int left = 20;
        double tol, change;
        do
        {
            M3 w = M3.Identity.Plus(g.Times(h));
            M3 wInv = w.Inverse();
            M3 wTInv = w.Transpose().Inverse();
            M3 v1 = wInv.Times(ak);
            M3 v2 = g.Times(wTInv);
            M3 akNext = ak.Times(v1);
            M3 gNext = g.Plus(ak.Times(v2).Times(ak.Transpose()));
            M3 hNext = h.Plus(v1.Transpose().Times(h).Times(ak));
            tol = 1e-6 * h.Frobenius();
            change = Math.Abs(hNext.Minus(h).Frobenius());
            ak = akNext;
            g = gNext;
            h = hNext;
        }
        while (change > tol && --left > 0);

        return h;
    }

    private static double RateDare(double a, double b, double q, double r)
    {
        double g = b / r * b;
        int left = 20;
        double tol, change;
        do
        {
            double inv = 1.0 / (1.0 + g * q);
            double v1 = inv * a;
            double v2 = g * inv;
            double aNext = a * v1;
            double gNext = g + a * v2 * a;
            double qNext = q + v1 * q * a;
            tol = 1e-6 * Math.Abs(q);
            change = Math.Abs(qNext - q);
            a = aNext;
            g = gNext;
            q = qNext;
        }
        while (change > tol && --left > 0);

        return q;
    }

    internal readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator *(V3 v, double s) => new(v.X * s, v.Y * s, v.Z * s);
        public double Dot(V3 o) => X * o.X + Y * o.Y + Z * o.Z;

        // The row vector times the matrix.
        public V3 TimesRows(M3 m) => new(X * m.A + Y * m.D + Z * m.G, X * m.B + Y * m.E + Z * m.H, X * m.C + Y * m.F + Z * m.I);

        public static M3 Outer(V3 c, V3 r) => new(c.X * r.X, c.X * r.Y, c.X * r.Z, c.Y * r.X, c.Y * r.Y, c.Y * r.Z,
                                                  c.Z * r.X, c.Z * r.Y, c.Z * r.Z);
    }

    // Row-major: A B C is the first row.
    internal readonly record struct M3(double A, double B, double C, double D, double E, double F, double G, double H, double I)
    {
        public static readonly M3 Identity = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public M3 Plus(M3 o) => new(A + o.A, B + o.B, C + o.C, D + o.D, E + o.E, F + o.F, G + o.G, H + o.H, I + o.I);
        public M3 Minus(M3 o) => new(A - o.A, B - o.B, C - o.C, D - o.D, E - o.E, F - o.F, G - o.G, H - o.H, I - o.I);
        public M3 Scale(double s) => new(A * s, B * s, C * s, D * s, E * s, F * s, G * s, H * s, I * s);
        public M3 Transpose() => new(A, D, G, B, E, H, C, F, I);

        public M3 Times(M3 o) => new(
            A * o.A + B * o.D + C * o.G, A * o.B + B * o.E + C * o.H, A * o.C + B * o.F + C * o.I,
            D * o.A + E * o.D + F * o.G, D * o.B + E * o.E + F * o.H, D * o.C + E * o.F + F * o.I,
            G * o.A + H * o.D + I * o.G, G * o.B + H * o.E + I * o.H, G * o.C + H * o.F + I * o.I);

        public double Frobenius() => Math.Sqrt(A * A + B * B + C * C + D * D + E * E + F * F + G * G + H * H + I * I);

        public M3 Inverse()
        {
            double det = A * (E * I - F * H) - B * (D * I - F * G) + C * (D * H - E * G);
            double s = 1.0 / det;
            return new M3((E * I - F * H) * s, (C * H - B * I) * s, (B * F - C * E) * s,
                          (F * G - D * I) * s, (A * I - C * G) * s, (C * D - A * F) * s,
                          (D * H - E * G) * s, (B * G - A * H) * s, (A * E - B * D) * s);
        }

        // Gelfand's formula, by repeated squaring: the norm of M^(2^k), to the power 2^-k.
        public double SpectralRadius()
        {
            M3 m = this;
            double logScale = 0.0;
            for (int k = 0; k < 40; k++)
            {
                double n = m.Frobenius();
                if (n == 0.0) return 0.0;
                m = m.Scale(1.0 / n);
                logScale = 2.0 * (logScale + Math.Log(n));
                m = m.Times(m);
            }

            return Math.Exp((logScale + Math.Log(m.Frobenius())) / Math.Pow(2.0, 40));
        }
    }
}
