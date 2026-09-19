using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether a steeper arrival leaves more velocity ungained at cutoff. <c>docs/ACCURACY-PLAN.md</c>
/// 5g, the half of it that needs no flight.
///
/// <para><b>The question.</b> 3bo measured the trim being asked for 4.5x more at a 54 degree
/// arrival than at 44 — 3.410 m/s owed at the split against 0.760 — and that demand is what puts
/// rung C out of reach. The decoupler shove is the same at any angle, so the extra must come from
/// the cutoff state or from the clearance wait.</para>
///
/// <para><b>The candidate.</b> An engine stops on a frame boundary, so the last frame adds
/// <c>accel x step x throttle</c>. A steeper arrival costs more delta-v, so the burn runs longer
/// and the stack is lighter when it ends — and a lighter stack under the same thrust has a larger
/// <c>accel</c>. If that is the mechanism, the residual at cutoff rises with the arrival preference
/// on its own, with no trim and no clearance involved.</para>
///
/// <para>Measurement, not a threshold: the rig's stack is not the flown one, so what is being read
/// is whether the residual moves with the angle and by how much, not the flown value.</para>
/// </summary>
public class SteepCutoffResidualTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 Equator(double radians)
        => new(R * Math.Cos(radians), R * Math.Sin(radians), 0.0);

    // IcbmFlightTests' pad rig, unchanged, so the arrival preference is the only thing varying.
    private static IcbmFlightRig PadRig()
    {
        double3 pad = Equator(0.0);

        return new IcbmFlightRig
        {
            Body = Earth,
            PositionCci = pad,
            VelocityCci = Earth.GroundVelocityCci(pad),
            BoundingSphereRadiusMetres = 41.67,
            Stages =
            [
                new() { DryMassKg = 4_000, PropellantKg = 46_000, ThrustNewtons = 1_400_000, ExhaustVelocity = 2_600 },
                new() { DryMassKg = 1_200, PropellantKg = 12_000, ThrustNewtons = 1_940_000, ExhaustVelocity = 3_000 },
            ],
        };
    }

    [Fact]
    public void TheResidualAtCutoffAgainstTheArrivalAsked()
    {
        Out.WriteLine($"{"pref",6}{"floor",8}{"arrival",9}{"residual m/s",14}"
                      + $"{"burn s",9}{"left kg",10}{"accel g",9}");

        foreach (double preference in new[] { 0.0, 0.5, 0.65, 0.8, 1.0 })
        {
            IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = preference });
            double3 aim = Equator(0.3138);          // ~2,000 km, the geometry 2148 flew
            IcbmFlightRig.Flight flight = PadRig().Fly(program, aim, 0.02, 3_000.0);

            if (!flight.Reached)
            {
                Out.WriteLine($"{preference,6:F2}   never reached cutoff: {flight.Hold}");
                continue;
            }

            // What the stack could still do to itself on the last frame: thrust over the mass that
            // was left, which is the term the candidate says grows.
            double massLeft = 1_200 + flight.PropellantLeftKg;
            double accelGee = flight.PropellantLeftKg > 0.0
                                  ? 1_940_000.0 / massLeft / 9.80665
                                  : double.NaN;

            Out.WriteLine($"{preference,6:F2}{program.ArrivalFloorDeg,8:F1}"
                          + $"{ArrivalDeg(flight, aim),9:F1}"
                          + $"{program.ResidualAtCutoff,14:F3}"
                          + $"{flight.CutoffSeconds,9:F0}{flight.PropellantLeftKg,10:F0}"
                          + $"{accelGee,9:F1}");
        }
    }

    // The arc's own arrival angle from the state the burn left, for the row to be readable.
    private static double ArrivalDeg(IcbmFlightRig.Flight flight, double3 aim)
        => BallisticArc.TryCheapest(Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci,
                                    aim, out BallisticArc.Solution arc, 1.0, false, double.NaN, 0.0)
               ? arc.ArrivalAngleDeg
               : double.NaN;

    /// <summary>
    /// How far the guidance's predicted cutoff position is from where cutoff actually happened,
    /// against the arrival angle. `ACCURACY-PLAN.md` 5h.
    ///
    /// <para><b>Why it should get worse when steep.</b> `BurnoutGuidance` predicts the cutoff
    /// position by holding gravity at its present value and taking the thrust displacement as a
    /// straight line along the current thrust direction. Both approximations are self-cancelling
    /// while the loop keeps running — "the next cycle sees and removes it" — but the <em>last</em>
    /// cycle's prediction is never corrected, and it is the one the trim spends the whole coast
    /// nulling against. A steeper arrival burns longer and cuts off lighter, at 41.8 g against 24.2
    /// (3bp), so the same extrapolation covers more ground.</para>
    ///
    /// <para>This is the reference position `BusTrim` propagates. 3bs measured what a wrong one
    /// costs: 1.622 m/s per km up and 0.517 downrange at 54 degrees.</para>
    /// </summary>
    [Fact]
    public void HowFarTheCutoffPredictionMisses()
    {
        double3 aim = Equator(0.3138);

        Out.WriteLine($"{"pref",6}{"arrival",9}{"predicted vs actual",21}{"up",10}{"downrange",12}");

        foreach (double preference in new[] { 0.0, 0.5, 0.65, 0.8 })
        {
            IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = preference });
            IcbmFlightRig.Flight flight = PadRig().Fly(program, aim, 0.02, 3_000.0);

            if (!flight.Reached)
            {
                Out.WriteLine($"{preference,6:F2}   never reached cutoff");
                continue;
            }

            double3 err = program.CutoffPositionCci - flight.CutoffPositionCci;
            double3 up = Vec.Unit(flight.CutoffPositionCci);
            double3 along = Vec.Unit(flight.CutoffVelocityCci
                                     - Vec.Dot(flight.CutoffVelocityCci, up) * up);

            Out.WriteLine($"{preference,6:F2}{ArrivalDeg(flight, aim),9:F1}"
                          + $"{Vec.Len(err) / 1000.0,18:F3} km"
                          + $"{Vec.Dot(err, up) / 1000.0,10:F3}{Vec.Dot(err, along) / 1000.0,12:F3}");
        }
    }
}
