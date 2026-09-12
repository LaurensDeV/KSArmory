using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What the engine's pulse mode buys the trim: a held key fires for whole frames, so the smallest
/// correction a hold can make is <c>acceleration x step</c> and it stops inside
/// <see cref="BusTrim.SettledMetresPerSecond"/> — which at ~380 m of miss per m/s is the 7-8 m floor
/// under the whole post-boost correction. A pulse is the thruster's own minimum instead.
///
/// <para>The rig carries the engine's contract rather than a tidier one: one pulse of
/// <see cref="TrimBus.PulseSeconds"/> and no more often than <see cref="TrimBus.PulseEverySeconds"/>,
/// however long the frame is. <c>docs/ACCURACY-PLAN.md</c> 3cu item 39.</para>
/// </summary>
public class BusTrimPulseTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    // The shipped bus, which is what sizes both thresholds: a frame of jets at 17 ms is 0.0095 m/s
    // against a pulse's 0.00056.
    private const double BusAcceleration = 0.56;
    private const double Step = 1.0 / 60.0;
    private const double Pulse = 0.001;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>
    /// Jets weak enough that a pulse phase runs past <see cref="BusTrim.DirectionStallSeconds"/>:
    /// about 15 s to cross the band at a millisecond a pulse, against 2 s on the shipped bus. That is
    /// the case the watch has to be held out of, and a phase shorter than the watch's own clock
    /// cannot show it.
    /// </summary>
    private const double WeakJets = 0.2;

    /// <summary>A coasting bus shoved off its solution along its own nose, as a decoupler does.</summary>
    private static (TrimBus Bus, double3 From, double3 Reference) Shoved(
        double metresPerSecond, double acceleration = BusAcceleration)
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 reference = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        double3 nose = Vec.Unit(reference);

        TrimBus bus = new()
        {
            PositionCci = from,
            VelocityCci = reference + nose * metresPerSecond,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(from, nose)),
            DownCci = -Vec.Unit(from),
            AxialAcceleration = acceleration,
            LateralAcceleration = acceleration,
            PulseSeconds = Pulse,
        };

        return (bus, from, reference);
    }

    /// <summary>Run the trim to a stop, and report what it left on the bus.</summary>
    private TrimCommand Null(double pulseSeconds, out bool pulsed,
                             double acceleration = BusAcceleration)
    {
        (TrimBus bus, double3 from, double3 reference) = Shoved(0.2, acceleration);

        BusTrim trim = new();
        trim.Begin();

        TrimCommand last = default;
        double elapsed = 0.0;
        pulsed = false;

        while (elapsed < BusTrim.MaxSeconds)
        {
            last = trim.Update(Step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                from, reference, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci,
                PulseSeconds: pulseSeconds));

            if (last.Done) break;

            pulsed |= last.Pulse;
            bus.Step(Earth, last.Fire, Step, last.Pulse);
            elapsed += Step;
        }

        Out.WriteLine($"pulse {pulseSeconds * 1000.0:F0} ms: {last.Said}, "
                      + $"{last.ToGainMetresPerSecond:F4} m/s left after {elapsed:F1} s");

        return last;
    }

    /// <summary>
    /// The floor a hold stops at is the settle band, and pulses finish well inside it.
    /// </summary>
    [Fact]
    public void APulsePhaseFinishesInsideTheBandAHoldStopsAt()
    {
        TrimCommand held = Null(pulseSeconds: 0.0, out bool heldPulsed);
        TrimCommand pulsed = Null(Pulse, out bool everPulsed);

        Assert.False(heldPulsed, "nothing may pulse with no pulse length to pulse for");
        Assert.True(everPulsed, "the trim never pulsed");
        Assert.False(pulsed.Done && pulsed.Said.Contains("stopped closing"),
                     $"the pulse phase stalled: {pulsed.Said}");

        // Three pulses wide, plus the frame the phase is entered on.
        double floor = BusTrim.PulseFloorPulses * BusAcceleration * Pulse;

        Out.WriteLine($"held {held.ToGainMetresPerSecond:F4} m/s, pulsed "
                      + $"{pulsed.ToGainMetresPerSecond:F4}, floor {floor:F4}");

        Assert.True(held.ToGainMetresPerSecond > 0.5 * BusTrim.SettledMetresPerSecond,
                    $"a hold should stop inside its band, left {held.ToGainMetresPerSecond:F4} m/s");
        Assert.True(pulsed.ToGainMetresPerSecond < 2.0 * floor,
                    $"pulses left {pulsed.ToGainMetresPerSecond:F4} m/s, above {2.0 * floor:F4}");
        Assert.True(pulsed.ToGainMetresPerSecond < 0.5 * held.ToGainMetresPerSecond,
                    "pulsing must finish closer than holding");
    }

    /// <summary>
    /// A pulse moves a component by a fraction of what the watch calls progress, so a phase that
    /// outlasts <see cref="BusTrim.DirectionStallSeconds"/> would have every live direction struck
    /// off under it — and a struck-off bus ends the correction as a refusal, which stops the passes.
    ///
    /// <para>On weak jets, where the phase runs about 15 s rather than 2.</para>
    /// </summary>
    [Fact]
    public void APulsePhaseOutlastingTheWatchStrikesNoDirectionOff()
    {
        TrimCommand pulsed = Null(Pulse, out bool everPulsed, WeakJets);

        Assert.True(everPulsed, "the trim never pulsed");
        Assert.DoesNotContain("struck off", pulsed.Said);
        Assert.DoesNotContain("nothing left aboard", pulsed.Said);
        Assert.True(pulsed.ToGainMetresPerSecond < BusTrim.SettledMetresPerSecond,
                    $"it gave up inside the band: {pulsed.Said}");
    }

    /// <summary>
    /// And the budget is charged for what a pulse burns rather than for the frame it sits in: a
    /// pulsing null costs a fraction of a held one, not several times it.
    /// </summary>
    [Fact]
    public void PulsesAreChargedForWhatTheyBurn()
    {
        (TrimBus bus, double3 from, double3 reference) = Shoved(0.2);

        BusTrim trim = new();
        trim.Begin();

        double elapsed = 0.0;

        while (elapsed < BusTrim.MaxSeconds)
        {
            TrimCommand last = trim.Update(Step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci,
                from, reference, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci,
                PulseSeconds: Pulse));

            if (last.Done) break;

            bus.Step(Earth, last.Fire, Step, last.Pulse);
            elapsed += Step;
        }

        Out.WriteLine($"spent {trim.SpentMetresPerSecond:F3} m/s nulling 0.2");

        // The shove itself is 0.2 m/s; the phase that finishes it must not cost a multiple of that.
        Assert.True(trim.SpentMetresPerSecond < 0.6,
                    $"a pulsing null spent {trim.SpentMetresPerSecond:F3} m/s on a 0.2 m/s shove");
    }
}
