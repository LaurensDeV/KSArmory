using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What an arrival-angle floor does to a shot that is being flown, which is a different question
/// from what the search does with it.
///
/// <para><c>ArrivalFloorTests</c> covers the search: given a state, does the bound pick a satisfying
/// arc. This flies the whole <see cref="IcbmProgram"/> at a floor and asks whether the shot still
/// has one when the engines stop — and where the miss on a constrained shot actually comes from.
/// </para>
///
/// <para><b>The planet sits at the origin and does not move</b>, per <see cref="DeorbitShot"/>, so
/// nothing here can see an epoch fault.</para>
/// </summary>
public class ArrivalFloorFlightTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double3 Downrange(double metres)
        => new(DeorbitShot.R * Math.Cos(metres / DeorbitShot.R),
               DeorbitShot.R * Math.Sin(metres / DeorbitShot.R), 0);

    /// <summary>The bus <see cref="AimConvergenceTests"/> flies, with the game's own actuator.</summary>
    private static IcbmFlightRig InOrbit() => new()
    {
        Body = Earth,
        PositionCci = new double3(DeorbitShot.R + 300_000.0, 0, 0),
        VelocityCci = new double3(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + 300_000.0)), 0),
        Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
        CommandLatencyFrames = 1,
        ThrottleRatePerSecond = 2.0,
        MinThrottle = 0.12,
        StepJitter = 0.5,
    };

    /// <summary>
    /// <see cref="AimCorrection"/> riding a flight the way <c>Ksa/IcbmComputer.cs</c> rides one:
    /// predict from the solved cutoff state with the warhead's own drag, score in the epoch the
    /// target is expressed in, freeze when the arrival commits.
    /// </summary>
    private sealed class Loop(IcbmFlightRig rig, bool off, Func<double3, double>? ground) : IcbmFlightRig.IAimLoop
    {
        private const double PredictIntervalSeconds = 0.5;
        private const double PredictStepSeconds = 2.0;

        private readonly AimCorrection _aim = new();
        private double _sincePredict = double.PositiveInfinity;
        private double _elapsed;
        private bool _resumed;

        /// <summary>Per prediction cycle: the aim held, what the arc loses, and the whole miss.</summary>
        internal readonly record struct Cycle(double Seconds, double BiasMetres, double ShortfallMetres,
                                              double MissMetres, double FlightSeconds,
                                              double ArrivalDeg, double CutoffFromLaunch);

        public List<Cycle> Cycles { get; } = [];

        public double BiasMetres => Vec.Len(_aim.BiasCci);

        public double3 Apply(double3 aimNowCci) => off ? aimNowCci : _aim.Apply(aimNowCci);

        public bool IsSteady => off || _aim.IsSteady;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci, double step)
        {
            _elapsed += step;

            if (program.IsBurning && double.IsFinite(program.CommittedArrivalFromNow)) _aim.Freeze();
            if (!program.IsBurning && !_resumed) { _resumed = true; _aim.Resume(); }

            _sincePredict += step;
            if (_sincePredict < PredictIntervalSeconds) return;
            _sincePredict = 0.0;

            bool fromCutoff = program.IsBurning && program.Arc is not null;
            double3 fromCci = fromCutoff ? program.CutoffPositionCci : rig.PositionCci;
            double3 alongCci = fromCutoff ? program.Arc!.Value.RequiredVelocityCci : rig.VelocityCci;

            if (!ImpactPredictor.TryPredict(Earth, fromCci, alongCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, ground, null,
                                            new ImpactPredictor.Drag(DeorbitShot.DensityAt, DeorbitShot.Warhead)))
            {
                return;
            }

            // The prediction departs from the cutoff state, so it un-carries its impact by the
            // flight time alone and leaves the answer in the body frame of that instant. The target
            // is in the frame of now.
            double3 scored = Earth.UncarryCci(hit.GroundFixedPointCci,
                                              fromCutoff ? command.SecondsToCutoff : 0.0);

            // While the engines are running, which is where the prediction departs from the solved
            // cutoff state and is therefore about the shot rather than about the bus.
            if (program.Arc is { } arc && fromCutoff)
            {
                Cycles.Add(new Cycle(_elapsed, Vec.Len(_aim.BiasCci),
                                     // What flying the arc costs against the point it was solved
                                     // to, which is the only thing this loop exists to remove.
                                     DeorbitShot.GroundMetres(scored, Apply(aimNowCci)),
                                     DeorbitShot.GroundMetres(scored, aimNowCci),
                                     arc.FlightSeconds, arc.ArrivalAngleDeg,
                                     _elapsed + command.SecondsToCutoff));
            }

            if (!off) _aim.Observe(scored, aimNowCci);
        }
    }

    private readonly record struct Shot(IcbmFlightRig.Flight Flight, IcbmProgram Program,
                                        Loop Loop, double3 AimAtEpoch, Func<double3, double>? Ground);

    private static Shot Fly(double floorDeg, bool off = false,
                            double metres = DeorbitShot.RangeMetres,
                            Func<double3, double>? ground = null)
    {
        IcbmFlightRig rig = InOrbit();
        Loop loop = new(rig, off, ground);
        rig.AimLoop = loop;

        IcbmProgram program = new(new IcbmConfig { Armed = true, MinArrivalAngleDeg = floorDeg });
        double3 aim = Downrange(metres);

        return new Shot(rig.Fly(program, aim, 0.02, 12_000.0), program, loop, aim, ground);
    }

    /// <summary>Where the warheads go, flown from the state the engines really stopped in.</summary>
    private static double MissMetres(in Shot shot)
    {
        if (!ImpactPredictor.TryPredict(Earth, shot.Flight.CutoffPositionCci,
                                        shot.Flight.CutoffVelocityCci, 1.0,
                                        ImpactPredictor.DefaultMaxSeconds,
                                        out ImpactPredictor.Impact hit, shot.Ground, null,
                                        new ImpactPredictor.Drag(DeorbitShot.DensityAt, DeorbitShot.Warhead)))
        {
            return double.NaN;
        }

        return DeorbitShot.GroundMetres(hit.GroundFixedPointCci,
                                        Earth.CarryCci(shot.AimAtEpoch, shot.Flight.CutoffSeconds));
    }

    /// <summary>
    /// The bound survives the arrival latch.
    ///
    /// <para>Pinning the arrival <em>instant</em> leaves the burn time and the flight time free to
    /// trade against each other inside it, and the cheaper split is the longer coast — which onto
    /// the same target is a shallower arrival. So a held arrival walks straight out of the bound the
    /// search was constrained by, and the operator is handed an arrival they refused: every degree
    /// of it is the whole reason the floor exists, because the velocity sensitivity roughly doubles
    /// from 20 degrees back down to 15.</para>
    /// </summary>
    [Theory]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(20.0)]
    public void TheFlownArcArrivesNoShallowerThanTheFloor(double floorDeg)
    {
        Shot shot = Fly(floorDeg);

        Assert.True(shot.Flight.Reached, $"the burn never reached coast: {shot.Flight.Hold}");
        Assert.NotNull(shot.Program.Arc);

        double arrived = shot.Program.Arc!.Value.ArrivalAngleDeg;

        Out.WriteLine($"floor {floorDeg:F0} deg: flew {arrived:F2} deg, "
                      + $"miss {MissMetres(shot) / 1000.0:F2} km");

        // Half a degree is what measuring the arrival on the vacuum arc rather than through the air
        // already costs, per docs/ARRIVAL-ANGLE.md, so it is the floor under any tolerance here.
        Assert.True(arrived >= floorDeg - 0.5,
                    $"asked for {floorDeg:F0} deg or steeper and flew {arrived:F2}");
    }

    /// <summary>
    /// And with no floor a committed arrival is held whatever it arrives at, which is the whole of
    /// the default path.
    ///
    /// <para>The bound is the only thing that can refuse a held arc, so this is where "unchanged
    /// when nobody set a floor" is decided. A flown miss is a downstream consequence and would not
    /// say which branch produced it.</para>
    /// </summary>
    [Fact]
    public void AHeldArrivalIsHeldWhateverItArrivesAtWhenNoFloorIsSet()
    {
        BallisticArc.Solution shot = DeorbitShot.Shot(out double3 from, out double3 target);
        double3 circular = new(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + DeorbitShot.PickupAltitude)), 0);

        BoosterPerformance booster = new(300_000.0, 300_000.0 / 3_100.0, 43_000.0, 40_000.0);

        Assert.True(BurnoutGuidance.TrySteer(Earth, from, circular, target, booster,
                                             out BurnoutGuidance.Command free, 1.0, false,
                                             0.0, double.NaN, shot.FlightSeconds));

        Out.WriteLine($"no floor: held {free.HeldTheArrival}, arrives at {free.Arc.ArrivalAngleDeg:F2} deg");

        Assert.True(free.HeldTheArrival, "a committed arrival must be held when no floor is set");

        // The same state with a floor above what that arc arrives at: refused, which hands the cycle
        // to the constrained search and re-commits to an arrival that satisfies the bound.
        Assert.True(BurnoutGuidance.TrySteer(Earth, from, circular, target, booster,
                                             out BurnoutGuidance.Command bound, 1.0, false,
                                             0.0, double.NaN, shot.FlightSeconds,
                                             free.Arc.ArrivalAngleDeg + 5.0));

        Out.WriteLine($"floor {free.Arc.ArrivalAngleDeg + 5.0:F2} deg: held {bound.HeldTheArrival}, "
                      + $"arrives at {bound.Arc.ArrivalAngleDeg:F2} deg");

        Assert.False(bound.HeldTheArrival, "an arrival under the floor must not be held");
    }

    /// <summary>
    /// Under a floor the shot needs no correction at all, and the correction is the whole miss.
    ///
    /// <para>The loop exists to remove what flying the arc loses against the point it was solved to,
    /// and a steep arrival abolishes that: <c>docs/ARRIVAL-ANGLE.md</c> prices the drag shortfall at
    /// 13.4 km on the graze the mod normally flies and 0.3 km at fifteen degrees. So the signal goes
    /// to nothing while the noise — the trajectory search still moving, see
    /// <see cref="TheSearchIsStillMovingWhenTheCorrectionOpens"/> — does not.</para>
    ///
    /// <para><b>This is a diagnosis rather than a design.</b> Turning the loop off under a floor
    /// would score better here and is not done: with the floor off it is worth 19.6 km at this
    /// range, and every headless improvement to it has so far been refused by flight —
    /// <c>docs/MIRV-NEXT.md</c> item -1.</para>
    /// </summary>
    [Fact]
    public void UnderAFloorTheCorrectionIsWhatMisses()
    {
        double correctedFloored = MissMetres(Fly(15.0));
        double uncorrectedFloored = MissMetres(Fly(15.0, off: true));
        double correctedFree = MissMetres(Fly(0.0));
        double uncorrectedFree = MissMetres(Fly(0.0, off: true));

        Out.WriteLine($"floor 15 deg: corrected {correctedFloored / 1000.0:F2} km, "
                      + $"uncorrected {uncorrectedFloored / 1000.0:F3} km");
        Out.WriteLine($"floor off   : corrected {correctedFree / 1000.0:F2} km, "
                      + $"uncorrected {uncorrectedFree / 1000.0:F2} km");

        // The loop earns its keep on the graze and cannot be simply removed.
        Assert.True(uncorrectedFree > 10_000.0,
                    $"the correction should be worth kilometres with no floor; {uncorrectedFree:F0} m");
        Assert.True(correctedFree < 2_000.0);

        // And under a floor it is spending them.
        Assert.True(uncorrectedFloored < 1_000.0,
                    $"a floored shot should need no correction; it missed by {uncorrectedFloored:F0} m");
        Assert.True(correctedFloored > uncorrectedFloored + 3_000.0,
                    $"the correction should be the miss under a floor: {correctedFloored:F0} m "
                    + $"against {uncorrectedFloored:F0} m");
    }

    /// <summary>
    /// The trajectory search is still moving when the correction takes its first readings, and under
    /// a floor it moves by minutes.
    ///
    /// <para>The instant the guidance expects to cut off at is the cheapest thing that says whether
    /// it has an answer yet: everything the prediction departs from is built from it. With no floor
    /// it is settled before the first prediction and never revised by as much as a second; under one
    /// the constrained search walks it across the burn — and the aim correction integrates the
    /// difference as though it were a drag shortfall.</para>
    /// </summary>
    [Fact]
    public void TheSearchIsStillMovingWhenTheCorrectionOpens()
    {
        foreach (double floorDeg in new[] { 0.0, 15.0 })
        {
            List<Loop.Cycle> cycles = Fly(floorDeg).Loop.Cycles;
            Assert.True(cycles.Count > 6);

            double worst = 0.0;
            for (int i = 1; i < 6; i++)
            {
                worst = Math.Max(worst, Math.Abs(cycles[i].CutoffFromLaunch - cycles[i - 1].CutoffFromLaunch));
            }

            Out.WriteLine($"floor {floorDeg,4:F0} deg: cutoff revised by at most {worst,8:F2} s over the "
                          + $"first six cycles; arc {cycles[0].ArrivalDeg:F2} -> {cycles[5].ArrivalDeg:F2} deg, "
                          + $"flight {cycles[0].FlightSeconds:F0} -> {cycles[5].FlightSeconds:F0} s, "
                          + $"shortfall {cycles[0].ShortfallMetres / 1000.0:F2} -> "
                          + $"{cycles[^1].ShortfallMetres / 1000.0:F2} km by cutoff");

            if (floorDeg > 0.0)
            {
                Assert.True(worst > 30.0,
                            $"a constrained search should still be moving; it revised by {worst:F2} s");

                // And the thing being corrected has gone by the time the search settles: what the
                // loop opened on was the search moving, not a shortfall.
                Assert.True(cycles[^1].ShortfallMetres < 3_000.0,
                            $"a steep arrival loses nothing to drag; {cycles[^1].ShortfallMetres:F0} m");
                Assert.True(cycles[0].ShortfallMetres > 5_000.0);
            }
            else
            {
                Assert.True(worst < 2.0,
                            $"an unconstrained search is settled at the first cycle; it revised by {worst:F2} s");

                // Steady, large, and real — which is what a loop can converge against.
                Assert.True(cycles[5].ShortfallMetres > 15_000.0);
            }
        }
    }

    /// <summary>
    /// The rig's mean sphere hides kilometres on the path that ships.
    ///
    /// <para><see cref="AimCorrection"/>'s only observer is <see cref="ImpactPredictor"/>, and on a
    /// smooth planet that observer is noiseless — so every extra cycle of the loop is free averaging
    /// of a clean signal, and any change that lets it run longer scores as a large win. Give the
    /// predictor ground to cross and the same shipped configuration moves by kilometres at ranges
    /// where the smooth rig reports tens of metres.</para>
    ///
    /// <para>That is the standing reason a headless result about this loop is a hypothesis:
    /// <c>docs/MIRV-NEXT.md</c> item -1 has the seven flights, five of which refused a change that
    /// made the loop act more on its own prediction.</para>
    /// </summary>
    [Theory]
    [InlineData(2_000_000.0)]
    [InlineData(3_459_000.0)]
    [InlineData(7_645_000.0)]
    public void GroundUnderTheObserverMovesTheShippedShot(double metres)
    {
        double smooth = MissMetres(Fly(0.0, metres: metres));
        double rough = MissMetres(Fly(0.0, metres: metres, ground: DeorbitShot.RoughGround));

        Out.WriteLine($"{metres / 1000.0,5:F0} km: mean sphere {smooth / 1000.0,7:F2} km, "
                      + $"with relief {rough / 1000.0,7:F2} km");

        Assert.True(rough > smooth + 500.0,
                    $"relief should move the shot; {rough:F0} m against {smooth:F0} m");
    }
}
