using System.Text.RegularExpressions;
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
///
/// <para><b>Every fixture here asserts the regime it is in before it asserts the rule.</b> A fresh
/// <see cref="BusTrim"/> has measured no thrust, so <see cref="BusTrim.StopBand(double, double,
/// double)"/> hands back the hold's own band and nothing pulses at all — which is how the first
/// build of these tests passed while the flown code did none of what they claimed.</para>
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

    /// <summary>
    /// Jets weak enough that a pulse phase moves a component slower than
    /// <see cref="BusTrim.ProgressMetresPerSecond"/> per <see cref="BusTrim.DirectionStallSeconds"/>:
    /// 0.0013 m/s a second, so the watch's clock trips at 4 s having seen 0.005 of the 0.01 it wants.
    /// On the shipped bus the same phase moves 0.0037 a second and the watch never trips, which is
    /// why a fixture at 0.56 cannot test the gating at all.
    /// </summary>
    private const double WeakJets = 0.2;

    /// <summary>Weaker still: a phase that cannot cross the band inside the per-null bound.</summary>
    private const double CrawlingJets = 0.05;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>What one run of the loop did, and which regime it was actually in while doing it.</summary>
    private sealed record Flight(TrimCommand Last, double Seconds, double PulsingSeconds,
                                 double WorstPulsedAt, bool EverPulsed, bool EverHeld,
                                 double WorstOwed);

    /// <summary>A bus coasting at 200 km on its solution, with its own axes as the control frame.</summary>
    private static (TrimBus Bus, double3 From, double3 Reference) Coasting(
        double axial = BusAcceleration, double lateral = BusAcceleration)
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 reference = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        double3 nose = Vec.Unit(reference);

        TrimBus bus = new()
        {
            PositionCci = from,
            VelocityCci = reference,
            NoseCci = nose,
            RightCci = Vec.Unit(Vec.Cross(from, nose)),
            DownCci = -Vec.Unit(from),
            AxialAcceleration = axial,
            LateralAcceleration = lateral,
            PulseSeconds = Pulse,
        };

        return (bus, from, reference);
    }

    // Spread across the axes on purpose. A shove along the nose alone cannot reach the state the
    // flown fault was in, because that state needs an error the loop cannot fire at beside one it
    // can.
    private static void Shove(TrimBus bus, double alongNose, double across = 0.0, double under = 0.0)
        => bus.VelocityCci += bus.NoseCci * alongNose + bus.RightCci * across + bus.DownCci * under;

    /// <summary>Run the loop against the rig until it stops, or for as long as asked.</summary>
    private Flight Fly(BusTrim trim, TrimBus bus, double3 from, double3 reference,
                       double pulseSeconds, double since = 0.0, double forSeconds = 300.0,
                       double3 keepOut = default)
    {
        double elapsed = since;
        TrimCommand last = default;
        double pulsing = 0.0;
        double worstPulsed = 0.0;
        double worstOwed = 0.0;
        bool everPulsed = false;
        bool everHeld = false;

        while (elapsed - since < forSeconds)
        {
            last = trim.Update(Step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci, from, reference, elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci,
                KeepOutTowardCci: keepOut, PulseSeconds: pulseSeconds));

            if (double.IsFinite(last.ToGainMetresPerSecond))
            {
                worstOwed = Math.Max(worstOwed, last.ToGainMetresPerSecond);
            }

            if (last.Done) break;

            if (last.Pulse)
            {
                everPulsed = true;
                pulsing += Step;
                worstPulsed = Math.Max(worstPulsed, last.ToGainMetresPerSecond);
            }
            else if (last.Fire != TrimAxes.None)
            {
                everHeld = true;
            }

            bus.Step(Earth, last.Fire, Step, last.Pulse);
            elapsed += Step;
        }

        return new Flight(last, elapsed - since, pulsing, worstPulsed, everPulsed, everHeld, worstOwed);
    }

    // The one thing that makes a pulse reachable at all: the loop has to have measured its own
    // thrusters, because the pulsing band is a multiple of what they do. Asserted rather than
    // assumed in every fixture below.
    private void Reached(Flight flight, string what)
    {
        Out.WriteLine($"{what}: {flight.Last.Said} | {flight.Seconds:F1} s, "
                      + $"{flight.PulsingSeconds:F1} s pulsing, worst owed {flight.WorstOwed:F3}, "
                      + $"worst pulsed at {flight.WorstPulsedAt:F4}, "
                      + $"measured {flight.Last.Acceleration:F3} m/s2");

        Assert.True(flight.Last.Acceleration > 0.1 * CrawlingJets,
                    "the loop measured no thrust, so its pulsing band was never narrower than the "
                    + "hold's and nothing here was under test");
    }

    /// <summary>
    /// The floor a hold stops at is the settle band, and pulses finish well inside it.
    /// </summary>
    [Fact]
    public void APulsePhaseFinishesInsideTheBandAHoldStopsAt()
    {
        (TrimBus holdOnly, double3 from, double3 reference) = Coasting();
        Shove(holdOnly, 0.2);

        BusTrim holding = new();
        holding.Begin();
        Flight held = Fly(holding, holdOnly, from, reference, pulseSeconds: 0.0);

        (TrimBus bus, _, _) = Coasting();
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();
        Flight pulsed = Fly(trim, bus, from, reference, Pulse);

        Reached(pulsed, "0.2 m/s shove, pulsing");

        Assert.False(held.EverPulsed, "nothing may pulse with no pulse length to pulse for");
        Assert.True(pulsed.EverPulsed, "the trim never pulsed");
        Assert.True(pulsed.EverHeld, "a 0.2 m/s shove is a hold's work down to the band first");
        Assert.False(pulsed.Last.Said.Contains("stopped closing"),
                     $"the pulse phase stalled: {pulsed.Last.Said}");

        // Three pulses wide, plus the frame the phase is entered on.
        double floor = BusTrim.PulseFloorPulses * BusAcceleration * Pulse;

        Out.WriteLine($"held {held.Last.ToGainMetresPerSecond:F4} m/s, pulsed "
                      + $"{pulsed.Last.ToGainMetresPerSecond:F4}, floor {floor:F4}");

        Assert.True(held.Last.ToGainMetresPerSecond > 0.5 * BusTrim.SettledMetresPerSecond,
                    $"a hold should stop inside its band, left {held.Last.ToGainMetresPerSecond:F4} m/s");
        Assert.True(pulsed.Last.ToGainMetresPerSecond < 2.0 * floor,
                    $"pulses left {pulsed.Last.ToGainMetresPerSecond:F4} m/s, above {2.0 * floor:F4}");
        Assert.True(pulsed.Last.ToGainMetresPerSecond < 0.5 * held.Last.ToGainMetresPerSecond,
                    "pulsing must finish closer than holding");
    }

    /// <summary>
    /// The entry is the hold's own stop rule read as a length, which is what makes it impossible to
    /// refuse a legitimate phase: <c>Choose</c> will not pick a component already inside the band,
    /// so the state a hold hands over has every component inside it, and the longest such vector is
    /// <c>√3</c> bands.
    /// </summary>
    [Fact]
    public void TheEntryIsTheLongestVectorAHoldCanLeaveBehind()
    {
        double band = BusTrim.StopBand(BusAcceleration, Step);

        Assert.Equal(BusTrim.SettledMetresPerSecond, band);

        // The corner of the region a hold stops in.
        Assert.Equal(Vec.Len(new double3(band, band, band)), BusTrim.PulseEntry(band), 12);

        // And a state a hold would still have been firing at is outside it.
        Assert.True(Vec.Len(new double3(band * 1.01, band, band)) > BusTrim.PulseEntry(band));

        // Flown: 14,313 pulse commands, none above 0.0340 m/s, and the eight faulty nulls start at
        // 0.105 -- so the constant sits in an empty gap rather than on a fitted edge.
        Assert.True(0.0340 < BusTrim.PulseEntry(band) && BusTrim.PulseEntry(band) < 0.105);
    }

    /// <summary>
    /// <b>And the entry is pinned from below, which is where it is easy to get wrong.</b> A hold
    /// leaves each of three axes just inside the band, so the error it hands over is <em>above</em>
    /// the band as a length while being out of a hold's reach on every axis — which is the whole
    /// state the phase exists for.
    ///
    /// <para>Gating on the total against the band alone refuses exactly this and drops the loop back
    /// to firing whole frames at sub-band components. Flown, phases start at a median 0.021 m/s and
    /// 0.028 at the ninth decile, so such a gate would refuse more than half of them.</para>
    /// </summary>
    [Fact]
    public void AnErrorSpreadOverThreeAxesEntersThePhaseAboveTheBand()
    {
        (TrimBus bus, double3 from, double3 reference) = Coasting();

        // Each a couple of band-widths, so each is held down to just inside the band and none of
        // them is left large enough for a hold to keep working on.
        Shove(bus, alongNose: 0.05, across: 0.05, under: 0.05);

        BusTrim trim = new();
        trim.Begin();

        Flight flight = Fly(trim, bus, from, reference, Pulse);

        Reached(flight, "three axes just inside the band");

        Assert.True(flight.EverPulsed,
                    $"never pulsed: the phase was refused at {flight.WorstPulsedAt:F4} m/s");

        // The regime, and the assertion in one: it entered while owing more than the band.
        Assert.True(flight.WorstPulsedAt > BusTrim.SettledMetresPerSecond,
                    $"entered at {flight.WorstPulsedAt:F4} m/s, inside the band -- this fixture is "
                    + "not the three-axis entry it claims to be");

        Assert.True(flight.WorstPulsedAt <= BusTrim.PulseEntry(BusTrim.StopBand(flight.Last.Acceleration, Step)));
        Assert.Contains("trimmed to", flight.Last.Said);
    }

    /// <summary>
    /// <b>The flown fault, by its first route.</b> The component is the largest direction still
    /// <em>available</em>, which is not the largest error: the keep-out withholds the axis a
    /// separation error lies along, and pulsing at what is left taps a side jet while metres per
    /// second stand on the withheld one — flown, 2.541 m/s for as long as the clocks allowed.
    ///
    /// <para>The bus owes a large withheld component and two small free ones, which is the only
    /// shape that reaches this: with nothing free the loop simply waits, and with the free ones
    /// above the band it holds them.</para>
    /// </summary>
    [Fact]
    public void NothingPulsesWhileTheInterlockWithholdsTheAxisTheErrorIsOn()
    {
        (TrimBus bus, double3 from, double3 reference) = Coasting();

        // 2.5 m/s along the nose, which is nulled by firing backward; 0.6 abeam, which is a hold's
        // work and is what lets the loop measure its thrusters at all; and 0.012 under, which ends
        // up inside the band and above the pulse floor.
        Shove(bus, alongNose: 2.5, across: 0.6, under: 0.012);

        BusTrim trim = new();
        trim.Begin();

        // The spent stack lies astern, so the backward push closes on it and is withheld -- the
        // stage has to lie along the direction the thrusters push, not along the error.
        Flight flight = Fly(trim, bus, from, reference, Pulse, forSeconds: 60.0,
                            keepOut: -bus.NoseCci);

        Reached(flight, "2.5 m/s withheld astern");

        Assert.True(flight.WorstOwed > 1.0,
                    $"the bus never owed metres per second: worst {flight.WorstOwed:F3}");
        Assert.True(flight.EverHeld, "nothing was ever held, so the thrusters were never measured");

        Assert.True(flight.WorstPulsedAt <= BusTrim.PulseEntry(BusTrim.StopBand(flight.Last.Acceleration, Step)),
                    $"pulsed with {flight.WorstPulsedAt:F3} m/s still on the bus");

        Assert.Contains("holding off the spent stack", flight.Last.Said);
    }

    /// <summary>
    /// <b>The same fault by the route it actually took in flight.</b> A struck-off axis is gone for
    /// the null just as surely as a withheld one, and the loop went on pulsing the two sub-band side
    /// components down to the pulse floor with 2.54 m/s standing on the tail — then reported
    /// <c>nothing left aboard</c> about it, four seconds later than it could have.
    /// </summary>
    [Fact]
    public void NothingPulsesWhileAStruckOffAxisCarriesMetresPerSecond()
    {
        // No lateral authority, so the large abeam component is the one that gets struck off.
        (TrimBus bus, double3 from, double3 reference) = Coasting(lateral: 0.0);

        Shove(bus, alongNose: 0.5, across: 2.5, under: 0.012);

        BusTrim trim = new();
        trim.Begin();

        Flight flight = Fly(trim, bus, from, reference, Pulse, forSeconds: 60.0);

        Reached(flight, "2.5 m/s on a struck-off axis");

        Assert.True(flight.WorstOwed > 1.0,
                    $"the bus never owed metres per second: worst {flight.WorstOwed:F3}");
        Assert.Contains("struck off", flight.Last.Said);

        Assert.True(flight.WorstPulsedAt <= BusTrim.PulseEntry(BusTrim.StopBand(flight.Last.Acceleration, Step)),
                    $"pulsed with {flight.WorstPulsedAt:F3} m/s still on the bus");
    }

    /// <summary>
    /// And the phase is bounded, because the clocks that judge a working loop —
    /// <see cref="BusTrim.StallSeconds"/> and <see cref="BusTrim.DirectionStallSeconds"/> — are
    /// sized for how fast a hold moves the number.
    ///
    /// <para>The bound is not the entry rule by another name: flown, the five nulls that crawled 24
    /// to 45 s never pulsed outside the band, and the eight that pulsed outside it all finished
    /// inside 16 s. Neither fault catches the other.</para>
    /// </summary>
    [Fact]
    public void ThePulsePhaseIsBoundedInTime()
    {
        // Jets weak enough that crossing the band a pulse at a time takes about a minute.
        (TrimBus bus, double3 from, double3 reference) = Coasting(CrawlingJets, CrawlingJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        Flight flight = Fly(trim, bus, from, reference, Pulse);

        Reached(flight, "crawling jets");

        // The regime: a phase that would have run well past the bound if nothing stopped it.
        Assert.True(flight.PulsingSeconds > 0.5 * BusTrim.PulseSecondsPerNull,
                    $"pulsed for only {flight.PulsingSeconds:F1} s, so the bound was never reached "
                    + "and this fixture tests nothing");

        Assert.True(flight.PulsingSeconds <= BusTrim.PulseSecondsPerNull + 1.0,
                    $"pulsed for {flight.PulsingSeconds:F1} s against a "
                    + $"{BusTrim.PulseSecondsPerNull:F0} s bound");

        // And what it does at the bound is finish as a hold would, rather than drop back to firing
        // whole frames at a component inside the band -- which is the overshoot the phase exists to
        // avoid.
        Assert.True(flight.Last.Done, "the null never finished");
        Assert.True(flight.Seconds < BusTrim.MaxSeconds,
                    $"the null ran its whole clock out rather than finishing: {flight.Seconds:F1} s");
        Assert.Contains("trimmed to", flight.Last.Said);
    }

    /// <summary>
    /// A pulse moves a component by a fraction of what the watch calls progress, so a phase that
    /// outlasts <see cref="BusTrim.DirectionStallSeconds"/> would have its direction struck off
    /// under it — and a struck-off bus ends the correction as a refusal, which stops the passes.
    ///
    /// <para><b>The null has to BEGIN inside the band</b>, which is the regime a later pass is in and
    /// the one a fixture that holds first cannot reach. <see cref="BusTrim.Resume"/> clears
    /// <c>_pushed</c> and keeps <c>_bestAccel</c>, and a null that never holds never measures again —
    /// so the axis reads dead for the whole phase. A fixture that holds first refills that reading
    /// and the mutation survives, which is exactly how the guard came to be deleted.</para>
    /// </summary>
    [Fact]
    public void APulseOnlyNullStrikesNoDirectionOff()
    {
        (TrimBus bus, double3 from, double3 reference) = Coasting(WeakJets, WeakJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        // The first pass, holding only: it measures the thrusters and stops at the band, which is
        // the state a post-boost correction re-arms onto.
        Flight first = Fly(trim, bus, from, reference, pulseSeconds: 0.0);

        Assert.False(first.EverPulsed, "the first pass was meant to hold only");
        Assert.True(first.Last.Acceleration > 0.1 * WeakJets, "the first pass measured nothing");

        trim.Resume();

        Flight flight = Fly(trim, bus, from, reference, Pulse, since: first.Seconds);

        Reached(flight, "pulse-only second pass");

        Assert.False(flight.EverHeld, "the fixture held, so it is not the pulse-only regime");
        Assert.True(flight.EverPulsed, "the second pass never pulsed");
        Assert.True(flight.PulsingSeconds > BusTrim.DirectionStallSeconds,
                    $"pulsed for only {flight.PulsingSeconds:F1} s, inside the watch's own "
                    + $"{BusTrim.DirectionStallSeconds:F0} s clock, so the gating was never exercised");

        Assert.DoesNotContain("struck off", flight.Last.Said);
        Assert.DoesNotContain("nothing left aboard", flight.Last.Said);
    }

    /// <summary>
    /// And the budget is charged for what a pulse burns rather than for the frame it sits in: a
    /// pulsing null costs a fraction of a held one, not several times it.
    /// </summary>
    [Fact]
    public void PulsesAreChargedForWhatTheyBurn()
    {
        (TrimBus bus, double3 from, double3 reference) = Coasting();
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        Flight flight = Fly(trim, bus, from, reference, Pulse);

        Reached(flight, "budget");
        Assert.True(flight.EverPulsed, "it never pulsed, so nothing pulsing was charged");

        Out.WriteLine($"spent {trim.SpentMetresPerSecond:F3} m/s nulling 0.2");

        // The shove itself is 0.2 m/s; the phase that finishes it must not cost a multiple of that.
        Assert.True(trim.SpentMetresPerSecond < 0.6,
                    $"a pulsing null spent {trim.SpentMetresPerSecond:F3} m/s on a 0.2 m/s shove");
    }

    /// <summary>
    /// The stall line says what this null's pulses asked for and what arrived, and it reads
    /// differently on a bus whose thrusters grant the pulse and one whose do not.
    /// </summary>
    /// <remarks>
    /// <para>Two faults produce an identical trace in flight — a reference drifting faster than the
    /// pulse phase can null it, and pulses that do not deliver — and 20 of 56 long-range flights ended
    /// on that trace (<c>docs/ACCURACY-PLAN.md</c> 3ey). The measurement is what separates them, so it
    /// has to be shown to separate them.</para>
    ///
    /// <para><b>The commanded count is not the denominator.</b> The engine grants one pulse per
    /// <see cref="TrimBus.PulseEverySeconds"/> however often it is asked, so at this step most commands
    /// deliver nothing by design and a ratio taken over all of them reads about a tenth on a healthy
    /// bus. The clause counts only the frames that fired, which is the trap this fixture pins.</para>
    /// </remarks>
    [Theory]
    [InlineData(1.0, "grants what it is asked")]
    [InlineData(0.01, "grants a hundredth")]
    public void TheStallSaysWhatThePulsesAskedForAndWhatArrived(double granted, string what)
    {
        (TrimBus bus, double3 from, double3 reference) = Coasting(WeakJets, WeakJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        // Hold first, so the thrusters are measured; then re-arm into the pulse-only regime a
        // post-boost pass is actually in.
        Flight first = Fly(trim, bus, from, reference, pulseSeconds: 0.0);
        Assert.True(first.Last.Acceleration > 0.1 * WeakJets, "the first pass measured nothing");

        // What the engine grants, against what the trim commands. This is the whole of candidate (b).
        bus.PulseSeconds = Pulse * granted;

        trim.Resume();
        Flight flight = Fly(trim, bus, from, reference, Pulse, since: first.Seconds);

        Out.WriteLine($"{what,-24}: {flight.Last.Said}");

        Assert.True(flight.EverPulsed, "the second pass never pulsed");

        if (granted >= 1.0)
        {
            // A bus that grants what it is asked does not stall at all: it converges past the band
            // and finishes. That is the control, and it is a stronger statement than any ratio.
            Assert.True(flight.Last.Done, $"a bus granting full pulses did not finish: {flight.Last.Said}");
            Assert.DoesNotContain("stopped closing", flight.Last.Said);
        }
        else
        {
            // A hundredth of a pulse is under the floor that counts a frame as having fired, so the
            // line says which of the two faults this is rather than leaving them indistinguishable.
            Assert.Contains("stopped closing", flight.Last.Said);
            Assert.Contains("none of them delivered", flight.Last.Said);
        }
    }

    /// <summary>
    /// A bus granting a known fraction of every pulse must read back as granting that fraction, and
    /// the interval the phase is entered on is where that goes wrong.
    /// </summary>
    /// <remarks>
    /// <para>A command written this frame reaches the engine's worker on the next one, so the first
    /// interval of a pulse phase was driven by the <em>hold</em> before it — a whole frame of jets,
    /// which at this step is sixteen pulses. Credited to the phase, that one interval is most of the
    /// delivery reading, which is what <c>docs/ACCURACY-PLAN.md</c> 3ez measured at 1.14x to 2.16x
    /// and took for the thrusters over-delivering.</para>
    ///
    /// <para><b>The lag is the whole fixture</b>, and <see cref="Fly"/> does not have it: it applies
    /// each command on the frame it was written, which is the one epoch where the unguarded reading
    /// is exact. <c>docs/KSA-FRAME-ORDER.md</c>.</para>
    /// </remarks>
    [Fact]
    public void TheDeliveryReadingIsThePulsesAndNotTheHoldBeforeThem()
    {
        // A reference running away faster than a pulse at a time can chase it, which is the only
        // thing that stalls a bus whose pulses arrive -- and the stall is the line the reading rides
        // on. Below the band per frame the phase simply finishes; above it the hold never hands over.
        const double DriftMetresPerSecondSquared = 0.05;

        (TrimBus bus, double3 from, double3 reference) = Coasting(WeakJets, WeakJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        double elapsed = 0.0;
        TrimCommand last = default;
        TrimCommand pending = default;
        int entries = 0;

        while (elapsed < 200.0)
        {
            last = trim.Update(Step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci, from,
                reference + bus.NoseCci * (DriftMetresPerSecondSquared * elapsed), elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci, PulseSeconds: Pulse));

            if (last.Done) break;

            if (last.Pulse && pending.Fire != TrimAxes.None && !pending.Pulse) entries++;

            // The engine's contract: what arrives this frame is what was written on the last one.
            bus.Step(Earth, pending.Fire, Step, pending.Pulse);
            pending = last;
            elapsed += Step;
        }

        Out.WriteLine($"{last.Said} | {elapsed:F1} s, {entries} hold-to-pulse entries");

        Assert.True(entries > 0, "the phase was never entered from a hold, so nothing could be miscredited");

        Match m = Regex.Match(last.Said, @"([\d.]+)x[,)]");
        Assert.True(m.Success, $"no delivery ratio was reported: {last.Said}");

        double ratio = double.Parse(m.Groups[1].Value);
        Out.WriteLine($"  delivered/asked = {ratio:F2}x on a bus that grants what it is asked");

        Assert.InRange(ratio, 0.90, 1.10);

        // And the reading beside it is calibrated by the same rig: a bus whose control axes are what
        // the trim thinks they are puts every pulse squarely on the direction asked, so anything under
        // one in flight is the impulse arriving sideways rather than the reference receding. Those are
        // the two candidates left for the long-range stall -- docs/ACCURACY-PLAN.md 3fd.
        Match along = Regex.Match(last.Said, @"([\d.]+) of it along the direction asked");
        Assert.True(along.Success, last.Said);
        Assert.InRange(double.Parse(along.Groups[1].Value), 0.95, 1.01);
    }

    /// <summary>
    /// A trim that stopped improving inside its own stop band has finished, and saying it gave up
    /// forfeits the whole post-cutoff correction — <see cref="IcbmConfig.StoppingInsideTheBandIsDone"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The threshold is a length against a per-axis band, which is the part that is easy to
    /// get wrong.</b> <c>Choose</c> will not pick a component already inside the band, so the longest
    /// vector a hold can leave behind is <c>root-three</c> bands. Flown, the residuals that stalled run
    /// to 0.03 m/s against a 0.020 band, so testing the length against the band alone still calls half
    /// of them failures — which is why this fixture asserts the residual is <em>above</em> the band.</para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStallInsideTheBandIsFinishingRatherThanGivingUp(bool insideIsDone)
    {
        // The runaway reference of TheDeliveryReadingIsThePulsesAndNotTheHoldBeforeThem -- the only
        // thing that stalls a bus whose pulses arrive -- at the rate that leaves the residual in the
        // gap between the band and the entry, which is the gap this switch's threshold is about.
        const double DriftMetresPerSecondSquared = 0.08;


        (TrimBus bus, double3 from, double3 reference) = Coasting(WeakJets, WeakJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        double elapsed = 0.0;
        TrimCommand last = default;
        TrimCommand pending = default;

        while (elapsed < 200.0)
        {
            last = trim.Update(Step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci, from,
                reference + bus.NoseCci * (DriftMetresPerSecondSquared * elapsed), elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci, PulseSeconds: Pulse,
                StoppingInsideTheBandIsDone: insideIsDone));

            if (last.Done) break;

            bus.Step(Earth, pending.Fire, Step, pending.Pulse);
            pending = last;
            elapsed += Step;
        }

        double band = BusTrim.StopBand(last.Acceleration, Step);

        Out.WriteLine($"inside-is-done {insideIsDone}: {last.Said} | gave up {trim.GaveUp}, "
                      + $"left {last.ToGainMetresPerSecond:F4} against a {band:F4} band "
                      + $"and a {BusTrim.PulseEntry(band):F4} entry");

        Assert.True(last.Done, "the null never ended, so there is no verdict to read");

        // The regime, and the whole reason the threshold is PulseEntry: this residual is inside what a
        // hold may leave and outside the band itself, so the band alone would call it a failure.
        Assert.InRange(last.ToGainMetresPerSecond, band, BusTrim.PulseEntry(band));

        if (insideIsDone)
        {
            Assert.False(trim.GaveUp, "a trim that stopped inside its own band reported no actuator left");
            Assert.Contains("settled and stopped improving", last.Said);
        }
        else
        {
            Assert.True(trim.GaveUp, "the control arm did not give up, so the switch is doing nothing");
            Assert.Contains("stopped closing", last.Said);
        }
    }

    /// <summary>
    /// <b>A pulse phase cannot escape a direction that does nothing</b>, because the watch that would
    /// strike it off is skipped while pulsing — so the greedy pick returns to it for ever and the null
    /// ends on the stall clock with nothing struck off.
    /// </summary>
    /// <remarks>
    /// <para>This is the flown signature of <c>docs/ACCURACY-PLAN.md</c> 3fc: of 32 nulls at 12,902 km,
    /// the 11 that stalled fired on <b>two</b> directions and changed axis once in twelve to forty
    /// granted pulses, where the 21 that finished fired on five or six and changed about once per
    /// grant. It demonstrates that the mechanism is reachable; it does not show that it is what
    /// happened, which needs a flight.</para>
    ///
    /// <para>The phase must be entered with no hold before it — <see cref="BusTrim.Resume"/> keeps the
    /// measured thrust — because a hold <em>does</em> run the watch and would strike the direction off,
    /// which is the escape the pulse phase has not got.</para>
    /// </remarks>
    [Fact]
    public void APulsePhaseCannotEscapeADirectionThatDoesNothing()
    {
        (TrimBus bus, double3 from, double3 reference) = Coasting(WeakJets, WeakJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        // Hold first, so the thrusters are measured and the pulsing band is narrower than the hold's.
        Flight held = Fly(trim, bus, from, reference, pulseSeconds: 0.0);
        Assert.True(held.Last.Acceleration > 0.1 * WeakJets, "the first pass measured nothing");

        // Only now: a hold runs the watch, so a direction killed before it would simply be struck off.
        bus.Dead = TrimAxes.Down | TrimAxes.Up;
        Shove(bus, alongNose: 0.0, across: 0.004, under: 0.012);

        trim.Resume();
        Flight flight = Fly(trim, bus, from, reference, Pulse, since: held.Seconds, forSeconds: 200.0);

        Out.WriteLine($"{flight.Last.Said} | {flight.Seconds:F1} s, {flight.PulsingSeconds:F1} s pulsing");

        Assert.False(flight.EverHeld, "it held, so the watch ran and this is not the pulse-only regime");
        Assert.True(flight.EverPulsed, "it never pulsed");

        // The whole point: the direction that does nothing is never named, and the null ends on the
        // stall rather than on "nothing left aboard moves the bus".
        Assert.DoesNotContain("struck off", flight.Last.Said);
        Assert.Contains("stopped closing", flight.Last.Said);

        // And the reading tells the two stalls apart, which is what makes it worth keeping. A dead
        // direction shows as a fired fraction far under the engine's own allowance -- one grant per
        // PulseEverySeconds, about a tenth of the commands at this step -- because the grants spent on
        // it deliver nothing to measure. Flown, the two stalls of 3ez fired 123 of 1,200 and 94 of 886,
        // which IS the allowance, so neither of them was this.
        Match counts = Regex.Match(flight.Last.Said, @"(\d+) pulses commanded, (\d+) fired");
        Assert.True(counts.Success, flight.Last.Said);

        double fired = double.Parse(counts.Groups[2].Value) / double.Parse(counts.Groups[1].Value);
        double allowance = Step / TrimBus.PulseEverySeconds;

        Out.WriteLine($"  fired {fired * 100.0:F1}% of commands against a {allowance * 100.0:F1}% allowance");

        Assert.True(fired < 0.5 * allowance,
                    $"fired {fired * 100.0:F1}% of commands, which is the engine's own allowance -- so "
                    + "this fixture is not in the dead-direction regime it claims to be");
    }

    /// <summary>
    /// A pulse phase that stops closing gives way to holding, and the hold finishes the null —
    /// <see cref="IcbmConfig.StallFallsBackToHolding"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The regression is the 200 s bound, not the verdict.</b> The version this replaces held
    /// the fallback open on a clock that its own branch returned before advancing, so the trim fired
    /// nothing until <see cref="BusTrim.MaxSeconds"/> and gave up anyway — 110 s wasted and the
    /// residual worse. Its fixture flew to 60 s against a 120 s timeout and could not see any of it, so
    /// this one flies to 200 and asserts the null ended well inside the cap.
    /// <c>docs/ACCURACY-PLAN.md</c> 3fb.</para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APulsePhaseThatStopsClosingGivesWayToHolding(bool fallBack)
    {
        // The runaway reference of 3fd: faster than a pulse phase's 3.7 mm/s per second and far inside
        // what a hold can answer, which is the whole regime this switch is about.
        const double DriftMetresPerSecondSquared = 0.08;

        (TrimBus bus, double3 from, double3 reference) = Coasting(WeakJets, WeakJets);
        Shove(bus, 0.2);

        BusTrim trim = new();
        trim.Begin();

        double elapsed = 0.0;
        TrimCommand last = default;
        TrimCommand pending = default;
        bool gaveWay = false;

        while (elapsed < 200.0)
        {
            last = trim.Update(Step, new TrimSituation(
                Earth, bus.PositionCci, bus.VelocityCci, from,
                reference + bus.NoseCci * (DriftMetresPerSecondSquared * elapsed), elapsed,
                bus.NoseCci, bus.RightCci, bus.DownCci, PulseSeconds: Pulse,
                StallFallsBackToHolding: fallBack));

            gaveWay |= last.Said.Contains("holding instead");

            if (last.Done) break;

            bus.Step(Earth, pending.Fire, Step, pending.Pulse);
            pending = last;
            elapsed += Step;
        }

        Out.WriteLine($"fall-back {fallBack}: {last.Said} | {elapsed:F1} s, gave up {trim.GaveUp}");

        Assert.True(last.Done, "the null never ended");

        if (fallBack)
        {
            Assert.True(gaveWay, "the phase never gave way, so the switch did nothing");
            Assert.False(trim.GaveUp, $"the hold did not finish the null: {last.Said}");

            // The deleted version's failure, which its own fixture could not reach.
            Assert.True(elapsed < 0.5 * BusTrim.MaxSeconds,
                        $"the null ran {elapsed:F1} s against a {BusTrim.MaxSeconds:F0} s cap -- the "
                        + "fallback is waiting on a clock that is not advancing");
        }
        else
        {
            Assert.False(gaveWay, "the control arm gave way, so the switch is not what is doing it");
            Assert.True(trim.GaveUp);
            Assert.Contains("stopped closing", last.Said);
        }
    }
}
