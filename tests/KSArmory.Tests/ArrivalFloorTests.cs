using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The arrival-angle floor: what it constrains, what it refuses, and what it costs.
///
/// <para><c>docs/ARRIVAL-ANGLE.md</c> is why it exists — the arrival angle is the dominant
/// precision lever and was an output of a delta-v minimisation, askable for only through
/// <c>IcbmConfig.Loft</c>, which from orbit could invert it. <c>ArrivalAngleTests</c> measures what
/// a steeper arrival is worth; this measures the control that asks for one.</para>
///
/// <para><b>The planet sits at the origin and does not move</b>, per <see cref="DeorbitShot"/>, so
/// nothing here can see an epoch fault.</para>
/// </summary>
public class ArrivalFloorTests(ITestOutputHelper Out)
{
    private const double Mu = DeorbitShot.Mu;
    private const double R = DeorbitShot.R;

    /// <summary>Exhaust velocity of the stage <c>DeorbitTests</c> flies, for the mass ratios.</summary>
    private const double ExhaustVelocity = 3_100.0;

    /// <summary>A 400 km circular platform, which is the geometry the budget is written against.</summary>
    private const double Altitude = 400_000.0;

    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double3 Platform => new(R + Altitude, 0, 0);
    private static double3 Circular => new(0, Math.Sqrt(Mu / (R + Altitude)), 0);

    private static double3 Ahead(double degrees)
    {
        double a = degrees * Math.PI / 180.0;
        return new double3(R * Math.Cos(a), R * Math.Sin(a), 0);
    }

    // What the arc actually does through the air, which is the angle that lands rather than the
    // one the solver reasoned about. Departing from where the platform will be after the wait,
    // because a window that waits an orbit is a shot from somewhere else entirely.
    private static ImpactPredictor.Impact FlyWindow(in BurnWindow.Window window)
    {
        double3 from = Platform;

        if (window.WaitSeconds > 0.0)
        {
            Assert.True(Kepler.TryCoast(Mu, Platform, Circular, window.WaitSeconds, out from, out _));
        }

        Assert.True(ImpactPredictor.TryPredict(Earth, from, window.Arc.RequiredVelocityCci, 1.0, 40_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt,
                                                                        DeorbitShot.Warhead)));
        return hit;
    }

    /// <summary>
    /// The floor is off at zero, and off means the search it was added to is the search that was
    /// there before.
    ///
    /// <para>Pinned to the numbers the reference shot solved to before the floor existed rather
    /// than to a re-derivation, because a bound threaded through a search is exactly the kind of
    /// change that moves an answer by a metre a second and passes every relative test.</para>
    /// </summary>
    [Fact]
    public void TheDefaultSolvesTheSameShotItAlwaysDid()
    {
        BallisticArc.Solution shot = DeorbitShot.Shot(out double3 _, out double3 _);

        // Measured on the code before the floor was threaded through, and reproduced by it exactly.
        Assert.Equal(487.3176637923442, shot.CheapestFlightSeconds, 9);
        Assert.Equal(487.3176637923442, shot.FlightSeconds, 9);
        Assert.Equal(-309.36951058623885, shot.RequiredVelocityCci.X, 9);
        Assert.Equal(7579.903943498994, shot.RequiredVelocityCci.Y, 9);
        Assert.Equal(0.0, shot.RequiredVelocityCci.Z, 9);
    }

    /// <summary>
    /// Passing the floor explicitly as zero is the same call as not passing it at all, which is
    /// what makes every existing caller free.
    /// </summary>
    [Fact]
    public void ZeroIsTheSameSearchAsSayingNothing()
    {
        foreach (double aheadDeg in new[] { 5.0, 20.0, 45.0, 90.0 })
        {
            double3 aim = Ahead(aheadDeg);

            Assert.True(BallisticArc.TryCheapest(Earth, Platform, Circular, aim,
                                                 out BallisticArc.Solution said));
            Assert.True(BallisticArc.TryCheapest(Earth, Platform, Circular, aim,
                                                 out BallisticArc.Solution zero,
                                                 minArrivalDeg: 0.0));

            Assert.Equal(said.FlightSeconds, zero.FlightSeconds);
            Assert.Equal(said.RequiredVelocityCci.X, zero.RequiredVelocityCci.X);
            Assert.Equal(said.RequiredVelocityCci.Y, zero.RequiredVelocityCci.Y);
            Assert.Equal(said.RequiredVelocityCci.Z, zero.RequiredVelocityCci.Z);

            Assert.True(BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                           out BurnWindow.Window plain));
            Assert.True(BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                           out BurnWindow.Window floored, 1.0, 0.0));

            Assert.Equal(plain.WaitSeconds, floored.WaitSeconds);
            Assert.Equal(plain.Cost, floored.Cost);
        }
    }

    /// <summary>
    /// What the floor was built for: <c>Loft</c> can invert the arrival from orbit, and the floor
    /// holds whatever loft does.
    ///
    /// <para>Measured without it, a 556 km shot arrives at 33.9 degrees at loft 1.0 and
    /// <b>6.2</b> at loft 1.8 — raising loft makes leaving now dearer too, the saving from waiting
    /// clears <c>IcbmProgram.WaitMustSaveMetresPerSecond</c>, and the computer defers an hour and a
    /// half to take the shallowest arrival available. The operator asked for steeper and got the
    /// graze. <c>ArrivalAngleTests.TheWindowSearchPrefersTheGrazeAndLoftCanMakeItWorse</c> is that
    /// same measurement kept.</para>
    /// </summary>
    [Fact]
    public void TheFloorHoldsAcrossEveryLoftThePanelOffers()
    {
        const double Floor = 15.0;

        double3 aim = Ahead(5.0);

        Out.WriteLine($"556 km ahead of a {Altitude / 1000:F0} km platform, floor {Floor:F0} deg:");

        foreach (double loft in new[] { 0.6, 1.0, 1.4, 1.8 })
        {
            Assert.True(BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                           out BurnWindow.Window open, loft, Floor));

            Assert.True(BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                           out BurnWindow.Window free, loft));

            Out.WriteLine($"  loft {loft:F1}: floored waits {open.WaitSeconds,7:F0} s, "
                          + $"costs {open.Cost,7:F0} m/s, arrives {open.Arc.ArrivalAngleDeg,6:F2} deg"
                          + $"   |   free waits {free.WaitSeconds,7:F0} s, costs {free.Cost,7:F0} m/s, "
                          + $"arrives {free.Arc.ArrivalAngleDeg,6:F2} deg");

            Assert.True(open.Arc.ArrivalAngleDeg >= Floor - 1e-6,
                        $"loft {loft:F1} arrives at {open.Arc.ArrivalAngleDeg:F2} deg, under the "
                        + $"{Floor:F0} deg floor");
        }
    }

    /// <summary>
    /// The inversion itself, as the property rather than as a number: with the floor set, asking
    /// for more loft can no longer make the arrival shallower.
    /// </summary>
    [Fact]
    public void MoreLoftCannotArriveShallowerOnceTheFloorIsSet()
    {
        const double Floor = 15.0;

        double3 aim = Ahead(5.0);

        Assert.True(BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                       out BurnWindow.Window flat, 1.0, Floor));
        Assert.True(BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                       out BurnWindow.Window lofted, 1.8, Floor));

        Assert.True(lofted.Arc.ArrivalAngleDeg >= Floor,
                    $"loft 1.8 inverted the arrival to {lofted.Arc.ArrivalAngleDeg:F2} deg against "
                    + $"{flat.Arc.ArrivalAngleDeg:F2} at loft 1.0");
    }

    /// <summary>
    /// A depressed shot is what the floor is for, so loft below one does not walk out of it.
    ///
    /// <para>Loft multiplies whatever the search settled on, so left unchecked a 0.6 flattens a
    /// satisfying arc straight back under the floor.</para>
    /// </summary>
    [Fact]
    public void LoftBelowOneCannotFlattenAnArcBackUnderTheFloor()
    {
        const double Floor = 20.0;

        double3 aim = Ahead(20.0);

        Assert.True(BallisticArc.TryCheapest(Earth, Platform, Circular, aim,
                                             out BallisticArc.Solution s, 0.6, false,
                                             double.NaN, Floor));

        Assert.True(s.ArrivalAngleDeg >= Floor - 1e-6,
                    $"a 0.6 loft flattened the shot to {s.ArrivalAngleDeg:F2} deg");
    }

    /// <summary>
    /// The seed is the constrained answer, and a constraint is idempotent where a multiplier is
    /// not: re-solving from what it returned returns the same thing.
    ///
    /// <para>That is what lets the floor coexist with the loft trap. A lofted flight time fed back
    /// as a seed is lofted again on the next cycle and the shot runs away — 162 km, measured, per
    /// <c>docs/ICBM-GUIDANCE.md</c>. A floor cannot do that, because the arc that satisfied it
    /// satisfies it again.</para>
    /// </summary>
    [Fact]
    public void ReSolvingFromItsOwnAnswerDoesNotWalkTheShotOut()
    {
        const double Floor = 18.0;

        double3 aim = Ahead(20.0);
        double seed = double.NaN;
        double first = double.NaN;

        for (int cycle = 0; cycle < 12; cycle++)
        {
            Assert.True(BallisticArc.TryCheapest(Earth, Platform, Circular, aim,
                                                 out BallisticArc.Solution s, 1.4, false, seed, Floor));

            Assert.True(s.ArrivalAngleDeg >= Floor - 1e-6);

            if (cycle == 0) first = s.FlightSeconds;

            Assert.True(Math.Abs(s.FlightSeconds - first) < 0.01,
                        $"cycle {cycle} flies {s.FlightSeconds:F1} s against the first cycle's "
                        + $"{first:F1} - the shot is walking out");

            seed = s.CheapestFlightSeconds;
        }

        Out.WriteLine($"twelve cycles at loft 1.4 with an {Floor:F0} deg floor all fly {first:F1} s");
    }

    /// <summary>
    /// A floor no arc satisfies is refused as its own failure, and named as one.
    ///
    /// <para>Mid-ascent the search is over flight time alone, and from a state that has already
    /// committed to a direction the steep arcs to a distant target go through the planet — 90
    /// degrees round from a 60 km pick-up reaches 46 degrees of arrival and no further. Reporting
    /// that as "no trajectory reaches that target" sends the operator after a different target when
    /// the answer is a lower floor.</para>
    /// </summary>
    [Fact]
    public void AFloorNothingReachesIsNamedRatherThanReportedAsAnUnreachableTarget()
    {
        IcbmConfig config = new() { Armed = true, MinArrivalAngleDeg = 55.0 };
        IcbmProgram program = new(config);

        IcbmCommand command = program.Update(0.0, Ascending(Ahead(90.0)));

        Out.WriteLine($"phase {command.Phase}, reach {command.Reach}: {command.Hold}");

        Assert.True(command.Reach == IcbmReach.TooShallow,
                    $"a 55 deg floor nothing reaches was reported as {command.Reach} in "
                    + $"{command.Phase}: {command.Hold}");

        Assert.Equal(IcbmPhase.NoSolution, command.Phase);
        Assert.Contains("55 deg or steeper", command.Hold);

        // The same shot with the floor off is not this failure at all, which is the whole point of
        // separating the two.
        IcbmProgram unbounded = new(new IcbmConfig { Armed = true });

        Assert.NotEqual(IcbmReach.TooShallow, unbounded.Update(0.0, Ascending(Ahead(90.0))).Reach);
    }

    /// <summary>
    /// The orbital half of the same refusal, which goes through the window search.
    ///
    /// <para><b>From orbit a floor is a price rather than a wall</b>, and that is worth knowing
    /// before reading this test as contrived. Searching a day of departures, every floor short of
    /// vertical is satisfiable — measured from a 400 km platform at 45, 60, 75, 85 and 89.5
    /// degrees, costing 5.0, 6.2, 7.5, 7.4 and 8.1 km/s, which is the orbital velocity being
    /// spent. Only a demand to arrive <em>exactly</em> vertically has no window at all, because a
    /// transfer arriving radially at a point is the degenerate case where no plane is determined.
    /// Mid-ascent is where a floor is a wall, and
    /// <see cref="AFloorNothingReachesIsNamedRatherThanReportedAsAnUnreachableTarget"/> is
    /// that.</para>
    /// </summary>
    [Fact]
    public void AFloorNoWindowReachesIsNamedToo()
    {
        IcbmConfig config = new() { Armed = true, MinArrivalAngleDeg = 90.0 };
        IcbmProgram program = new(config);

        IcbmCommand command = program.Update(0.0, InOrbit(Ahead(5.0)));

        Out.WriteLine($"phase {command.Phase}, reach {command.Reach}: {command.Hold}");

        Assert.True(command.Reach == IcbmReach.TooShallow,
                    $"a 90 deg floor no window reaches was reported as {command.Reach} in "
                    + $"{command.Phase}: {command.Hold}");

        Assert.Contains("90 deg or steeper", command.Hold);
    }

    /// <summary>
    /// The floor is measured on the arc, because the arc is what the search has: the drag model
    /// lives in <c>ImpactPredictor</c>, and flying one per candidate flight time would put a
    /// trajectory integration inside a golden section.
    ///
    /// <para>What that costs is measured here. Over 10 to 30 degrees the arc and the flown arrival
    /// agree to within half a degree, and drag only bends the answer where the arrival is already a
    /// graze — a 3.6 degree arc arrives at 7.1, per <c>docs/ARRIVAL-ANGLE.md</c>. So the floor is
    /// conservative at the shallow end and optimistic at the steep, by less than the resolution
    /// anyone would set it to.</para>
    /// </summary>
    [Fact]
    public void TheArcAndWhatLandsAgreeToWithinHalfADegree()
    {
        Out.WriteLine("floor | arc arrives | flown arrives | downrange");

        foreach (double floor in new[] { 10.0, 12.0, 15.0, 20.0, 30.0 })
        {
            double3 aim = Ahead(20.0);

            Assert.True(BallisticArc.TryCheapest(Earth, Platform, Circular, aim,
                                                 out BallisticArc.Solution s, 1.0, false,
                                                 double.NaN, floor));

            Assert.True(ImpactPredictor.TryPredict(Earth, Platform, s.RequiredVelocityCci, 1.0, 40_000.0,
                                                   out ImpactPredictor.Impact hit, null, null,
                                                   new ImpactPredictor.Drag(DeorbitShot.DensityAt,
                                                                            DeorbitShot.Warhead)));

            double flown = BallisticArc.DescentAngleDeg(hit.PointCci, hit.VelocityCci);

            Out.WriteLine($"  {floor,5:F0} | {s.ArrivalAngleDeg,11:F2} | {flown,13:F2} | "
                          + $"{R * Vec.AngleBetween(Platform, hit.PointCci) / 1000,6:F0} km");

            Assert.True(Math.Abs(flown - s.ArrivalAngleDeg) < 0.5,
                        $"a {floor:F0} deg arc landed at {flown:F2} deg");
        }
    }

    /// <summary>
    /// What the floor costs, which is propellant and reach and nothing else.
    ///
    /// <para>The table in <c>docs/ARRIVAL-ANGLE.md</c> is this trade approached from the other
    /// side — it brakes a platform by hand and reads off the arrival. This asks the guidance for
    /// the arrival and reads off the brake, which is the number an operator actually spends.</para>
    /// </summary>
    [Fact]
    public void WhatAFlooredShotCosts()
    {
        foreach (double aheadDeg in new[] { 20.0, 45.0 })
        {
            double3 aim = Ahead(aheadDeg);

            Out.WriteLine($"{R * aheadDeg * Math.PI / 180.0 / 1000:F0} km ahead of a "
                          + $"{Altitude / 1000:F0} km platform:");

            foreach (double floor in new[] { 0.0, 10.0, 15.0, 20.0, 30.0 })
            {
                if (!BurnWindow.TryFind(Earth, Platform, Circular, aim,
                                        out BurnWindow.Window w, 1.0, floor))
                {
                    Out.WriteLine($"  floor {floor,4:F0} deg: no window");
                    continue;
                }

                ImpactPredictor.Impact hit = FlyWindow(w);
                double ratio = Math.Exp(w.Cost / ExhaustVelocity);

                Out.WriteLine($"  floor {floor,4:F0} deg: waits {w.WaitSeconds,7:F0} s, "
                              + $"burns {w.Cost,6:F0} m/s, arc arrives {w.Arc.ArrivalAngleDeg,6:F2} deg, "
                              + $"flown {BallisticArc.DescentAngleDeg(hit.PointCci, hit.VelocityCci),6:F2} deg, "
                              + $"{100.0 / ratio,5:F1}% of the stack arrives, "
                              + $"impact {Vec.Len(hit.VelocityCci),5:F0} m/s");
            }
        }
    }

    // A stack climbing away with air still on it, which is the phase whose solve goes straight to
    // BallisticArc rather than through the window search.
    private static IcbmState Ascending(double3 aim)
        => new(Earth, new double3(R + 60_000.0, 0, 0), new double3(500.0, 2500.0, 0), aim,
               HasAim: true, new BoosterPerformance(400_000.0, 140.0, 20_000.0, 8_000.0),
               DeorbitShot.DensityAt(new double3(R + 60_000.0, 0, 0)), PropellantAvailable: true);

    // Above the air with the engines out, which is the phase that holds for a window.
    private static IcbmState InOrbit(double3 aim)
        => new(Earth, Platform, Circular, aim, HasAim: true,
               new BoosterPerformance(400_000.0, 140.0, 20_000.0, 8_000.0), 0.0,
               PropellantAvailable: true);
}
