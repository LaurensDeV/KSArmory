using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The reach ellipse read off the columns a salvo already flies, against the numbers
/// <c>MirvDivertTests</c> priced it at.
/// </summary>
public class DivertFootprintTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    // The flown release at 6,179 km: ~/shots/2026-09-17-threearm shot 001, GeoSat FAT_1 round 1.
    private static readonly double3 FlownPositionCci = new(3_835_254.4, -5_998_331.6, -1_302_454.7);
    private static readonly double3 FlownVelocityCci = new(279.7753, 2844.5295, -3967.0558);

    private static double DensityAt(double3 p)
    {
        double altitude = Math.Max(0.0, Vec.Len(p) - R);
        return altitude >= 167_410.0 ? 0.0 : Math.Exp(-altitude / 8_000.0);
    }

    private static ReleaseFocus.FlownSensitivity Columns()
    {
        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, R, true);

        return ReleaseFocus.FlownSensitivity.TryFly(Earth, FlownPositionCci, FlownVelocityCci, air)
               ?? throw new InvalidOperationException("the columns did not come down");
    }

    private static DivertFootprint Flown(DivertFootprint.ArrivalClock clock)
    {
        Assert.True(DivertFootprint.TryFrom(Earth, Columns(), clock, fromTheRealState: true,
                                            out DivertFootprint f));
        return f;
    }

    /// <summary>
    /// The columns the kicks already fly give the same reach phase 0 measured a different way.
    /// </summary>
    /// <remarks>
    /// <c>MirvDivertTests</c> prices this geometry at <b>688 m along per m/s and 355 across</b> by
    /// differencing whole predicted landings, with the arrival clock left free. If this reproduces that
    /// off the sensitivity columns, the display needs no flying of its own — which is the claim the whole
    /// of phase 2 rests on.
    /// </remarks>
    [Fact]
    public void TheFreeClockEllipseIsWhatPhaseZeroPricedByFlyingLandings()
    {
        DivertFootprint f = Flown(DivertFootprint.ArrivalClock.Free);

        Out.WriteLine($"semi-major {f.SemiMajorMetresPerMetrePerSecond:F1} m per m/s, "
                      + $"semi-minor {f.SemiMinorMetresPerMetrePerSecond:F1}, "
                      + $"long axis {f.OrientationRad * 180.0 / Math.PI:+0.00;-0.00;0.00} deg off downrange, "
                      + $"{f.FlightSeconds:F0} s of flight");

        Assert.InRange(f.SemiMajorMetresPerMetrePerSecond, 620.0, 760.0);
        Assert.InRange(f.SemiMinorMetresPerMetrePerSecond, 320.0, 390.0);

        // Stretched along the ground track, which is what makes it an ellipse rather than a disc.
        Assert.True(f.SemiMajorMetresPerMetrePerSecond > f.SemiMinorMetresPerMetrePerSecond);
        Assert.InRange(Math.Abs(f.OrientationRad) * 180.0 / Math.PI, 0.0, 2.0);
    }

    /// <summary>
    /// The arrival the flight pins costs along the track and nothing across it, which is what makes the
    /// footprint a far smaller thing than phase 0 priced.
    /// </summary>
    /// <remarks>
    /// <para><c>MirvDivertTests.ThePinnedArrivalIsWhatADivertActuallyCosts</c> flies whole landings against
    /// the same three-by-three and reads <b>1.94x</b> along the track at this state and <b>1.00x</b> across
    /// it. Across is exact rather than approximate: moving a target across the track does not change when
    /// it is reached, so the clock row has nothing to take out of that row.</para>
    ///
    /// <para>Against the free-clock 688 / 355, pinned is about <b>355 / 355</b> — the along-track reach
    /// collapsing onto the cross-track one, which is what <c>0.96 · t_go</c> describes.</para>
    /// </remarks>
    [Fact]
    public void PinningTheArrivalCostsAlongTheTrackAndNothingAcrossIt()
    {
        DivertFootprint free = Flown(DivertFootprint.ArrivalClock.Free);
        DivertFootprint pinned = Flown(DivertFootprint.ArrivalClock.Pinned);

        double alongRatio = free.AlongTrackMetresPerMetrePerSecond / pinned.AlongTrackMetresPerMetrePerSecond;
        double crossRatio = free.CrossTrackMetresPerMetrePerSecond / pinned.CrossTrackMetresPerMetrePerSecond;

        Out.WriteLine($"free   {free.AlongTrackMetresPerMetrePerSecond,7:F1} along, "
                      + $"{free.CrossTrackMetresPerMetrePerSecond,6:F1} across, m per m/s");
        Out.WriteLine($"pinned {pinned.AlongTrackMetresPerMetrePerSecond,7:F1} along, "
                      + $"{pinned.CrossTrackMetresPerMetrePerSecond,6:F1} across");
        Out.WriteLine($"       {alongRatio,7:F2}x       {crossRatio,6:F2}x");

        Assert.InRange(alongRatio, 1.6, 2.4);
        Assert.InRange(crossRatio, 0.99, 1.01);

        // 0.96 · t_go, which is the one part of the shape the epoch alone decides.
        Assert.InRange(pinned.CrossTrackMetresPerMetrePerSecond / pinned.FlightSeconds, 0.90, 1.02);
    }

    /// <summary>
    /// Free-clock the footprint is an ellipse worth orienting; pinned it is a disc, and its orientation
    /// means nothing.
    /// </summary>
    /// <remarks>
    /// The long axis is <c>450 · cot γ</c> and the short one <c>0.96 · t_go</c>, so the aspect ratio is
    /// the arrival angle. Pinning removes the whole of the long axis's advantage, leaving the two within
    /// 3% of each other at every geometry measured — so a display drawing the free ellipse where the
    /// flight pins the clock shows a region 2x too long here and 12x at 12,902 km.
    /// </remarks>
    [Fact]
    public void ThePinnedFootprintIsADiscWhereTheFreeOneIsAnEllipse()
    {
        DivertFootprint free = Flown(DivertFootprint.ArrivalClock.Free);
        DivertFootprint pinned = Flown(DivertFootprint.ArrivalClock.Pinned);

        double freeAspect = free.SemiMajorMetresPerMetrePerSecond / free.SemiMinorMetresPerMetrePerSecond;
        double pinnedAspect = pinned.SemiMajorMetresPerMetrePerSecond / pinned.SemiMinorMetresPerMetrePerSecond;

        Out.WriteLine($"free {freeAspect:F2}:1, pinned {pinnedAspect:F2}:1");

        Assert.InRange(freeAspect, 1.6, 2.4);
        Assert.InRange(pinnedAspect, 1.0, 1.05);
    }

    /// <summary>
    /// The arrival-clock row is an output of the release state like the landing columns, so it is carried
    /// along the coast with them.
    /// </summary>
    /// <remarks>
    /// The whole of the clock row's job is its <em>direction</em> — the pinned reach is the ground rows with
    /// that direction projected out — and a row left where it was flown leans 0.009° off the one the later
    /// state would fly, against 0.0001° carried. Small here because the carry is bounded at
    /// <see cref="ReleaseFocus.FlownSensitivity.ReusableWithinSeconds"/>; the release loop phase 3 wants runs
    /// for minutes.
    /// </remarks>
    [Fact]
    public void TheArrivalClockRowIsCarriedAlongTheCoastLikeTheLandingColumns()
    {
        ReleaseFocus.FlownSensitivity atRelease = Columns();

        double tau = ReleaseFocus.FlownSensitivity.ReusableWithinSeconds;
        Assert.True(Kepler.TryCoast(Mu, FlownPositionCci, FlownVelocityCci, tau, out double3 later,
                                    out double3 laterVelocity));

        MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
        ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, R, true);

        ReleaseFocus.FlownSensitivity direct =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, later, laterVelocity, air)
            ?? throw new InvalidOperationException("the later columns did not come down");

        double3 flown = direct.ArrivalSecondsPerMetrePerSecond;
        double3 carried = atRelease.For(Mu, later, direct.FlightSeconds).ArrivalSecondsPerMetrePerSecond;
        double3 stale = atRelease.ArrivalSecondsPerMetrePerSecond;

        double carriedError = Vec.Len(carried - flown) / Vec.Len(flown);
        double staleError = Vec.Len(stale - flown) / Vec.Len(flown);

        Out.WriteLine($"{tau:F1} s along the coast: carried {carriedError:E2} off the flown row, stale {staleError:E2}");

        Assert.InRange(carriedError, 0.0, 1e-4);
        Assert.True(staleError > 3e-3, $"the stale row is only {staleError:E2} off, so the carry proves nothing");
    }

    /// <summary>
    /// Nothing about pinning can make a divert cheaper: it is one more condition out of the same three
    /// unknowns.
    /// </summary>
    [Fact]
    public void PinningTheArrivalNeverMakesADivertCheaper()
    {
        DivertFootprint free = Flown(DivertFootprint.ArrivalClock.Free);
        DivertFootprint pinned = Flown(DivertFootprint.ArrivalClock.Pinned);

        foreach ((double along, double cross) in new[] { (50_000.0, 0.0), (0.0, 20_000.0), (30_000.0, 10_000.0),
                                                         (-40_000.0, 5_000.0) })
        {
            double cheap = free.CostMetresPerSecond(along, cross);
            double dear = pinned.CostMetresPerSecond(along, cross);

            Out.WriteLine($"{along / 1000.0,7:F0} km along, {cross / 1000.0,5:F0} across: "
                          + $"{cheap,6:F2} m/s free, {dear,6:F2} pinned");

            Assert.True(dear >= cheap - 1e-9, $"{along}/{cross}: pinned {dear:F3} under free {cheap:F3}");
        }
    }

    /// <summary>The cost of a displacement is the ellipse read backwards, so the two must invert.</summary>
    [Fact]
    public void TheCostOfADisplacementInvertsTheReach()
    {
        foreach (DivertFootprint.ArrivalClock clock in new[] { DivertFootprint.ArrivalClock.Pinned,
                                                               DivertFootprint.ArrivalClock.Free })
        {
            DivertFootprint f = Flown(clock);

            foreach ((double along, double cross) in new[] { (50_000.0, 0.0), (0.0, 20_000.0), (30_000.0, 10_000.0) })
            {
                double spent = f.CostMetresPerSecond(along, cross);

                Assert.True(f.Reaches(along, cross, spent + 1e-6), $"{clock} {along}/{cross} unreachable at its own cost");
                Assert.False(f.Reaches(along, cross, spent * 0.9), $"{clock} {along}/{cross} reachable at 90% of its cost");
            }

            // And the edge along a bearing is that cost inverted, which is what the two reach members read.
            Assert.Equal(1.0, f.CostMetresPerSecond(f.AlongTrackMetresPerMetrePerSecond, 0.0), 6);
            Assert.Equal(1.0, f.CostMetresPerSecond(0.0, f.CrossTrackMetresPerMetrePerSecond), 6);
        }

        // Free-clock, a move along the long axis is the cheap one, which is the whole point of that shape.
        DivertFootprint freeClock = Flown(DivertFootprint.ArrivalClock.Free);
        Assert.True(freeClock.CostMetresPerSecond(50_000.0, 0.0) < freeClock.CostMetresPerSecond(0.0, 50_000.0));
    }

    /// <summary>
    /// Nothing is reachable on no budget, and the ellipse never claims otherwise — the failure a region
    /// display makes silently is drawing something and calling everything inside it reachable.
    /// </summary>
    [Fact]
    public void NoBudgetReachesNothingButWhereItAlreadyLands()
    {
        foreach (DivertFootprint.ArrivalClock clock in new[] { DivertFootprint.ArrivalClock.Pinned,
                                                               DivertFootprint.ArrivalClock.Free })
        {
            DivertFootprint f = Flown(clock);

            Assert.True(f.Reaches(0.0, 0.0, 0.0));
            Assert.False(f.Reaches(1.0, 0.0, 0.0));
            Assert.False(f.Reaches(0.0, 1.0, 0.0));
        }
    }

    // ------------------------------------------------- the reach before the flight has flown

    /// <summary>
    /// <b>The measurement the pre-launch reach rests on.</b> Pinned, the footprint is the release
    /// epoch and nothing else: the arc it is flown on cannot move it.
    /// </summary>
    /// <remarks>
    /// <para>The free-clock long axis is <c>450 · cot γ</c>, which belongs to the arc actually flown —
    /// a pad-drawn estimate of it was out by 1.04x, 0.51x and 0.59x at three flown geometries. Pinning
    /// the arrival takes that whole axis out, and what is left is <c>0.96 · t_go</c> in <em>both</em>
    /// directions. So the one quantity the pad cannot know is the one the pinned reach does not use,
    /// which is what lets a target be placed before launch.</para>
    ///
    /// <para>Swept over 2,002–12,787 km of range, 13.3°–52.2° of arrival and two lofts at a 420 s
    /// release: the free major runs <b>497 to 1,816</b> m per m/s, a 3.66x swing, and the pinned reach
    /// <b>395 to 407</b>, a 1.03x one — the low end being the 12,787 km shot arriving at 13.3°, which
    /// is the shallowest geometry in the matrix. If this ever stops holding,
    /// <c>docs/MIRV-TARGETS.md</c>'s pre-launch half goes with it.</para>
    /// </remarks>
    [Fact]
    public void ThePinnedReachIsTheReleaseEpochAndTheArcCannotMoveIt()
    {
        double lo = double.PositiveInfinity, hi = 0.0;
        double freeLo = double.PositiveInfinity, freeHi = 0.0;
        int flown = 0;

        foreach ((double reachMetres, double freeMajor, double _) in Sweep(420.0))
        {
            lo = Math.Min(lo, reachMetres);
            hi = Math.Max(hi, reachMetres);
            freeLo = Math.Min(freeLo, freeMajor);
            freeHi = Math.Max(freeHi, freeMajor);
            flown++;
        }

        Out.WriteLine($"{flown} geometries at a 420 s release: pinned {lo:F0} to {hi:F0} m per m/s, "
                      + $"free major {freeLo:F0} to {freeHi:F0}");

        Assert.True(flown >= 15, $"only {flown} geometries flew, which settles nothing");

        // The free axis swings by more than three times; the pinned one by a few per cent.
        Assert.True(freeHi / freeLo > 3.0, $"the free major only moved {freeHi / freeLo:F2}x");
        Assert.InRange(hi / lo, 1.0, 1.05);
    }

    /// <summary>
    /// And <see cref="DivertFootprint.PinnedMetresPerSecondToGo"/> is a floor over the whole band it
    /// is allowed in, not a fit to the middle of it.
    /// </summary>
    /// <remarks>
    /// It is drawn before the flight can check it, so the one failure that matters is promising ground
    /// the bus cannot reach. The ratio decays with the epoch — 0.96 at 420 s, 0.89 at 745 — because the
    /// out-of-plane response is harmonic about the body and saturates over a quarter period, which is
    /// why the band has a top at all.
    /// </remarks>
    [Fact]
    public void TheEpochEstimateIsAFloorEverywhereItIsAllowed()
    {
        foreach (double seconds in new[] { DivertFootprint.EpochLeastSeconds, 240.0, 420.0, 550.0,
                                           DivertFootprint.EpochMostSeconds })
        {
            Assert.True(DivertFootprint.TryAtTheEpoch(Flown(DivertFootprint.ArrivalClock.Pinned).Frame,
                                                      seconds, out DivertFootprint estimate));

            double worst = double.PositiveInfinity;

            foreach ((double reachMetres, double _, double tGo) in Sweep(seconds))
            {
                worst = Math.Min(worst, reachMetres * (seconds / tGo));
            }

            Out.WriteLine($"{seconds,5:F0} s: estimated {estimate.SemiMinorMetresPerMetrePerSecond,6:F0} "
                          + $"against a worst flown {worst,6:F0} m per m/s");

            Assert.True(estimate.SemiMinorMetresPerMetrePerSecond <= worst,
                        $"at {seconds:F0} s the estimate promises {estimate.SemiMinorMetresPerMetrePerSecond:F0} "
                        + $"where the worst geometry reaches {worst:F0}");
        }
    }

    /// <summary>Outside that band it refuses, because the constant stops being a floor there.</summary>
    [Fact]
    public void OutsideTheBandThereIsNoEstimateRatherThanAWrongOne()
    {
        ArrivalFrame frame = Flown(DivertFootprint.ArrivalClock.Pinned).Frame;

        foreach (double seconds in new[] { double.NaN, 0.0, -1.0, DivertFootprint.EpochLeastSeconds - 1.0,
                                           DivertFootprint.EpochMostSeconds + 1.0, 3_600.0 })
        {
            Assert.False(DivertFootprint.TryAtTheEpoch(frame, seconds, out _), $"{seconds} was estimated");
        }
    }

    /// <summary>The estimate is a disc, and says it was not read off a flown state.</summary>
    [Fact]
    public void TheEstimateIsADiscAndKnowsItIsAnEstimate()
    {
        Assert.True(DivertFootprint.TryAtTheEpoch(Flown(DivertFootprint.ArrivalClock.Pinned).Frame,
                                                  420.0, out DivertFootprint estimate));

        Assert.Equal(estimate.SemiMajorMetresPerMetrePerSecond,
                     estimate.SemiMinorMetresPerMetrePerSecond, 9);
        Assert.Equal(DivertFootprint.ArrivalClock.Pinned, estimate.Clock);
        Assert.False(estimate.FromTheRealState);
        Assert.True(Flown(DivertFootprint.ArrivalClock.Pinned).FromTheRealState);
    }

    // Arcs across the range, the arrival angle and the loft, each coasted to `secondsToGo` before
    // its own arrival and the columns flown from there: the pinned reach, the free long axis it is
    // being compared against, and the fall the columns actually took.
    private static IEnumerable<(double Pinned, double FreeMajor, double FlightSeconds)> Sweep(double secondsToGo)
    {
        foreach (double latitudeDeg in new[] { 0.0, 25.0 })
        foreach (double downrangeDeg in new[] { 18.0, 40.0, 60.0, 90.0, 115.0 })
        foreach (double loft in new[] { 1.0, 1.35 })
        {
            double3 cutoffCci = new(R + 200_000.0, 0.0, 0.0);

            double d = downrangeDeg * Math.PI / 180.0, lat = latitudeDeg * Math.PI / 180.0;
            double3 aimCci = new double3(Math.Cos(d) * Math.Cos(lat), Math.Sin(d) * Math.Cos(lat),
                                         Math.Sin(lat)) * R;

            if (!BallisticArc.TryCheapest(Earth, cutoffCci, Vec.Zero, aimCci,
                                          out BallisticArc.Solution arc, loft)) continue;

            double coast = arc.FlightSeconds - secondsToGo;
            if (coast <= 10.0) continue;

            if (!Kepler.TryCoast(Mu, cutoffCci, arc.RequiredVelocityCci, coast, out double3 atRelease,
                                 out double3 velocity)) continue;
            if (Vec.Len(atRelease) < R + 100_000.0) continue;

            MunitionProfile warhead = Arsenal.ReentryVehicleMk21;
            ReleaseFocus.Air air = new(new ImpactPredictor.Drag(DensityAt, warhead), 2.0, R, true);

            ReleaseFocus.FlownSensitivity? columns =
                ReleaseFocus.FlownSensitivity.TryFly(Earth, atRelease, velocity, air);
            if (columns is null) continue;

            if (!DivertFootprint.TryFrom(Earth, columns, DivertFootprint.ArrivalClock.Pinned, true,
                                         out DivertFootprint pinned)) continue;
            if (!DivertFootprint.TryFrom(Earth, columns, DivertFootprint.ArrivalClock.Free, true,
                                         out DivertFootprint free)) continue;

            yield return (pinned.SemiMinorMetresPerMetrePerSecond,
                          free.SemiMajorMetresPerMetrePerSecond, columns.FlightSeconds);
        }
    }

    /// <summary>
    /// The arrival frame is taken against the ground, not the inertial velocity. The axes cannot tell
    /// the difference — both frames span one horizontal plane — but everything angular can.
    /// </summary>
    [Fact]
    public void TheFrameIsTheGroundsAndOnlyTheAnglesKnow()
    {
        ReleaseFocus.FlownSensitivity flown = Columns();

        Assert.True(DivertFootprint.TryFrom(Earth, flown, DivertFootprint.ArrivalClock.Free,
                                            fromTheRealState: true, out DivertFootprint ground));

        Assert.True(ArrivalFrame.TryAt(flown.ArrivedCci, flown.ArrivalVelocityCci, out ArrivalFrame inertial));

        double turn = Math.Acos(Math.Clamp(Vec.Dot(ground.Frame.Downrange, inertial.Downrange), -1.0, 1.0));
        Out.WriteLine($"the two downranges are {turn * 180.0 / Math.PI:F2} deg apart");

        Assert.InRange(turn * 180.0 / Math.PI, 1.0, 20.0);
    }
}
