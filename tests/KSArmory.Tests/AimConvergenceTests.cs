using System.Reflection;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The aim correction converges and the shot then walks away from it anyway.
///
/// <para>Flown at 3,459 km from a near-orbital pickup, the per-cycle trace fell to about a
/// kilometre of predicted miss, the arrival latched, <see cref="AimCorrection.Freeze"/> was called
/// — and the prediction went on rising to about ten kilometres with the aim no longer moving. So
/// the residue is not the correction failing to converge; something else moves after it stops.</para>
///
/// <para>This flies the whole <see cref="IcbmProgram"/> with the correction in the loop, wired the
/// way <c>Ksa/IcbmComputer.cs</c> wires it, so the candidates can be turned off one at a time.</para>
/// </summary>
public class AimConvergenceTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;
    private const double ScaleHeight = 8_000.0;
    private const double EarthSpin = 7.2921159e-5;

    /// <summary>How fast the ground moves under a shot on the equator. The drift rate, as it turns out.</summary>
    private const double GroundSpeed = EarthSpin * R;

    /// <summary>The flown geometry: a shallow near-orbital arrival at a target this far downrange.</summary>
    private const double ShotMetres = 3_459_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), EarthSpin);

    private static double DensityAt(double3 pointCci)
        => Math.Exp(-Math.Max(0.0, Vec.Len(pointCci) - R) / ScaleHeight);

    private static double GroundMetres(double3 a, double3 b) => R * Vec.AngleBetween(a, b);

    /// <summary>A place on the equator, that far ahead of the pickup along the track.</summary>
    private static double3 Downrange(double metres)
        => new(R * Math.Cos(metres / R), R * Math.Sin(metres / R), 0);

    /// <summary>The same bus <see cref="CutoffResidualTests"/> flies, with the game's actuator.</summary>
    private static IcbmFlightRig InOrbit()
        => new()
        {
            Body = Earth,
            PositionCci = new double3(R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 300_000.0)), 0),
            Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = 2.0,
            MinThrottle = 0.12,
            StepJitter = 0.5,
        };


    /// <summary>
    /// The arrival floor these measurements were taken at, which is off.
    ///
    /// <para>Stated rather than inherited from <see cref="IcbmConfig.MinArrivalAngleDeg"/>'s
    /// default. Every number asserted below belongs to the seven-degree arrival the shot flies with
    /// no floor asked for — the velocity-side terms scale with the trajectory's sensitivity and the
    /// surface-side ones with <c>cot γ</c> — so a test that inherits the default is measuring
    /// whatever geometry the default happens to name.</para>
    /// </summary>
    private const double ShallowArrival = 0.0;

    private static IcbmProgram Armed() => new(new IcbmConfig { Armed = true, MinArrivalAngleDeg = ShallowArrival });

    /// <summary>What one correction cycle was told and what it had done about it.</summary>
    private readonly record struct Cycle(
        double Seconds,
        double BiasMetres,
        double ScoredMissMetres,
        double EpochCorrectedMissMetres,
        double SecondsToCutoff,
        bool Latched);

    /// <summary>
    /// <see cref="AimCorrection"/> riding a flight exactly as <c>IcbmComputer</c> rides one: predict
    /// from the solved cutoff state with the warhead's own drag, score against the target, freeze
    /// when the guidance commits to an arrival.
    ///
    /// <para>The switches are the experiments, and each is off by default so the loop reproduces
    /// what ships.</para>
    /// </summary>
    private sealed class Loop(IcbmFlightRig rig, MunitionProfile warhead) : IcbmFlightRig.IAimLoop
    {
        private const double PredictIntervalSeconds = 0.5;
        private const double PredictStepSeconds = 2.0;

        // The one thing here that reaches past a public surface. The arrival latch has no switch —
        // it is a consequence of the aim going steady — and it is one of two things that change at
        // commitment, so telling them apart means holding one still.
        private static readonly FieldInfo Arrival =
            typeof(IcbmProgram).GetField("_arrivalFromLaunch",
                                         BindingFlags.NonPublic | BindingFlags.Instance)!;

        private readonly AimCorrection _aim = new();
        private double _sincePredict = double.PositiveInfinity;

        /// <summary>Let the correction keep running after the arrival commits.</summary>
        public bool NeverFreeze;

        /// <summary>Do not correct at all — the aim stays exactly where it was pointed.</summary>
        public bool Off;

        /// <summary>Keep the transfer free to choose its own arrival for the whole burn.</summary>
        public bool NeverLatchTheArrival;

        /// <summary>
        /// Take the prediction's ground-fixed point back to the epoch the target is expressed in.
        ///
        /// <para>The prediction departs from the <em>cutoff</em> state, so what it un-carries by is
        /// the flight time alone — leaving its answer in the body frame of the cutoff instant while
        /// the target is in the frame of now.</para>
        /// </summary>
        public bool UncarryToTheCutoffEpoch;

        public readonly List<Cycle> Trace = [];

        public double BiasMetres => Vec.Len(_aim.BiasCci);

        /// <summary>Whether the loop stopped on its own, having run out of improvement to find.</summary>
        public bool Settled => _aim.Settled;

        public double3 Apply(double3 aimNowCci) => Off ? aimNowCci : _aim.Apply(aimNowCci);

        public bool IsSteady => Off || _aim.IsSteady;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci, double step)
        {
            if (NeverLatchTheArrival) Arrival.SetValue(program, double.NaN);

            if (!NeverFreeze && double.IsFinite(program.CommittedArrivalFromNow)) _aim.Freeze();

            _sincePredict += step;
            if (_sincePredict < PredictIntervalSeconds) return;
            _sincePredict = 0.0;

            bool fromCutoff = program.IsBurning && program.Arc is not null;
            double3 fromCci = fromCutoff ? program.CutoffPositionCci : rig.PositionCci;
            double3 alongCci = fromCutoff ? program.Arc!.Value.RequiredVelocityCci : rig.VelocityCci;

            if (!ImpactPredictor.TryPredict(Earth, fromCci, alongCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, null, null,
                                            new ImpactPredictor.Drag(DensityAt, warhead)))
            {
                return;
            }

            double toCutoff = fromCutoff ? command.SecondsToCutoff : 0.0;

            double3 asShipped = hit.GroundFixedPointCci;
            double3 atTargetsEpoch = Earth.UncarryCci(asShipped, toCutoff);
            double3 scored = UncarryToTheCutoffEpoch ? atTargetsEpoch : asShipped;

            Trace.Add(new Cycle(program.SecondsSinceLaunch, BiasMetres,
                                GroundMetres(scored, aimNowCci),
                                GroundMetres(atTargetsEpoch, aimNowCci),
                                toCutoff, double.IsFinite(program.CommittedArrivalFromNow)));

            if (!Off) _aim.Observe(scored, aimNowCci);
        }
    }

    private readonly record struct Shot(
        IcbmFlightRig.Flight Flight, IcbmProgram Program, Loop Loop, double3 AimAtEpoch);

    private static Shot Fly(Action<Loop>? configure = null, double metres = ShotMetres)
    {
        IcbmFlightRig rig = InOrbit();
        Loop loop = new(rig, Arsenal.ReentryVehicleMk21);
        configure?.Invoke(loop);
        rig.AimLoop = loop;

        IcbmProgram program = Armed();
        double3 aim = Downrange(metres);

        return new Shot(rig.Fly(program, aim, 0.02, 6_000.0), program, loop, aim);
    }

    /// <summary>Where the warheads actually go, flown from the state the engines really stopped in.</summary>
    private static double ActualMissMetres(in Shot shot, double3 extraVelocityCci = default)
    {
        if (!ImpactPredictor.TryPredict(Earth, shot.Flight.CutoffPositionCci,
                                        shot.Flight.CutoffVelocityCci + extraVelocityCci,
                                        1.0, ImpactPredictor.DefaultMaxSeconds,
                                        out ImpactPredictor.Impact hit, null, null,
                                        new ImpactPredictor.Drag(DensityAt, Arsenal.ReentryVehicleMk21)))
        {
            return double.NaN;
        }

        return GroundMetres(hit.GroundFixedPointCci,
                            Earth.CarryCci(shot.AimAtEpoch, shot.Flight.CutoffSeconds));
    }

    private double Report(string what, in Shot shot)
    {
        Out.WriteLine($"--- {what}");
        Out.WriteLine($"    {"t",7} {"bias km",9} {"scored km",10} {"true km",9} {"to cutoff",10} "
                      + $"{"turn km",8}  latched");

        List<Cycle> trace = shot.Loop.Trace;
        int every = Math.Max(1, trace.Count / 24);

        for (int i = 0; i < trace.Count; i++)
        {
            if (i % every != 0 && i != trace.Count - 1) continue;

            Cycle c = trace[i];
            Out.WriteLine($"    {c.Seconds,7:F1} {c.BiasMetres / 1000.0,9:F1} "
                          + $"{c.ScoredMissMetres / 1000.0,10:F2} {c.EpochCorrectedMissMetres / 1000.0,9:F2} "
                          + $"{c.SecondsToCutoff,10:F1} {GroundSpeed * c.SecondsToCutoff / 1000.0,8:F1}  "
                          + (c.Latched ? "yes" : ""));
        }

        double actual = ActualMissMetres(shot);

        Out.WriteLine($"    reached {shot.Flight.Reached}, cutoff at {shot.Flight.CutoffSeconds:F1} s, "
                      + $"residual {shot.Program.ResidualAtCutoff:F3} m/s, "
                      + $"bias {shot.Loop.BiasMetres / 1000.0:F1} km");
        Out.WriteLine($"    ACTUAL miss flown from the real cutoff state: {actual / 1000.0:F2} km");
        Out.WriteLine("");

        return actual;
    }

    /// <summary>
    /// The rig. It reproduces the flight's shape — a predicted miss that falls to under a kilometre,
    /// then rises with the aim frozen — and says what the shot really does.
    ///
    /// <para>The two miss columns are the whole diagnosis. <c>scored</c> is what the correction is
    /// told; <c>true</c> is the same prediction taken back to the epoch the target is expressed in.
    /// <c>turn km</c> is what the planet turns during the rest of the burn, and the gap between the
    /// two columns is exactly that.</para>
    /// </summary>
    [Fact]
    public void TheShippedLoopFlownEndToEnd()
    {
        Shot shot = Fly();
        Report("shipped wiring", shot);

        Assert.True(shot.Flight.Reached, $"the burn never reached coast: {shot.Flight.Hold}");

        // The shape being reproduced: converged, frozen, and then walking away from its own answer.
        List<Cycle> frozen = [.. shot.Loop.Trace.Where(c => c.Latched)];

        Assert.True(frozen.Count > 4, "the arrival never latched, so this is not the flown case");
        Assert.True(frozen[0].ScoredMissMetres < 2_000.0,
                    $"it had not converged by the freeze: {frozen[0].ScoredMissMetres / 1000.0:F1} km");
        Assert.True(frozen[^1].ScoredMissMetres > 8_000.0,
                    $"the predicted miss did not drift after the freeze: "
                    + $"{frozen[^1].ScoredMissMetres / 1000.0:F1} km");

        // And the answer: nothing about the shot moved. The same prediction, taken back to the
        // epoch the target is expressed in, sits still for the whole burn — what drifts is the
        // ruler, which loses the planet's remaining turn as the countdown runs down.
        double flattest = frozen.Min(c => c.EpochCorrectedMissMetres);
        double widest = frozen.Max(c => c.EpochCorrectedMissMetres);

        double drift = (frozen[^1].ScoredMissMetres - frozen[0].ScoredMissMetres)
                     / (frozen[^1].Seconds - frozen[0].Seconds);

        Out.WriteLine($"the scored miss drifts at {drift:F0} m/s, against {GroundSpeed:F0} m/s of "
                      + "ground speed under the shot");
        Out.WriteLine($"the same prediction at the target's own epoch stays within "
                      + $"{(widest - flattest) / 1000.0:F2} km for the whole burn");

        Assert.True(widest - flattest < 2_000.0,
                    $"the shot itself moved {(widest - flattest) / 1000.0:F1} km, so the drift is "
                    + "not purely the epoch the miss is measured in");
    }

    /// <summary>
    /// Every candidate, one at a time, scored on where the warheads actually go.
    ///
    /// <para>Only one of them moves it: the prediction departs from the cutoff state, so it
    /// un-carries its impact by the flight time alone and leaves the answer in the body frame of the
    /// cutoff instant — while the target it is scored against is in the frame of now. The gap is the
    /// rest of the burn's worth of planet, and the correction dutifully removes it by aiming
    /// wrong.</para>
    /// </summary>
    [Fact]
    public void OnlyTheEpochTheMissIsMeasuredInAccountsForIt()
    {
        double shipped = Report("shipped wiring", Fly());
        double noFreeze = Report("Freeze() never called", Fly(l => l.NeverFreeze = true));
        double noLatch = Report("arrival never latched", Fly(l => l.NeverLatchTheArrival = true));
        double off = Report("no aim correction at all", Fly(l => l.Off = true));
        double epoch = Report("prediction taken back to the target's epoch",
                              Fly(l => l.UncarryToTheCutoffEpoch = true));

        Out.WriteLine($"shipped                    {shipped / 1000.0,8:F2} km");
        Out.WriteLine($"no freeze                  {noFreeze / 1000.0,8:F2} km");
        Out.WriteLine($"no arrival latch           {noLatch / 1000.0,8:F2} km");
        Out.WriteLine($"no correction              {off / 1000.0,8:F2} km");
        Out.WriteLine($"scored at the right epoch  {epoch / 1000.0,8:F2} km");

        Assert.True(epoch < 2_000.0,
                    $"scoring at the right epoch should close the shot; it left {epoch / 1000.0:F1} km");

        // The one that matters: as shipped the correction makes the shot worse than not correcting.
        Assert.True(shipped > off,
                    "the shipped correction should be making this worse, which is the fault");
    }

    /// <summary>
    /// And it is not one geometry's accident. The gap between the two ways of scoring is the ground
    /// speed times whatever is left of the burn, so every shot with a burn in front of it has it.
    /// </summary>
    [Theory]
    [InlineData(2_000_000.0)]
    // Flown 2026-08-24 and missed by 1.3 km: the correction gave up ("aim settled 1.2 km out")
    // and the round then tracked its own release probe to 0.1 km, so the whole miss was the aim.
    [InlineData(2_433_000.0)]
    [InlineData(3_459_000.0)]
    [InlineData(5_000_000.0)]
    [InlineData(7_645_000.0)]
    public void TheSameFaultAtEveryRange(double metres)
    {
        double shipped = ActualMissMetres(Fly(metres: metres));
        double uncorrected = ActualMissMetres(Fly(l => l.Off = true, metres));

        Shot atEpoch = Fly(l => l.UncarryToTheCutoffEpoch = true, metres);
        double epoch = ActualMissMetres(atEpoch);

        double epochRunning = ActualMissMetres(Fly(l =>
        {
            l.UncarryToTheCutoffEpoch = true;
            l.NeverFreeze = true;
        }, metres));

        Out.WriteLine($"{metres / 1000.0,5:F0} km shot: uncorrected {uncorrected / 1000.0,7:F2} km, "
                      + $"shipped {shipped / 1000.0,7:F2} km, "
                      + $"right epoch {epoch / 1000.0,6:F2} km, "
                      + $"right epoch and never frozen {epochRunning / 1000.0,6:F2} km");
        Out.WriteLine($"            at the right epoch the aim moved {atEpoch.Loop.BiasMetres / 1000.0:F1} km"
                      + (atEpoch.Loop.Settled ? ", and it had stopped correcting by cutoff" : ""));

        // The claim that has to hold everywhere: scored at the right epoch the correction is at
        // worst harmless. As shipped it is not — on a shot that needed no correction at all it
        // invents one out of the planet's turn.
        Assert.True(epoch <= uncorrected + 2_000.0,
                    $"scoring at the right epoch left {epoch / 1000.0:F1} km against "
                    + $"{uncorrected / 1000.0:F1} km for not correcting at all");
    }

    /// <summary>
    /// The predicted miss is not a monotonic function of the aim, so the loop has to be allowed to
    /// walk past its own best to reach the answer on the far side of the hump.
    ///
    /// <para>At 7,645 km the worsening patch is five cycles long — 3.34 km of predicted miss out to
    /// 5.89 and back — and beyond it the aim is worth 1.73 km. Giving up inside the patch does not
    /// merely keep the worse aim: it is what makes <see cref="AimCorrection.IsSteady"/> true, so the
    /// arrival commits, and the aim that was kept is then worth 15.86 km against the 3.34 it was
    /// kept for. Flown from the real cutoff state, 15.74 km against 1.15.</para>
    /// </summary>
    [Fact]
    public void TheLoopWalksPastItsOwnBestToReachTheAnswerBeyondIt()
    {
        Shot shot = Fly(l => l.UncarryToTheCutoffEpoch = true, 7_645_000.0);
        List<Cycle> trace = shot.Loop.Trace;

        // The patch itself, and then where the aim ended up — a loop that stopped inside it holds an
        // aim it has since measured to be several times worse than the best it stopped for.
        double best = double.PositiveInfinity;
        int worst = 0;
        int run = 0;

        for (int i = 0; i < trace.Count; i++)
        {
            if (i < 12 || i == trace.Count - 1)
            {
                Out.WriteLine($"    {trace[i].Seconds,6:F2} s  bias {trace[i].BiasMetres / 1000.0,8:F2} km"
                              + $"  predicted miss {trace[i].ScoredMissMetres / 1000.0,8:F2} km"
                              + (trace[i].Latched ? "  arrival committed" : ""));
            }

            // Counted the way the loop counts it, so the number printed is the one
            // AimCorrection.WorseBeforeStopping has to be larger than.
            double miss = trace[i].ScoredMissMetres;

            if (miss < best - AimCorrection.ImprovedByMetres) { best = miss; run = 0; }
            else if (miss > best + AimCorrection.ImprovedByMetres) worst = Math.Max(worst, ++run);
        }

        double actual = ActualMissMetres(shot);

        Out.WriteLine($"    the longest run of cycles worse than the best so far: {worst}");
        Out.WriteLine($"    ACTUAL miss flown from the real cutoff state: {actual / 1000.0:F2} km");

        Assert.True(trace[^1].ScoredMissMetres < 3_000.0,
                    $"the loop ended holding an aim it predicts is {trace[^1].ScoredMissMetres / 1000.0:F1} km "
                    + $"off, having banked {best / 1000.0:F1} km — the record it kept is of a plant "
                    + "that has since moved, so it gave up inside the hump");

        Assert.True(actual < 3_000.0,
                    $"the warheads land {actual / 1000.0:F1} km off, against 1.15 km for a loop "
                    + $"patient enough to cross a {worst}-cycle worsening patch");
    }

    /// <summary>
    /// What the residual is worth on this trajectory, measured rather than assumed: fly the real
    /// cutoff state, then fly it again with what was left to gain added back.
    /// </summary>
    [Fact]
    public void TheCutoffResidualIsNotWhatIsMovingIt()
    {
        Shot shot = Fly();

        double asFlown = ActualMissMetres(shot);
        double perfect = ActualMissMetres(shot, shot.Program.ResidualVectorCci);

        Out.WriteLine($"residual {shot.Program.ResidualAtCutoff:F4} m/s");
        Out.WriteLine($"  miss as flown            {asFlown / 1000.0:F2} km");
        Out.WriteLine($"  miss with it removed     {perfect / 1000.0:F2} km");
        Out.WriteLine($"  so the residual is worth {(asFlown - perfect) / 1000.0:F2} km");

        Assert.True(Math.Abs(asFlown - perfect) < 0.05 * asFlown,
                    "the residual should account for almost none of this miss");
    }
}
