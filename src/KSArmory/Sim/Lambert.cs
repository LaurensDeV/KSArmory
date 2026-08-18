using Brutal.Numerics;

namespace KSArmory;

/// <summary>
/// The two-body transfer between two points in a stated time — Lambert's problem, in universal
/// variables.
///
/// <para>This is the one piece of the ICBM computer that is solved rather than flown, and it has
/// to be: the question "what velocity at burnout puts me there" has no answer by integration
/// without already knowing the velocity. Everything downstream of it is numerical — the impact
/// prediction re-flies the arc against real terrain, and the steering law re-solves this every
/// cycle rather than trusting the first answer.</para>
///
/// <para>Universal variables rather than a conic-specific form because a depressed shot is
/// genuinely hyperbolic while a lofted one is a tall ellipse, and a solver that has to be told
/// which is which is a solver that fails at the boundary between them. The iteration is a
/// bisection on <c>psi</c>, which converges for anything inside the bracket and cannot run
/// away — Newton on this function does, near the parabolic point.</para>
/// </summary>
internal static class Lambert
{
    // How far the bisection may look. Covers everything from hyperbolic to a full ellipse.
    private const double PsiUpper = 4.0 * Math.PI * Math.PI;

    private const double PsiLower = -4.0 * Math.PI * Math.PI;

    /// <summary>Enough for the bracket to close to well under a millimetre per second.</summary>
    public const int MaxIterations = 48;

    /// <summary>What a solved transfer is: the velocity needed at each end of it.</summary>
    internal readonly record struct Transfer(double3 DepartureVelocityCci, double3 ArrivalVelocityCci);

    /// <param name="fromCci">Where the arc starts.</param>
    /// <param name="toCci">Where it must arrive.</param>
    /// <param name="flightSeconds">How long it may take. The whole shape of the arc follows from this.</param>
    /// <param name="mu">The body's gravitational parameter.</param>
    /// <param name="longWay">
    /// Take the arc round the far side. A missile flies the short way; the long way is the route
    /// over the opposite pole, which costs enormously more and exists here because it is free.
    /// </param>
    public static bool TrySolve(double3 fromCci, double3 toCci, double flightSeconds, double mu,
                                out Transfer transfer, bool longWay = false)
    {
        transfer = default;

        if (!Vec.IsFinite(fromCci) || !Vec.IsFinite(toCci)) return false;
        if (!(flightSeconds > 0.0) || !double.IsFinite(flightSeconds)) return false;
        if (!(mu > 0.0)) return false;

        double r1 = fromCci.Length();
        double r2 = toCci.Length();
        if (!(r1 > 0.0) || !(r2 > 0.0)) return false;

        double cosTransfer = Math.Clamp(Vec.Dot(fromCci, toCci) / (r1 * r2), -1.0, 1.0);

        // A is zero for a transfer angle of exactly zero or exactly half a turn. The second is the
        // antipodal shot, where the two points and the centre are collinear and no plane is
        // determined: every azimuth is an equally valid answer, so there is no solution to return.
        double a = (longWay ? -1.0 : 1.0) * Math.Sqrt(r1 * r2 * (1.0 + cosTransfer));
        if (Math.Abs(a) < 1e-9) return false;

        double psiLow = PsiLower;
        double psiUp = PsiUpper;
        double psi = 0.0;
        double y = 0.0;

        for (int i = 0; i < MaxIterations; i++)
        {
            double c2 = Kepler.StumpffC(psi);
            double c3 = Kepler.StumpffS(psi);
            if (!(c2 > 0.0)) return false;

            y = r1 + r2 + a * (psi * c3 - 1.0) / Math.Sqrt(c2);

            // y going negative means the chord is being asked for from an arc that cannot reach
            // it. Raising the floor rather than failing is what lets the bracket recover.
            if (a > 0.0 && y < 0.0)
            {
                psiLow = psi;
                psi = 0.5 * (psiLow + psiUp);
                continue;
            }
            if (y < 0.0) return false;

            double chi = Math.Sqrt(y / c2);
            double dt = (chi * chi * chi * c3 + a * Math.Sqrt(y)) / Math.Sqrt(mu);

            if (dt <= flightSeconds) psiLow = psi; else psiUp = psi;
            psi = 0.5 * (psiLow + psiUp);
        }

        double f = 1.0 - y / r1;
        double g = a * Math.Sqrt(y / mu);
        double gDot = 1.0 - y / r2;
        if (Math.Abs(g) < 1e-9) return false;

        double3 v1 = (toCci - fromCci * f) / g;
        double3 v2 = (toCci * gDot - fromCci) / g;

        if (!Vec.IsFinite(v1) || !Vec.IsFinite(v2)) return false;

        transfer = new Transfer(v1, v2);
        return true;
    }

}
