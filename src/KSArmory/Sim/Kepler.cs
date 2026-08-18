using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// Where a coasting body will be later, in closed form.
///
/// <para>Universal variables, so one routine covers a circular orbit, the tall ellipse of a lofted
/// shot and the hyperbola of a depressed one without being told which it has. The alternative is a
/// conic-specific propagator plus a decision about which to call, and that decision is wrong
/// exactly at the boundary between them.</para>
///
/// <para><b>Closed form rather than stepped, and that is the point.</b> Deciding <em>when</em> to
/// start a burn means asking where the vehicle will be at hundreds of candidate moments, each of
/// which is then a trajectory solve of its own. Integrating to each one turns a search into an
/// afternoon; this makes it a few microseconds. <see cref="ImpactPredictor"/> stays numerical
/// because it has to fly through terrain, which no conic knows about.</para>
/// </summary>
internal static class Kepler
{
    /// <summary>Enough for the Newton iteration to converge from a poor guess.</summary>
    public const int MaxIterations = 64;

    /// <summary>
    /// The state <paramref name="seconds"/> later, under gravity alone.
    /// </summary>
    public static bool TryCoast(double mu, double3 positionCci, double3 velocityCci, double seconds,
                                out double3 nextPositionCci, out double3 nextVelocityCci)
    {
        nextPositionCci = positionCci;
        nextVelocityCci = velocityCci;

        if (!(mu > 0.0) || !double.IsFinite(seconds)) return false;
        if (!Vec.IsFinite(positionCci) || !Vec.IsFinite(velocityCci)) return false;
        if (seconds == 0.0) return true;

        double r0 = positionCci.Length();
        if (!(r0 > 0.0)) return false;

        double sqrtMu = Math.Sqrt(mu);
        double rDotV = Vec.Dot(positionCci, velocityCci);

        // Reciprocal of the semi-major axis, and the only thing that says which conic this is.
        double alpha = 2.0 / r0 - velocityCci.LengthSquared() / mu;

        double chi = InitialGuess(mu, r0, rDotV, alpha, seconds, sqrtMu);
        if (!double.IsFinite(chi)) return false;

        double r = r0;
        double psi = 0.0;
        double c2 = 0.5;
        double c3 = 1.0 / 6.0;

        for (int i = 0; i < MaxIterations; i++)
        {
            psi = chi * chi * alpha;
            c2 = StumpffC(psi);
            c3 = StumpffS(psi);

            r = chi * chi * c2
              + rDotV / sqrtMu * chi * (1.0 - psi * c3)
              + r0 * (1.0 - psi * c2);

            if (!(Math.Abs(r) > 1e-9)) return false;

            double next = chi + (sqrtMu * seconds
                                 - chi * chi * chi * c3
                                 - rDotV / sqrtMu * chi * chi * c2
                                 - r0 * chi * (1.0 - psi * c3)) / r;

            if (!double.IsFinite(next)) return false;
            if (Math.Abs(next - chi) < 1e-9) { chi = next; break; }
            chi = next;
        }

        psi = chi * chi * alpha;
        c2 = StumpffC(psi);
        c3 = StumpffS(psi);
        r = chi * chi * c2 + rDotV / sqrtMu * chi * (1.0 - psi * c3) + r0 * (1.0 - psi * c2);
        if (!(Math.Abs(r) > 1e-9)) return false;

        double f = 1.0 - chi * chi / r0 * c2;
        double g = seconds - chi * chi * chi / sqrtMu * c3;
        double gDot = 1.0 - chi * chi / r * c2;
        double fDot = sqrtMu / (r * r0) * chi * (psi * c3 - 1.0);

        nextPositionCci = positionCci * f + velocityCci * g;
        nextVelocityCci = positionCci * fDot + velocityCci * gDot;

        return Vec.IsFinite(nextPositionCci) && Vec.IsFinite(nextVelocityCci);
    }

    /// <summary>
    /// How long this orbit takes to come round, or NaN for one that never does.
    ///
    /// <para>The natural horizon for "when should the burn start": past one revolution the geometry
    /// repeats, so a search that runs longer is re-examining answers it already has.</para>
    /// </summary>
    public static double PeriodSeconds(double mu, double3 positionCci, double3 velocityCci)
    {
        double r0 = positionCci.Length();
        if (!(mu > 0.0) || !(r0 > 0.0)) return double.NaN;

        double energy = velocityCci.LengthSquared() * 0.5 - mu / r0;
        if (!(energy < 0.0)) return double.NaN;

        double a = -mu / (2.0 * energy);
        return 2.0 * Math.PI * Math.Sqrt(a * a * a / mu);
    }

    private static double InitialGuess(double mu, double r0, double rDotV, double alpha,
                                       double seconds, double sqrtMu)
    {
        // Elliptical: the mean-motion estimate is close enough that Newton lands in a few passes.
        if (alpha > 1e-9) return sqrtMu * seconds * alpha;

        // Hyperbolic: the closed-form guess, which matters because Newton diverges from a bad one
        // out here rather than merely converging slowly.
        if (alpha < -1e-9)
        {
            double a = 1.0 / alpha;
            double sign = Math.Sign(seconds);
            if (sign == 0) sign = 1;

            double numerator = -2.0 * mu * alpha * seconds;
            double denominator = rDotV + sign * Math.Sqrt(-mu * a) * (1.0 - r0 * alpha);
            if (Math.Abs(denominator) < 1e-12) return sqrtMu * seconds * alpha;

            return sign * Math.Sqrt(-a) * Math.Log(numerator / denominator);
        }

        // Parabolic, where both of the above are singular.
        return sqrtMu * seconds / r0;
    }

    /// <summary>
    /// Stumpff C. Series near zero rather than the closed form, which is 0/0 at the parabolic point
    /// — and a depressed shot passes straight through it on its way to hyperbolic.
    /// </summary>
    public static double StumpffC(double z)
    {
        if (z > 1e-6)
        {
            double s = Math.Sqrt(z);
            return (1.0 - Math.Cos(s)) / z;
        }
        if (z < -1e-6)
        {
            double s = Math.Sqrt(-z);
            return (Math.Cosh(s) - 1.0) / -z;
        }
        return 0.5 + z * (-1.0 / 24.0 + z / 720.0);
    }

    /// <summary>Stumpff S, with the same series near zero and for the same reason.</summary>
    public static double StumpffS(double z)
    {
        if (z > 1e-6)
        {
            double s = Math.Sqrt(z);
            return (s - Math.Sin(s)) / (s * s * s);
        }
        if (z < -1e-6)
        {
            double s = Math.Sqrt(-z);
            return (Math.Sinh(s) - s) / (s * s * s);
        }
        return 1.0 / 6.0 + z * (-1.0 / 120.0 + z / 5040.0);
    }
}
