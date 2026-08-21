using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The six-warhead group at the sub-kilometre level, taken apart into terms with a number each.
///
/// <para>Two quantities, and every number here says which it belongs to. <b>Common bias</b> is how
/// far the group's own centre sits from the target; <b>spread</b> is the widest gap between any two
/// of the six. They have different causes and different fixes, and a term that moves all six
/// together cannot be read off the scatter.</para>
///
/// <para>Measurement only. Nothing asserts an improvement — <c>ErrorBudgetTests</c> is the same
/// discipline one level up, and this shares its rig through <see cref="DeorbitShot"/> so the two
/// budgets cannot disagree about what the shot is.</para>
///
/// <para><b>What this rig cannot see.</b> The planet is at the origin and carries no velocity, which
/// is the one case where a frame carrier is identically zero — so nothing measured here can detect
/// an epoch fault in a term differenced against a body sample. <c>AirSampleEpochTests</c> pins that
/// convention instead, and the flown numbers for it are in <c>docs/MIRV-NEXT.md</c> item 2c.</para>
/// </summary>
public class MirvBudgetTests(ITestOutputHelper Out)
{
    /// <summary>What the bus trims its cutoff residual down to, measured in flight.</summary>
    private const double TrimmedResidual = 0.017;

    /// <summary>
    /// How long the whole salvo takes, measured in flight. With re-pointing off every tube wants
    /// the same attitude, so the magazine empties in consecutive frames.
    /// </summary>
    private const double FlownSalvoSeconds = 0.1;

    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double GroundMetres(double3 a, double3 b) => DeorbitShot.GroundMetres(a, b);

    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    // ---------------------------------------------------------------- the two trajectories

    /// <summary>
    /// Where a salvo leaves from, and along which line.
    ///
    /// <para>Two of these are compared throughout, because they are not the same trajectory and the
    /// difference is most of the gap between the headless spread and the flown one.</para>
    /// </summary>
    /// <param name="What">Which of the two it is, for the report.</param>
    /// <param name="PositionCci">Where the engines stopped.</param>
    /// <param name="VelocityCci">What the bus is doing there.</param>
    /// <param name="NoseCci">The line the bus holds, which is what the tubes are canted about.</param>
    /// <param name="TargetCci">Where the shot is aimed, in the epoch the release happens in.</param>
    /// <param name="PropellantLeftKg">
    /// What the running stage still had. A shot that arrives at cutoff dry is short of the
    /// trajectory it wanted, and no aim can move it — which is a different failure from a
    /// correction that will not converge and reads the same from the miss alone.
    /// </param>
    private readonly record struct Departure(string What, double3 PositionCci, double3 VelocityCci,
                                             double3 NoseCci, double3 TargetCci,
                                             double PropellantLeftKg = double.NaN);

    /// <summary>
    /// The idealised arc: the cheapest transfer from a 200 km circular pickup to a point 3,459 km
    /// downrange, held nose-retrograde. This is what <c>ErrorBudgetTests</c> and
    /// <c>PerTubeTrimTests</c> measure on.
    /// </summary>
    private static Departure Idealised()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 from, out double3 target);
        return new Departure("cheapest arc from a 200 km pickup", from, arc.RequiredVelocityCci,
                             -Vec.Unit(arc.RequiredVelocityCci), target);
    }

    /// <summary>
    /// The trajectory the guidance actually leaves the bus on: the whole <see cref="IcbmProgram"/>
    /// flown through <see cref="IcbmFlightRig"/> with the aim correction in the loop, from the same
    /// 300 km orbit <c>AimConvergenceTests</c> and <c>CutoffResidualTests</c> fly.
    ///
    /// <para>It matters that this is not the arc above. The program picks its own flight time and
    /// its own cutoff point, and the shot it settles on is materially less sensitive to a metre a
    /// second — which is the difference between a 2.7 km headless spread and the flown one.</para>
    /// </summary>
    /// <param name="residualMps">Velocity still to gain when the engines stopped.</param>
    /// <param name="predictedMissMetres">The last miss the correction loop was told about.</param>
    /// <param name="configure">Which of the loop's parts to leave out, for an A/B.</param>
    /// <param name="biasMetres">How far the correction had moved the aim by cutoff.</param>
    /// <param name="rangeMetres">How far downrange to aim, for a check across more than one shot.</param>
    /// <param name="cycles">How many cycles observed, and how many found no arc to observe.</param>
    private static Departure AsGuided(out double residualMps, out double predictedMissMetres,
                                      Action<ShippedAimLoop>? configure = null,
                                      Action<double>? biasMetres = null,
                                      double rangeMetres = DeorbitShot.RangeMetres,
                                      Action<int, int>? cycles = null)
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

        double3 aimAtEpoch = new(DeorbitShot.R * Math.Cos(rangeMetres / DeorbitShot.R),
                                 DeorbitShot.R * Math.Sin(rangeMetres / DeorbitShot.R), 0);

        ShippedAimLoop loop = new();
        configure?.Invoke(loop);
        rig.AimLoop = loop;

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aimAtEpoch, DeorbitShot.NominalFrame, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

        residualMps = program.ResidualAtCutoff;
        predictedMissMetres = loop.LastMissMetres;
        biasMetres?.Invoke(loop.BiasMetres);
        cycles?.Invoke(loop.Observations, loop.Refusals);

        // The line the bus holds through the coast is the one the burn ended on, which is what
        // IcbmProgram latches and what the release sequence measures its tubes against.
        double3 nose = Vec.Unit(flight.CoastDirectionCci.Equals(Vec.Zero)
                                    ? flight.LastBurnDirectionCci
                                    : flight.CoastDirectionCci);

        return new Departure("guided, as IcbmProgram leaves it", flight.CutoffPositionCci,
                             flight.CutoffVelocityCci, nose,
                             Earth.CarryCci(aimAtEpoch, flight.CutoffSeconds),
                             flight.PropellantLeftKg);
    }

    /// <summary>
    /// <see cref="AimCorrection"/> ridden the way <c>Ksa/IcbmComputer.cs</c> rides it today: predict
    /// from the solved cutoff state with the warhead's own drag and the mean ejection kick already
    /// added, bring the answer back to the epoch the target is expressed in, score against the
    /// target rather than against the biased aim, and freeze when the arrival commits.
    /// </summary>
    private sealed class ShippedAimLoop : IcbmFlightRig.IAimLoop
    {
        private const double PredictIntervalSeconds = 0.5;
        private const double PredictStepSeconds = 2.0;

        private readonly AimCorrection _aim = new();
        private double _sincePredict = double.PositiveInfinity;

        /// <summary>Do not correct at all — the aim stays exactly where it was pointed.</summary>
        public bool Off;

        /// <summary>Stop the correction when the arrival commits, which is not what ships.</summary>
        public bool StopAtTheArrival;

        /// <summary>The last predicted miss the loop was told about, in metres.</summary>
        public double LastMissMetres { get; private set; } = double.NaN;

        /// <summary>How many cycles reached the correction, and how many found no arc to fly.</summary>
        public int Observations { get; private set; }

        /// <inheritdoc cref="Observations"/>
        public int Refusals { get; private set; }

        /// <summary>How far the aim ended up from the target, in metres.</summary>
        public double BiasMetres => Vec.Len(_aim.BiasCci);

        public double3 Apply(double3 aimNowCci) => Off ? aimNowCci : _aim.Apply(aimNowCci);

        public bool IsSteady => Off || _aim.IsSteady;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci,
                                double step)
        {
            if (StopAtTheArrival && double.IsFinite(program.CommittedArrivalFromNow)) _aim.Freeze();

            _sincePredict += step;
            if (_sincePredict < PredictIntervalSeconds) return;
            _sincePredict = 0.0;

            if (!program.IsBurning || program.Arc is not { } arc) return;

            // The kick the computer assumes: the whole ejection speed along the line the bus holds,
            // which is the mean of the six tube axes and the line the correction converges against.
            double3 kick = Vec.Unit(command.ThrustDirectionCci) * Warhead.LaunchSpeed;

            if (!ImpactPredictor.TryPredict(Earth, program.CutoffPositionCci,
                                            arc.RequiredVelocityCci + kick, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, null, null,
                                            new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)))
            {
                Refusals++;
                return;
            }

            Observations++;

            double departsIn = double.IsFinite(command.SecondsToCutoff)
                               ? Math.Max(0.0, command.SecondsToCutoff) : 0.0;

            double3 scored = Earth.UncarryCci(hit.GroundFixedPointCci, departsIn);
            LastMissMetres = GroundMetres(scored, aimNowCci);

            if (!Off) _aim.Observe(scored, aimNowCci);
        }
    }

    // ---------------------------------------------------------------- the group

    /// <summary>The six tube axes in the frame a bus holding <paramref name="noseCci"/> is in.</summary>
    private static double3[] TubeAxes(double3 noseCci, double rollTurns = 0.0)
    {
        Tube[] tubes = Arsenal.MirvBus.Tubes;
        doubleQuat attitude = doubleQuat.CreateFromAxisAngle(noseCci, rollTurns * 2.0 * Math.PI)
                              * Vec.RotationFromTo(new double3(1, 0, 0), noseCci);

        double3[] axes = new double3[tubes.Length];
        for (int i = 0; i < tubes.Length; i++) axes[i] = Vec.Unit(attitude * tubes[i].Direction);
        return axes;
    }

    /// <summary>
    /// Where the six come down, predicted, released <paramref name="paceSeconds"/> apart from a bus
    /// coasting the whole time.
    ///
    /// <para>Every impact is un-carried by its own release delay as well as by its flight, so all
    /// six are places on the ground measured from one epoch. Carrying it the other way reports the
    /// planet's own turn as a miss, which is 465 m a second at the equator.</para>
    /// </summary>
    private static double3[] Group(in Departure d, double3[] axes, double paceSeconds,
                                   bool onTheMeanInstead = false)
    {
        double3 mean = ReleasePointing.ReferenceAxis(axes);
        double3[] landed = new double3[axes.Length];

        for (int i = 0; i < axes.Length; i++)
        {
            double delay = i * paceSeconds;

            Assert.True(Kepler.TryCoast(DeorbitShot.Mu, d.PositionCci, d.VelocityCci, delay,
                                        out double3 r, out double3 v));

            double3 kick = (onTheMeanInstead ? mean : axes[i]) * Warhead.LaunchSpeed;

            landed[i] = Earth.UncarryCci(DeorbitShot.Land(r, v + kick), delay);
        }

        return landed;
    }

    private void Report(string what, IReadOnlyList<double3> landed, double3 target)
    {
        double closest = double.MaxValue, furthest = 0.0, mean = 0.0;
        foreach (double3 p in landed)
        {
            double m = GroundMetres(p, target);
            closest = Math.Min(closest, m);
            furthest = Math.Max(furthest, m);
            mean += m / landed.Count;
        }

        Out.WriteLine($"  {what,-38}: bias {DeorbitShot.CommonBias(landed, target),6:F0} m, "
                      + $"spread {DeorbitShot.Spread(landed),6:F0} m "
                      + $"(misses {closest:F0}-{furthest:F0} m, mean {mean:F0} m)");
    }

    // ---------------------------------------------------------------- the terms

    /// <summary>
    /// What the two trajectories are, and what a metre a second is worth on each.
    ///
    /// <para><b>The sensitivity is the whole reason the headless spread and the flown one differ.</b>
    /// Every velocity-side term below — the cant, the residual, the trim's leavings — is a number of
    /// metres a second multiplied by one of these, so getting the trajectory wrong scales the entire
    /// budget.</para>
    /// </summary>
    [Fact]
    public void WhatAMetrePerSecondIsWorthOnEachTrajectory()
    {
        Departure guided = AsGuided(out double residual, out double predicted);
        Out.WriteLine($"guided: cutoff {Earth.AltitudeOf(guided.PositionCci) / 1000.0:F0} km up "
                      + $"doing {Vec.Len(guided.VelocityCci):F0} m/s, residual {residual:F3} m/s, "
                      + $"the loop's last predicted miss {predicted / 1000.0:F2} km");

        foreach (Departure d in new[] { Idealised(), guided })
        {
            Out.WriteLine($"{d.What}:");

            foreach ((string name, double3 axis) in Axes(d))
            {
                Out.WriteLine($"  {name,-12}: {PerMetrePerSecond(d, axis),6:F0} m per m/s");
            }

            Out.WriteLine($"  the bus's nose is {Vec.AngleBetween(d.NoseCci, -Vec.Unit(d.VelocityCci)) * 180.0 / Math.PI:F1} "
                          + "deg off retrograde");
            Out.WriteLine($"  uncorrected, a warhead off it lands "
                          + $"{GroundMetres(DeorbitShot.Land(d.PositionCci, d.VelocityCci + d.NoseCci * Warhead.LaunchSpeed), d.TargetCci) / 1000.0:F2} km out");
        }
    }

    private static (string Name, double3 Axis)[] Axes(in Departure d)
    {
        double3 prograde = Vec.Unit(d.VelocityCci);
        double3 radial = Vec.Unit(d.PositionCci);
        double3 cross = Vec.Unit(Vec.Cross(radial, prograde));
        return [("prograde", prograde), ("radial", radial), ("cross-track", cross)];
    }

    /// <summary>How far the impact moves per metre a second added along one axis at release.</summary>
    private static double PerMetrePerSecond(in Departure d, double3 axis)
    {
        const double delta = 0.5;
        double3 kick = d.NoseCci * Warhead.LaunchSpeed;

        return GroundMetres(DeorbitShot.Land(d.PositionCci, d.VelocityCci + kick + axis * delta),
                            DeorbitShot.Land(d.PositionCci, d.VelocityCci + kick - axis * delta))
               / (2.0 * delta);
    }

    /// <summary>
    /// Term: the tube cant. Six tubes on a six-degree cone at 2 m/s, released together from one
    /// attitude.
    ///
    /// <para>It is <b>almost pure spread</b>: a cone puts the same axial share on every tube, so
    /// what separates them is entirely lateral. What little bias it carries is the cosine — every
    /// tube throws <c>2·cos(6°)</c> along the line rather than the 2 m/s the prediction assumes.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void WhatTheTubeCantSpreadsTheGroupBy(double rollTurns)
    {
        Departure guided = AsGuided(out double _, out double _);

        foreach (Departure d in new[] { Idealised(), guided })
        {
            double3[] axes = TubeAxes(d.NoseCci, rollTurns);
            double3 mean = ReleasePointing.ReferenceAxis(axes);

            Out.WriteLine($"{d.What}, roll {rollTurns:F2} turns:");
            Report("as canted, released together", Group(d, axes, 0.0), d.TargetCci);
            Report("every tube on the mean instead", Group(d, axes, 0.0, onTheMeanInstead: true),
                   d.TargetCci);

            // The cosine: what the prediction assumes against what a canted tube actually throws.
            double3 assumed = mean * Warhead.LaunchSpeed;
            double3 actual = axes[0] * Warhead.LaunchSpeed;

            Out.WriteLine($"  each tube is {Vec.Len(actual - assumed):F4} m/s off the mean, of which "
                          + $"{Math.Abs(Vec.Dot(actual - assumed, mean)):F4} is along the line");
        }
    }

    /// <summary>
    /// <b>What the cant is worth depends on the attitude the burn happened to end on</b>, and by
    /// nearly an order of magnitude.
    ///
    /// <para>The cant is a cone about the bus's nose, so the six kicks differ only in the plane
    /// square to it — and the impact's sensitivity in that plane is whatever two directions the nose
    /// happens to leave there. One of them can nearly cancel: prograde and radial move the impact
    /// the same way, so a nose tipped between them leaves a combination that barely moves it at all.
    /// Nothing chooses the attitude for this; it is where velocity-to-be-gained ran out.</para>
    /// </summary>
    [Fact]
    public void HowMuchTheHeldAttitudeDecidesTheCantSpread()
    {
        Departure guided = AsGuided(out double _, out double _);

        double3 prograde = Vec.Unit(guided.VelocityCci);
        double3 radial = Vec.Unit(guided.PositionCci);
        double3 cross = Vec.Unit(Vec.Cross(radial, prograde));

        Out.WriteLine($"the nose the burn left: {Vec.Dot(guided.NoseCci, prograde):+0.000;-0.000} prograde, "
                      + $"{Vec.Dot(guided.NoseCci, radial):+0.000;-0.000} radial, "
                      + $"{Vec.Dot(guided.NoseCci, cross):+0.000;-0.000} cross-track");

        double worst = 0.0, best = double.MaxValue;
        double3 worstNose = Vec.Zero;

        // A coarse sweep of the whole sphere. The point is the range, not any one attitude.
        for (int i = 0; i <= 18; i++)
        {
            for (int j = 0; j < 36; j++)
            {
                double polar = i * Math.PI / 18.0;
                double azimuth = j * Math.PI / 18.0;

                double3 nose = Vec.Unit(prograde * Math.Cos(polar)
                                        + (radial * Math.Cos(azimuth) + cross * Math.Sin(azimuth))
                                          * Math.Sin(polar));

                double spread = DeorbitShot.Spread(Group(guided, TubeAxes(nose), 0.0));

                if (spread > worst) { worst = spread; worstNose = nose; }
                best = Math.Min(best, spread);
            }
        }

        Out.WriteLine($"across every attitude the bus could hold, the same cant spreads the group "
                      + $"{best:F0}-{worst:F0} m");
        Out.WriteLine($"  the worst of them is {Vec.Dot(worstNose, prograde):+0.000;-0.000} prograde, "
                      + $"{Vec.Dot(worstNose, radial):+0.000;-0.000} radial, "
                      + $"{Vec.Dot(worstNose, cross):+0.000;-0.000} cross-track");

        Report("held retrograde", Group(guided, TubeAxes(-prograde), 0.0), guided.TargetCci);
        Report("held nose-down", Group(guided, TubeAxes(-radial), 0.0), guided.TargetCci);
        Report("as the burn left it", Group(guided, TubeAxes(guided.NoseCci), 0.0), guided.TargetCci);
    }

    /// <summary>
    /// Term: the bus's own rate at release. <see cref="ReleaseSequence.LateralBudgetMetresPerSecond"/>
    /// is what a release may spend, and it is spent per round — so it is spread rather than bias.
    /// </summary>
    [Fact]
    public void WhatTheReleaseBudgetItselfIsWorth()
    {
        Departure guided = AsGuided(out double _, out double _);
        double3[] axes = TubeAxes(guided.NoseCci);
        double cant = Vec.AngleBetween(axes[0], ReleasePointing.ReferenceAxis(axes));

        Out.WriteLine($"the tubes are canted {cant * 180.0 / Math.PI:F2} deg, which at "
                      + $"{Warhead.LaunchSpeed:F1} m/s puts "
                      + $"{2.0 * Math.Sin(cant / 2.0) * Warhead.LaunchSpeed:F4} m/s at the tube");

        // Two rounds released at opposite ends of the budget, in the worst direction the sweep can
        // point: the gate bounds one round's lateral error, so the group can carry twice it.
        double3 sideways = Vec.Unit(Vec.Cross(guided.NoseCci, Vec.Unit(guided.PositionCci)));
        double budget = ReleaseSequence.LateralBudgetMetresPerSecond;

        foreach (double3 axis in new[] { sideways, Vec.Unit(Vec.Cross(guided.NoseCci, sideways)) })
        {
            double3 plus = DeorbitShot.Land(guided.PositionCci,
                                            guided.VelocityCci + guided.NoseCci * Warhead.LaunchSpeed
                                            + axis * budget);
            double3 minus = DeorbitShot.Land(guided.PositionCci,
                                             guided.VelocityCci + guided.NoseCci * Warhead.LaunchSpeed
                                             - axis * budget);

            Out.WriteLine($"  two rounds a whole budget apart square to the nose land "
                          + $"{GroundMetres(plus, minus):F0} m apart");
        }
    }

    /// <summary>
    /// Term: release pacing. Every second a warhead is held past cutoff spends the leverage its
    /// ejection kick has along the arc, so a paced salvo walks its impacts down a ramp.
    ///
    /// <para>The flown salvo empties the magazine in consecutive frames, so this should be worth
    /// almost nothing — which is the claim being checked rather than assumed.</para>
    /// </summary>
    [Fact]
    public void WhatTheReleasePacingCosts()
    {
        Departure guided = AsGuided(out double _, out double _);

        foreach (Departure d in new[] { Idealised(), guided })
        {
            Out.WriteLine($"{d.What}:");
            double3[] axes = TubeAxes(d.NoseCci);

            foreach (double pace in new[] { 0.0, FlownSalvoSeconds / 5.0, 0.5, 1.0, 3.0 })
            {
                Report($"{pace * 1000,6:F0} ms between tubes", Group(d, axes, pace), d.TargetCci);
            }

            // And the ramp on its own, with no cant in it: one tube's impact against the delay.
            double3 kick = d.NoseCci * Warhead.LaunchSpeed;
            double3 atCutoff = DeorbitShot.Land(d.PositionCci, d.VelocityCci + kick);

            foreach (double delay in new[] { 0.02, FlownSalvoSeconds, 1.0, 10.0 })
            {
                Assert.True(Kepler.TryCoast(DeorbitShot.Mu, d.PositionCci, d.VelocityCci, delay,
                                            out double3 r, out double3 v));

                Out.WriteLine($"  held {delay,5:F2} s: "
                              + $"{GroundMetres(atCutoff, Earth.UncarryCci(DeorbitShot.Land(r, v + kick), delay)),6:F1} m "
                              + "from the t+0 impact");
            }
        }
    }

    /// <summary>
    /// Term: what the burn leaves behind and the trim does not take out. Pure bias — it is one
    /// velocity error on the bus, so all six warheads inherit it identically.
    /// </summary>
    [Fact]
    public void WhatTheCutoffResidualAndTheTrimLeave()
    {
        Departure guided = AsGuided(out double residual, out double _);

        foreach (Departure d in new[] { Idealised(), guided })
        {
            Out.WriteLine($"{d.What}:");

            double sumOfSquares = 0.0;
            foreach ((string name, double3 axis) in Axes(d))
            {
                double perMetre = PerMetrePerSecond(d, axis);
                sumOfSquares += perMetre * perMetre;

                Out.WriteLine($"  {name,-12}: {perMetre * TrimmedResidual,6:F0} m at the trimmed "
                              + $"{TrimmedResidual} m/s, {perMetre * residual,6:F0} m at this "
                              + $"flight's own {residual:F3} m/s cutoff residual");
            }

            // The residual's direction is not recorded in flight, so the honest single number is
            // the root mean square over the three axes rather than the worst of them.
            double isotropic = Math.Sqrt(sumOfSquares / 3.0);

            Out.WriteLine($"  root mean square: {isotropic * TrimmedResidual:F0} m trimmed, "
                          + $"{isotropic * residual:F0} m at {residual:F3} m/s");
        }
    }

    /// <summary>
    /// Term: the round disagreeing with the predictor that aimed it. Pure bias, because all six fly
    /// nearly the same trajectory — and invisible to the correction loop, whose only observer is
    /// that predictor.
    ///
    /// <para>A 1 ms flight is the reference rather than the predictor, so the round's own
    /// integration error is separated from the two models genuinely differing. Whatever is left at
    /// 1 ms is the model gap: fourth-order Runge-Kutta with gravity at every stage against
    /// symplectic Euler on 5 ms sub-steps with gravity held for the frame.</para>
    /// </summary>
    [Fact]
    public void ThePredictorAgainstTheRoundItPredicts()
    {
        Departure guided = AsGuided(out double _, out double _);

        foreach (Departure d in new[] { Idealised(), guided })
        {
            double3 v = d.VelocityCci + d.NoseCci * Warhead.LaunchSpeed;

            Assert.True(ImpactPredictor.TryPredict(Earth, d.PositionCci, v, 2.0, 20_000.0,
                                                   out ImpactPredictor.Impact predicted, null, null,
                                                   new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

            (double3 reference, double referenceSeconds) =
                DeorbitShot.FlyTheRound(d.PositionCci, v, 0.001);

            Out.WriteLine($"{d.What}:");
            Out.WriteLine($"  the predictor and a 1 ms round are "
                          + $"{GroundMetres(predicted.GroundFixedPointCci, reference):F0} m apart "
                          + $"({referenceSeconds - predicted.Seconds:+0.000;-0.000} s)");

            foreach (double dt in new[] { DeorbitShot.NominalFrame, 0.05, 0.13, 0.32 })
            {
                (double3 landed, double seconds) = DeorbitShot.FlyTheRound(d.PositionCci, v, dt);

                Out.WriteLine($"  {dt * 1000,4:F0} ms frame: "
                              + $"{GroundMetres(predicted.GroundFixedPointCci, landed),6:F0} m from "
                              + $"the prediction, {GroundMetres(reference, landed),6:F0} m from the "
                              + $"1 ms round ({seconds - referenceSeconds:+0.000;-0.000} s)");
            }

            // And the step the world is actually held to, which is neither of those: the scenario
            // asks for 8x through the coast and the round's own faithful step pulls it back for
            // the entry.
            (double3 warped, double _) =
                DeorbitShot.FlyTheRoundAsWarped(d.PositionCci, v, DeorbitShot.ScenarioWarp);

            Out.WriteLine($"  as flown ({DeorbitShot.ScenarioWarp:F0}x coast, "
                          + $"{Medium.FaithfulStepInAir * 1000:F0} ms in air): "
                          + $"{GroundMetres(predicted.GroundFixedPointCci, warped):F0} m from the "
                          + $"prediction, {GroundMetres(reference, warped):F0} m from the 1 ms round");

            // At 1x as well, because the flown pair of runs the budget is being spent against were
            // one of each and the gap between them is the whole reason to know this number.
            (double3 unwarped, double _) =
                DeorbitShot.FlyTheRoundAsWarped(d.PositionCci, v, 1.0);

            Out.WriteLine($"  as flown (1x coast): "
                          + $"{GroundMetres(predicted.GroundFixedPointCci, unwarped):F0} m from the "
                          + $"prediction, {GroundMetres(reference, unwarped):F0} m from the 1 ms round");
        }
    }

    /// <summary>
    /// Term: gravity and the air's motion are frame-level arguments to <see cref="Slug"/>, held
    /// across every 5 ms sub-step, while <see cref="ImpactPredictor"/> re-evaluates gravity at each
    /// Runge-Kutta stage.
    ///
    /// <para>Priced, not fixed. <c>Sim/BallisticBody.cs</c> already carries <c>Mu</c>, so an
    /// analytic per-sub-step gravity needs no call into the game — but this is measurement and the
    /// decision is somebody else's.</para>
    /// </summary>
    [Fact]
    public void WhatHoldingGravityForAWholeFrameCosts()
    {
        Departure guided = AsGuided(out double _, out double _);
        double3 v = guided.VelocityCci + guided.NoseCci * Warhead.LaunchSpeed;

        (double3 reference, double _) = DeorbitShot.FlyTheRound(guided.PositionCci, v, 0.001);

        Out.WriteLine("against a 1 ms round, which is the same code with nothing held:");

        foreach (double dt in new[] { DeorbitShot.NominalFrame, 0.05, 0.13, 0.2, 0.32 })
        {
            (double3 held, double _) = DeorbitShot.FlyTheRound(guided.PositionCci, v, dt);
            (double3 freshGravity, double _) =
                DeorbitShot.FlyTheRound(guided.PositionCci, v, dt, new DeorbitShot.Refresh(true, false));
            (double3 freshAir, double _) =
                DeorbitShot.FlyTheRound(guided.PositionCci, v, dt, new DeorbitShot.Refresh(false, true));
            (double3 freshBoth, double _) =
                DeorbitShot.FlyTheRound(guided.PositionCci, v, dt, new DeorbitShot.Refresh(true, true));

            Out.WriteLine($"  {dt * 1000,4:F0} ms frame: as flown {GroundMetres(reference, held),6:F0} m, "
                          + $"gravity per sub-step {GroundMetres(reference, freshGravity),6:F0} m, "
                          + $"air per sub-step {GroundMetres(reference, freshAir),6:F0} m, "
                          + $"both {GroundMetres(reference, freshBoth),6:F0} m");
        }

        // And at the step the world is actually held to, which is what a fix would be worth.
        (double3 warpedHeld, double _) =
            DeorbitShot.FlyTheRoundAsWarped(guided.PositionCci, v, DeorbitShot.ScenarioWarp);
        (double3 warpedFresh, double _) =
            DeorbitShot.FlyTheRoundAsWarped(guided.PositionCci, v, DeorbitShot.ScenarioWarp,
                                            new DeorbitShot.Refresh(true, true));

        Out.WriteLine($"  as flown ({DeorbitShot.ScenarioWarp:F0}x coast): "
                      + $"held {GroundMetres(reference, warpedHeld):F0} m, "
                      + $"re-read {GroundMetres(reference, warpedFresh):F0} m "
                      + $"-- the freeze is worth {GroundMetres(warpedHeld, warpedFresh):F0} m");
    }

    /// <summary>
    /// Term: what the aim correction leaves on the table. Pure bias — one aim serves all six.
    ///
    /// <para>The loop converges against a prediction taken from a cutoff state that is still moving,
    /// and runs until the engines stop. Stopping it when the <em>arrival</em> commits leaves a
    /// residue the aim cannot follow — not a convergence failure, and not visible in the loop's own
    /// readout, which reports the miss it last measured rather than the one it ends up with.</para>
    ///
    /// <para><c>Sim/PostBoostAim.cs</c> is the lever that reopens it after cutoff and is not
    /// modelled here, so this is the bias before any post-boost pass has run.</para>
    /// </summary>
    [Fact]
    public void WhatTheAimCorrectionLeavesOnTheTable()
    {
        foreach ((string what, Action<ShippedAimLoop>? configure) in
                 new (string, Action<ShippedAimLoop>?)[]
                 {
                     ("no correction at all", l => l.Off = true),
                     ("as shipped", null),
                     ("stopped at the arrival", l => l.StopAtTheArrival = true),
                 })
        {
            double bias = double.NaN;
            Departure d = AsGuided(out double residual, out double predicted, configure,
                                   b => bias = b);

            double3[] landed = Group(d, TubeAxes(d.NoseCci),
                                     FlownSalvoSeconds / (Arsenal.MirvBus.Tubes.Length - 1));

            Out.WriteLine($"{what}: aim moved {bias / 1000.0:F1} km, the loop last reported "
                          + $"{predicted / 1000.0:F2} km, residual {residual:F3} m/s");
            Report("  where the group actually goes", landed, d.TargetCci);
        }
    }

    /// <summary>
    /// And whether that is one geometry's luck. Four ranges, the same two wirings.
    ///
    /// <para>The bias here is where the six actually go, not what the loop reported — those are
    /// different numbers and only the first is the shot.</para>
    ///
    /// <para>7,645 km is the one range where stopping early wins — 1.28 km against 3.23 — and it is
    /// the same geometry item 7c is about, where the miss is furthest from a monotonic function of
    /// the aim and the loop is walking a 200 km bias. Everything inside the flown envelope goes the
    /// other way.</para>
    /// </summary>
    [Theory]
    [InlineData(2_000_000.0)]
    [InlineData(3_459_000.0)]
    [InlineData(5_000_000.0)]
    [InlineData(7_645_000.0)]
    public void WhetherStoppingTheAimAtTheArrivalPaysAtAnyRange(double rangeMetres)
    {
        foreach ((string what, Action<ShippedAimLoop>? configure) in
                 new (string, Action<ShippedAimLoop>?)[]
                 {
                     ("no correction", l => l.Off = true),
                     ("as shipped", null),
                     ("stopped at the arrival", l => l.StopAtTheArrival = true),
                 })
        {
            double moved = double.NaN;
            int seen = 0, refused = 0;
            Departure d = AsGuided(out double _, out double predicted, configure,
                                   b => moved = b, rangeMetres, (o, r) => (seen, refused) = (o, r));

            double3[] landed = Group(d, TubeAxes(d.NoseCci),
                                     FlownSalvoSeconds / (Arsenal.MirvBus.Tubes.Length - 1));

            Out.WriteLine($"  {rangeMetres / 1000.0,6:F0} km, {what,-14}: "
                          + $"bias {DeorbitShot.CommonBias(landed, d.TargetCci) / 1000.0,8:F2} km, "
                          + $"spread {DeorbitShot.Spread(landed),6:F0} m, "
                          + $"aim moved {moved / 1000.0,7:F1} km, "
                          + $"loop reported {predicted / 1000.0:F2} km "
                          + $"off {seen} cycles ({refused} with no arc), "
                          + $"{d.PropellantLeftKg:F0} kg left");
        }
    }

    /// <summary>
    /// The whole group, flown as the game flies it: six real <see cref="Slug"/>s off the guided
    /// cutoff state, at the step the world is held to, released at the flown pace.
    ///
    /// <para>This is the number the rest of the file is a decomposition of. Everything above is
    /// predicted; this is the only place the rounds are actually integrated.</para>
    /// </summary>
    [Fact]
    public void TheWholeGroupFlownAsTheGameFliesIt()
    {
        Departure guided = AsGuided(out double residual, out double predictedMiss);
        double3[] axes = TubeAxes(guided.NoseCci);

        Out.WriteLine($"cutoff residual {residual:F3} m/s, the loop's last predicted miss "
                      + $"{predictedMiss / 1000.0:F2} km");

        foreach (double warp in new[] { 1.0, DeorbitShot.ScenarioWarp })
        {
            double3[] landed = new double3[axes.Length];

            for (int i = 0; i < axes.Length; i++)
            {
                double delay = i * (FlownSalvoSeconds / (axes.Length - 1));

                Assert.True(Kepler.TryCoast(DeorbitShot.Mu, guided.PositionCci, guided.VelocityCci,
                                            delay, out double3 r, out double3 vv));

                // The flight is already taken out of the impact the rig returns; what is left is
                // this tube's own wait, so that all six are places on the ground in one epoch.
                (double3 impact, double _) =
                    DeorbitShot.FlyTheRoundAsWarped(r, vv + axes[i] * Warhead.LaunchSpeed, warp);

                landed[i] = Earth.UncarryCci(impact, delay);
            }

            Report($"flown, {warp:F0}x coast", landed, guided.TargetCci);
        }

        // The same six predicted rather than flown, so the round-versus-predictor term can be read
        // off as the difference between the two biases.
        Report("predicted rather than flown",
               Group(guided, axes, FlownSalvoSeconds / (axes.Length - 1)), guided.TargetCci);
    }
}
