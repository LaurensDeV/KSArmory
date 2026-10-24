using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Scored on where the bus would land if it stopped here and fell, not on the loop's own numbers:
/// a trim that reports zero velocity to gain and lands somewhere else has proved nothing.
/// </summary>
public class BusTrimTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

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
    /// Scored on the ground rather than at a fixed instant: an along-track error is mostly a change
    /// in <em>when</em> the arc arrives, so a fixed-time sample under-reads it by nearly tenfold.
    /// </summary>
    private static double MissMetres(double3 positionCci, double3 velocityCci,
                                     double3 aimAtEpoch, double sinceEpoch)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, positionCci, velocityCci, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact hit),
                    "it never came down");

        return R * Vec.AngleBetween(hit.GroundFixedPointCci, Earth.CarryCci(aimAtEpoch, sinceEpoch));
    }

    private (double Seconds, double Miss, TrimCommand Last) Trim(
        BusTrim trim, TrimBus bus, double3 aimAtEpoch, double3 referenceFrom, double3 referenceVelocity,
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

            bus.Step(Earth, last.Fire, step);
            elapsed += step;
        }

        if (log) Out.WriteLine($"{elapsed - since:F1} s, {last.Said}, measured {last.Acceleration:F3} m/s2");

        return (elapsed - since, MissMetres(bus.PositionCci, bus.VelocityCci, aimAtEpoch, elapsed), last);
    }

    /// <summary>
    /// The shove is 1.1 m/s along the bus's own axis, where a decoupler on the mounting joint puts
    /// it; flown untrimmed, 163 m before the split and 3.1–4.1 km after it.
    /// </summary>
    [Fact]
    public void TheDecouplerShoveIsTakenBackOutBeforeAnythingLeaves()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);

        // The bus is nose-first along the line the burn ended on, which is what it holds through
        // the coast for the warheads to leave along.
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
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
    /// A trim that only looked once allowed to fire could not tell an error that arrived with the
    /// separation from one that grew during the clearance wait.
    /// </summary>
    [Fact]
    public void AHeldTrimStillSolvesAndStillFiresNothing()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
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

            bus.Step(Earth, TrimAxes.None, step);
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
    /// The trim is on by default, so a launcher with no decoupler and a clean cutoff has to pay
    /// nothing for it.
    /// </summary>
    [Fact]
    public void ABusAlreadyOnItsSolutionFiresNothing()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
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

            bus.Step(Earth, c.Fire, 1.0 / 60.0);
            elapsed += 1.0 / 60.0;
        }

        Assert.True(trim.Done, "it never finished");
        Assert.False(trim.GaveUp, trim.Said);
        Assert.Equal(TrimAxes.None, everFired);
    }

    /// <summary>
    /// The split is deferred through the engine's input buffer, so for the first frames the bus is
    /// still attached and exactly on its solution; a trim that stops there never looks again.
    /// </summary>
    [Fact]
    public void ItWillNotFinishBeforeTheShoveCouldHaveArrived()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
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

            bus.Step(Earth, c.Fire, step);
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
    /// Not the shipped layout (<see cref="BusAuthorityTests"/>): an axial-only bus is what exercises
    /// striking a direction off, where a loop assuming three-axis authority would push for ever.
    /// </summary>
    [Fact]
    public void ADirectionThatMovesNothingIsStruckOffRatherThanWaitedOn()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 right = Vec.Unit(Vec.Cross(fromCci, nose));

        TrimBus bus = new()
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

        TrimBus bus = new()
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
    /// A re-solved transfer demands about 20 m/s more per second the arrival is out on a deorbit;
    /// nulling against the flown trajectory has no such parameter. docs/ICBM-GUIDANCE.md has the flight.
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

        // It decays a few per cent rather than holding exactly, because the shoved vehicle is on a
        // different conic and the two drift apart — the real answer rather than an error in it.
        Assert.InRange(c.ToGainMetresPerSecond, 1.0, 1.15);
    }

    /// <summary>
    /// Anything it invented would be a transfer from where the vehicle is, the trajectory it is
    /// already on, so it would null nothing and report success.
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
    /// A timing limit, not a control error — the same arithmetic that makes a guided cutoff
    /// unflyable at high timewarp, against a smaller number.
    /// </summary>
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.3)]
    public void TheResidualIsWhatOneStepOfTheThrustersAdds(double step)
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
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

    /// <summary>
    /// A correction pass re-arms the trim onto a moved arc, and a bus dry at the release cannot null
    /// the separation. <see cref="PostBoostAim.MaxTrimMetresPerSecond"/> reads this total.
    /// </summary>
    [Fact]
    public void WhatTheThrustersSpendIsCountedAcrossPassesRatherThanPerNull()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + nose * 1.1,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        BusTrim trim = new();
        trim.Begin();

        (double firstTook, _, _) =
            Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, 1.0 / 60.0);

        double afterFirst = trim.SpentMetresPerSecond;
        Out.WriteLine($"one null of a 1.1 m/s shove cost {afterFirst:F2} m/s of tank");

        Assert.True(afterFirst > 0.5, $"a 1.1 m/s null spent only {afterFirst:F2} m/s");

        // The clock carries on from the first pass: the trim propagates the cutoff trajectory by
        // SecondsSinceReference, so restarting it reads the coast since cutoff as error.
        bus.VelocityCci += nose * 1.1;
        trim.Resume();

        Trim(trim, bus, aimAtEpoch, fromCci, arc.RequiredVelocityCci, 1.0 / 60.0, since: firstTook);

        Out.WriteLine($"two nulls: {trim.SpentMetresPerSecond:F2} m/s of tank");

        Assert.True(trim.SpentMetresPerSecond > afterFirst * 1.5,
                    $"a second null left the total at {trim.SpentMetresPerSecond:F2} m/s "
                    + $"against {afterFirst:F2} m/s after the first — the tank refilled");
    }

    /// <summary>
    /// <see cref="BusTrim.SpentMetresPerSecond"/> is cumulative, so a caller banking it per run and
    /// adding the running total overcounts with the square of the pass count. docs/MIRV-NEXT.md 0c.
    /// </summary>
    [Fact]
    public void TheBudgetIsWhatTheTankLost_NotWhatEachNullAddsToEveryNullAfterIt()
    {
        const double Budget = 25.0;
        const double ShovePerPass = 3.0;

        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + nose * ShovePerPass,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        BusTrim trim = new();
        trim.Begin();

        double step = 1.0 / 60.0;
        double elapsed = 0.0;
        double refusedAt = double.NaN;

        for (int pass = 0; pass < 4; pass++)
        {
            if (pass > 0)
            {
                bus.VelocityCci += nose * ShovePerPass;
                trim.Resume();
            }

            for (int frame = 0; frame < 60 * 300; frame++)
            {
                // Asked every frame, exactly as the computer asks it. An implementation that
                // carries state between these calls is the defect this exists to catch.
                if (!trim.WithinBudget(Budget) && double.IsNaN(refusedAt))
                {
                    refusedAt = trim.SpentMetresPerSecond;
                }

                TrimCommand last = trim.Update(step, new TrimSituation(
                    Earth, bus.PositionCci, bus.VelocityCci,
                    fromCci, arc.RequiredVelocityCci, elapsed,
                    bus.NoseCci, bus.RightCci, bus.DownCci));

                if (last.Done) break;

                bus.Step(Earth, last.Fire, step);
                elapsed += step;
            }

            // The gap between a null finishing and the next correction pass re-arming it. The
            // computer goes on asking through it, which is where a banked total takes its step up.
            for (int idle = 0; idle < 60; idle++)
            {
                if (!trim.WithinBudget(Budget) && double.IsNaN(refusedAt))
                {
                    refusedAt = trim.SpentMetresPerSecond;
                }
            }
        }

        double spent = trim.SpentMetresPerSecond;
        Out.WriteLine($"four nulls of {ShovePerPass:F1} m/s: the tank lost {spent:F2} m/s "
                      + $"of a {Budget:F0} m/s budget");

        Assert.True(spent > ShovePerPass * 2.0,
                    $"four {ShovePerPass:F1} m/s nulls spent only {spent:F2} m/s");

        Assert.True(spent < Budget,
                    $"the bus really did spend its budget ({spent:F2} m/s) — retune the test");

        Assert.True(double.IsNaN(refusedAt),
                    $"refused a {Budget:F0} m/s budget with {refusedAt:F2} m/s spent");

        Assert.False(trim.WithinBudget(spent / 2.0), "accepted a budget it had already spent");
        Assert.True(trim.WithinBudget(-1.0), "a negative budget is unlimited");
    }

    /// <summary>
    /// <c>IcbmComputer</c> holds the salvo until the trim is Done, and a tank does not refill, so a
    /// budget expressed as withheld fire holds the warheads for ever. docs/MIRV-NEXT.md 0c.
    /// </summary>
    [Fact]
    public void ASpentBudgetEndsTheTrim_WhereAClearanceWaitOnlyPausesIt()
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out double3 aimAtEpoch);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        TrimBus Shoved() => new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + nose * 1.1,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(fromCci, nose)),
            DownCci = -Vec.Unit(fromCci),
        };

        TrimCommand Fly(BusTrim trim, TrimBus bus, bool mayFire, double budget, double forSeconds)
        {
            double step = 1.0 / 60.0;
            double elapsed = 0.0;
            TrimCommand last = default;

            while (elapsed < forSeconds)
            {
                last = trim.Update(step, new TrimSituation(
                    Earth, bus.PositionCci, bus.VelocityCci,
                    fromCci, arc.RequiredVelocityCci, elapsed,
                    bus.NoseCci, bus.RightCci, bus.DownCci, mayFire, budget));

                if (last.Done) break;

                bus.Step(Earth, last.Fire, step);
                elapsed += step;
            }

            return last;
        }

        // A budget too small to null a 1.1 m/s shove with. It has to end, and say so.
        BusTrim spent = new();
        spent.Begin();

        TrimCommand onBudget = Fly(spent, Shoved(), mayFire: true, budget: 0.25,
                                   forSeconds: BusTrim.MaxSeconds * 2.0);

        Out.WriteLine($"spent budget: done {onBudget.Done}, \"{onBudget.Said}\", "
                      + $"{spent.SpentMetresPerSecond:F2} m/s of tank");

        Assert.True(onBudget.Done, "a spent budget left the trim running, so the warheads stay aboard");
        Assert.True(spent.GaveUp, "it finished as though it had done the job");

        // The other stop, on the same shove: still waiting to clear, so it must NOT be done.
        BusTrim waiting = new();
        waiting.Begin();

        TrimCommand onClearance = Fly(waiting, Shoved(), mayFire: false, budget: -1.0,
                                      forSeconds: 30.0);

        Out.WriteLine($"clearance wait: done {onClearance.Done}, \"{onClearance.Said}\"");

        Assert.False(onClearance.Done, "a clearance wait finished the trim, which releases untrimmed");
        Assert.True(onClearance.ToGainMetresPerSecond > 0.0, "it stopped solving while it waited");
    }

    [Fact]
    public void ABusThatHasNotFiredHasSpentNothing()
    {
        BusTrim trim = new();

        Assert.Equal(0.0, trim.SpentMetresPerSecond);

        trim.Reset();
        Assert.Equal(0.0, trim.SpentMetresPerSecond);
    }

    /// <summary>
    /// A shove null is ones of m/s, an aim correction grows with the trajectory: four of six flown
    /// shots at 12,902 km asked 11.5–13.4 m/s against the fixed ten.
    /// </summary>
    [Theory]
    [InlineData(double.NaN, true)]
    [InlineData(5.0, true)]
    [InlineData(30.0, false)]
    public void TheCallerMaySizeThePerSolveCeilingButNeverBelowTheConstant(double asked, bool refused)
    {
        BallisticArc.Solution arc = Deorbit(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        BusTrim trim = new();
        trim.Begin();

        // Twelve metres a second off the solution: over the shipped ceiling, under a
        // correction-sized one.
        TrimCommand c = trim.Update(1.0 / 60.0, new TrimSituation(
            Earth, fromCci, arc.RequiredVelocityCci + nose * 12.0,
            fromCci, arc.RequiredVelocityCci, 0.0,
            nose, Vec.Unit(Vec.Cross(fromCci, nose)), -Vec.Unit(fromCci),
            MaxMetresPerSecond: asked));

        Assert.Equal(refused, c.Said.Contains("more than the"));
    }
}
