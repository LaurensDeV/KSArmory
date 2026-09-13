using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// A turning bus throws each warhead with the spin at its own mouth, and what that and the tube ring
/// do on the ground.
///
/// <para>Every release prediction flies the tubes' mean without spin, so the aim loop lands that on
/// the target. Each round leaves its own mouth on a 0.86 m ring carrying <c>ω × arm</c>: the part
/// common to the six moves the group's centre, and the part that goes round the ring turns what is
/// left of it. <c>docs/ACCURACY-PLAN.md</c> items 41 and 42.</para>
///
/// <para>The bus sits where <c>SOLVER SCALE 8</c> stacks it, which a split keeps: its centre of mass
/// 2.34 m behind the tubes' mean, 2.44 m past the rocket's assembly origin. Landings are read to the
/// surface, as <see cref="ReleaseFocusTests"/> reads them.</para>
/// </summary>
public class SeparationSpinTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_375_278.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    // One warhead away on 2026-09-12-query, shot 001 seat 8: 852 km up, 5.0 km/s, 340 s to go.
    private static readonly double3 TracedPositionCci = new(3_326_222.3, -6_201_274.6, -1_632_562.5);
    private static readonly double3 TracedVelocityCci = new(-146.5688, 2_952.0146, -4_034.5339);
    private const double ThrownFromTrackDeg = 142.0;

    // The bus part as the save places it on the rocket's assembly axis, and its mass seat.
    private static readonly double3 BusPositionAsmb = new(-2.7426, 0, 0);
    private static readonly doubleQuat BusRotationAsmb = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), Math.PI);
    private static readonly double3 CentreOfMassAsmb = BusPositionAsmb + BusRotationAsmb * new double3(0.30, 0, 0);

    // A bus turning at the rate that throws the common spin the night logged, about 4 mm/s.
    internal static readonly double3 TurningBodyRates = new(0.4e-3, 1.2e-3, -1.2e-3);

    public enum Arc { Traced, Long }

    internal readonly record struct Flight(double3 PositionCci, double3 VelocityCci, double3 MeanGroundCci,
                                           double Seconds, double ArrivalDeg);

    // Each tube's mouth from the tubes' mean and the spin it is thrown with, in Cci.
    internal readonly record struct Tubes(double3[] OffsetsCci, double3[] SpinsCci, double3 CommonSpinCci,
                                          double MeanArm);

    internal static Flight FlightFor(Arc arc)
    {
        double3 position = TracedPositionCci, velocity = TracedVelocityCci;

        if (arc == Arc.Long)
        {
            // A lofted arc from the same release point, flown for five times as long.
            double3 normal = Vec.Unit(Vec.Cross(TracedPositionCci, TracedVelocityCci));
            double3 target = doubleQuat.CreateFromAxisAngle(normal, 40.0 * Math.PI / 180.0)
                             * Vec.Unit(TracedPositionCci) * R;

            Assert.True(Lambert.TrySolve(position, target, 1_500.0, Mu, out Lambert.Transfer transfer));
            velocity = transfer.DepartureVelocityCci;
        }

        Assert.True(TryLand(position, velocity, out double3 ground, out ImpactPredictor.Impact mean),
                    "the mean state never came down");
        Assert.True(ArrivalFrame.TryAt(mean.PointCci, mean.VelocityCci, out ArrivalFrame frame));

        Flight flight = new(position, velocity, ground, mean.Seconds, frame.BelowHorizontalDegrees(mean.VelocityCci));

        if (arc == Arc.Traced)
        {
            Assert.InRange(flight.Seconds, 320.0, 360.0);
            Assert.InRange(flight.ArrivalDeg, 28.0, 36.0);
        }
        else
        {
            Assert.InRange(flight.Seconds, 1_400.0, 1_600.0);
        }

        return flight;
    }

    internal static Tubes TubesFor(in Flight flight, double rollTurns, double3 bodyRates)
    {
        double3 track = Vec.Unit(flight.VelocityCci);
        double3 down = Vec.Unit(Vec.Cross(Vec.Cross(flight.PositionCci, flight.VelocityCci), flight.VelocityCci));
        double thrown = ThrownFromTrackDeg * Math.PI / 180.0;
        double3 line = track * Math.Cos(thrown) + down * Math.Sin(thrown);

        doubleQuat asmb2Cci = doubleQuat.CreateFromAxisAngle(line, rollTurns * 2.0 * Math.PI)
                              * Vec.RotationFromTo(new double3(1, 0, 0), line);
        Assert.True(Vec.AngleBetween(asmb2Cci * new double3(1, 0, 0), line) < 1e-9, "the bus is not on its line");

        LauncherProfile bus = Arsenal.MirvBus;
        double3[] mouths = new double3[bus.TubeCount];
        double3 meanMouth = Vec.Zero;

        for (int tube = 0; tube < mouths.Length; tube++)
        {
            Assert.True(TubeGeometry.TryMuzzlePartFrame(bus, tube, Vec.Zero, doubleQuat.Identity, out double3 part));
            mouths[tube] = BusPositionAsmb + BusRotationAsmb * part;
            meanMouth += mouths[tube] / mouths.Length;
        }

        double3 omega = asmb2Cci * bodyRates;
        double3[] offsets = new double3[mouths.Length];
        double3[] spins = new double3[mouths.Length];

        for (int tube = 0; tube < mouths.Length; tube++)
        {
            offsets[tube] = asmb2Cci * (mouths[tube] - meanMouth);
            spins[tube] = FireGeometry.SpinVelocityAt(omega, asmb2Cci, mouths[tube], CentreOfMassAsmb);
        }

        return new Tubes(offsets, spins, FireGeometry.SpinVelocityAt(omega, asmb2Cci, meanMouth, CentreOfMassAsmb),
                         Vec.Len(meanMouth - CentreOfMassAsmb));
    }

    internal static bool TryLand(double3 positionCci, double3 velocityCci, out double3 groundCci,
                                 out ImpactPredictor.Impact hit)
    {
        groundCci = Vec.Zero;

        if (!ImpactPredictor.TryPredict(Earth, positionCci, velocityCci, 2.0, 20_000.0, out hit, null, null,
                                        new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21)))
        {
            return false;
        }

        double3 up = Vec.Unit(hit.PointCci);
        double depth = R - Vec.Len(hit.PointCci);
        double sinking = -Vec.Dot(hit.VelocityCci, up);
        if (!(sinking > 0.0)) return false;

        double back = depth / sinking;
        groundCci = Earth.UncarryCci(hit.PointCci - hit.VelocityCci * back, hit.Seconds - back);
        return true;
    }

    internal static double3 LandedFromTheMean(in Flight flight, double3 offsetCci, double3 velocityCci)
    {
        Assert.True(TryLand(flight.PositionCci + offsetCci, flight.VelocityCci + velocityCci,
                            out double3 ground, out _), "a warhead never came down");
        return ground - flight.MeanGroundCci;
    }

    // The diagnostic's prediction, carried back into the ground's frame the landings are read in.
    private static double3 Predicted(in Flight flight, double3 offsetCci, double3 velocityCci)
    {
        Assert.True(ReleaseFocus.TryLandingShift(Earth, flight.PositionCci, flight.VelocityCci, flight.Seconds,
                                                 offsetCci, velocityCci, out double3 shift));
        return Earth.UncarryCci(shift, flight.Seconds);
    }

    /// <summary>
    /// What the release line prints is where each warhead lands, for its ring, its spin and both —
    /// the slope a night regresses the landings on.
    /// </summary>
    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void TheLandingShiftPredictsWhereTheRingAndTheSpinLand(Arc arc)
    {
        Flight flight = FlightFor(arc);
        Tubes tubes = TubesFor(flight, 0.37, TurningBodyRates);

        Assert.InRange(tubes.MeanArm, 2.33, 2.35);
        Assert.InRange(Vec.Len(tubes.CommonSpinCci), 0.003, 0.005);

        double worst = 0.0;

        for (int tube = 0; tube < tubes.OffsetsCci.Length; tube++)
        {
            double3 offset = tubes.OffsetsCci[tube];
            double3 spin = tubes.SpinsCci[tube];

            (double3 flown, double3 predicted)[] cases =
            [
                (LandedFromTheMean(flight, offset, Vec.Zero), Predicted(flight, offset, Vec.Zero)),
                (LandedFromTheMean(flight, Vec.Zero, spin), Predicted(flight, Vec.Zero, spin)),
                (LandedFromTheMean(flight, offset, spin), Predicted(flight, offset, spin)),
            ];

            foreach ((double3 flown, double3 predicted) in cases)
            {
                Assert.True(Vec.Len(flown) > 0.1, $"tube {tube + 1} lands {Vec.Len(flown) * 100.0:F1} cm out, "
                                                  + "which a prediction of nothing would match");
                worst = Math.Max(worst, Vec.Len(flown - predicted));
            }
        }

        Out.WriteLine($"  {arc}: {flight.Seconds:F0} s at {flight.ArrivalDeg:F1} deg, "
                      + $"worst prediction {worst * 100.0:F2} cm");

        Assert.True(worst < 0.01, $"the prediction is {worst * 100.0:F1} cm from where a warhead lands");
    }
}
