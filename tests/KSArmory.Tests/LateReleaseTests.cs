using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What holding a warhead back costs, on an arc that is already flying itself.
///
/// <para>Flown: four deorbit shots of the same 2,300–2,700 km range, every one cutting off within
/// 0.1 km of its own prediction, and the warheads landing further off the later they were let go —
/// 431 m at a ~50 s release, 8.2–9.0 km at ~106 s, and tightly grouped each time. A tight group
/// far from the target is a bias, so something moved the shot between cutoff and release while
/// nothing was burning.</para>
///
/// <para>Nothing moved the <em>arc</em>. What moved is the ejection kick's leverage on it: two
/// metres a second applied at cutoff walks the impact eight kilometres, and the same two metres a
/// second applied a hundred seconds later walks it five. The aim correction converges during the
/// burn against a prediction that models the release at the instant the engines stop, so it takes
/// out the leverage the kick has <em>there</em> — and every second the bus then spends holding on
/// to the warhead is leverage the correction has already spent.</para>
/// </summary>
public class LateReleaseTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static readonly MunitionProfile Warhead = Arsenal.ReentryVehicleMk21;

    private static ImpactPredictor.Drag Air => new(DensityAt, Warhead);

    // The flown shot: a deorbit off a 200 km circular orbit, arriving 2,500 km downrange.
    private static double3 CutoffPositionCci => new(R + 200_000.0, 0, 0);

    private static double3 CircularVelocityCci => new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

    private static double3 TargetCci
        => new(R * Math.Cos(2_500_000.0 / R), R * Math.Sin(2_500_000.0 / R), 0);

    /// <summary>
    /// Where a warhead let go <paramref name="tau"/> seconds after cutoff comes down, as a place on
    /// the ground in the frame of the epoch it was released in — which is the frame
    /// <see cref="ImpactPredictor.Impact.GroundFixedPointCci"/> answers in, and the only one it is
    /// comparable with the target in.
    /// </summary>
    private static double3 Released(double3 cutoffVelocityCci, double tau, double3 kickCci)
    {
        Assert.True(Kepler.TryCoast(Mu, CutoffPositionCci, cutoffVelocityCci, tau,
                                    out double3 positionCci, out double3 velocityCci),
                    $"the bus would not coast to t+{tau:F0}");

        Assert.True(ImpactPredictor.TryPredict(Earth, positionCci, velocityCci + kickCci, 2.0,
                                               20_000.0, out ImpactPredictor.Impact hit, null, null, Air),
                    $"a warhead released at t+{tau:F0} never came down");

        return hit.GroundFixedPointCci;
    }

    private static double MissMetres(double3 landedCci, double tau)
        => R * Vec.AngleBetween(landedCci, Earth.CarryCci(TargetCci, tau));

    /// <summary>
    /// The correction loop the computer runs, converged against a release
    /// <paramref name="convergeAt"/> seconds after cutoff. Zero is what it does now.
    /// </summary>
    private static (double3 CutoffVelocityCci, double3 KickCci, double BiasMetres) Converge(double convergeAt)
    {
        AimCorrection aim = new();
        double3 cutoffVelocityCci = Vec.Zero;
        double3 kickCci = Vec.Zero;

        for (int i = 0; i < 30; i++)
        {
            Assert.True(BallisticArc.TryCheapest(Earth, CutoffPositionCci, CircularVelocityCci,
                                                 aim.Apply(TargetCci), out BallisticArc.Solution arc));

            cutoffVelocityCci = arc.RequiredVelocityCci;

            // The bus holds the line it was cut off on, and the tubes point along it, so that is
            // the direction the separation spring throws.
            kickCci = Vec.Unit(cutoffVelocityCci - CircularVelocityCci) * Warhead.LaunchSpeed;

            // Both terms at the same epoch. The landing is a ground point in the release epoch's
            // frame and the target is held at cutoff, so the landing is the one that moves.
            aim.Observe(Earth.UncarryCci(Released(cutoffVelocityCci, convergeAt, kickCci), convergeAt),
                        TargetCci);
        }

        return (cutoffVelocityCci, kickCci, Vec.Len(aim.BiasCci));
    }

    /// <summary>
    /// The control, and the thing that makes the rest of this mean anything: with nothing ejecting
    /// the warhead, when it is let go does not matter at all. Everything the prediction does about
    /// epochs — carrying the target, un-carrying the impact, sampling the terrain — cancels
    /// exactly, so a drift measured with a kick is the kick and not the bookkeeping.
    /// </summary>
    [Fact]
    public void AnArcDoesNotCareWhenTheWarheadIsLetGoOfIt()
    {
        (double3 cutoffVelocityCci, _, _) = Converge(0.0);

        double at0 = MissMetres(Released(cutoffVelocityCci, 0.0, Vec.Zero), 0.0);

        foreach (double tau in new[] { 50.0, 106.0, 200.0 })
        {
            double moved = Math.Abs(MissMetres(Released(cutoffVelocityCci, tau, Vec.Zero), tau) - at0);
            Out.WriteLine($"  t+{tau,5:F0} with no ejection: {moved:F1} m from where t+0 landed");

            Assert.True(moved < 100.0,
                        $"a release at t+{tau:F0} with no kick moved the impact {moved:F0} m, which "
                        + "is an epoch fault in the prediction rather than anything about the release");
        }
    }

    /// <summary>
    /// And with the separation spring it walks, because a given push is worth less impact the
    /// further down the arc it is applied. Monotone, so it is leverage running down rather than
    /// noise.
    /// </summary>
    [Fact]
    public void TheEjectionKickLosesItsLeverageAsTheArcRunsDown()
    {
        (double3 cutoffVelocityCci, double3 kickCci, _) = Converge(0.0);

        double3 unkicked = Released(cutoffVelocityCci, 0.0, Vec.Zero);
        double previous = 0.0;

        foreach (double tau in new[] { 0.0, 50.0, 106.0, 200.0 })
        {
            // How far the kick moves the impact, measured against the same arc flown without it.
            double leverage = R * Vec.AngleBetween(Released(cutoffVelocityCci, tau, kickCci),
                                                   Earth.CarryCci(unkicked, tau));

            Out.WriteLine($"  {Warhead.LaunchSpeed} m/s applied at t+{tau,5:F0} moves the impact "
                          + $"{leverage / 1000.0:F3} km");

            if (tau > 0.0)
            {
                Assert.True(leverage < previous - 500.0,
                            $"the kick's leverage at t+{tau:F0} was {leverage / 1000.0:F3} km against "
                            + $"{previous / 1000.0:F3} km before it, which is not it running down");
            }

            previous = leverage;
        }
    }

    /// <summary>
    /// The whole fault, as an A/B on one number: the epoch the correction is converged against.
    ///
    /// <para>Converged for a release at cutoff — which is what a prediction flown from
    /// <c>CutoffPositionCci</c> with the ejection kick already added describes — the shot is exact
    /// for a warhead let go the instant the engines stop and drifts by kilometres for one let go a
    /// minute later. Converged for the epoch the warhead actually leaves, the same arc puts it on
    /// the target.</para>
    /// </summary>
    [Fact]
    public void ACorrectionConvergedForTheWrongReleaseEpochMissesByTheDelay()
    {
        const double delay = 106.0;

        (double3 atCutoff, double3 kickA, double biasA) = Converge(0.0);
        (double3 atRelease, double3 kickB, double biasB) = Converge(delay);

        double earlyOnTime = MissMetres(Released(atCutoff, 0.0, kickA), 0.0);
        double earlyLate = MissMetres(Released(atCutoff, delay, kickA), delay);
        double lateEarly = MissMetres(Released(atRelease, 0.0, kickB), 0.0);
        double lateOnTime = MissMetres(Released(atRelease, delay, kickB), delay);

        Out.WriteLine($"  converged for t+0   (bias {biasA / 1000.0:F1} km): "
                      + $"released at t+0 {earlyOnTime:F0} m, at t+{delay:F0} {earlyLate:F0} m");
        Out.WriteLine($"  converged for t+{delay:F0} (bias {biasB / 1000.0:F1} km): "
                      + $"released at t+0 {lateEarly:F0} m, at t+{delay:F0} {lateOnTime:F0} m");

        Assert.True(earlyOnTime < 100.0, $"the loop did not converge: {earlyOnTime:F0} m");
        Assert.True(lateOnTime < 100.0, $"the loop did not converge: {lateOnTime:F0} m");

        Assert.True(earlyLate > 2_000.0,
                    $"holding the warhead {delay:F0} s past cutoff cost only {earlyLate:F0} m, so "
                    + "this rig can no longer see what the flown salvo did");

        // Symmetric, because it is one quantity being taken out at the wrong point on the arc:
        // converging late and releasing early is the same error the other way round.
        Assert.True(Math.Abs(earlyLate - lateEarly) < 0.1 * earlyLate,
                    $"the two directions should cost the same; they were {earlyLate:F0} m and "
                    + $"{lateEarly:F0} m");
    }

    /// <summary>
    /// It is one quantity times another, so it scales with the separation spring exactly. A bus
    /// that threw harder would pay proportionally more for the same wait, which is the shape that
    /// says the ejection is what is being mis-timed rather than the arc.
    /// </summary>
    [Fact]
    public void TheCostOfWaitingIsProportionalToWhatTheSpringThrows()
    {
        const double delay = 106.0;

        double3 kickDirection = Vec.Unit(Converge(0.0).KickCci);
        double perMetrePerSecond = double.NaN;

        foreach (double speed in new[] { 1.0, 2.0, 4.0 })
        {
            // Re-converged for each spring, because the bias absorbs that spring's own leverage.
            AimCorrection aim = new();
            double3 velocityCci = Vec.Zero;

            for (int i = 0; i < 30; i++)
            {
                Assert.True(BallisticArc.TryCheapest(Earth, CutoffPositionCci, CircularVelocityCci,
                                                     aim.Apply(TargetCci), out BallisticArc.Solution arc));
                velocityCci = arc.RequiredVelocityCci;
                aim.Observe(Released(velocityCci, 0.0, kickDirection * speed), TargetCci);
            }

            double cost = MissMetres(Released(velocityCci, delay, kickDirection * speed), delay);
            Out.WriteLine($"  {speed} m/s of separation: {cost / 1000.0:F3} km for a {delay:F0} s wait");

            if (double.IsNaN(perMetrePerSecond)) perMetrePerSecond = cost / speed;
            else
            {
                Assert.True(Math.Abs(cost / speed - perMetrePerSecond) < 0.05 * perMetrePerSecond,
                            $"{speed} m/s cost {cost / speed:F0} m per m/s against "
                            + $"{perMetrePerSecond:F0} m per m/s at one, which is not proportional");
            }
        }
    }

    /// <summary>
    /// The other candidate, measured and ruled out. <see cref="AimCorrection.BiasCci"/> is a free
    /// vector in the body's <em>inertial</em> frame while the target it is added to is a point on a
    /// turning planet, so an aim that is right at one instant is stale at the next — and the loop
    /// stops re-converging for the whole of the bus trim, which was 48 s in flight.
    ///
    /// <para>Real, and an order of magnitude too small to be what the salvo did: a few hundred
    /// metres at the size of bias real terrain produces, against kilometres from the release epoch.
    /// It is also nearly free at the equator and worst at the poles, because most of what goes
    /// stale is radial and <see cref="AimCorrection.Apply"/> renormalises that away.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(26.5)]
    [InlineData(60.0)]
    public void AStaleBiasFrameIsRealAndTooSmallToBeThis(double latitudeDeg)
    {
        const double delay = 106.0;

        double lat = latitudeDeg * Math.PI / 180.0;
        double3 target = new(R * Math.Cos(lat) * Math.Cos(0.35), R * Math.Cos(lat) * Math.Sin(0.35),
                             R * Math.Sin(lat));

        // Due north at the target, the size rising ground drives the correction to.
        double3 up = Vec.Unit(target);
        double3 north = Vec.Unit(Vec.RejectFrom(new double3(0, 0, 1), up));

        AimCorrection aim = new();
        aim.Observe(target - north * (136_000.0 / AimCorrection.Gain), target);

        // Where the aim would be if the bias had been carried with the ground, against where it is.
        double stale = R * Vec.AngleBetween(aim.Apply(Earth.CarryCci(target, delay)),
                                            Earth.CarryCci(aim.Apply(target), delay));

        Out.WriteLine($"  {latitudeDeg:F1} deg, a 136 km bias, {delay:F0} s: the aim is {stale:F0} m "
                      + "from where the ground carried it");

        Assert.True(stale < 1_000.0,
                    $"the stale frame was worth {stale:F0} m, which is no longer the smaller of the "
                    + "two mechanisms and this test's claim has to be re-argued");
    }
}
