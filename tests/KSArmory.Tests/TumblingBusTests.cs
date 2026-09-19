using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Releasing a bus that will not hold an attitude by waiting for the geometry instead of commanding
/// it: firing each tube at the moment its own axis sweeps nearest the line, rather than turning the
/// vehicle onto it.
///
/// <para><b>It does not work, and these tests are why.</b> The idea is the obvious answer to a
/// vehicle with slew authority and no ability to hold — which is what the flown bus is — so it is
/// worth being able to show that it loses rather than re-deriving it. <c>docs/ICBM-GUIDANCE.md</c>
/// has the arithmetic; the three tests below are its three legs, and each one alone is enough.</para>
///
/// <para>Nothing here drives a policy the mod implements. The comparison is made in the sequencer's
/// own currency — <see cref="ReleaseSequence.LateralFromCant"/>, metres a second at the tube — so a
/// change to what a release is priced in reaches these too.</para>
/// </summary>
public class TumblingBusTests(ITestOutputHelper Out)
{
    private static readonly double Ejection = Arsenal.ReentryVehicleMk21.LaunchSpeed;

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    private static double3[] BusTubes()
    {
        Tube[] tubes = CantedRing.Tubes;
        double3[] axes = new double3[tubes.Length];
        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(tubes[i].Direction);
        return axes;
    }

    // A tumble is one rotation about a fixed axis, which is what a body spinning about a principal
    // axis does. The tube axes are carried round by it and the latched reference is not.
    private static double3 Tumbled(double3 axis, double3 tumbleAxis, double radians)
        => Vec.Unit(doubleQuat.CreateFromAxisAngle(Vec.Unit(tumbleAxis), radians) * axis);

    // Tumble axes at a spread of angles from the bus's own, clocked round it, so no single lucky or
    // unlucky orientation decides anything below.
    private static IEnumerable<double3> TumbleAxes(double3 reference)
    {
        double3 e1 = Vec.AnyPerpendicular(reference);
        double3 e2 = Vec.Unit(Vec.Cross(reference, e1));

        foreach (double fromAxis in new[] { 5.0, 20.0, 45.0, 70.0, 90.0 })
        {
            foreach (double clock in new[] { 0.0, 15.0, 30.0, 45.0 })
            {
                double p = fromAxis * Math.PI / 180.0;
                double c = clock * Math.PI / 180.0;
                yield return Vec.Unit(reference * Math.Cos(p)
                                      + (e1 * Math.Cos(c) + e2 * Math.Sin(c)) * Math.Sin(p));
            }
        }
    }

    /// <summary>
    /// The case that looks ideal is the case that buys nothing at all.
    ///
    /// <para>A roll about the bus's own axis does sweep the six tubes through each other's clock
    /// positions — but they all ride one cone of the cant's own half-angle, so each tube arrives
    /// exactly where the last one was, which is six degrees off the line. The offset is not merely
    /// un-improved by waiting; it is <em>constant</em>, so there is no crossing to predict and no
    /// moment that is better than now.</para>
    /// </summary>
    [Fact]
    public void ARollAboutTheBusAxisNeverBringsATubeNearerTheLine()
    {
        double3[] axes = BusTubes();
        double3 reference = ReleasePointing.ReferenceAxis(axes);

        double worst = 0.0;

        // Against each tube's own starting angle rather than a nominal six degrees: the profile's
        // axes are written to five places, so the tubes differ from one another in the fourth. That
        // is the geometry as declared, and it is not what a roll does to it.
        for (int tube = 0; tube < axes.Length; tube++)
        {
            double started = Degrees(ReleasePointing.OffReferenceRadians(axes[tube], reference));

            for (int step = 0; step <= 720; step++)
            {
                double off = Degrees(ReleasePointing.OffReferenceRadians(
                                         Tumbled(axes[tube], reference, step * Math.PI / 360.0),
                                         reference));
                worst = Math.Max(worst, Math.Abs(off - started));
            }
        }

        Out.WriteLine($"through two full rolls about its own axis every tube holds the angle it "
                      + $"started at, to within {worst:F9} deg");

        Assert.True(worst < 1e-9,
                    "a roll about the bus axis is supposed to leave every tube exactly one cant off "
                    + $"the line for ever; this one varied by {worst:F9} deg, so the null case is "
                    + "not null and the rest of this file is measuring something else");
    }

    /// <summary>
    /// What waiting can buy at its absolute best, with no clock and no cost: not enough.
    ///
    /// <para>Under a fixed-axis tumble a tube traces a cone about that axis, so the nearest it ever
    /// comes to the line is the difference between that cone's half-angle and the reference's own —
    /// which works out at one cant times the cosine of the tube's clock angle from the plane the
    /// tumble axis and the reference share. Two of six tubes can reach the line; the two a quarter
    /// turn away from them can never leave the full cant.</para>
    ///
    /// <para>So the ring as a whole cannot be brought onto the line by any amount of waiting, and
    /// the mean of what it can reach is about three and a half degrees against six.</para>
    /// </summary>
    [Fact]
    public void WaitingCannotPutTheWholeRingOnTheLine()
    {
        double3[] axes = BusTubes();
        double3 reference = ReleasePointing.ReferenceAxis(axes);

        double bestMean = double.PositiveInfinity;

        foreach (double3 tumble in TumbleAxes(reference))
        {
            double sum = 0.0;

            for (int tube = 0; tube < axes.Length; tube++)
            {
                double nearest = double.PositiveInfinity;

                // A whole revolution, finely enough that a crossing cannot be stepped over.
                for (int step = 0; step <= 7200; step++)
                {
                    nearest = Math.Min(nearest,
                                       Degrees(ReleasePointing.OffReferenceRadians(
                                                   Tumbled(axes[tube], tumble,
                                                           step * Math.PI / 3600.0), reference)));
                }

                sum += nearest;
            }

            bestMean = Math.Min(bestMean, sum / axes.Length);
        }

        double firingNow = ReleaseSequence.LateralFromCant(6.0, Ejection);
        double waiting = ReleaseSequence.LateralFromCant(bestMean, Ejection);

        Out.WriteLine($"the luckiest tumble axis leaves the ring a mean {bestMean:F2} deg off the "
                      + $"line - {waiting:F3} m/s at the tube against {firingNow:F3} firing now");

        Assert.True(bestMean > 3.0,
                    $"waiting is supposed to be unable to clear the ring; this reached {bestMean:F2} "
                    + "deg, which would make the idea worth building");
    }

    /// <summary>
    /// The leg that actually kills it: waiting is self-defeating, because the thing being waited for
    /// is also what is going wrong.
    ///
    /// <para>The reference is latched while the bus is on it, so from that instant the bus axis is
    /// walking away from the line at the tumble rate. Every second spent waiting for one tube's
    /// crossing is a second of that walk added to <em>every tube not yet fired</em> — and the two
    /// effects very nearly cancel, so the mean angle a salvo is released at does not come down at
    /// all.</para>
    ///
    /// <para>Priced with waiting <b>free</b> — no window, no clock, and none of the ~26 m of miss a
    /// second of holding costs by <c>docs/MIRV-NEXT.md</c> item 2b — which is the strongest form the
    /// idea has and still not enough to matter.</para>
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    [InlineData(6.0)]
    public void FiringOnTheCrossingDoesNotBeatFiringNowOnATumblingBus(double degreesPerSecond)
    {
        double3[] axes = BusTubes();
        double3 reference = ReleasePointing.ReferenceAxis(axes);

        double worstRatio = 0.0;
        double bestRatio = double.PositiveInfinity;

        foreach (double3 tumble in TumbleAxes(reference))
        {
            double now = SpreadMetresPerSecond(axes, reference, tumble, degreesPerSecond,
                                               patienceSeconds: 0.0, out double nowOff, out _);

            // Twenty seconds of patience per tube: far more than a release window affords, and past
            // the point where more stops changing the answer.
            double crossing = SpreadMetresPerSecond(axes, reference, tumble, degreesPerSecond,
                                                    patienceSeconds: 20.0, out double waitedOff,
                                                    out double salvoSeconds);

            // A ratio that is not a number leaves every comparison below false, so the assertion
            // would pass on a measurement that never happened.
            Assert.True(now > 0.0 && double.IsFinite(crossing),
                        $"the spread did not measure: {now:F3} firing now, {crossing:F3} waiting");

            double ratio = crossing / now;
            worstRatio = Math.Max(worstRatio, ratio);

            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                Out.WriteLine($"{degreesPerSecond:F1} deg/s, best so far: {now:F3} -> {crossing:F3} "
                              + $"m/s at the tube ({nowOff:F1} -> {waitedOff:F1} deg off, salvo "
                              + $"{salvoSeconds:F1} s)");
            }
        }

        Out.WriteLine($"{degreesPerSecond:F1} deg/s: waiting is between {bestRatio:P0} and "
                      + $"{worstRatio:P0} of firing now");

        Assert.True(bestRatio > 0.65,
                    $"waiting for the crossing got the spread to {bestRatio:P0} of firing now at "
                    + $"{degreesPerSecond:F1} deg/s. Below about two thirds it would be worth the "
                    + "window it costs, and this file's conclusion would need revisiting");
    }

    // The salvo's dispersion, as the spread of the lateral velocities it puts on the six rounds.
    //
    // A vector rather than a magnitude: six rounds thrown one cant off the line in six different
    // directions is the ring the whole exercise is trying to close, and six thrown the same way is
    // a bias the aim correction already takes out. Measuring the angle alone cannot tell them apart.
    private static double SpreadMetresPerSecond(double3[] axes, double3 reference, double3 tumble,
                                                double degreesPerSecond, double patienceSeconds,
                                                out double meanOffDegrees, out double salvoSeconds)
    {
        const double Step = 1.0 / 60.0;

        double rate = degreesPerSecond * Math.PI / 180.0;
        double3[] thrown = new double3[axes.Length];

        double cursor = 0.0;
        double first = double.NaN;
        double offSum = 0.0;

        for (int tube = 0; tube < axes.Length; tube++)
        {
            // The best moment this tube is offered between the last release and its own deadline.
            double bestAt = cursor;
            double bestOff = double.PositiveInfinity;

            for (double t = cursor; t <= cursor + patienceSeconds + 1e-9; t += Step)
            {
                double off = ReleasePointing.OffReferenceRadians(
                                 Tumbled(axes[tube], tumble, rate * t), reference);

                if (off < bestOff)
                {
                    bestOff = off;
                    bestAt = t;
                }
            }

            if (double.IsNaN(first)) first = bestAt;

            offSum += Degrees(bestOff);
            thrown[tube] = (Tumbled(axes[tube], tumble, rate * bestAt) - reference) * Ejection;

            // A tube cannot fire before the one ahead of it in the magazine has gone.
            cursor = bestAt + Step;
        }

        meanOffDegrees = offSum / axes.Length;
        salvoSeconds = cursor - Step - first;

        double3 mean = Vec.Zero;
        for (int i = 0; i < thrown.Length; i++) mean += thrown[i];
        mean /= thrown.Length;

        double sum = 0.0;
        for (int i = 0; i < thrown.Length; i++) sum += Vec.Len2(thrown[i] - mean);

        return Math.Sqrt(sum / thrown.Length);
    }

    /// <summary>
    /// The requirement that survives all of the above: a bus whose tubes never stop sweeping still
    /// gets its warheads away, and in seconds rather than in the sixty a per-tube timeout would take.
    ///
    /// <para>This is the path the flown vehicle actually takes with re-pointing on — it is handed a
    /// turn it cannot follow, so the turn is abandoned on the evidence rather than waited out, and
    /// the finding is latched so the other five tubes do not each rediscover it.</para>
    /// </summary>
    [Fact]
    public void ATumblingBusStillEmptiesItsMagazineInSeconds()
    {
        const double Step = 1.0 / 60.0;

        double3[] axes = BusTubes();
        ReleaseSequence deploy = new();
        Assert.True(deploy.Begin(axes));

        double3 reference = deploy.ReferenceCci;
        double3 tumble = Vec.Unit(Vec.Cross(reference, Vec.AnyPerpendicular(reference)));
        double rate = 2.0 * Math.PI / 180.0;

        int tube = 0;
        double elapsed = 0.0;
        double firstAway = double.NaN;

        for (int i = 0; i < 60 * 300 && tube < axes.Length; i++)
        {
            elapsed = i * Step;

            ReleaseCommand r = deploy.Update(Step, new ReleaseSituation(
                ReadyToDeploy: true, NextTube: tube, TubesLeft: axes.Length - tube,
                NextTubeAxisCci: Tumbled(axes[tube], tumble, rate * elapsed),

                // The whole bus turns, so its own axis goes round with the tubes.
                NoseAxisCci: Tumbled(reference, tumble, rate * elapsed),
                SweepMetresPerSecond: 0.113, EjectionMetresPerSecond: Ejection,
                SecondsLeftToDeploy: 170.0,
                HeldDirectionCci: reference, HeldRollCci: Vec.AnyPerpendicular(reference)));

            if (r.ReleaseNow)
            {
                if (double.IsNaN(firstAway)) firstAway = elapsed;
                Out.WriteLine($"{elapsed,6:F2} s  {r.Said}");
                tube++;
            }
        }

        Assert.True(tube == axes.Length,
                    $"a tumbling bus held {axes.Length - tube} of its warheads for the whole "
                    + $"{elapsed:F0} s; it is supposed to give up and release rather than keep them");

        Out.WriteLine($"whole salvo away in {elapsed:F1} s, first at {firstAway:F1} s");

        Assert.True(elapsed < ReleaseSequence.PerTubeTimeoutSeconds,
                    $"the salvo took {elapsed:F1} s, which is the per-tube timeout being waited out "
                    + "rather than the turn being abandoned on the evidence");
    }
}
