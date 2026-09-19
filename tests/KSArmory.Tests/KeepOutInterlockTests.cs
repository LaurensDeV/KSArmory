using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The trim is not allowed to push the bus into the stage it just dropped.
///
/// <para><b>Why a gate was not enough.</b> <see cref="SeparationClearance"/> is consulted before a
/// pass begins, and a pass that begins clear can still close the gap — because closing it is what
/// nulling the relative velocity <em>does</em>. Flown 2026-08-25: a clearance latch let a pass run
/// on a stale reading and the bus hit its own spent stack, 28 s of thrashing ending in
/// <c>nothing left aboard moves the bus</c>.</para>
///
/// <para>So the check moved inside the command. Every case here is about one frame's choice of
/// direction rather than about the manoeuvre as a whole, because that is the granularity the fault
/// had.</para>
/// </summary>
public class KeepOutInterlockTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private const double AxialAccel = 0.551;
    private const double LateralAccel = AxialAccel * 4.243 / 4.000;

    /// <summary>
    /// One trim run, with the discarded stage held in a fixed direction from the bus.
    /// </summary>
    /// <param name="towardStack">
    /// Which way the stage lies, as a multiple of the bus's own axes, or zero for the unconstrained
    /// control. Fixed rather than propagated: what is under test is the choice of direction, and a
    /// moving stack would make the assertion about the geometry instead.
    /// </param>
    private (string Said, double ToGain, TrimAxes Fired, int Frames) Run(double3 towardStack)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 right = Vec.Unit(Vec.Cross(fromCci, nose));
        double3 down = -Vec.Unit(fromCci);

        TrimBus bus = new()
        {
            PositionCci = fromCci,

            // Owed along the nose and to the right, so both an axial and a lateral direction are
            // genuinely worth firing and the interlock has something to withhold.
            VelocityCci = arc.RequiredVelocityCci + right * 1.5 + nose * 2.0,
            NoseCci = nose,
            RightCci = right,
            DownCci = down,
            AxialAcceleration = AxialAccel,
            LateralAcceleration = LateralAccel,
        };

        BusTrim trim = new();
        trim.Begin();

        double step = 1.0 / 60.0;
        double elapsed = 0.0;
        TrimTracker fired = new();
        TrimCommand last = default;
        int frames = 0;

        while (elapsed < 60.0)
        {
            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                fromCci, arc.RequiredVelocityCci, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci,
                KeepOutTowardCci: towardStack));

            fired.Saw(last.Fire);
            frames++;

            if (last.Done) break;

            bus.Step(Earth, last.Fire, step);
            elapsed += step;
        }

        return (last.Said, last.ToGainMetresPerSecond, fired.Seen, frames);
    }

    private sealed class TrimTracker
    {
        public TrimAxes Seen { get; private set; }

        public void Saw(TrimAxes fire) => Seen |= fire;
    }

    /// <summary>
    /// The control, and the thing the rest is measured against: with nothing to avoid the trim
    /// fires both the directions it owes and converges.
    /// </summary>
    [Fact]
    public void WithNothingToAvoidTheTrimFiresEveryDirectionItOwes()
    {
        (string said, double toGain, TrimAxes fired, _) = Run(Vec.Zero);

        Out.WriteLine($"{said} ({toGain:F3} m/s left, fired {fired})");

        Assert.True(toGain < 0.1, $"did not converge: {toGain:F3} m/s left");
        Assert.NotEqual(TrimAxes.None, fired & (TrimAxes.Forward | TrimAxes.Backward));
        Assert.NotEqual(TrimAxes.None, fired & (TrimAxes.Right | TrimAxes.Left));
    }

    /// <summary>
    /// The interlock proper. The bus owes velocity backward along its nose, and the stage is
    /// astern — so firing to null it drives into the stage, and that one direction is withheld
    /// while the lateral one still runs.
    /// </summary>
    [Fact]
    public void ADirectionThatWouldCloseOnTheStackIsWithheldAndTheOthersStillFire()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);

        // The bus is travelling too fast along its nose, so nulling that fires Backward -- and the
        // stage has to lie along the direction the thrusters push, not along the error.
        (string said, _, TrimAxes fired, _) = Run(-nose);

        Out.WriteLine($"{said} (fired {fired})");

        Assert.Equal(TrimAxes.None, fired & TrimAxes.Backward);
        Assert.NotEqual(TrimAxes.None, fired & (TrimAxes.Right | TrimAxes.Left));
    }

    /// <summary>
    /// A stage exactly abeam constrains nothing the bus wants to do along its nose. The test is a
    /// strict inequality on purpose — an interlock that refused square pushes would cost a
    /// manoeuvre where it should cost a frame.
    /// </summary>
    [Fact]
    public void AStackExactlySquareToThePushDoesNotBlockIt()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 down = -Vec.Unit(fromCci);

        // Square to both directions the bus actually fires in.
        (string said, double toGain, TrimAxes fired, _) = Run(down);

        Out.WriteLine($"{said} ({toGain:F3} m/s left, fired {fired})");

        Assert.NotEqual(TrimAxes.None, fired & TrimAxes.Backward);
        Assert.True(toGain < 0.1, $"a square stack should cost nothing: {toGain:F3} m/s left");
    }

    /// <summary>
    /// <b>The trap this class exists for.</b> With every useful direction withheld, <c>Choose</c>
    /// returns <c>None</c> — which is the same value it returns when there is genuinely nothing
    /// left to push. Reading the two as one reports <c>trimmed to N m/s</c> about a bus that was
    /// never allowed to fire, and releases the warheads on it.
    /// </summary>
    [Fact]
    public void AnInterlockedTrimWaitsRatherThanReportingItselfFinished()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 right = Vec.Unit(Vec.Cross(fromCci, nose));

        // Along the whole of what the thrusters would push, so every direction worth firing
        // closes on the stage.
        (string said, double toGain, TrimAxes fired, int frames) =
            Run(-Vec.Unit(nose * 2.0 + right * 1.5));

        Out.WriteLine($"after {frames} frames: {said} ({toGain:F3} m/s left, fired {fired})");

        Assert.Equal(TrimAxes.None, fired);
        Assert.DoesNotContain("trimmed to", said);
        Assert.Contains("holding off the spent stack", said);
        Assert.True(toGain > 1.0, $"the bus should still owe its solution: {toGain:F3} m/s");
    }

    /// <summary>
    /// And the wait is bounded. An interlock that never lifts must not hold the warheads for the
    /// rest of the flight — <see cref="BusTrim.MaxSeconds"/> ends it, the same stop a trim that
    /// cannot converge gets.
    /// </summary>
    [Fact]
    public void AnInterlockThatNeverLiftsStillEndsTheTrim()
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out double3 fromCci, out _);
        double3 nose = Vec.Unit(arc.RequiredVelocityCci);
        double3 right = Vec.Unit(Vec.Cross(fromCci, nose));
        double3 down = -Vec.Unit(fromCci);
        double3 toward = -Vec.Unit(nose * 2.0 + right * 1.5);

        TrimBus bus = new()
        {
            PositionCci = fromCci,
            VelocityCci = arc.RequiredVelocityCci + right * 1.5 + nose * 2.0,
            NoseCci = nose,
            RightCci = right,
            DownCci = down,
            AxialAcceleration = AxialAccel,
            LateralAcceleration = LateralAccel,
        };

        BusTrim trim = new();
        trim.Begin();

        double step = 1.0 / 60.0;
        double elapsed = 0.0;
        TrimCommand last = default;

        while (elapsed < BusTrim.MaxSeconds + 10.0)
        {
            last = trim.Update(step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                fromCci, arc.RequiredVelocityCci, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci,
                KeepOutTowardCci: toward));

            if (last.Done) break;

            elapsed += step;
        }

        Out.WriteLine($"ended at {elapsed:F1} s: {last.Said}");

        Assert.True(last.Done, "an interlock that never lifts held the warheads for ever");
        Assert.True(elapsed <= BusTrim.MaxSeconds + 1.0,
                    $"took {elapsed:F1} s against a {BusTrim.MaxSeconds:F0} s bound");
    }

    /// <summary>
    /// A caller with nothing to say passes the default, and the default is no constraint — so
    /// every existing site behaves exactly as it did before the field existed.
    /// </summary>
    [Fact]
    public void TheDefaultSituationCarriesNoConstraint()
    {
        Assert.Equal(Vec.Zero, default(TrimSituation).KeepOutTowardCci);
    }
}
