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
        BusTrim trim, Bus bus, double3 aimAtEpoch, double3 referenceFrom, double3 referenceVelocity,
        double step, double maxSeconds = 300.0, bool log = false, double since = 0.0)
    {
        double elapsed = since;
        TrimCommand last = default;

        while (elapsed - since < maxSeconds)
        {
            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                referenceFrom, referenceVelocity, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci));

            if (last.Done) break;

            bus.Step(last.Fire, step);
            elapsed += step;
        }

        if (log) Out.WriteLine($"{elapsed - since:F1} s, {last.Said}, measured {last.Acceleration:F3} m/s2");

        return (elapsed - since, MissMetres(bus.PositionCci, bus.VelocityCci, aimAtEpoch, elapsed), last);
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
            Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, 1.0 / 60.0, log: true);

        Out.WriteLine($"untrimmed {untrimmed / 1000.0:F1} km -> trimmed {miss:F0} m in {seconds:F1} s");

        Assert.True(untrimmed > 2_000.0, $"the shove should be kilometres, was {untrimmed:F0} m");
        Assert.True(last.Done && !trim.GaveUp, last.Said);
        Assert.True(miss < untrimmed / 20.0, $"trimmed to {miss:F0} m against {untrimmed:F0} m untrimmed");
    }

    /// <summary>
    /// Held, it still solves — and that is the whole reason the flag exists.
    ///
    /// <para>The trim waits for the bus to coast clear of the stack it dropped, because nulling the
    /// decoupler's shove is nulling the separation. A loop that only starts looking once it is
    /// allowed to act cannot tell an error that arrived with the separation from one that grew
    /// during the wait, and those are different faults with different fixes. Flown: 203.83 m/s at
    /// the moment it was released, against 1.23 m/s the flight before when it was not held at all.
    /// </para>
    /// </summary>
    [Fact]
    public void AHeldTrimStillSolvesAndStillFiresNothing()
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

        double step = 1.0 / 60.0;
        double elapsed = 0.0;

        // Far longer than SettleSeconds and than the stall clocks, so anything that ran while held
        // would have ended it.
        for (int i = 0; i < 3000; i++)
        {
            TrimCommand held = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                fromCci, arc.RequiredVelocityCci, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci, MayFire: false));

            Assert.Equal(TrimAxes.None, held.Fire);
            Assert.False(held.Done, held.Said);

            // Still the shove, not zero and not hundreds. A band rather than a value because the
            // rig integrates by Euler and wanders a few centimetres a second over a minute; what
            // is being pinned is that a held trim reports the real number the whole time.
            Assert.InRange(held.ToGainMetresPerSecond, 0.5, 2.0);

            bus.Step(TrimAxes.None, step);
            elapsed += step;
        }

        // And the moment it is let go it works, having spent none of its budget waiting. The
        // arrival keeps counting from where the hold left it — handing back the one latched at
        // cutoff would be 50 s of arrival error, which on this arc is over a kilometre a second.
        (double seconds, _, TrimCommand last) =
            Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, step, log: true, since: elapsed);

        Assert.True(last.Done && !trim.GaveUp, last.Said);

        // What it owed when it was released, which is the number the log prints beside what it
        // owed at the split.
        Assert.InRange(trim.AtReleaseMetresPerSecond, 0.5, 2.0);
        Assert.True(seconds < 5.0, $"the wait should have cost it no budget; took {seconds:F1} s");
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
                fromCci, arc.RequiredVelocityCci, elapsed,
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
                fromCci, arc.RequiredVelocityCci, elapsed,
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
            Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, 1.0 / 60.0, log: true);

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
            Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, 1.0 / 60.0, log: true);

        Assert.True(last.Done && trim.GaveUp, last.Said);
        Assert.True(seconds < BusTrim.MaxSeconds, $"it took the whole budget: {seconds:F0} s");
        Assert.Contains("m/s left on the bus", last.Said);
    }

    /// <summary>
    /// The answer does not depend on when the trim is asked, and that is the whole change.
    ///
    /// <para>A trim built on a re-solved transfer is only as good as the arrival time and aim point
    /// it is parameterised by, and on a deorbit it demands about twenty metres a second more for
    /// every second the arrival is out. Flown: a bus that owed 0.21 m/s at the split owed 228.97
    /// after coasting 48 s clear of its spent stack, pushed by nothing at all. Nulling against the
    /// trajectory the guidance actually flew to has no such parameter.</para>
    ///
    /// <para>The aim does not appear in <see cref="TrimSituation"/> at all any more, so the loop
    /// that once wound the two together is gone by construction rather than by rule.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(60.0)]
    [InlineData(300.0)]
    public void WhatItOwesDoesNotDependOnWhenItIsAsked(double delay)
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        // The shove, applied at the split and never touched again.
        double3 shoved = arc.RequiredVelocityCci + nose * 1.1;

        // Coast the shoved bus, in closed form so the rig's own integrator cannot muddy it.
        Assert.True(Kepler.TryCoast(Mu, fromCci, shoved, delay, out double3 p, out double3 v));

        BusTrim trim = new();
        trim.Begin();

        TrimCommand c = trim.Update(1.0 / 60.0, new TrimSituation(
            Earth, p, v, fromCci, arc.RequiredVelocityCci, delay,
            nose, Vec.Unit(Vec.Cross(fromCci, nose)), -Vec.Unit(fromCci), MayFire: false));

        Out.WriteLine($"asked {delay,5:F0} s after cutoff -> owes {c.ToGainMetresPerSecond:F4} m/s");

        // Still the shove five minutes later. It decays a few per cent rather than holding exactly,
        // because the shoved vehicle is on a different conic and the two drift apart — which is the
        // real answer rather than an error in it. The old formulation asked for hundreds.
        Assert.InRange(c.ToGainMetresPerSecond, 1.0, 1.15);
    }

    /// <summary>
    /// Without the trajectory the burn was flown to there is nothing to trim towards, and it
    /// refuses rather than inventing one.
    ///
    /// <para>Anything it could invent would be a transfer from wherever the vehicle happens to be,
    /// which is the trajectory it is already on — so it would decide the bus was exactly where it
    /// should be and null nothing, reporting success at a shot it never touched.</para>
    /// </summary>
    [Fact]
    public void WithNoCutoffSolutionItRefusesRatherThanInventingOne()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        BusTrim trim = new();
        trim.Begin();

        TrimCommand c = trim.Update(1.0 / 60.0, new TrimSituation(
            Earth, fromCci, arc.RequiredVelocityCci + nose * 1.1,
            fromCci, Vec.Zero, 0.0,
            nose, Vec.Unit(Vec.Cross(fromCci, nose)), -Vec.Unit(fromCci)));

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

        (_, _, TrimCommand last) = Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, step, log: true);

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
