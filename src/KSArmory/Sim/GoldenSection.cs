namespace KSArmory;

/// <summary>The minimum of a cost over an interval, by golden section.</summary>
public static class GoldenSection
{
    private const double Ratio = 0.6180339887498949;

    // A NaN cost compares false and moves the low end, so an unreachable region is walked out of
    // rather than kept; a caller that cares checks the answer it gets back.
    public static double Minimise(Func<double, double> cost, double lo, double hi, int iterations,
                                  double tolerance)
    {
        double c = hi - (hi - lo) * Ratio;
        double d = lo + (hi - lo) * Ratio;

        for (int i = 0; i < iterations && hi - lo > tolerance; i++)
        {
            double costC = cost(c);
            double costD = cost(d);

            if (costC < costD) hi = d; else lo = c;

            c = hi - (hi - lo) * Ratio;
            d = lo + (hi - lo) * Ratio;
        }

        return 0.5 * (lo + hi);
    }
}
