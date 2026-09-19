using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the ascent does when the target is close, which is not what a short shot wants.
///
/// <para><b>Every target from 100 km to 800 km gets the identical shot.</b> Same cutoff altitude,
/// same speed, same climb angle, same burn time, same propellant left — to every digit. A 100 km
/// target is flown exactly as an 800 km one, and the arc each actually wants is nothing like the
/// other: 1,697 m/s and a 73 km apogee at 300 km against 2,372 m/s and 146 km at 600.</para>
///
/// <para>It is not the release gate, the arrival floor or the pitch schedule. All three were moved
/// and none of them unsticks it: <c>DeployAltitudeMetres</c> to 20 km changes only the hold
/// message, <c>ArrivalPreference</c> to zero moves the long shots and not these, and
/// <c>TurnEndMetres</c> to 15 km moves the numbers by 6% and keeps them identical across the
/// range.</para>
///
/// <para>What is left is the handover. Closed-loop guidance cannot take the wheel until the air is
/// thin, and by then the stack is at ~65 km doing ~3 km/s — already more than a short shot needs,
/// so there is nothing left for the loop to decide. The floor is the ascent, and the ascent is
/// flown before anything knows how far away the target is.</para>
/// </summary>
public class ShortRangeAscentTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double3 OnTheGround(double eastMetres)
    {
        double a = eastMetres / R;
        return new double3(R * Math.Cos(a), R * Math.Sin(a), 0.0);
    }

    /// <summary>A pad-launched stack, standing still on the equator.</summary>
    // IcbmFlightTests' own pad rig, unchanged, because it is the one configuration known to fly a
    // ground launch here. The vehicle has to start co-rotating -- a pad is not at rest in Cci -- and
    // varying anything else would make the range stop being the only variable.
    private static IcbmFlightRig PadRig()
    {
        double3 pad = OnTheGround(0.0);

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

    /// <summary>
    /// What the flown shot does as the target comes closer, beside the arc that was wanted.
    ///
    /// <para><c>burnout r_dot</c> is the tell: a short lofted shot leaves steeply climbing, and an
    /// orbital insertion leaves nearly flat. If that stays flat as the range falls, the ascent is
    /// flying to orbit whatever it was asked for.</para>
    /// </summary>
    [Theory]
    [InlineData(100_000.0)]
    [InlineData(200_000.0)]
    [InlineData(300_000.0)]
    [InlineData(450_000.0)]
    [InlineData(600_000.0)]
    [InlineData(800_000.0)]
    [InlineData(1_200_000.0)]
    [InlineData(2_500_000.0)]
    [InlineData(6_269_000.0)]
    public void TheAscentFliesTheSameShapeWhateverTheRange(double rangeMetres)
    {
        IcbmProgram program = new(new IcbmConfig { Armed = true });
        double3 aim = OnTheGround(rangeMetres);
        IcbmFlightRig.Flight flight = PadRig().Fly(program, aim, 0.02, 4_000.0);

        string line = $"{rangeMetres / 1000.0,8:F0} km";

        if (!flight.Reached)
        {
            Out.WriteLine(line + $"   never reached cutoff: {flight.Hold}");
            return;
        }

        double speed = Vec.Len(flight.CutoffVelocityCci);
        double alt = Vec.Len(flight.CutoffPositionCci) - R;
        double rDot = Vec.Dot(flight.CutoffVelocityCci, Vec.Unit(flight.CutoffPositionCci));

        // The climb angle at burnout: 90 is straight up, 0 is horizontal.
        double climbDeg = double.RadiansToDegrees(Math.Asin(Math.Clamp(rDot / speed, -1.0, 1.0)));

        // What a circular orbit at that height would need, as the yardstick for "gone orbital".
        double circular = Math.Sqrt(Mu / (R + alt));

        Out.WriteLine(line
                      + $"   cutoff {alt / 1000.0,6:F1} km  {speed,7:F0} m/s"
                      + $"  climb {climbDeg,5:F1} deg"
                      + $"  {100.0 * speed / circular,5:F0}% of circular"
                      + $"  burn {flight.CutoffSeconds,5:F0} s"
                      + $"  left {flight.PropellantLeftKg,7:F0} kg"
                      + $"  {flight.FinalPhase}"
                      + (string.IsNullOrEmpty(flight.Hold) ? "" : $"  hold={flight.Hold}"));
    }

    /// <summary>
    /// The arc a short shot actually wants, for comparison — solved, not flown.
    ///
    /// <para>A minimum-energy arc to 300 km leaves at 45 degrees and needs a fraction of orbital
    /// speed. Anything the ascent does that looks nothing like this is the programme rather than
    /// the geometry.</para>
    /// </summary>
    [Fact]
    public void WhatAShortArcActuallyWants()
    {
        BallisticBody body = new(Mu, R, new double3(0, 0, 1), 0.0);

        Out.WriteLine($"{"range",10}{"best time",11}{"leaves at",11}{"needs",10}{"apogee",10}");

        foreach (double rangeKm in new[] { 300.0, 600.0, 1_200.0, 2_500.0, 6_269.0 })
        {
            double3 from = OnTheGround(0.0);
            double3 aim = OnTheGround(rangeKm * 1000.0);

            double bestCost = double.MaxValue, bestT = 0.0;
            BallisticArc.Solution best = default;

            for (double t = 60.0; t <= 3_000.0; t += 5.0)
            {
                if (!BallisticArc.TrySolve(body, from, aim, t, out BallisticArc.Solution s)) continue;
                if (s.LowestRadius < R * 0.999) continue;
                double cost = Vec.Len(s.RequiredVelocityCci);
                if (cost < bestCost) { bestCost = cost; best = s; bestT = t; }
            }

            if (bestCost == double.MaxValue)
            {
                Out.WriteLine($"{rangeKm,9:F0} km   no arc");
                continue;
            }

            double climb = double.RadiansToDegrees(Math.Asin(Math.Clamp(
                Vec.Dot(best.RequiredVelocityCci, Vec.Unit(from)) / bestCost, -1.0, 1.0)));

            Out.WriteLine($"{rangeKm,9:F0} km{bestT,10:F0} s{climb,10:F1} deg{bestCost,9:F0} m/s"
                          + $"{(best.ApogeeRadius - R) / 1000.0,9:F0} km");
        }
    }
}
