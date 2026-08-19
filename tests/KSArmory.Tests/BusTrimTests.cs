using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the post-boost vehicle's own thrusters buy, against the two things that move it off a
/// solution the main burn arrived at exactly: the frame the cutoff landed on, and the decoupler
/// that drops the spent stack.
///
/// <para>Scored where it matters — how far from the aim point the bus would arrive if it stopped
/// here and fell — rather than on the loop's own intermediate numbers. A trim that reports zero
/// velocity to gain and lands somewhere else has proved nothing.</para>
/// </summary>
public class BusTrimTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>
    /// A bus in flight, with a thruster set and nothing else. Its control frame is fixed, because
    /// the trim never turns it: the whole reason it resolves onto the vehicle's own axes rather
    /// than pointing at the answer is that the release line is already decided by then.
    /// </summary>
    private sealed class Bus
    {
        public double3 PositionCci;
        public double3 VelocityCci;

        public double3 NoseCci;
        public double3 RightCci;
        public double3 DownCci;

        /// <summary>What the axial pair can do. Every thruster set has one.</summary>
        public double AxialAcceleration = 3.0;

        /// <summary>What the lateral jets can do. Zero is the layout the shipped bus has.</summary>
        public double LateralAcceleration;

        public void Step(TrimAxes fire, double seconds)
        {
            double3 thrust = Push(fire, TrimAxes.Forward, NoseCci, AxialAcceleration)
                           + Push(fire, TrimAxes.Backward, -NoseCci, AxialAcceleration)
                           + Push(fire, TrimAxes.Right, RightCci, LateralAcceleration)
                           + Push(fire, TrimAxes.Left, -RightCci, LateralAcceleration)
                           + Push(fire, TrimAxes.Down, DownCci, LateralAcceleration)
                           + Push(fire, TrimAxes.Up, -DownCci, LateralAcceleration);

            double3 gravity = Earth.GravityCci(PositionCci);

            VelocityCci += (gravity + thrust) * seconds;
            PositionCci += VelocityCci * seconds;
        }

        private static double3 Push(TrimAxes fire, TrimAxes direction, double3 along, double magnitude)
            => (fire & direction) != TrimAxes.None ? Vec.Unit(along) * magnitude : Vec.Zero;
    }

    /// <summary>A deorbit from 200 km arriving about 2,700 km downrange — the flown shot.</summary>
    private static BallisticArc.Solution Deorbit(out double3 fromCci, out double3 aimAtEpoch)
    {
        fromCci = new double3(R + 200_000.0, 0, 0);

        double range = 2_700_000.0;
        aimAtEpoch = new double3(R * Math.Cos(range / R), R * Math.Sin(range / R), 0);

        double3 circular = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

        Assert.True(BallisticArc.TryCheapest(Earth, fromCci, circular, aimAtEpoch,
                                             out BallisticArc.Solution arc));
        return arc;
    }

    /// <summary>
    /// How far from the aim point a free fall from here comes down.
    ///
    /// <para>Scored on the ground rather than at the committed instant, because that is what the
    /// miss is: an along-track error is mostly a change in <em>when</em> the arc arrives, so a
    /// position sampled at a fixed time under-reads it by most of an order of magnitude.</para>
    /// </summary>
    private static double MissMetres(double3 positionCci, double3 velocityCci,
                                     double3 aimAtEpoch, double sinceEpoch)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, positionCci, velocityCci, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact hit),
                    "it never came down");

        return R * Vec.AngleBetween(hit.GroundFixedPointCci, Earth.CarryCci(aimAtEpoch, sinceEpoch));
    }

    /// <summary>
    /// Run the loop against a bus until it stops. Returns how long it took and how far off the
    /// arrival the bus is left.
    /// </summary>
    private (double Seconds, double Miss, TrimCommand Last) Trim(
        BusTrim trim, Bus bus, double3 aimAtEpoch, double arrival, double step,
        double maxSeconds = 300.0, bool log = false)
    {
        double elapsed = 0.0;
        TrimCommand last = default;

        while (elapsed < maxSeconds)
        {
            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                Earth.CarryCci(aimAtEpoch, elapsed), arrival - elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci));

            if (last.Done) break;

            bus.Step(last.Fire, step);
            elapsed += step;
        }

        if (log) Out.WriteLine($"{elapsed:F1} s, {last.Said}, measured {last.Acceleration:F3} m/s2");

        return (elapsed, MissMetres(bus.PositionCci, bus.VelocityCci, aimAtEpoch, elapsed), last);
    }

    /// <summary>
    /// The whole job: a decoupler's shove is kilometres of miss, and the bus's own thrusters take
    /// it back out.
    ///
    /// <para>Flown as 163 m for the one warhead that left before the split and 3.1–4.1 km for the
    /// five that left after it. The shove here is the same 1.1 m/s, along the bus's own axis, which
    /// is where a decoupler on the mounting joint puts it.</para>
    /// </summary>
    [Fact]
    public void TheDecouplerShoveIsTakenBackOutBeforeAnythingLeaves()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);

        // The bus is nose-first along the line the burn ended on, which is what it holds through
        // the coast for the warheads to leave along.
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        Bus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + nose * 1.1,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        double untrimmed = MissMetres(bus.PositionCci, bus.VelocityCci, aimAtEpoch, 0.0);

        BusTrim trim = new();
        trim.Begin();

        (double seconds, double miss, TrimCommand last) =
            Trim(trim, bus, aimAtEpoch, arc.FlightSeconds, 1.0 / 60.0, log: true);

        Out.WriteLine($"untrimmed {untrimmed / 1000.0:F1} km -> trimmed {miss:F0} m in {seconds:F1} s");

        Assert.True(untrimmed > 2_000.0, $"the shove should be kilometres, was {untrimmed:F0} m");
        Assert.True(last.Done && !trim.GaveUp, last.Said);
        Assert.True(miss < untrimmed / 20.0, $"trimmed to {miss:F0} m against {untrimmed:F0} m untrimmed");
    }

    /// <summary>
    /// A bus already on its solution fires nothing. The trim is on by default, so a launcher with
    /// no decoupler and a clean cutoff has to pay nothing for it.
    /// </summary>
    [Fact]
    public void ABusAlreadyOnItsSolutionFiresNothing()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        Bus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        BusTrim trim = new();
        trim.Begin();

        TrimAxes everFired = TrimAxes.None;
        double elapsed = 0.0;

        for (int i = 0; i < 2000; i++)
        {
            TrimCommand c = trim.Update(1.0 / 60.0, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                Earth.CarryCci(aimAtEpoch, elapsed), arc.FlightSeconds - elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci));

            everFired |= c.Fire;
            if (c.Done) break;

            bus.Step(c.Fire, 1.0 / 60.0);
            elapsed += 1.0 / 60.0;
        }

        Assert.True(trim.Done, "it never finished");
        Assert.False(trim.GaveUp, trim.Said);
        Assert.Equal(TrimAxes.None, everFired);
    }

    /// <summary>
    /// It may not call itself finished on the state before the shove.
    ///
    /// <para>The split is deferred through the engine's input buffer, so for the first frames after
    /// it is asked for the bus is still attached and exactly on its solution. A trim that stops
    /// there stops on a problem that has not arrived, and nothing afterwards looks again — which is
    /// the whole of the ordering bug this replaced.</para>
    /// </summary>
    [Fact]
    public void ItWillNotFinishBeforeTheShoveCouldHaveArrived()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        Bus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        BusTrim trim = new();
        trim.Begin();

        double step = 1.0 / 60.0;
        double elapsed = 0.0;
        bool shoved = false;

        for (int i = 0; i < 4000; i++)
        {
            TrimCommand c = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                Earth.CarryCci(aimAtEpoch, elapsed), arc.FlightSeconds - elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci));

            Assert.False(c.Done && !shoved, $"finished at {elapsed:F2} s, before the split landed");

            if (c.Done) break;

            bus.Step(c.Fire, step);
            elapsed += step;

            // The decoupler, half a second late — well inside the settle and well outside a frame.
            if (!shoved && elapsed >= 0.5)
            {
                bus.VelocityCci += nose * 1.1;
                shoved = true;
            }
        }

        Assert.True(shoved && trim.Done && !trim.GaveUp, trim.Said);

        double miss = MissMetres(bus.PositionCci, bus.VelocityCci, aimAtEpoch, elapsed);

        Out.WriteLine($"shove at 0.5 s, trimmed to {miss:F0} m by {elapsed:F1} s");
        Assert.True(miss < 300.0, $"{miss:F0} m left after a shove that arrived during the settle");
    }

    /// <summary>
    /// A bus with only an axial pair still gets the axial error out, and says what it could not
    /// reach rather than holding its warheads over it.
    ///
    /// <para>Which is the shipped layout: four clusters of four, arranged for pitch, yaw, roll and
    /// axial thrust, with no lateral translation at all. A loop that assumed three-axis authority
    /// would push at nothing for ever.</para>
    /// </summary>
    [Fact]
    public void ADirectionThatMovesNothingIsStruckOffRatherThanWaitedOn()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 right = Vec.Unit(Vec.Cross(fromCci, nose));

        Bus bus = new()
        {
            PositionCci = fromCci,

            // Lateral first and larger, so the loop reaches for the direction it has no authority
            // on before the one it does. Picking the largest component is what makes that order.
            VelocityCci = arc.RequiredVelocityCci + right * 1.5 + nose * 0.8,
            NoseCci = nose,
            RightCci = right,
            DownCci = -Vec.Unit(fromCci),
            LateralAcceleration = 0.0,
        };

        BusTrim trim = new();
        trim.Begin();

        (double seconds, _, TrimCommand last) =
            Trim(trim, bus, aimAtEpoch, arc.FlightSeconds, 1.0 / 60.0, log: true);

        // Against the arc from where the bus ended up, not the one solved from where it started:
        // a minute of coast puts those hundreds of kilometres apart, and the required velocity with
        // them.
        Assert.True(BallisticArc.TrySolve(Earth, bus.PositionCci,
                                          Earth.CarryCci(aimAtEpoch, seconds),
                                          arc.FlightSeconds - seconds,
                                          out BallisticArc.Solution now));

        double3 left = now.VelocityToGain(bus.VelocityCci);
        double alongNose = Math.Abs(Vec.Dot(left, nose));

        Out.WriteLine($"{seconds:F1} s, {Vec.Len(left):F3} m/s left, {alongNose:F3} of it along the nose");

        Assert.True(last.Done, "it never stopped");
        Assert.True(trim.GaveUp, "it should have said it could not finish the job");
        Assert.True(seconds < BusTrim.MaxSeconds, $"it took the whole budget: {seconds:F0} s");
        Assert.True(alongNose < 0.05, $"{alongNose:F3} m/s left on the axis it could actually push");
    }

    /// <summary>
    /// A bus with no thrusters at all releases anyway. Warheads still aboard when the release
    /// altitude closes are no shot at all, which is worse than an untrimmed salvo.
    /// </summary>
    [Fact]
    public void ABusWithNothingToFireWithGivesUpRatherThanHoldingItsWarheads()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        Bus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + nose * 1.1,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
            AxialAcceleration = 0.0,
            LateralAcceleration = 0.0,
        };

        BusTrim trim = new();
        trim.Begin();

        (double seconds, _, TrimCommand last) =
            Trim(trim, bus, aimAtEpoch, arc.FlightSeconds, 1.0 / 60.0, log: true);

        Assert.True(last.Done && trim.GaveUp, last.Said);
        Assert.True(seconds < BusTrim.MaxSeconds, $"it took the whole budget: {seconds:F0} s");
        Assert.Contains("m/s left on the bus", last.Said);
    }

    /// <summary>
    /// The trim and the aim correction must not both be running, and this is what happens when
    /// they are.
    ///
    /// <para>Both drive the same vehicle and both read the same prediction, so the bias absorbs a
    /// displacement the trim itself put there, the trim reads the moved aim as a larger error, and
    /// the pair wind each other up. Flown: jumps every 0.51 s — exactly the prediction interval —
    /// each bigger than the last, from 0.28 m/s to 139 m/s in ten seconds, on a shot that had been
    /// 0.1 km from the target at cutoff.</para>
    ///
    /// <para>Runs the loop both ways off one setup. <b>It reproduces the coupling, not the flown
    /// divergence</b> — the loop gain depends on which axis the bus can push and how sensitive the
    /// arc is to it, and in flight the trim happened to be firing radially, which is twice as
    /// expensive as the along-track axis this rig picks. So the assertion is on the mechanism
    /// rather than the magnitude: the correction must not absorb a displacement the trim put there.
    /// The log is the evidence for how far that goes when it does.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheAimCorrectionMustSitOutWhileTheTrimIsFiring(bool observeWhileTrimming)
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        Bus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),

            // The bus's own, measured in flight rather than assumed: the thrusters logged
            // 0.9-1.1 m/s2, and the runaway outruns exactly that.
            AxialAcceleration = 1.0,
            LateralAcceleration = 1.0,
        };

        // Warheads are thrown off the tubes rather than dropped, so the aim the guidance solved to
        // is displaced to absorb that. It is what gives the correction something to hold, and
        // therefore something to wind up.
        double3 kick = Vec.Unit(Vec.Cross(nose, fromCci)) * 2.0;

        AimCorrection aim = new();

        // The state the trim actually starts from, which is not a fresh one: by cutoff the
        // correction has been running for the whole burn and the guidance has been solving to the
        // aim it produced. Starting from zero bias instead lets the first observation do work that
        // is genuinely owed, and the runaway never gets going -- the setup hides the fault.
        for (int i = 0; i < 40; i++)
        {
            Assert.True(BallisticArc.TrySolve(Earth, fromCci, aim.Apply(aimAtEpoch),
                                              arc.FlightSeconds, out BallisticArc.Solution onAim));

            bus.VelocityCci = onAim.RequiredVelocityCci;

            Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, bus.VelocityCci + kick, 2.0,
                                                   20_000.0, out ImpactPredictor.Impact settled));

            aim.Observe(settled.GroundFixedPointCci, aimAtEpoch);
        }

        Out.WriteLine($"bias converged to {Vec.Len(aim.BiasCci) / 1000.0:F1} km before the split");

        // And now the decoupler, which is the only thing the trim is there for.
        bus.VelocityCci += nose * 1.1;

        double3 biasAtCutoff = aim.BiasCci;

        BusTrim trim = new();
        trim.Begin();

        double step = 1.0 / 60.0;
        double sincePredict = 0.0;
        double elapsed = 0.0;
        double peak = 0.0;
        TrimCommand last = default;

        for (int i = 0; i < 3000 && !last.Done; i++)
        {
            double3 trueAim = Earth.CarryCci(aimAtEpoch, elapsed);

            sincePredict += step;
            if (sincePredict >= 0.5)
            {
                sincePredict = 0.0;

                // The same observer the mod has: fly the warhead from the bus's live state, and
                // score where it lands against the target.
                if ((observeWhileTrimming || last.Done)
                    && ImpactPredictor.TryPredict(Earth, bus.PositionCci, bus.VelocityCci + kick,
                                                  2.0, 20_000.0, out ImpactPredictor.Impact hit))
                {
                    aim.Observe(hit.GroundFixedPointCci, trueAim);
                }
            }

            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci, aim.Apply(trueAim),
                arc.FlightSeconds - elapsed, bus.NoseCci, bus.RightCci, bus.DownCci));

            peak = Math.Max(peak, double.IsFinite(last.ToGainMetresPerSecond)
                                      ? last.ToGainMetresPerSecond : 0.0);

            if (last.Done) break;

            bus.Step(last.Fire, step);
            elapsed += step;
        }

        double moved = Vec.Len(aim.BiasCci - biasAtCutoff);

        Out.WriteLine($"observing={observeWhileTrimming}: peaked at {peak:F2} m/s after {elapsed:F1} s"
                      + $", bias moved {moved:F0} m -- {last.Said}");

        if (observeWhileTrimming)
        {
            // The coupling has to still be here, or the case below is measuring nothing. Both
            // halves of it: the correction takes in the trim's own displacement, and the trim then
            // burns at what that put in front of it.
            Assert.True(moved > 100.0, $"the correction should have absorbed the trim's own work; moved {moved:F0} m");
            Assert.True(peak > 1.1 * 1.5, $"and the trim should have chased it; peaked at {peak:F2} m/s");
            return;
        }

        Assert.True(last.Done && !trim.GaveUp, last.Said);

        // The whole fix, stated exactly: the aim the trim is solving to does not move under it.
        Assert.Equal(0.0, moved, 6);
        Assert.True(peak <= 1.2, $"nothing should have grown it past the 1.1 m/s shove; peaked at {peak:F2} m/s");
    }

    /// <summary>
    /// Without a committed arrival there is nothing to trim towards, and it refuses rather than
    /// picking one.
    ///
    /// <para>The cheapest arc from any state converges on the arc that state is already flying, so
    /// a trim that chose its own arrival would decide the bus was exactly where it should be and
    /// null nothing — reporting success at a shot it never touched.</para>
    /// </summary>
    [Fact]
    public void WithNoCommittedArrivalItRefusesRatherThanChoosingOne()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        BusTrim trim = new();
        trim.Begin();

        TrimCommand c = trim.Update(1.0 / 60.0, new TrimSituation(
            Earth, fromCci, arc.RequiredVelocityCci + nose * 1.1, aimAtEpoch,
            double.NaN, nose, Vec.Unit(Vec.Cross(fromCci, nose)), -Vec.Unit(fromCci)));

        Assert.True(c.Done && trim.GaveUp, c.Said);
        Assert.Equal(TrimAxes.None, c.Fire);
    }

    /// <summary>
    /// The residual is a timing limit, not a control error: it is what one step of the thrusters
    /// adds, so a longer step leaves proportionally more behind.
    ///
    /// <para>Which is why the trim registers with the warp policy alongside a burn. It is the same
    /// arithmetic that makes a guided cutoff unflyable at high timewarp, against a smaller number.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.3)]
    public void TheResidualIsWhatOneStepOfTheThrustersAdds(double step)
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        Bus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + nose * 1.1,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        BusTrim trim = new();
        trim.Begin();

        (_, _, TrimCommand last) = Trim(trim, bus, aimAtEpoch, arc.FlightSeconds, step, log: true);

        // The loop's own last solve, which is the residual against the arc from where the bus
        // actually is. Differencing against the velocity solved at the start would measure the
        // coast as well as the trim.
        double left = last.ToGainMetresPerSecond;
        double quantum = bus.AxialAcceleration * step;

        Out.WriteLine($"step {step * 1000.0:F0} ms: {left:F3} m/s left, one step is {quantum:F3} m/s");

        Assert.True(last.Done && !trim.GaveUp, last.Said);
        Assert.True(left <= Math.Max(BusTrim.SettledMetresPerSecond, 0.5 * quantum),
                    $"{left:F3} m/s left against a {quantum:F3} m/s step");
    }
}
