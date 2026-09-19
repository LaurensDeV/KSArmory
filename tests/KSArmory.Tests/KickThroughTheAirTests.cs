using System.Diagnostics;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Six warheads released 22 ms apart from the flown bus, each kicked onto the group's aim, and where each lands:
/// the separation kick solved in vacuum against the same kick solved through the air.
/// </summary>
/// <remarks>
/// <para><b>The vacuum solve is the spread 3eo flew.</b> Coasted for the drag flight's time the arc it solves on
/// is 1.9 km under the ground on the Mk 21's constant and 7.8 km on its drag from its shape, and it leaves 0.19%
/// and 0.80% of the ring's image: 2.1 and 8.8 mm of each group's spread, against 3.1 and 8.5 mm flown with the
/// walk's own scatter on top, and the flown landings regress on the logged ring at +0.15% and +0.63%.
/// <c>docs/ACCURACY-PLAN.md</c> 3eq.</para>
///
/// <para><b>A planet at the origin is enough here</b>, unlike for the walk: every warhead of a salvo carries the
/// same frame terms, and what is scored is where the six land against each other and against the probe's own
/// prediction.</para>
/// </remarks>
public class KickThroughTheAirTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_375_278.0;
    private const double Omega = 7.2921159e-5;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), Omega);

    private static double DensityAt(double3 p) => Math.Exp(-Math.Max(0.0, Vec.Len(p) - R) / 8_000.0);

    // ReleaseFocusTests' traced release: 852 km, 5.0 km/s, 340 s to go.
    private static readonly double3 TracedPositionCci = new(3_326_222.3, -6_201_274.6, -1_632_562.5);
    private static readonly double3 TracedVelocityCci = new(-146.5688, 2_952.0146, -4_034.5339);

    // The release line the Chaco night reported.
    private const double ThrownFromTrackDeg = 164.0;

    private const double ReleaseGapSeconds = 0.022;

    private static MunitionProfile Constant => Arsenal.ReentryVehicleMk21;
    private static MunitionProfile Shape => Arsenal.Mk21WithDragFromShape(Arsenal.ReentryVehicleMk21);

    private sealed class Sphere : IGroundTest
    {
        public bool TryGround(double3 positionEcl, out double3 centreEcl, out double surfaceRadius)
        {
            centreEcl = Vec.Zero;
            surfaceRadius = R;
            return true;
        }
    }

    public enum Solve { None, Vacuum, ThroughTheAir }

    [Flags]
    private enum Terms { Ring = 1, Miss = 2, Both = 3 }

    private readonly record struct Group(double CentreDown, double CentreCross, double Spread);

    private static double3[] Ring(double3 positionCci, double3 velocityCci)
    {
        double3 track = Vec.Unit(velocityCci);
        double3 down = Vec.Unit(Vec.Cross(Vec.Cross(positionCci, velocityCci), velocityCci));
        double thrown = ThrownFromTrackDeg * Math.PI / 180.0;
        double3 line = track * Math.Cos(thrown) + down * Math.Sin(thrown);
        doubleQuat bus = Vec.RotationFromTo(new double3(1, 0, 0), line);

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

    private static ImpactPredictor.Impact Predict(MunitionProfile warhead, double3 p, double3 v)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, p, v, 2.0, 20_000.0, out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DensityAt, warhead),
                                               stopOnTheSurface: true));
        return hit;
    }

    private static ReleaseFocus.Air AirFor(MunitionProfile warhead, double groundRadius)
        => new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, groundRadius, true);

    // The round as IcbmComputer configures a released warhead. Ground-fixed at the release instant.
    private static double3 FlyTheRound(MunitionProfile warhead, double3 p, double3 v)
    {
        const double Frame = 0.025;

        Slug round = new(p, v, null, 1, p, Vec.Zero)
        {
            Munition = warhead,
            ResampleGroundNearImpact = true,
            SecondOrder = true,
            DragAtMidpointVelocity = true,
            StopOnTheTerrain = true,
            AirVelocityAtOwnSubStep = true,
            GroundQueryAtOwnEpoch = true,
        };

        double3 spin = new(0, 0, Omega);

        RoundFields fields = new(
            GravityAt: (q, _) => Vec.Unit(-q) * (Mu / Vec.Len2(q)),
            AirDensityAt: (q, _) => DensityAt(q),
            Ground: new Sphere(),
            AirVelocityAt: (q, _) => Vec.Cross(spin, q));

        double t = 0.0;
        for (int i = 0; i < 1_000_000 && round.State == RoundState.Flying; i++)
        {
            t += Frame;
            RoundDriver.Fly(round, Frame, null, Vec.Unit(-round.PositionEcl) * (Mu / Vec.Len2(round.PositionEcl)),
                            Vec.Cross(spin, round.PositionEcl), Vec.Zero, warhead, 0.0, fields);
        }

        Assert.True(round.HitGround);
        return Earth.UncarryCci(round.PositionEcl, t + round.DetonationElapsedInFrame);
    }

    /// <summary>
    /// A salvo as the computer lets it go: each warhead from the bus coasted on, with its own probe, its tube's
    /// offset, the spin its mouth threw it with, and the aim loop's miss — 0.5 m long and 0.2 m across.
    /// </summary>
    /// <param name="columnsLateBy">How long after the columns were flown the first warhead leaves.</param>
    private static Group Salvo(MunitionProfile warhead, Solve solve, Terms terms, bool throughTheRound = false,
                               double columnsLateBy = 0.0)
    {
        double3 bus = TracedPositionCci;
        double3 busVelocity = TracedVelocityCci;

        ImpactPredictor.Impact first = Predict(warhead, bus, busVelocity);
        Assert.True(ArrivalFrame.TryAt(first.PointCci, first.VelocityCci, out ArrivalFrame frame));

        double3 aim = (terms & Terms.Miss) != 0
            ? first.GroundFixedPointCci - Earth.UncarryCci(frame.Downrange * 0.5 + frame.Cross * 0.2, first.Seconds)
            : first.GroundFixedPointCci;

        double3[] ring = Ring(bus, busVelocity);
        double3 turning = Vec.Unit(Vec.Cross(bus, busVelocity)) * 0.757e-3;
        double3 common = Vec.Unit(Vec.Cross(busVelocity, bus)) * 0.0036;

        ReleaseFocus.FlownSensitivity? columns = solve == Solve.ThroughTheAir
            ? ReleaseFocus.FlownSensitivity.TryFly(Earth, bus, busVelocity, AirFor(warhead, Vec.Len(first.PointCci)))
            : null;
        Assert.True(solve != Solve.ThroughTheAir || columns is not null, "the columns did not come down");

        double3[] landed = new double3[ring.Length];

        for (int tube = 0; tube < ring.Length; tube++)
        {
            double since = columnsLateBy + tube * ReleaseGapSeconds;
            Assert.True(Kepler.TryCoast(Mu, bus, busVelocity, since, out double3 from, out double3 velocity));

            ImpactPredictor.Impact probe = Predict(warhead, from, velocity);
            double3 offset = (terms & Terms.Ring) != 0 ? ring[tube] : Vec.Zero;
            double3 spin = common + Vec.Cross(turning, ring[tube]);

            ReleaseFocus.Separation kick = ReleaseFocus.Kick(
                Earth, from, velocity, probe.Seconds, offset, spin, focusRing: true, cancelSpin: true,
                new ReleaseFocus.ProbeMiss(probe.GroundFixedPointCci, Earth.CarryCci(aim, since)),
                throughTheAir: columns);

            Assert.True(Vec.Len2(offset) == 0.0 || kick.RingFocused, $"tube {tube + 1}'s ring would not solve");
            Assert.Equal(ReleaseFocus.MissOutcome.Cancelled, kick.Miss);
            Assert.Equal(solve == Solve.ThroughTheAir, kick.ThroughTheAir);

            double3 p0 = from + offset;
            double3 v0 = velocity + spin + (solve == Solve.None ? Vec.Zero : kick.KickCci);

            double3 lands = throughTheRound ? FlyTheRound(warhead, p0, v0) : Predict(warhead, p0, v0).GroundFixedPointCci;
            landed[tube] = Earth.UncarryCci(lands, since);
        }

        double3[] off = [.. landed.Select(l => frame.Resolve(Earth.CarryCci(l - aim, first.Seconds)) * 1000.0)];
        double down = off.Average(o => o.Y), cross = off.Average(o => o.Z);
        double spread = Math.Sqrt(off.Average(o => (o.Y - down) * (o.Y - down) + (o.Z - cross) * (o.Z - cross)));

        return new Group(down, cross, spread);
    }

    private static double Centre(in Group group) => Math.Sqrt(group.CentreDown * group.CentreDown
                                                                + group.CentreCross * group.CentreCross);

    private void Say(string name, Solve solve, Terms terms, in Group group)
        => Out.WriteLine($"{name,-9} {terms,-5} {solve,-13} centre ({group.CentreDown,9:F4} down, "
                         + $"{group.CentreCross,9:F4} cross) mm, spread {group.Spread,10:F4} mm");

    [Fact]
    public void TheVacuumSolveLeavesMoreOfTheGroupTheMoreTheRoundDrags()
    {
        Dictionary<(string, Solve), Group> both = [];

        foreach ((string name, MunitionProfile warhead) in new[] { ("constant", Constant), ("shape", Shape) })
        {
            ImpactPredictor.Impact hit = Predict(warhead, TracedPositionCci, TracedVelocityCci);
            Assert.True(Kepler.TryCoast(Mu, TracedPositionCci, TracedVelocityCci, hit.Seconds, out double3 coasted, out _));
            Out.WriteLine($"{name}: {hit.Seconds:F1} s, arriving at {Vec.Len(hit.VelocityCci):F0} m/s; the vacuum coast "
                          + $"for that long is {(R - Vec.Len(coasted)) / 1000.0:F2} km under the ground");

            foreach (Terms terms in new[] { Terms.Ring, Terms.Miss, Terms.Both })
            {
                foreach (Solve solve in new[] { Solve.None, Solve.Vacuum, Solve.ThroughTheAir })
                {
                    Group group = Salvo(warhead, solve, terms);
                    Say(name, solve, terms, group);
                    if (terms == Terms.Both) both[(name, solve)] = group;
                }
            }

            Out.WriteLine("");
        }

        Assert.InRange(both[("constant", Solve.Vacuum)].Spread, 1.5, 3.0);
        Assert.InRange(both[("shape", Solve.Vacuum)].Spread, 7.0, 11.0);
        Assert.InRange(Centre(both[("constant", Solve.Vacuum)]), 0.2, 1.0);
        Assert.InRange(Centre(both[("shape", Solve.Vacuum)]), 1.5, 3.5);
    }

    /// <summary>The columns flown once a salvo land every warhead on the aim, on either round.</summary>
    [Theory]
    [InlineData("constant")]
    [InlineData("shape")]
    public void ThroughTheAirTheGroupClosesOnTheAim(string round)
    {
        MunitionProfile warhead = round == "shape" ? Shape : Constant;

        Group vacuum = Salvo(warhead, Solve.Vacuum, Terms.Both);
        Group air = Salvo(warhead, Solve.ThroughTheAir, Terms.Both);
        Say(round, Solve.Vacuum, Terms.Both, vacuum);
        Say(round, Solve.ThroughTheAir, Terms.Both, air);

        Assert.True(air.Spread < 0.02, $"through the air the group is still {air.Spread:F4} mm across");
        Assert.True(Centre(air) < 0.02, $"through the air the group's centre is still {Centre(air):F4} mm off");
    }

    /// <summary>And the real round, flown the way a released warhead is, lands where the kick sent it.</summary>
    [Fact]
    public void TheRoundLandsWhereTheKickThroughTheAirSendsIt()
    {
        Group vacuum = Salvo(Shape, Solve.Vacuum, Terms.Both, throughTheRound: true);
        Group air = Salvo(Shape, Solve.ThroughTheAir, Terms.Both, throughTheRound: true);
        Say("round", Solve.Vacuum, Terms.Both, vacuum);
        Say("round", Solve.ThroughTheAir, Terms.Both, air);

        Assert.True(vacuum.Spread > 5.0, $"the vacuum solve left only {vacuum.Spread:F2} mm, so this cannot tell them apart");
        Assert.True(air.Spread < 0.02, $"through the air the round's group is still {air.Spread:F4} mm across");
        Assert.True(Centre(air) < 0.02, $"through the air the round's centre is still {Centre(air):F4} mm off");
    }

    /// <summary>
    /// Columns flown for one release serve the ones after it, carried along the coast — and as they were, they would
    /// not.
    /// </summary>
    [Fact]
    public void ColumnsFlownOnceAreCarriedToEachRelease()
    {
        ImpactPredictor.Impact hit = Predict(Shape, TracedPositionCci, TracedVelocityCci);
        ReleaseFocus.FlownSensitivity? columns =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, TracedPositionCci, TracedVelocityCci,
                                                 AirFor(Shape, Vec.Len(hit.PointCci)));
        Assert.NotNull(columns);

        foreach (double late in new[] { 0.5, 1.0, ReleaseFocus.FlownSensitivity.ReusableWithinSeconds })
        {
            Group group = Salvo(Shape, Solve.ThroughTheAir, Terms.Both, columnsLateBy: late);
            Out.WriteLine($"columns {late:F1} s older than the first release: spread {group.Spread:F4} mm, "
                          + $"centre {Centre(group):F4} mm");

            Assert.True(group.Spread < 0.02, $"{late:F1} s along the coast the group is {group.Spread:F4} mm across");
            Assert.True(Centre(group) < 0.02, $"{late:F1} s along the coast the centre is {Centre(group):F4} mm off");
            Assert.True(columns.Covers(columns.FlightSeconds - late, Vec.Len(hit.PointCci)));
        }

        Assert.False(columns.Covers(columns.FlightSeconds - ReleaseFocus.FlownSensitivity.ReusableWithinSeconds - 0.1,
                                    Vec.Len(hit.PointCci)));
        Assert.False(columns.Covers(columns.FlightSeconds,
                                    Vec.Len(hit.PointCci) + ReleaseFocus.FlownSensitivity.ReusableWithinMetres + 1.0));
    }

    /// <summary>With no air to fly through, the flown solve is the coasted one: the same mathematics, measured.</summary>
    [Fact]
    public void WithNoAirTheKickThroughTheAirIsTheCoastedOne()
    {
        MunitionProfile vacuum = Arsenal.ReentryVehicleMk21.Copy();
        vacuum.DragK = 0f;
        Assert.Equal(0.0, vacuum.AppliedDragK);

        ImpactPredictor.Impact hit = Predict(vacuum, TracedPositionCci, TracedVelocityCci);
        ReleaseFocus.FlownSensitivity? columns =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, TracedPositionCci, TracedVelocityCci, AirFor(vacuum, R));
        Assert.NotNull(columns);

        foreach (double3 offset in Ring(TracedPositionCci, TracedVelocityCci))
        {
            Assert.True(ReleaseFocus.TryKick(Earth, TracedPositionCci, TracedVelocityCci, hit.Seconds, offset,
                                             out double3 coasted));
            Assert.True(ReleaseFocus.TryKick(Earth, columns, offset, out double3 flown));

            Assert.True(Vec.Len(flown - coasted) < 1e-3 * Vec.Len(coasted),
                        $"flown {Vec.Len(flown) * 1000.0:F4} mm/s against coasted {Vec.Len(coasted) * 1000.0:F4}, "
                        + $"{Vec.Len(flown - coasted) * 1e6:F3} um/s apart");
        }
    }

    [Fact]
    public void ColumnsThatDoNotComeDownAreNone()
    {
        double3 high = new(R + 2_000_000.0, 0, 0);
        double3 circular = new(0, Math.Sqrt(Mu / Vec.Len(high)), 0);

        Assert.Null(ReleaseFocus.FlownSensitivity.TryFly(Earth, high, circular, AirFor(Shape, R)));
        Assert.Null(ReleaseFocus.FlownSensitivity.TryFly(Earth, TracedPositionCci, TracedVelocityCci,
                                                         AirFor(Shape, double.NaN)));
    }

    /// <summary>What the seven flights cost, for the log's own number to be read against.</summary>
    [Fact]
    public void WhatTheColumnsCost()
    {
        ImpactPredictor.Impact hit = Predict(Shape, TracedPositionCci, TracedVelocityCci);
        ReleaseFocus.Air air = AirFor(Shape, Vec.Len(hit.PointCci));

        ReleaseFocus.FlownSensitivity.TryFly(Earth, TracedPositionCci, TracedVelocityCci, air);

        const int Runs = 20;
        Stopwatch watch = Stopwatch.StartNew();
        for (int i = 0; i < Runs; i++)
        {
            Assert.NotNull(ReleaseFocus.FlownSensitivity.TryFly(Earth, TracedPositionCci, TracedVelocityCci, air));
        }

        Out.WriteLine($"seven flights take {watch.Elapsed.TotalMilliseconds / Runs:F2} ms here, on a density that is "
                      + "one exponential; the game's is a lookup of its own");
    }
}
