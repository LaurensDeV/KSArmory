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
}
