using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What a wrong reference position costs the trim, against the arrival angle. `ACCURACY-PLAN.md` 5h.
///
/// <para><b>Where the reference comes from.</b> During the burn `IcbmProgram` sets
/// <c>ReferencePositionCci = command.CutoffPositionCci</c> — the guidance's <em>prediction</em> of
/// where cutoff will happen, refreshed each cycle. The last cycle's prediction is the one that
/// survives, and nothing corrects it afterwards: cutoff arrives, the coast begins, and the trim
/// spends the whole flight nulling against a conic that departs from wherever that prediction
/// said.</para>
///
/// <para><b>Why that could be the missing factor.</b> `BusTrim` nulls
/// <c>RequiredVelocity(reference) - v</c>, and the required velocity is a function of the departure
/// point. Move the departure and the whole arc has to change to still reach the target in the
/// committed time. If that sensitivity is steeper at a steep arrival, a prediction error of the
/// same size costs more — which is the shape 3bo's unexplained 3.6x wants.</para>
///
/// <para>Measurement only, and it prices one term rather than claiming it is the whole.</para>
/// </summary>
public class ReferencePositionSensitivityTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 Ground(double metres)
        => new(R * Math.Cos(metres / R), R * Math.Sin(metres / R), 0.0);

    /// <summary>
    /// How much the required velocity moves per kilometre the departure point is wrong by, in the
    /// three directions a cutoff prediction can be wrong in.
    /// </summary>
    [Fact]
    public void RequiredVelocityAgainstAWrongDeparture()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 aim = Ground(2_000_000.0);

        double3 up = Vec.Unit(from);
        double3 downrange = Vec.Unit(aim - Vec.Dot(aim, up) * up);
        double3 cross = Vec.Cross(up, downrange);

        Out.WriteLine($"{"floor",7}{"arrival",9}{"flight s",10}"
                      + $"{"m/s per km up",16}{"downrange",12}{"cross",10}");

        foreach (double floor in new[] { 0.0, 20.0, 34.0, 44.0, 54.0, 60.0 })
        {
            if (!BallisticArc.TryCheapest(Earth, from, double3.Zero, aim,
                                          out BallisticArc.Solution arc, 1.0, false,
                                          double.NaN, floor))
            {
                Out.WriteLine($"{floor,7:F0}   no arc");
                continue;
            }

            // Hold the flight time the arc committed to; that is what the trim is solving against.
            double flight = arc.FlightSeconds;
            double3 want = arc.RequiredVelocityCci;

            double Rate(double3 axis)
            {
                const double step = 1_000.0;
                return BallisticArc.TrySolve(Earth, from + step * axis, aim, flight,
                                             out BallisticArc.Solution moved)
                           ? Vec.Len(moved.RequiredVelocityCci - want)
                           : double.NaN;
            }

            Out.WriteLine($"{floor,7:F0}{arc.ArrivalAngleDeg,9:F1}{flight,10:F0}"
                          + $"{Rate(up),16:F3}{Rate(downrange),12:F3}{Rate(cross),10:F3}");
        }
    }

    /// <summary>
    /// The same sensitivity expressed as what it takes to break the trim: how far the cutoff
    /// prediction has to be wrong before the demand crosses
    /// <see cref="BusTrim.MaxMetresPerSecond"/>.
    /// </summary>
    [Fact]
    public void HowWrongTheCutoffPredictionHasToBeToBreakTheTrim()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 aim = Ground(2_000_000.0);
        double3 up = Vec.Unit(from);

        Out.WriteLine($"{"floor",7}{"arrival",9}{"m/s per km",13}{"km to reach 10 m/s",21}");

        foreach (double floor in new[] { 0.0, 34.0, 44.0, 54.0 })
        {
            if (!BallisticArc.TryCheapest(Earth, from, double3.Zero, aim,
                                          out BallisticArc.Solution arc, 1.0, false,
                                          double.NaN, floor))
            {
                continue;
            }

            double3 want = arc.RequiredVelocityCci;

            if (!BallisticArc.TrySolve(Earth, from + 1_000.0 * up, aim, arc.FlightSeconds,
                                       out BallisticArc.Solution moved))
            {
                continue;
            }

            double perKm = Vec.Len(moved.RequiredVelocityCci - want);

            Out.WriteLine($"{floor,7:F0}{arc.ArrivalAngleDeg,9:F1}{perKm,13:F3}"
                          + $"{BusTrim.MaxMetresPerSecond / perKm,21:F2}");
        }
    }

    /// <summary>
    /// What a wrong <em>arrival time</em> costs, which is the other input the trim solves against.
    ///
    /// <para><c>IcbmComputer</c>'s own note prices this at "about 2.35 m/s for every second the
    /// arrival is out at 12,902 km", and that is where `BusTrim.MaxMetresPerSecond` is crossed at
    /// 4.3 s. If the rate is steeper at a steep arrival, the same second of latched-arrival error
    /// costs more — the last candidate for 3bo's unexplained factor.</para>
    /// </summary>
    [Fact]
    public void RequiredVelocityAgainstAWrongArrivalTime()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 aim = Ground(2_000_000.0);

        Out.WriteLine($"{"floor",7}{"arrival",9}{"flight s",10}"
                      + $"{"m/s per second out",20}{"s to reach 10 m/s",19}");

        foreach (double floor in new[] { 0.0, 34.0, 44.0, 54.0, 60.0 })
        {
            if (!BallisticArc.TryCheapest(Earth, from, double3.Zero, aim,
                                          out BallisticArc.Solution arc, 1.0, false,
                                          double.NaN, floor))
            {
                continue;
            }

            double3 want = arc.RequiredVelocityCci;

            if (!BallisticArc.TrySolve(Earth, from, aim, arc.FlightSeconds + 1.0,
                                       out BallisticArc.Solution later))
            {
                continue;
            }

            double perSecond = Vec.Len(later.RequiredVelocityCci - want);

            Out.WriteLine($"{floor,7:F0}{arc.ArrivalAngleDeg,9:F1}{arc.FlightSeconds,10:F0}"
                          + $"{perSecond,20:F3}{BusTrim.MaxMetresPerSecond / perSecond,19:F2}");
        }
    }
}
