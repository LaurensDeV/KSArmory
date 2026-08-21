using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the modelled release direction is worth to the post-boost correction, and what the three
/// ways of handling a direction that will not hold still actually cost.
///
/// <para><c>PostBoostObserverTests</c> apportions what <em>moves</em> the reading. This asks the
/// next question: given that the nose moves, what should the prediction do about it. The three
/// answers are to track the live direction, to latch one and hold it, or to leave the kick out
/// altogether and predict the bus — and they are not close to each other.</para>
///
/// <para>Measured on the same 3,459 km shot through <see cref="DeorbitShot"/>, from the cutoff the
/// guidance actually converges on.</para>
/// </summary>
public class ReleaseDirectionTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    /// <summary>The band of release directions a separated bus is measured drifting through.</summary>
    private const double FlownLowDegrees = 95.0;

    /// <summary>What a free-rolling separated bus turns at — KSA's own minimum rate bit.</summary>
    private const double FreeRollDegPerSecond = 1.8;

    private static bool TryLand(double3 fromCci, double3 velocityCci, out double3 groundCci,
                                out double seconds)
    {
        groundCci = Vec.Zero;
        seconds = double.NaN;

        if (!ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 2.0,
                                        ImpactPredictor.DefaultMaxSeconds,
                                        out ImpactPredictor.Impact hit, null, null,
                                        new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)))
        {
            return false;
        }

        groundCci = hit.GroundFixedPointCci;
        seconds = hit.Seconds;
        return true;
    }

    private static double3 Land(double3 fromCci, double3 velocityCci)
    {
        Assert.True(TryLand(fromCci, velocityCci, out double3 g, out double _),
                    "the prediction never came down");
        return g;
    }

    /// <summary>
    /// The aim correction as <c>Ksa/IcbmComputer.cs</c> rides it during the burn, so the cutoff the
    /// sweeps run from is the one the guidance converges on rather than an ideal arc.
    /// </summary>
    private sealed class Loop : IcbmFlightRig.IAimLoop
    {
        public readonly AimCorrection Aim = new();
        private double _sincePredict = double.PositiveInfinity;

        public double BiasMetres => Vec.Len(Aim.BiasCci);

        public double3 Apply(double3 aimNowCci) => Aim.Apply(aimNowCci);

        public bool IsSteady => Aim.IsSteady;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci,
                                double step)
        {
            if (double.IsFinite(program.CommittedArrivalFromNow)) Aim.Freeze();

            _sincePredict += step;
            if (_sincePredict < 0.5) return;
            _sincePredict = 0.0;

            if (!program.IsBurning || program.Arc is not { } arc) return;

            double3 kick = Vec.Unit(command.ThrustDirectionCci) * Warhead.LaunchSpeed;

            if (!ImpactPredictor.TryPredict(Earth, program.CutoffPositionCci,
                                            arc.RequiredVelocityCci + kick, 2.0,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, null, null,
                                            new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)))
            {
                return;
            }

            double departsIn = double.IsFinite(command.SecondsToCutoff)
                               ? Math.Max(0.0, command.SecondsToCutoff) : 0.0;

            Aim.Observe(Earth.UncarryCci(hit.GroundFixedPointCci, departsIn), aimNowCci);
        }
    }

    /// <summary>Where the guidance leaves the bus, with its aim already corrected.</summary>
    private readonly record struct Cutoff(double3 PositionCci, double3 VelocityCci, double3 NoseCci,
                                          double3 TargetCci, double BiasMetres, AimCorrection Aim);

    private static Cutoff AtCutoff()
    {
        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = new double3(DeorbitShot.R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + 300_000.0)), 0),
            Stages =
            [
                new()
                {
                    DryMassKg = 3_000, PropellantKg = 40_000,
                    ThrustNewtons = 300_000, ExhaustVelocity = 3_100,
                },
            ],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = 2.0,
            MinThrottle = 0.12,
            StepJitter = 0.5,
        };

        double3 aimAtEpoch = new(DeorbitShot.R * Math.Cos(DeorbitShot.RangeMetres / DeorbitShot.R),
                                 DeorbitShot.R * Math.Sin(DeorbitShot.RangeMetres / DeorbitShot.R), 0);

        Loop loop = new();
        rig.AimLoop = loop;

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aimAtEpoch, DeorbitShot.NominalFrame, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

        double3 nose = Vec.Unit(flight.CoastDirectionCci.Equals(Vec.Zero)
                                    ? flight.LastBurnDirectionCci
                                    : flight.CoastDirectionCci);

        return new Cutoff(flight.CutoffPositionCci, flight.CutoffVelocityCci, nose,
                          Earth.CarryCci(aimAtEpoch, flight.CutoffSeconds), loop.BiasMetres,
                          loop.Aim);
    }

    private static double3 Turned(double3 nose, double degrees, bool otherPlane)
    {
        double3 axis = otherPlane
                       ? Vec.Unit(Vec.Cross(nose, Vec.AnyPerpendicular(nose)))
                       : Vec.AnyPerpendicular(nose);

        double a = degrees * Math.PI / 180.0;
        return Vec.Unit(nose * Math.Cos(a) + axis * Math.Sin(a));
    }

    /// <summary>
    /// What a perfectly converged aim still leaves on the ground, when the direction it converged
    /// against is not the one the warhead leaves along.
    ///
    /// <para>A correction that has converged has put <c>I(x, v + k0)</c> on the target. The warhead
    /// then leaves with <c>k1</c> and lands at <c>I(x, v + k1)</c>, so the residual is the ground
    /// distance between the two predictions — which is what the predictor already answers, with no
    /// loop needed.</para>
    /// </summary>
    /// <param name="modelledSpeed">The kick the aim converged against — zero for a bus prediction.</param>
    private static double Residual(in Cutoff at, double driftDegrees, double kickSpeed,
                                   double modelledSpeed, bool otherPlane)
    {
        double3 converged = Land(at.PositionCci, at.VelocityCci + at.NoseCci * modelledSpeed);
        double3 k1 = Turned(at.NoseCci, driftDegrees, otherPlane) * kickSpeed;

        return DeorbitShot.GroundMetres(Land(at.PositionCci, at.VelocityCci + k1), converged);
    }

    /// <summary>The worse of the two planes the nose can drift in.</summary>
    private static double WorstResidual(in Cutoff at, double driftDegrees, double kickSpeed,
                                        double modelledSpeed)
        => Math.Max(Residual(at, driftDegrees, kickSpeed, modelledSpeed, false),
                    Residual(at, driftDegrees, kickSpeed, modelledSpeed, true));

    /// <summary>
    /// Leaving the ejection kick out of the prediction is not a way of escaping a direction nobody
    /// knows: it converges the <em>bus</em> onto the target and lets every warhead miss by the whole
    /// kick.
    ///
    /// <para>That is the thing to weigh a stale direction against, and it is 7.96 km at the shipped
    /// 2 m/s — worse than mismodelling the direction by anything short of about 55 degrees.</para>
    /// </summary>
    [Fact]
    public void PredictingTheBusRatherThanTheWarheadCostsTheWholeKick()
    {
        Cutoff at = AtCutoff();
        double speed = Warhead.LaunchSpeed;

        double busOnly = WorstResidual(at, 0.0, speed, modelledSpeed: 0.0);

        Out.WriteLine($"cutoff {Earth.AltitudeOf(at.PositionCci) / 1000.0:F0} km up at "
                      + $"{Vec.Len(at.VelocityCci):F0} m/s, aim biased {at.BiasMetres / 1000.0:F1} km");
        Out.WriteLine($"predicting the bus, kick {speed} m/s: {busOnly / 1000.0:F2} km");
        Out.WriteLine("");
        Out.WriteLine("   drift |  stale direction | no kick modelled");

        double crossover = double.NaN;

        foreach (double drift in new[] { 2.0, 5.0, 10.0, 22.11, 45.0, 55.0, 60.0, 90.0,
                                         FlownLowDegrees, 180.0 })
        {
            double stale = WorstResidual(at, drift, speed, modelledSpeed: speed);
            double none = WorstResidual(at, drift, speed, modelledSpeed: 0.0);

            if (double.IsNaN(crossover) && stale > none) crossover = drift;

            Out.WriteLine($"  {drift,6:F2} | {stale,10:F0} m   | {none,10:F0} m");
        }

        Out.WriteLine("");
        Out.WriteLine($"a stale direction stops paying past about {crossover:F0} deg of drift");

        Assert.True(busOnly > 7_000.0,
                    $"a bus-only prediction only cost {busOnly:F0} m, so it would be a live option");

        // Below the crossover the stale direction is the better model, and the drift a correction
        // actually accumulates is a few degrees a pass.
        Assert.True(WorstResidual(at, 22.11, speed, speed)
                    < WorstResidual(at, 22.11, speed, 0.0),
                    "modelling a direction a whole pointing band out was no better than modelling "
                    + "none at all");

        Assert.InRange(crossover, 45.0, 90.0);
    }

    /// <summary>
    /// Every metre of this is proportional to <see cref="MunitionProfile.LaunchSpeed"/>, which makes
    /// the ejection speed the one lever that shrinks the whole problem rather than trading one part
    /// of it against another.
    ///
    /// <para>The tubes are parallel and on a 0.86 m bolt circle, so the kick has only to unseat the
    /// warheads — how much of it is wanted is a design question about the bus, not a guidance
    /// one.</para>
    /// </summary>
    [Fact]
    public void TheWholeTermIsProportionalToTheEjectionSpeed()
    {
        Cutoff at = AtCutoff();

        Out.WriteLine("  m/s | bus-only | 22 deg stale | per m/s");

        double perUnit = double.NaN;

        foreach (double speed in new[] { 2.0, 1.0, 0.5, 0.25, 0.1 })
        {
            double busOnly = WorstResidual(at, 0.0, speed, modelledSpeed: 0.0);
            double stale = WorstResidual(at, 22.11, speed, modelledSpeed: speed);

            Out.WriteLine($"  {speed,4:F2} | {busOnly,7:F0} m | {stale,10:F0} m | "
                          + $"{busOnly / speed,7:F0} m");

            // Same ratio at every speed, or it is not a scale factor.
            if (double.IsNaN(perUnit)) perUnit = busOnly / speed;
            else Assert.InRange(busOnly / speed, perUnit * 0.97, perUnit * 1.03);
        }

        Out.WriteLine("");
        Out.WriteLine($"the kick is worth {perUnit:F0} m of impact per m/s on this arc");
    }

    // -------------------------------------------------------------- the loop, not the leverage

    /// <summary>Which direction the prediction the correction reads is flown with.</summary>
    private enum Model
    {
        /// <summary>The live nose, as shipped.</summary>
        Live,

        /// <summary>The nose as it was at the first pass, held for the whole correction.</summary>
        Latched,

        /// <summary>No kick at all — the bus's own arc.</summary>
        None,
    }

    private readonly record struct Run(double MissMetres, int Reads, double Seconds, string Said);

    /// <summary>
    /// The post-boost correction, flown headlessly against a bus whose nose is turning, driving the
    /// real <see cref="PostBoostAim"/> and the real <see cref="AimCorrection"/>.
    ///
    /// <para>The plant is the one <c>IcbmProgram.ResolveCoastArc</c> gives it: re-solve the transfer
    /// from where the bus is to the corrected aim at the committed arrival, and let the trim fly it.
    /// The trim is modelled as exact — it nulls to 0.017 m/s in flight, which
    /// <c>PostBoostObserverTests</c> prices at 70 m against a nose term of 9.4–13.5 km.</para>
    /// </summary>
    /// <param name="giveUp">
    /// Release without correcting once the bus has failed to hold still for
    /// <see cref="PostBoostAim.SettlesWithinSeconds"/> — what refusing to read a moving instrument
    /// amounts to, kept here as the thing the shipped behaviour is weighed against.
    /// </param>
    private static Run Correct(in Cutoff at, Model model, double driftDegPerSecond, double kickSpeed,
                               bool giveUp = false)
    {
        AimCorrection aim = at.Aim;
        aim.Resume();

        PostBoostAim seq = new();

        double3 x = at.PositionCci;
        double3 v = at.VelocityCci;
        double3 targetNow = at.TargetCci;
        double3 nose = at.NoseCci;

        if (!TryLand(x, v + nose * kickSpeed, out double3 _, out double remaining))
        {
            return new Run(double.NaN, 0, 0.0, "no arrival");
        }

        double3 latched = nose;
        double freshMiss = double.NaN;
        double t = 0.0;
        double lastPassAt = -100.0;
        double unsteadyFor = 0.0;
        int reads = 0;
        string said = "";

        const double step = 0.05;
        const double settleSeconds = 1.5;

        while (t < PostBoostAim.MaxSeconds)
        {
            double3 modelled = model switch
            {
                Model.Live => nose * kickSpeed,
                Model.Latched => latched * kickSpeed,
                _ => Vec.Zero,
            };

            bool trimSettled = t - lastPassAt >= settleSeconds;
            int passesBefore = seq.Cycles;

            PostBoostAim.Decision d = seq.Update(step, new PostBoostSituation(
                TrimSettled: trimSettled,
                ReleaseDirectionCci: modelled,
                PredictedMissMetres: freshMiss,
                AimHasSettled: aim.Settled,
                TrimSpentMetresPerSecond: 0.0));

            said = d.Said;

            // The old rule, on the clock it was meant to run on: hold out for the settle, and
            // release uncorrected when it does not come.
            if (seq.Steady) unsteadyFor = 0.0;
            else if (trimSettled) unsteadyFor += step;

            if (giveUp && unsteadyFor >= PostBoostAim.SettlesWithinSeconds)
            {
                said = $"gave up after {t:F0} s of the bus not holding still";
                break;
            }

            if (d.MayRelease) break;

            if (d.MayMeasure)
            {
                if (!TryLand(x, v + modelled, out double3 hit, out double _)) break;

                aim.Observe(hit, targetNow);
                freshMiss = DeorbitShot.GroundMetres(hit, targetNow);
                reads++;
            }

            if (seq.Cycles > passesBefore)
            {
                freshMiss = double.NaN;
                lastPassAt = t;

                if (BallisticArc.TrySolve(Earth, x, aim.Apply(targetNow), remaining,
                                          out BallisticArc.Solution corrected))
                {
                    v = corrected.RequiredVelocityCci;
                }
            }

            if (!Kepler.TryCoast(Earth.Mu, x, v, step, out double3 nx, out double3 nv)) break;

            x = nx;
            v = nv;
            targetNow = Earth.CarryCci(targetNow, step);
            remaining -= step;
            t += step;

            if (driftDegPerSecond != 0.0) nose = Turned(nose, driftDegPerSecond * step, false);
        }

        if (!TryLand(x, v + nose * kickSpeed, out double3 landed, out double _))
        {
            return new Run(double.NaN, reads, t, said);
        }

        return new Run(DeorbitShot.GroundMetres(landed, targetNow), reads, t, said);
    }

    /// <summary>
    /// A moving instrument is worth reading, because this is a loop that re-reads: it tracks the
    /// direction rather than mistaking it, and only the last cycle's drift reaches the release.
    ///
    /// <para><b>What it buys is the worst case, not the best.</b> On any single drift rate the two
    /// are within a few hundred metres of each other and the sign can go either way — the burn's own
    /// aim is sometimes the luckier one. What refusing to read removes is the floor: a bus that
    /// never settles releases on whatever the burn happened to leave, and nothing aboard has looked
    /// at the shot since. That is the same trade item 0 of <c>docs/MIRV-NEXT.md</c> got the wrong
    /// way round with the lateral jets.</para>
    ///
    /// <para>Above about 1 deg/s the nose never holds inside
    /// <see cref="PostBoostAim.SteadyWithinDegrees"/> for <see cref="PostBoostAim.SteadySeconds"/>
    /// at all, and a separated bus is measured rolling at 1.8.</para>
    /// </summary>
    [Fact]
    public void ACorrectionThatKeepsReadingBeatsOneThatGivesUp()
    {
        double speed = Warhead.LaunchSpeed;
        double[] rates = [0.0, 1.0, FreeRollDegPerSecond, 3.0, 6.0];

        double worstReading = 0.0;
        double worstGivingUp = 0.0;
        int leanestRead = int.MaxValue;

        Out.WriteLine("  drift |        reading |       giving up");

        foreach (double rate in rates)
        {
            Run read = Correct(AtCutoff(), Model.Live, rate, speed);
            Run quit = Correct(AtCutoff(), Model.Live, rate, speed, giveUp: true);

            Out.WriteLine($"  {rate,5:F1}/s | {read.MissMetres,7:F0} m ({read.Reads,2}) "
                          + $"| {quit.MissMetres,7:F0} m ({quit.Reads,2})");

            worstReading = Math.Max(worstReading, read.MissMetres);
            worstGivingUp = Math.Max(worstGivingUp, quit.MissMetres);
            leanestRead = Math.Min(leanestRead, read.Reads);
        }

        Out.WriteLine("");
        Out.WriteLine($"worst across the band: {worstReading:F0} m reading, "
                      + $"{worstGivingUp:F0} m giving up");

        Assert.True(leanestRead > 0,
                    "there is a drift rate at which the correction still takes no reading at all");

        Assert.True(worstReading < worstGivingUp,
                    $"the worst case reading is {worstReading:F0} m against {worstGivingUp:F0} m "
                    + "for giving up, so refusing a moving instrument costs nothing");
    }

    /// <summary>
    /// And latching one direction for the whole correction does not beat tracking the live one.
    ///
    /// <para>It is the obvious answer to a moving instrument and it is the wrong one: a latched
    /// direction is a reading the loop converges against and then does not release along, so the
    /// whole correction's drift lands in the residual instead of one cycle's.</para>
    /// </summary>
    [Fact]
    public void LatchingTheDirectionDoesNotBeatTrackingIt()
    {
        double speed = Warhead.LaunchSpeed;

        Run live = Correct(AtCutoff(), Model.Live, 3.0, speed);
        Run latched = Correct(AtCutoff(), Model.Latched, 3.0, speed);

        Out.WriteLine($"tracking the live nose: {live.MissMetres:F0} m ({live.Reads} reads)");
        Out.WriteLine($"latching it at the first pass: {latched.MissMetres:F0} m "
                      + $"({latched.Reads} reads)");

        Assert.True(live.MissMetres < latched.MissMetres,
                    $"latching landed {latched.MissMetres:F0} m against {live.MissMetres:F0} m for "
                    + "tracking, so it would be the cheaper answer after all");
    }
}
