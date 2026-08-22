using Brutal.Numerics;
using KSArmory;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a shot launched from the ground arrives at, against one deorbited from a platform.
///
/// <para>The mod's flown shot arrives at 7.1 degrees because it starts at 207 km already doing
/// 7,360 m/s: a deorbit is a graze by construction, and <c>docs/ARRIVAL-ANGLE.md</c> measures that
/// no amount of braking makes it steeper than about 7 with a Mk 21. A pad launch is a different
/// geometry — the arc is symmetric about its apogee, so what it arrives at is what it left at, and
/// steepness is bought with loft rather than by cancelling an orbit.</para>
/// </summary>
public class GroundLaunchArrivalTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private static double3 OnTheGround(BallisticBody body, double eastMetres)
    {
        double angle = eastMetres / body.SurfaceRadius;
        return new double3(body.SurfaceRadius * Math.Cos(angle),
                           body.SurfaceRadius * Math.Sin(angle), 0.0);
    }

    /// <summary>The cheapest arc for a range, and what lofting it buys.</summary>
    [Fact]
    public void APadLaunchArrivesFarSteeperThanADeorbit()
    {
        BallisticBody body = new(DeorbitShot.Mu, DeorbitShot.R, new double3(0, 0, 1), 0.0);

        _out.WriteLine("range      flight   arrives   burnout speed   apogee");

        foreach (double rangeKm in new[] { 1_000.0, 3_459.0, 6_000.0, 10_000.0 })
        {
            double3 from = OnTheGround(body, 0.0);
            double3 aim = OnTheGround(body, rangeKm * 1000.0);

            // Sweep flight time; the cheapest arc is the minimum-energy one, and longer times are
            // the lofted family above it.
            double bestCost = double.MaxValue;
            BallisticArc.Solution best = default;
            double bestT = 0.0;

            for (double t = 200.0; t <= 6000.0; t += 5.0)
            {
                if (!BallisticArc.TrySolve(body, from, aim, t, out BallisticArc.Solution s)) continue;
                if (s.LowestRadius < body.SurfaceRadius * 0.999) continue;

                double cost = Vec.Len(s.RequiredVelocityCci);
                if (cost >= bestCost) continue;
                bestCost = cost; best = s; bestT = t;
            }

            Assert.True(bestCost < double.MaxValue, $"no arc found for {rangeKm} km");
            _out.WriteLine($"{rangeKm,6:F0} km  {bestT,6:F0} s  {best.ArrivalAngleDeg,7:F1} deg  "
                           + $"{bestCost,10:F0} m/s  {(best.ApogeeRadius - body.SurfaceRadius) / 1000.0,7:F0} km");

            // A deorbit arrives at about 7 degrees whatever it does. A pad launch must do better.
            Assert.True(best.ArrivalAngleDeg > 20.0,
                        $"{rangeKm} km arrives at {best.ArrivalAngleDeg:F1} deg");
        }
    }

    /// <summary>What loft costs, and what it buys, on the range the whole budget is measured on.</summary>
    [Fact]
    public void LoftBuysArrivalAngleFromThePad()
    {
        BallisticBody body = new(DeorbitShot.Mu, DeorbitShot.R, new double3(0, 0, 1), 0.0);
        double3 from = OnTheGround(body, 0.0);
        double3 aim = OnTheGround(body, DeorbitShot.RangeMetres);

        double cheapest = double.MaxValue;
        foreach (double t in Times())
        {
            if (BallisticArc.TrySolve(body, from, aim, t, out BallisticArc.Solution s)
                && s.LowestRadius >= body.SurfaceRadius * 0.999)
            {
                cheapest = Math.Min(cheapest, Vec.Len(s.RequiredVelocityCci));
            }
        }

        _out.WriteLine("flight   arrives   burnout    vs cheapest   apogee");

        foreach (double t in new[] { 900.0, 1100.0, 1300.0, 1600.0, 2000.0, 2600.0 })
        {
            if (!BallisticArc.TrySolve(body, from, aim, t, out BallisticArc.Solution s)) continue;
            if (s.LowestRadius < body.SurfaceRadius * 0.999) continue;

            double cost = Vec.Len(s.RequiredVelocityCci);
            _out.WriteLine($"{t,5:F0} s  {s.ArrivalAngleDeg,7:F1} deg  {cost,7:F0} m/s  "
                           + $"{(cost / cheapest - 1.0) * 100.0,9:F0}%  "
                           + $"{(s.ApogeeRadius - body.SurfaceRadius) / 1000.0,7:F0} km");
        }

        static IEnumerable<double> Times()
        {
            for (double t = 200.0; t <= 6000.0; t += 5.0) yield return t;
        }
    }
}
