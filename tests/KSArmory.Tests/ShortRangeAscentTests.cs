using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the ascent does as the target comes closer, beside the arc a short shot wants.
///
/// <para><c>docs/SHORT-RANGE.md</c>, "What is known", has why every short target in this rig flew
/// one identical cutoff — closed-loop guidance cannot take the wheel until the air is thin, by which
/// time the stack is already past what a short shot needs — and what answered it.</para>
///
/// <para>This rig is two liquid stages that throttle and stop on command; the stack the game flies
/// is in <see cref="GameStackShortRangeTests"/>.</para>
/// </summary>
[Trait("kind", "study")]
public class ShortRangeAscentTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    internal static double3 OnTheGround(double eastMetres)
    {
        double a = eastMetres / R;
        return new double3(R * Math.Cos(a), R * Math.Sin(a), 0.0);
    }

    /// <summary>A pad-launched stack, standing still on the equator.</summary>
    // IcbmFlightTests' own pad rig, unchanged, because it is the one configuration known to fly a
    // ground launch here. The vehicle has to start co-rotating -- a pad is not at rest in Cci -- and
    // varying anything else would make the range stop being the only variable.
    internal static IcbmFlightRig PadRig()
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
