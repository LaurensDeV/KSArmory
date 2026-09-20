using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What several targets actually cost a bus: how far apart they can be, how many fit, and whether
/// the window between the warhead's own radius and the budget is wide enough to be a feature.
///
/// <para><b>Measurement only.</b> Nothing here asserts an improvement; the assertions are sanity
/// checks on the rig, so a geometry that stops reconstructing fails loudly rather than printing
/// nonsense.</para>
///
/// <para><b>The reach is pinned, which is what the flight is on.</b>
/// <see cref="DivertFootprint.ArrivalClock.Pinned"/> is one projection off the same columns
/// <see cref="ReleaseFocus.FlownSensitivity"/> already flies per salvo, and it is the reach a player
/// has today — free-clock is 1.94x longer along the track at the 6,179 km gate and nothing across
/// it, and no release loop re-commits the arrival per destination.</para>
///
/// <para>The along-track reach is the one quoted throughout: pinned, the two axes agree to within a
/// few per cent, and along is the dearer, so a chain laid across the track is never worse than this
/// says.</para>
/// </summary>
public class ReleaseTradeTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private const double R = DeorbitShot.R;
    private const double Mu = DeorbitShot.Mu;

    private static ReleaseFocus.Air Air
        => new(new ImpactPredictor.Drag(DeorbitShot.DensityAt, Arsenal.ReentryVehicleMk21), 2.0, R, true);

    /// <summary>A bus somewhere on its coast, with the seconds it has left to arrival.</summary>
    private readonly record struct Shot(string Name, double3 PositionCci, double3 VelocityCci,
                                        double CoastSeconds);

    /// <summary>
    /// The state a warhead is released on at 6,179 km, off the flown log — <c>~/shots/2026-09-17-threearm</c>
    /// shot 001, <c>GeoSat FAT_1</c> round 1, 359.3 s of flight left — carried back to its own cutoff.
    /// </summary>
    private static Shot Cutoff6179
    {
        get
        {
            double3 p = new(3_835_254.4, -5_998_331.6, -1_302_454.7);
            double3 v = new(279.7753, 2844.5295, -3967.0558);

            Assert.True(Kepler.TryCoast(Mu, p, -v, 1340.0 - 359.3, out double3 back, out double3 backV));

            return new Shot("6,179 km", back, -backV, 1340.0);
        }
    }

    /// <summary>The 12,902 km shot, reconstructed off its own log's cutoff altitude, range and flight time.</summary>
    private static Shot Cutoff12902
    {
        get
        {
            double3 from = new(R + 157_000.0, 0, 0);
            double theta = 12_902_000.0 / R;
            double3 arrival = new(R * Math.Cos(theta), R * Math.Sin(theta), 0);

            Assert.True(BallisticArc.TrySolve(Earth, from, Earth.UncarryCci(arrival, 1881.0), 1881.0,
                                              out BallisticArc.Solution arc),
                        "12,902 km does not reconstruct");

            return new Shot("12,902 km", from, arc.RequiredVelocityCci, 1881.0);
        }
    }

    /// <summary>What a metre a second of divert is worth on the ground, <paramref name="elapsed"/> into the coast.</summary>
    private static double ReachAt(Shot shot, double elapsed)
    {
        double3 p = shot.PositionCci, v = shot.VelocityCci;

        if (elapsed > 0.0) Assert.True(Kepler.TryCoast(Mu, p, v, elapsed, out p, out v));

        ReleaseFocus.FlownSensitivity columns =
            ReleaseFocus.FlownSensitivity.TryFly(Earth, p, v, Air)
            ?? throw new InvalidOperationException($"{shot.Name} at t+{elapsed:F0} s did not come down");

        Assert.True(DivertFootprint.TryFrom(Earth, columns, DivertFootprint.ArrivalClock.Pinned,
                                            fromTheRealState: true, out DivertFootprint f));

        return f.AlongTrackMetresPerMetrePerSecond;
    }

    /// <summary>
    /// The reach at each of <paramref name="targets"/> releases, for a schedule that starts where it
    /// is told to.
    /// </summary>
    /// <param name="fromCutoff">
    /// Seconds into the coast the first release happens. Zero is the naive loop that starts the
    /// moment the bus is free; <see cref="ReleaseItinerary"/>'s own schedule starts late enough that
    /// the last release lands on the gate.
    /// </param>
    private static double[] Reach(Shot shot, int targets, double fromCutoff, double hopSeconds)
    {
        double[] reach = new double[targets];
        for (int k = 0; k < targets; k++) reach[k] = ReachAt(shot, fromCutoff + (k * hopSeconds));

        return reach;
    }

    /// <summary>Where an end-on-the-gate itinerary of <paramref name="targets"/> starts, seconds into the coast.</summary>
    private static double GateStart(Shot shot, int targets, double gateSeconds, double hopSeconds)
        => shot.CoastSeconds - gateSeconds - ((targets - 1) * hopSeconds);

    private static ReleaseItinerary.Bus BusFor(Shot shot)
        => new(new IcbmConfig().ReleaseBeforeArrivalSeconds, shot.CoastSeconds, Warheads: 6);

    // ---------------------------------------------------------------------------------------
    // 1. How far apart six targets can be
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The trade the six-target question actually turns on: with the whole budget spent on hops,
    /// how long a chain of six does it buy, and how far apart are its neighbours?
    /// </summary>
    /// <remarks>
    /// Priced both ways round the schedule decision — a loop that starts the moment the bus is free,
    /// and <see cref="ReleaseItinerary"/>'s own, which starts late enough that the last release still
    /// lands on <see cref="IcbmConfig.ReleaseBeforeArrivalSeconds"/>. The gap between them is what
    /// ending on the gate costs.
    /// </remarks>
    [Fact]
    public void HowFarApartSixTargetsCanBe()
    {
        const int targets = 6;
        double hop = ReleaseItinerary.MedianHopSeconds;

        Out.WriteLine($"six targets, {ReleaseItinerary.SingleTargetMetresPerSecond:F1} m/s already spent "
                      + $"of {PostBoostAim.MaxTrimMetresPerSecond:F0}, hops {hop:F1} s apart\n");

        foreach (Shot shot in new[] { Cutoff6179, Cutoff12902 })
        {
            ReleaseItinerary.Bus bus = BusFor(shot);

            foreach ((string what, double start) in
                     new[]
                     {
                         ("from cutoff", 0.0),
                         ("ending on the gate", GateStart(shot, targets, bus.GateSeconds, hop)),
                     })
            {
                double[] reach = Reach(shot, targets, start, hop);
                double spacing = ReleaseItinerary.SpacingWithin(targets, reach, bus);

                Out.WriteLine($"{shot.Name}, {what} (first release t+{start:F0} s of a "
                              + $"{shot.CoastSeconds:F0} s coast)");
                Out.WriteLine($"   reach per m/s: {string.Join(" -> ", reach.Select(r => $"{r:F0}"))} m");
                Out.WriteLine($"   widest neighbour spacing {spacing / 1000.0:F1} km, "
                              + $"so a chain {(targets - 1) * spacing / 1000.0:F1} km end to end");

                ReleaseItinerary at = ReleaseItinerary.Chain(targets, spacing, reach, bus);

                Out.WriteLine($"   {at.Describe()}");
                Assert.Equal(targets, at.Fits);
            }

            Out.WriteLine("");
        }
    }

    // ---------------------------------------------------------------------------------------
    // 2. The trade the other way round
    // ---------------------------------------------------------------------------------------

    /// <summary>How many targets a budget reaches at a spacing somebody would actually pick.</summary>
    [Fact]
    public void HowManyTargetsFitAtAGivenSpacing()
    {
        double[] spacings = [4_000.0, 10_000.0, 25_000.0, 50_000.0];
        double hop = ReleaseItinerary.MedianHopSeconds;

        foreach (Shot shot in new[] { Cutoff6179, Cutoff12902 })
        {
            ReleaseItinerary.Bus bus = BusFor(shot);

            foreach ((string what, Func<int, double> start) in
                     new (string, Func<int, double>)[]
                     {
                         ("from cutoff", _ => 0.0),
                         ("ending on the gate", n => GateStart(shot, n, bus.GateSeconds, hop)),
                     })
            {
                Out.WriteLine($"{shot.Name}, {what}");

                foreach (double spacing in spacings)
                {
                    // The schedule moves with the count, so the reach a sixth target is priced at is
                    // not the reach a third is: ask at the size being tested.
                    int fits = 1;
                    for (int n = 2; n <= bus.Warheads; n++)
                    {
                        double[] reach = Reach(shot, n, start(n), hop);
                        if (ReleaseItinerary.TargetsWithin(spacing, reach, bus) >= n) fits = n;
                    }

                    double[] atFits = Reach(shot, fits, start(fits), hop);
                    ReleaseItinerary plan = ReleaseItinerary.Chain(fits, spacing, atFits, bus);

                    Out.WriteLine($"   {spacing / 1000.0,5:F0} km apart: {fits} target(s), "
                                  + $"{plan.NeedsMetresPerSecond:F1} m/s, "
                                  + $"{plan.LeftMetresPerSecond:F1} left -- which still buys "
                                  + $"{plan.LeftBuysMetres / 1000.0:F0} km");
                }

                Out.WriteLine("");
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // 3. The floor the weapon puts under it
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the window is wide enough to be a feature at all: bounded below by the warhead, which
    /// makes anything nearer a pattern rather than a second target, and above by the budget.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is asserted about the width</b>, because the width is the answer. A window under
    /// 1x says every set that size is one pattern under another name at that geometry and schedule,
    /// which is a fact about the design rather than a regression.
    /// </remarks>
    [Fact]
    public void TheWindowBetweenTheWarheadAndTheBudget()
    {
        double charge = Arsenal.ReentryVehicleMk21.ChargeKg;
        double lethal = Warhead.LethalRadius(charge);
        double blast = Warhead.BlastRadius(charge);

        Out.WriteLine($"Mk 21: lethal {lethal / 1000.0:F1} km, blast {blast / 1000.0:F1} km -- "
                      + "two targets inside one blast radius are a pattern, not two targets\n");

        double hop = ReleaseItinerary.MedianHopSeconds;

        foreach (Shot shot in new[] { Cutoff6179, Cutoff12902 })
        {
            ReleaseItinerary.Bus bus = BusFor(shot);

            foreach ((string what, bool onTheGate) in new[] { ("from cutoff", false), ("on the gate", true) })
            {
                for (int targets = 2; targets <= 6; targets++)
                {
                    double start = onTheGate ? GateStart(shot, targets, bus.GateSeconds, hop) : 0.0;
                    double[] reach = Reach(shot, targets, start, hop);
                    double widest = ReleaseItinerary.SpacingWithin(targets, reach, bus);

                    Assert.True(widest > 0.0 && double.IsFinite(widest), $"{shot.Name}: no spacing at all");

                    Out.WriteLine($"{shot.Name,-10} {what,-11} {targets} targets: "
                                  + $"{blast / 1000.0:F0} to {widest / 1000.0,5:F1} km of spacing, "
                                  + $"window {widest / blast,5:F2}x"
                                  + (widest < blast ? "  -- a pattern, not a set of targets" : ""));
                }

                Out.WriteLine("");
            }
        }
    }
}
