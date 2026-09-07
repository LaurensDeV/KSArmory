using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Why the trim's debt at the split is six times larger at a steep arrival. `ACCURACY-PLAN.md` 5h.
///
/// <para><b>What is known.</b> Flown at 2,000 km, the debt at the split runs 0.550 / 0.610 / 0.760
/// m/s at 17, 34 and 44 degrees and <b>3.410</b> at 54. The cutoff residual accounts for 1.6x of
/// that (3bp) and the lighter bus for the same 1.7x on the decoupler kick. Neither makes 6.2x, and
/// the clearance wait is ruled out — the steep arm drifts <em>slowest</em> through it.</para>
///
/// <para><b>What this asks.</b> `BusTrim.TrySolve` propagates the reference cutoff state forward by
/// <c>SecondsSinceReference</c> and differences the velocities. So the debt is not just the kick: it
/// is the kick <em>plus how fast two conics separate</em> once one of them has been nudged. That
/// separation rate belongs to the trajectory, and a lofted arc is not the same trajectory as a flat
/// one. Give both the identical kick and the identical delay, and read what the trim thinks it
/// owes.</para>
/// </summary>
public class SteepSplitDebtTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 Ground(double metres)
        => new(R * Math.Cos(metres / R), R * Math.Sin(metres / R), 0.0);

    /// <summary>
    /// The cheapest arc to a fixed target that arrives no shallower than a stated floor, from a
    /// fixed departure — so the floor is the only thing that varies.
    /// </summary>
    private static bool ArcAtFloor(double3 fromCci, double3 aimCci, double floorDeg,
                                   out BallisticArc.Solution arc)
        => BallisticArc.TryCheapest(Earth, fromCci, double3.Zero, aimCci, out arc,
                                    1.0, false, double.NaN, floorDeg);

    /// <summary>
    /// What the trim is told it owes, a fixed delay after a fixed kick, against the arrival angle.
    ///
    /// <para>The kick is 1.1 m/s — the shipped decoupler against a six-tonne bus — applied along
    /// the joint, which is the direction a decoupler actually pushes. Everything else is held.</para>
    /// </summary>
    [Fact]
    public void TheDebtAtTheSplitAgainstTheArrivalAngle()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 aim = Ground(2_000_000.0);

        Out.WriteLine($"{"floor",7}{"arrival",9}{"flight s",10}{"|v| at cutoff",15}"
                      + $"{"owed after 5 s",16}{"per m/s kicked",16}");

        foreach (double floor in new[] { 0.0, 20.0, 34.0, 44.0, 54.0, 60.0 })
        {
            if (!ArcAtFloor(from, aim, floor, out BallisticArc.Solution arc))
            {
                Out.WriteLine($"{floor,7:F0}   no arc satisfying that floor");
                continue;
            }

            double3 want = arc.RequiredVelocityCci;

            // The decoupler pushes along the joint, which by the coast is the release line — take
            // it as the velocity direction, which is what the bus is holding.
            double3 kick = 1.1 * Vec.Unit(want);
            double3 actual = want + kick;

            // Five seconds of coasting, the same for every arrival, then ask what the trim owes.
            const double delay = 5.0;

            Assert.True(Kepler.TryCoast(Mu, from, want, delay, out _, out double3 shouldBe),
                        "the reference would not propagate");
            Assert.True(Kepler.TryCoast(Mu, from, actual, delay, out _, out double3 isDoing),
                        "the kicked arc would not propagate");

            double owed = Vec.Len(shouldBe - isDoing);

            Out.WriteLine($"{floor,7:F0}{arc.ArrivalAngleDeg,9:F1}{arc.FlightSeconds,10:F0}"
                          + $"{Vec.Len(want),15:F0}{owed,16:F3}{owed / 1.1,16:F2}");
        }
    }

    /// <summary>
    /// The same question with the delay varied, to separate "the arc amplifies a kick" from "the
    /// steep arc simply waits longer before anyone looks".
    /// </summary>
    [Fact]
    public void HowTheDebtGrowsWithTheDelay()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 aim = Ground(2_000_000.0);

        Out.WriteLine($"{"floor",7}{"arrival",9}" + string.Concat(
            new[] { 1.0, 5.0, 10.0, 20.0, 40.0 }.Select(s => $"{s + " s",12}")));

        foreach (double floor in new[] { 0.0, 34.0, 44.0, 54.0 })
        {
            if (!ArcAtFloor(from, aim, floor, out BallisticArc.Solution arc)) continue;

            double3 want = arc.RequiredVelocityCci;
            double3 actual = want + 1.1 * Vec.Unit(want);

            string row = "";
            foreach (double delay in new[] { 1.0, 5.0, 10.0, 20.0, 40.0 })
            {
                Kepler.TryCoast(Mu, from, want, delay, out _, out double3 shouldBe);
                Kepler.TryCoast(Mu, from, actual, delay, out _, out double3 isDoing);
                row += $"{Vec.Len(shouldBe - isDoing),12:F3}";
            }

            Out.WriteLine($"{floor,7:F0}{arc.ArrivalAngleDeg,9:F1}{row}");
        }
    }
}
