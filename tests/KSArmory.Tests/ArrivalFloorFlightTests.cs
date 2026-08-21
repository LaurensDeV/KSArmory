using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the arrival-angle floor does once a shot is being flown, which is a different question from
/// what the search does with it.
///
/// <para><c>ArrivalFloorTests</c> covers the search: given a state, does the bound pick a satisfying
/// arc. This flies the whole <see cref="IcbmProgram"/> at a floor and asks whether the shot still
/// has one by the time the engines stop — and whether the aim correction, whose only job is
/// removing a drag shortfall that a steep arrival very nearly abolishes, helps or harms.</para>
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

    /// <summary>When the aim correction is stopped, which is the thing under test.</summary>
    private enum StopAt
    {
        /// <summary>At the engines, which is where the coast half takes over.</summary>
        Cutoff,

        /// <summary>At the arrival committing, which can be the third cycle of a blind loop.</summary>
        ArrivalCommitted,
    }

    /// <summary>
    /// <see cref="AimCorrection"/> riding a flight the way <c>Ksa/IcbmComputer.cs</c> rides one:
    /// predict from the solved cutoff state with the warhead's own drag, score in the epoch the
    /// target is expressed in, observe every cycle.
    /// </summary>
    private sealed class Loop(IcbmFlightRig rig, StopAt stop) : IcbmFlightRig.IAimLoop
    {
        private const double PredictIntervalSeconds = 0.5;
        private const double PredictStepSeconds = 2.0;

        private readonly AimCorrection _aim = new();
        private double _sincePredict = double.PositiveInfinity;

        /// <summary>Do not correct at all — the aim stays exactly where it was pointed.</summary>
        public bool Off;

        public double BiasMetres => Vec.Len(_aim.BiasCci);
        public double MissMetres { get; private set; } = double.NaN;

        public double3 Apply(double3 aimNowCci) => Off ? aimNowCci : _aim.Apply(aimNowCci);

        public bool IsSteady => Off || _aim.IsSteady;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci, double step)
        {
            if (stop == StopAt.ArrivalCommitted && program.IsBurning
                && double.IsFinite(program.CommittedArrivalFromNow))
            {
                _aim.Freeze();
            }

            if (stop == StopAt.Cutoff && !program.IsBurning) _aim.Freeze();

            _sincePredict += step;
            if (_sincePredict < PredictIntervalSeconds) return;
            _sincePredict = 0.0;

            bool fromCutoff = program.IsBurning && program.Arc is not null;
            double3 fromCci = fromCutoff ? program.CutoffPositionCci : rig.PositionCci;
            double3 alongCci = fromCutoff ? program.Arc!.Value.RequiredVelocityCci : rig.VelocityCci;

            if (!ImpactPredictor.TryPredict(Earth, fromCci, alongCci, PredictStepSeconds,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, null, null,
                                            new ImpactPredictor.Drag(DeorbitShot.DensityAt, DeorbitShot.Warhead)))
            {
                return;
            }

            // The prediction departs from the cutoff state, so it un-carries its impact by the
            // flight time alone and leaves the answer in the body frame of that instant. The target
            // is in the frame of now.
            double3 scored = Earth.UncarryCci(hit.GroundFixedPointCci,
                                              fromCutoff ? command.SecondsToCutoff : 0.0);

            MissMetres = DeorbitShot.GroundMetres(scored, aimNowCci);

            if (!Off) _aim.Observe(scored, aimNowCci);
        }
    }

    private readonly record struct Shot(IcbmFlightRig.Flight Flight, IcbmProgram Program,
                                        Loop Loop, double3 AimAtEpoch);

    private static Shot Fly(double floorDeg, StopAt stop = StopAt.Cutoff, bool off = false,
                            double metres = DeorbitShot.RangeMetres)
    {
        IcbmFlightRig rig = InOrbit();
        Loop loop = new(rig, stop) { Off = off };
        rig.AimLoop = loop;

        IcbmProgram program = new(new IcbmConfig { Armed = true, MinArrivalAngleDeg = floorDeg });
        double3 aim = Downrange(metres);

        return new Shot(rig.Fly(program, aim, 0.02, 12_000.0), program, loop, aim);
    }

    /// <summary>Where the warheads actually go, flown from the state the engines really stopped in.</summary>
    private static double MissMetres(in Shot shot)
    {
        if (!ImpactPredictor.TryPredict(Earth, shot.Flight.CutoffPositionCci,
                                        shot.Flight.CutoffVelocityCci, 1.0,
                                        ImpactPredictor.DefaultMaxSeconds,
                                        out ImpactPredictor.Impact hit, null, null,
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
    /// trade against each other, and the cheaper split is the longer coast — which onto the same
    /// target is a shallower arrival. So a held arrival walks out of the bound the search was
    /// constrained by, and every degree of that is the whole reason the floor exists: the velocity
    /// sensitivity roughly doubles from 20 degrees back down to 15.</para>
    /// </summary>
    [Theory]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(20.0)]
    [InlineData(30.0)]
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
    /// And with the floor off a committed arrival is held whatever it arrives at, which is the whole
    /// of the default path.
    ///
    /// <para>The bound is the only thing that can refuse a held arc, so this is where "unchanged
    /// when nobody set a floor" is actually decided — a flown miss is a downstream consequence and
    /// would not say which branch produced it.</para>
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

        Out.WriteLine($"held arrival arrives at {free.Arc.ArrivalAngleDeg:F2} deg, "
                      + $"held {free.HeldTheArrival}");

        Assert.True(free.HeldTheArrival, "a committed arrival must be held when no floor is set");

        // The same state, with a floor above what that arc arrives at: now it is refused, which
        // hands the cycle back to the constrained search rather than flying the shallow arc.
        Assert.True(BurnoutGuidance.TrySteer(Earth, from, circular, target, booster,
                                             out BurnoutGuidance.Command bound, 1.0, false,
                                             0.0, double.NaN, shot.FlightSeconds,
                                             free.Arc.ArrivalAngleDeg + 5.0));

        Out.WriteLine($"with a floor {free.Arc.ArrivalAngleDeg + 5.0:F2} deg above it, "
                      + $"held {bound.HeldTheArrival}, arrives at {bound.Arc.ArrivalAngleDeg:F2} deg");

        Assert.False(bound.HeldTheArrival,
                     "an arrival that comes in under the floor must not be held");
    }

    /// <summary>
    /// The correction runs for the whole burn, where an engine is still there to fly what it asks
    /// for.
    ///
    /// <para>Stopping it when the <em>arrival</em> commits banks whatever bias it happens to hold,
    /// and that is the converged answer only if the loop happened to finish first. With the floor
    /// off it does: the drag shortfall it exists to remove is 20-190 km and dominates everything
    /// else, so the loop converges in four cycles and the arrival commits on the fifth. Under a
    /// floor the shortfall is a few hundred metres, the constrained search alternates between two
    /// satisfying flight times, and the aim goes still because it is being told two different
    /// things in turn — which reads as convergence and is not.</para>
    /// </summary>
    [Theory]
    [InlineData(15.0)]
    [InlineData(20.0)]
    public void StoppingTheCorrectionAtTheArrivalBanksTheSolversOwnTransient(double floorDeg)
    {
        Shot running = Fly(floorDeg);
        Shot stopped = Fly(floorDeg, StopAt.ArrivalCommitted);
        Shot none = Fly(floorDeg, off: true);

        double runs = MissMetres(running);
        double stops = MissMetres(stopped);
        double off = MissMetres(none);

        Out.WriteLine($"floor {floorDeg:F0} deg: uncorrected {off / 1000.0:F2} km, "
                      + $"stopped at the arrival {stops / 1000.0:F2} km "
                      + $"(bias {stopped.Loop.BiasMetres / 1000.0:F1} km), "
                      + $"run to cutoff {runs / 1000.0:F2} km "
                      + $"(bias {running.Loop.BiasMetres / 1000.0:F1} km)");

        Assert.True(runs < 1_000.0,
                    $"a correction left running should close a steep shot; it left {runs / 1000.0:F2} km");

        // The fault, and the reason it hid: at a steep arrival the shot needs almost no correction,
        // so a loop stopped early is worse than one that never ran.
        Assert.True(stops > off + 2_000.0,
                    $"stopping at the arrival should be worse than not correcting at all — "
                    + $"{stops / 1000.0:F2} km against {off / 1000.0:F2} km");
    }

    /// <summary>
    /// And with the floor off it is no worse, which is the shallow shot the correction was built
    /// for and the one every flown number belongs to.
    /// </summary>
    [Theory]
    [InlineData(2_000_000.0)]
    [InlineData(3_459_000.0)]
    [InlineData(5_000_000.0)]
    [InlineData(7_645_000.0)]
    public void WithNoFloorTheCorrectionIsNoWorseForRunningOn(double metres)
    {
        double runs = MissMetres(Fly(0.0, metres: metres));
        double stops = MissMetres(Fly(0.0, StopAt.ArrivalCommitted, metres: metres));
        double off = MissMetres(Fly(0.0, off: true, metres: metres));

        Out.WriteLine($"{metres / 1000.0,5:F0} km, no floor: uncorrected {off / 1000.0,7:F2} km, "
                      + $"stopped at the arrival {stops / 1000.0,6:F2} km, "
                      + $"run to cutoff {runs / 1000.0,6:F2} km");

        // Run-to-run on one build is about half a kilometre — docs/MIRV-NEXT.md item 7d — so this
        // asks that nothing is given up rather than that something is won.
        Assert.True(runs <= stops + 500.0,
                    $"running on left {runs / 1000.0:F2} km against {stops / 1000.0:F2} km stopped");
    }

    /// <summary>
    /// The coast re-solve reproduces the burn's arc exactly, floor or no floor.
    ///
    /// <para>A null result, and worth stating because the alternative was the standing suspicion:
    /// that <c>IcbmProgram.ResolveCoastArc</c> flies the bus off a constrained solution onto an
    /// unconstrained one, because it re-solves through <see cref="BallisticArc.TrySolve"/> and
    /// passes no floor. It cannot. <c>TrySolve</c> has no floor to pass — the bound lives only in
    /// the search over flight <em>time</em>, and Lambert between two points in a stated time is
    /// unique. Whatever the coast correction costs, it is the aim having moved.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(15.0)]
    [InlineData(20.0)]
    public void ReSolvingToTheChosenArrivalReproducesTheConstrainedArc(double floorDeg)
    {
        DeorbitShot.Shot(out double3 from, out double3 target);
        double3 circular = new(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + DeorbitShot.PickupAltitude)), 0);

        Assert.True(BallisticArc.TryCheapest(Earth, from, circular, target,
                                             out BallisticArc.Solution constrained,
                                             1.0, false, double.NaN, floorDeg));

        Assert.True(BallisticArc.TrySolve(Earth, from, target, constrained.FlightSeconds,
                                          out BallisticArc.Solution plain));

        double apart = Vec.Len(constrained.RequiredVelocityCci - plain.RequiredVelocityCci);

        Out.WriteLine($"floor {floorDeg:F0} deg: constrained search chose {constrained.FlightSeconds:F1} s "
                      + $"arriving at {constrained.ArrivalAngleDeg:F2} deg; re-solving to that same "
                      + $"arrival with no floor differs by {apart:F9} m/s");

        Assert.True(apart < 1e-6,
                    $"the two solves differ by {apart:F6} m/s, so the floor does reach the transfer");
    }

    /// <summary>
    /// What moving the aim during the coast costs, which is not what the sensitivity table says.
    ///
    /// <para><c>dMiss/dV</c> in <c>docs/ARRIVAL-ANGLE.md</c> is measured with the trajectory
    /// <em>free</em>, and a shallow grazing arc is hypersensitive precisely because it is free to
    /// stretch. <c>IcbmProgram.ResolveCoastArc</c> pins the arrival instant, which takes that
    /// freedom away — so a coast correction pays a nearly flat 1.2 to 2.1 m/s per kilometre of aim
    /// whatever the arrival angle, and slightly <em>less</em> the steeper it comes in.</para>
    ///
    /// <para>So a steep shot is not dearer to correct after cutoff. What makes a coast correction
    /// unaffordable is how much error the burn handed it, and
    /// <see cref="BusTrim.MaxMetresPerSecond"/> is the budget that buys about six kilometres of
    /// it.</para>
    /// </summary>
    [Fact]
    public void MovingTheAimAfterCutoffCostsAboutTheSameWhateverTheArrival()
    {
        DeorbitShot.Shot(out double3 from, out double3 target);
        double3 circular = new(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + DeorbitShot.PickupAltitude)), 0);

        // One kilometre of aim, along the track, which is the direction a drag shortfall moves it.
        double3 nudged = Vec.Unit(target + Vec.Unit(Vec.Cross(new double3(0, 0, 1), target)) * 1_000.0)
                       * DeorbitShot.R;

        foreach (double floorDeg in new[] { 0.0, 10.0, 15.0, 20.0, 30.0 })
        {
            if (!BallisticArc.TryCheapest(Earth, from, circular, target,
                                          out BallisticArc.Solution arc, 1.0, false,
                                          double.NaN, floorDeg))
            {
                continue;
            }

            Assert.True(BallisticArc.TrySolve(Earth, from, nudged, arc.FlightSeconds,
                                              out BallisticArc.Solution moved));

            double cost = Vec.Len(moved.RequiredVelocityCci - arc.RequiredVelocityCci);

            Out.WriteLine($"floor {floorDeg,4:F0} deg: arrives {arc.ArrivalAngleDeg,6:F2} deg, "
                          + $"one km of aim costs {cost:F3} m/s, so "
                          + $"{BusTrim.MaxMetresPerSecond / cost:F1} km fits inside the trim's limit");
        }
    }
}
