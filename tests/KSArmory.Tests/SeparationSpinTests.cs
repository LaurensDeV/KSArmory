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
    private static readonly double3 TurningBodyRates = new(0.4e-3, 1.2e-3, -1.2e-3);

    // Rolling about its own axis and nothing else, which throws a ring and no common part.
    private static readonly double3 RollingBodyRates = new(1.5e-3, 0.0, 0.0);

    // The bar every correction is held to, against landings a metre or more apart.
    private const double OnePoint = 0.01;

    public enum Arc { Traced, Long }

    public enum Correction { None, Ring, Spin, Both }

    private readonly record struct Flight(double3 PositionCci, double3 VelocityCci, double3 MeanGroundCci,
                                          double Seconds, double ArrivalDeg);

    // Each tube's mouth from the tubes' mean and the spin it is thrown with, in Cci.
    private readonly record struct Tubes(double3[] OffsetsCci, double3[] SpinsCci, double3 CommonSpinCci,
                                         double3 LineCci, double MeanArm);

    private readonly record struct Group(double3[] FromMean, double3 Centre, double Across);

    private static Flight FlightFor(Arc arc)
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

    private static Tubes TubesFor(in Flight flight, double rollTurns, double3 bodyRates)
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
                         line, Vec.Len(meanMouth - CentreOfMassAsmb));
    }

    private static bool TryLand(double3 positionCci, double3 velocityCci, out double3 groundCci,
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

    private static double3 LandedFromTheMean(in Flight flight, double3 offsetCci, double3 velocityCci)
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

    // Six warheads from their own mouths, each thrown with its own spin and given what the correction asks.
    private static Group Fly(in Flight flight, in Tubes tubes, Correction correction)
    {
        bool ring = correction is Correction.Ring or Correction.Both;
        bool spin = correction is Correction.Spin or Correction.Both;

        double3[] fromMean = new double3[tubes.OffsetsCci.Length];
        double3 centre = Vec.Zero;

        for (int tube = 0; tube < fromMean.Length; tube++)
        {
            ReleaseFocus.Separation kick = ReleaseFocus.Kick(Earth, flight.PositionCci, flight.VelocityCci,
                                                             flight.Seconds, tubes.OffsetsCci[tube],
                                                             tubes.SpinsCci[tube], ring, spin);
            Assert.Equal(ring, kick.RingFocused);
            Assert.Equal(spin, kick.SpinCancelled);

            fromMean[tube] = LandedFromTheMean(flight, tubes.OffsetsCci[tube], tubes.SpinsCci[tube] + kick.KickCci);
            centre += fromMean[tube] / fromMean.Length;
        }

        double across = 0.0;
        for (int a = 0; a < fromMean.Length; a++)
        {
            for (int b = a + 1; b < fromMean.Length; b++) across = Math.Max(across, Vec.Len(fromMean[a] - fromMean[b]));
        }

        return new Group(fromMean, centre, across);
    }

    private void Say(Arc arc, Correction correction, in Group group)
        => Out.WriteLine($"  {arc,-6} {correction,-4}: centre {Vec.Len(group.Centre) * 100.0:F2} cm from the mean's impact, "
                         + $"{group.Across * 100.0:F2} cm across");

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

        Assert.True(worst < OnePoint, $"the prediction is {worst * 100.0:F1} cm from where a warhead lands");
    }

    /// <summary>
    /// Uncorrected, the spin the six share carries the group's centre off the mean's impact, to where
    /// the common spin alone lands, and the ring spreads them round it.
    /// </summary>
    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void WithNoCorrectionTheSpinMovesTheCentreAndTheRingSpreadsIt(Arc arc)
    {
        Flight flight = FlightFor(arc);
        Tubes tubes = TubesFor(flight, 0.37, TurningBodyRates);

        foreach (double3 offset in tubes.OffsetsCci) Assert.InRange(Vec.Len(offset), 0.858, 0.862);
        foreach (double3 spin in tubes.SpinsCci) Assert.InRange(Vec.Len(spin), 0.001, 0.006);

        Group plain = Fly(flight, tubes, Correction.None);
        Say(arc, Correction.None, plain);

        Assert.True(Vec.Len(plain.Centre) > 1.0, $"the centre is only {Vec.Len(plain.Centre):F2} m off");
        Assert.True(plain.Across > 0.3, $"the six are only {plain.Across:F2} m apart");
        Assert.True(Vec.Len(plain.Centre - Predicted(flight, Vec.Zero, tubes.CommonSpinCci)) < OnePoint,
                    "the centre is not where the common spin lands");
    }

    /// <summary>
    /// Focusing only the ring leaves every warhead where its spin alone would put it: the centre stays
    /// off, and what is left of the ring is the spin's.
    /// </summary>
    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void FocusingOnlyTheRingLeavesTheSpinsCentre(Arc arc)
    {
        Flight flight = FlightFor(arc);
        Tubes tubes = TubesFor(flight, 0.37, TurningBodyRates);

        Group plain = Fly(flight, tubes, Correction.None);
        Group ring = Fly(flight, tubes, Correction.Ring);
        Say(arc, Correction.Ring, ring);

        Assert.True(Vec.Len(ring.Centre - plain.Centre) < OnePoint,
                    $"focusing the ring moved the centre {Vec.Len(ring.Centre - plain.Centre) * 100.0:F1} cm");
        Assert.True(Vec.Len(ring.Centre) > 1.0, "the spin's centre went with the ring");

        for (int tube = 0; tube < tubes.SpinsCci.Length; tube++)
        {
            double3 spinAlone = LandedFromTheMean(flight, Vec.Zero, tubes.SpinsCci[tube]);
            Assert.True(Vec.Len(ring.FromMean[tube] - spinAlone) < OnePoint,
                        $"tube {tube + 1} is {Vec.Len(ring.FromMean[tube] - spinAlone) * 100.0:F1} cm from its spin's own landing");
        }

        Assert.True(ring.Across > 0.05, $"no spin ring survived: {ring.Across * 100.0:F1} cm across");
    }

    /// <summary>
    /// A bus rolling about its own axis throws each round square to its offset, so what survives a
    /// position-only focus is the ring again, a quarter turn round the tubes.
    /// </summary>
    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void ARollingBusLeavesItsRingTurnedAQuarter(Arc arc)
    {
        Flight flight = FlightFor(arc);
        Tubes tubes = TubesFor(flight, 0.37, RollingBodyRates);

        Assert.True(Vec.Len(tubes.CommonSpinCci) < 1e-9, "a roll about the axis has no common part");

        Group ringOnly = Fly(flight, tubes with { SpinsCci = new double3[tubes.SpinsCci.Length] }, Correction.None);
        Group focused = Fly(flight, tubes, Correction.Ring);

        Assert.True(focused.Across > 0.05, $"nothing survived the focus: {focused.Across * 100.0:F1} cm across");

        double3 up = Vec.Unit(flight.MeanGroundCci);
        double3 alongFirst = Vec.Unit(Vec.RejectFrom(ringOnly.FromMean[0], up));
        double3 acrossFirst = Vec.Cross(up, alongFirst);
        double3 e1 = Vec.Unit(tubes.OffsetsCci[0]);
        double3 e2 = Vec.Cross(tubes.LineCci, e1);

        // A quarter turn back round the tubes, measured on two ground axes. Not exactly a quarter: a
        // velocity lands only roughly where the same offset over the flight time does.
        foreach (double3 axis in new[] { alongFirst, acrossFirst })
        {
            double turn = Math.IEEERemainder(Phase(focused, axis) - Phase(ringOnly, axis), 360.0);
            Out.WriteLine($"  {arc}: turned {turn:F2} deg");

            Assert.InRange(turn, -95.0, -85.0);
        }

        double Phase(in Group group, double3 axis)
        {
            double a = 0.0, b = 0.0;

            for (int tube = 0; tube < group.FromMean.Length; tube++)
            {
                double3 offset = tubes.OffsetsCci[tube];
                double theta = Math.Atan2(Vec.Dot(offset, e2), Vec.Dot(offset, e1));
                double reading = Vec.Dot(group.FromMean[tube] - group.Centre, axis);
                a += reading * Math.Cos(theta);
                b += reading * Math.Sin(theta);
            }

            return Math.Atan2(b, a) * 180.0 / Math.PI;
        }
    }

    /// <summary>
    /// Giving the spin back restores the centre and leaves each warhead exactly where its tube's ring
    /// alone would put it.
    /// </summary>
    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void CancellingTheSpinRestoresTheCentreAndLeavesTheRing(Arc arc)
    {
        Flight flight = FlightFor(arc);
        Tubes tubes = TubesFor(flight, 0.37, TurningBodyRates);

        Group plain = Fly(flight, tubes, Correction.None);
        Assert.True(Vec.Len(plain.Centre) > 1.0, "the spin does not move this centre, so nothing here is tested");

        Group cancelled = Fly(flight, tubes, Correction.Spin);
        Say(arc, Correction.Spin, cancelled);

        Assert.True(Vec.Len(cancelled.Centre) < OnePoint,
                    $"the centre is still {Vec.Len(cancelled.Centre) * 100.0:F1} cm off");

        for (int tube = 0; tube < tubes.OffsetsCci.Length; tube++)
        {
            double3 ringAlone = LandedFromTheMean(flight, tubes.OffsetsCci[tube], Vec.Zero);
            Assert.True(Vec.Len(cancelled.FromMean[tube] - ringAlone) < 0.001,
                        $"tube {tube + 1} is {Vec.Len(cancelled.FromMean[tube] - ringAlone) * 100.0:F2} cm from its ring's own landing");
        }
    }

    [Theory]
    [InlineData(Arc.Traced)]
    [InlineData(Arc.Long)]
    public void BothLandEveryWarheadOnTheMeansImpact(Arc arc)
    {
        Flight flight = FlightFor(arc);
        Tubes tubes = TubesFor(flight, 0.37, TurningBodyRates);

        Group plain = Fly(flight, tubes, Correction.None);
        Assert.True(Vec.Len(plain.Centre) > 1.0 && plain.Across > 0.3, "nothing here needs correcting");

        Group both = Fly(flight, tubes, Correction.Both);
        Say(arc, Correction.Both, both);

        foreach (double3 landed in both.FromMean)
        {
            Assert.True(Vec.Len(landed) < OnePoint, $"a warhead is {Vec.Len(landed) * 100.0:F2} cm from the mean's impact");
        }
    }

    /// <summary>With the spin not given back, a separation is exactly what the ring focus alone gave.</summary>
    [Fact]
    public void WithTheCancellationOffTheKickIsTheRingFocusExactly()
    {
        Flight flight = FlightFor(Arc.Traced);
        Tubes tubes = TubesFor(flight, 0.37, TurningBodyRates);

        for (int tube = 0; tube < tubes.OffsetsCci.Length; tube++)
        {
            double3 offset = tubes.OffsetsCci[tube];
            double3 spin = tubes.SpinsCci[tube];

            Assert.True(ReleaseFocus.TryKick(Earth, flight.PositionCci, flight.VelocityCci, flight.Seconds, offset,
                                             out double3 ringKick));
            Assert.True(Vec.Len(spin) > 0.001 && Vec.Len(ringKick) > 0.001, "nothing to tell the kicks apart by");

            Assert.Equal(ringKick, Kick(true, false).KickCci);
            Assert.Equal(Vec.Zero, Kick(false, false).KickCci);
            Assert.Equal(-spin, Kick(false, true).KickCci);
            Assert.Equal(ringKick - spin, Kick(true, true).KickCci);

            ReleaseFocus.Separation Kick(bool ring, bool cancel)
                => ReleaseFocus.Kick(Earth, flight.PositionCci, flight.VelocityCci, flight.Seconds, offset, spin,
                                     ring, cancel);
        }
    }
}
