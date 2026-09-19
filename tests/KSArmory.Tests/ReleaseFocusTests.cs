using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Six warheads from six mouths on a ring, one velocity, and the kick that lands them on one point.
///
/// <para>Every release prediction is of the tubes' mean, so the aim loop lands the mean on the target
/// and each round on the ground image of its own mouth's offset from it. <see cref="ReleaseFocus"/>
/// is the separation velocity that cancels that image. <c>docs/ACCURACY-PLAN.md</c> item 41.</para>
///
/// <para><b>Landings are read to the surface, not to where the predictor stopped.</b> The predictor
/// accepts a crossing up to <see cref="ImpactPredictor.CrossingToleranceMetres"/> deep, which at a
/// 32° arrival is 0.4 m of ground varying with where each arc's steps happen to fall — eight times the
/// bar these tests hold. Carrying each stop back up its own velocity to the sphere removes that to
/// micrometres and leaves the flight itself, drag included, untouched.</para>
/// </summary>
public class ReleaseFocusTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;

    // The stop radius on the traced flight, so the traced state arrives when and how it did.
    private const double R = 6_375_278.0;

    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    // One warhead away on 2026-09-12-query, shot 001 seat 8: 852 km up, 5.0 km/s, 340 s to go.
    private static readonly double3 TracedPositionCci = new(3_326_222.3, -6_201_274.6, -1_632_562.5);
    private static readonly double3 TracedVelocityCci = new(-146.5688, 2_952.0146, -4_034.5339);

    // The release line that flight reported, as the angle from the track.
    private const double ThrownFromTrackDeg = 142.0;

    private const double RingRadius = 0.86;

    // What the fix is for: the six on one point to within what nobody could see.
    private const double FocusedAcross = 0.05;

    private enum Kick { None, Solved, Reversed, OffsetOverFlight }

    private readonly record struct Group(double Across, double3 CentreCci, double FlightSeconds,
                                         double ArrivalDeg, double LargestKick, double SmallestKick);

    /// <summary>Six tubes' offsets from their mean, on a bus pointing its tubes along the release line.</summary>
    private static double3[] Ring(double3 velocityCci, double rollTurns)
    {
        double3 track = Vec.Unit(velocityCci);
        double3 down = Vec.Unit(Vec.Cross(Vec.Cross(TracedPositionCci, velocityCci), velocityCci));
        double thrown = ThrownFromTrackDeg * Math.PI / 180.0;
        double3 line = track * Math.Cos(thrown) + down * Math.Sin(thrown);

        doubleQuat bus = doubleQuat.CreateFromAxisAngle(line, rollTurns * 2.0 * Math.PI)
                         * Vec.RotationFromTo(new double3(1, 0, 0), line);

        LauncherProfile launcher = Arsenal.MirvBus;
        double3[] offsets = new double3[launcher.TubeCount];

        for (int tube = 0; tube < offsets.Length; tube++)
        {
            Assert.True(TubeGeometry.TryOffsetFromMeanPartFrame(launcher, tube, Vec.Zero,
                                                                doubleQuat.Identity, out double3 inPart));
            offsets[tube] = bus * inPart;
        }

        return offsets;
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

    private Group Fly(double3 positionCci, double3 velocityCci, double rollTurns, Kick kick)
    {
        Group group = FlyQuietly(positionCci, velocityCci, rollTurns, kick);
        Out.WriteLine($"  {kick,-16} roll {rollTurns:F2}: {group.Across * 100.0:F2} cm across, "
                      + $"kicks {group.SmallestKick * 1000.0:F3}-{group.LargestKick * 1000.0:F3} mm/s, "
                      + $"{group.FlightSeconds:F1} s at {group.ArrivalDeg:F1} deg");
        return group;
    }

    private static Group FlyQuietly(double3 positionCci, double3 velocityCci, double rollTurns, Kick kick)
    {
        Assert.True(TryLand(positionCci, velocityCci, out _, out ImpactPredictor.Impact mean),
                    "the mean state never came down");

        Assert.True(ArrivalFrame.TryAt(mean.PointCci, mean.VelocityCci, out ArrivalFrame arrival));
        double arrivalDeg = arrival.BelowHorizontalDegrees(mean.VelocityCci);

        double3[] ring = Ring(velocityCci, rollTurns);
        double3[] landed = new double3[ring.Length];
        double largest = 0.0, smallest = double.MaxValue;

        for (int tube = 0; tube < ring.Length; tube++)
        {
            double3 velocity = Vec.Zero;

            if (kick is Kick.Solved or Kick.Reversed)
            {
                Assert.True(ReleaseFocus.TryKick(Earth, positionCci, velocityCci, mean.Seconds, ring[tube],
                                                 out velocity),
                            $"tube {tube + 1} would not solve");
                if (kick == Kick.Reversed) velocity = -velocity;
            }
            else if (kick == Kick.OffsetOverFlight)
            {
                velocity = -ring[tube] / mean.Seconds;
            }

            largest = Math.Max(largest, Vec.Len(velocity));
            smallest = Math.Min(smallest, Vec.Len(velocity));

            Assert.True(TryLand(positionCci + ring[tube], velocityCci + velocity, out landed[tube], out _),
                        $"tube {tube + 1} never came down");
        }

        double across = 0.0;
        double3 centre = Vec.Zero;

        for (int a = 0; a < landed.Length; a++)
        {
            centre += landed[a] / landed.Length;
            for (int b = a + 1; b < landed.Length; b++) across = Math.Max(across, Vec.Len(landed[a] - landed[b]));
        }

        return new Group(across, centre, mean.Seconds, arrivalDeg, largest, smallest);
    }

    // A lofted arc from the same release point, flown for five times as long.
    private static void LongFlight(out double3 positionCci, out double3 velocityCci)
    {
        positionCci = TracedPositionCci;
        double3 normal = Vec.Unit(Vec.Cross(TracedPositionCci, TracedVelocityCci));
        double3 target = doubleQuat.CreateFromAxisAngle(normal, 40.0 * Math.PI / 180.0)
                         * Vec.Unit(TracedPositionCci) * R;

        Assert.True(Lambert.TrySolve(positionCci, target, 1_500.0, Mu, out Lambert.Transfer transfer));
        velocityCci = transfer.DepartureVelocityCci;
    }

    private static void AssertTheTracedRegime(in Group group)
    {
        Assert.InRange(group.FlightSeconds, 320.0, 360.0);
        Assert.InRange(group.ArrivalDeg, 28.0, 36.0);
    }

    /// <summary>The fixture carries the bus's own ring, and the ring is what spreads the group.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void WithoutAKickTheSixLandAsTheirRing(double roll)
    {
        foreach (double3 offset in Ring(TracedVelocityCci, roll))
        {
            Assert.InRange(Vec.Len(offset), RingRadius - 0.002, RingRadius + 0.002);
        }

        Group plain = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.None);
        AssertTheTracedRegime(plain);

        Assert.InRange(plain.Across, 1.2, 3.5);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void TheKickLandsEveryTubeOnTheMeansImpact(double roll)
    {
        Group plain = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.None);
        AssertTheTracedRegime(plain);
        Assert.True(plain.Across >= 1.2, $"only {plain.Across:F2} m across without a kick");

        Group focused = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.Solved);

        Assert.True(focused.Across < FocusedAcross,
                    $"the kick left {focused.Across * 100.0:F1} cm across, from {plain.Across * 100.0:F0}");
    }

    /// <summary>The sign: the same kick the other way lands each round as far out again.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void AKickTheWrongWayDoublesTheSpread(double roll)
    {
        Group plain = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.None);
        AssertTheTracedRegime(plain);
        Assert.True(plain.Across >= 1.2, $"only {plain.Across:F2} m across without a kick");

        Group reversed = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.Reversed);

        Assert.InRange(reversed.Across / plain.Across, 1.8, 2.2);
    }

    /// <summary>
    /// And a solve rather than <c>-offset / T</c>, which a flight long enough for gravity's gradient to
    /// matter tells apart.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void TheKickIsSolvedRatherThanTheOffsetOverTheFlight(double roll)
    {
        LongFlight(out double3 position, out double3 velocity);

        Group naive = Fly(position, velocity, roll, Kick.OffsetOverFlight);
        Assert.InRange(naive.FlightSeconds, 1_400.0, 1_600.0);
        Assert.True(naive.Across > 4.0 * FocusedAcross,
                    $"-offset/T already lands them within {naive.Across * 100.0:F1} cm, so this flight "
                    + "cannot tell a solve from it");

        Group focused = Fly(position, velocity, roll, Kick.Solved);

        Assert.True(focused.Across < FocusedAcross,
                    $"the kick left {focused.Across * 100.0:F1} cm across at {focused.FlightSeconds:F0} s");
    }

    /// <summary>The kicks sum to nothing over the ring, so what the aim loop is closing does not move.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void TheKickLeavesTheGroupsCentreWhereItWas(double roll)
    {
        Group plain = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.None);
        Group focused = Fly(TracedPositionCci, TracedVelocityCci, roll, Kick.Solved);
        AssertTheTracedRegime(focused);
        Assert.True(focused.SmallestKick > 0.001, $"a kick of {focused.SmallestKick * 1000.0:F3} mm/s moves nothing");

        double moved = Vec.Len(focused.CentreCci - plain.CentreCci);
        Out.WriteLine($"  centre moved {moved * 1e6:F1} um");

        Assert.True(moved < 0.01, $"the group's centre moved {moved * 100.0:F1} cm");
    }

    [Fact]
    public void AKickIsRefusedOnceTheRoundHasFlown()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        double3 kick = new(0.002, -0.001, 0.0005);

        Slug round = new(TracedPositionCci, TracedVelocityCci, null, 1, TracedPositionCci, Vec.Zero)
        {
            Munition = warhead,
        };

        Assert.Equal(0.0, round.Age);
        Assert.True(round.TryAddSeparationVelocity(kick));
        Assert.Equal(TracedVelocityCci + kick, round.VelocityEcl);

        round.Update(0.02, null, Vec.Zero, Vec.Zero, TracedPositionCci, warhead, 0.0);
        Assert.True(round.Age > 0.0, "the round never stepped, so nothing here is tested");

        double3 flying = round.VelocityEcl;

        Assert.False(round.TryAddSeparationVelocity(kick));
        Assert.Equal(flying, round.VelocityEcl);
    }
}
