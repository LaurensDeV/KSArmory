using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// The 3,459 km near-orbital shot, taken apart into named terms with a number each.
///
/// <para>Measurement only. Nothing here asserts an improvement — every test either reports a
/// number or pins a fact that was measured, so the budget can be re-run after a change.</para>
/// </summary>
public class ErrorBudgetTests(ITestOutputHelper Out)
{
    private const double Mu = DeorbitShot.Mu;
    private const double R = DeorbitShot.R;
    private const double ScaleHeight = DeorbitShot.ScaleHeight;
    private const double EarthSpin = DeorbitShot.EarthSpin;

    /// <summary>The flown residual this budget is being spent against.</summary>
    private const double FlownResidual = 0.36;

    /// <summary>
    /// The speed <c>Ksa/BallisticScenario.cs</c> asks for once the salvo is away, which is what
    /// sets the frame step the coast is flown at. <c>WarpPolicy</c> then slows it for the entry.
    /// </summary>
    private const double BallisticScenarioWarp = 8.0;

    private static BallisticBody Earth => DeorbitShot.Earth;

    private static double DensityAt(double3 pointCci) => DeorbitShot.DensityAt(pointCci);

    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    private static double GroundMetres(double3 a, double3 b) => DeorbitShot.GroundMetres(a, b);

    /// <summary>
    /// The shot: picked up at near-orbital speed 200 km up, aimed 3,459 km downrange.
    /// </summary>
    private static BallisticArc.Solution Shot(out double3 from, out double3 target)
        => DeorbitShot.Shot(out from, out target);

    [Fact]
    public void WhatTheGeometryIs()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);

        Out.WriteLine($"cutoff at {Vec.Len(from) - R:F0} m altitude");
        Out.WriteLine($"required speed {Vec.Len(arc.RequiredVelocityCci):F1} m/s");
        Out.WriteLine($"circular at that radius {Math.Sqrt(Mu / Vec.Len(from)):F1} m/s");
        Out.WriteLine($"flight time {arc.FlightSeconds:F0} s");

        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact vac));
        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact air, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));

        double gammaVac = 90.0 - Vec.AngleBetween(vac.PointCci, vac.VelocityCci) * 180.0 / Math.PI;
        double gammaAir = 90.0 - Vec.AngleBetween(air.PointCci, air.VelocityCci) * 180.0 / Math.PI;

        Out.WriteLine($"vacuum arrival: {arc.FlightSeconds:F0} s, gamma {-gammaVac:F2} deg, "
                      + $"{Vec.Len(vac.VelocityCci):F0} m/s, {GroundMetres(vac.GroundFixedPointCci, target) / 1000.0:F1} km from target");
        Out.WriteLine($"drag arrival:   {air.Seconds:F0} s, gamma {-gammaAir:F2} deg, "
                      + $"{Vec.Len(air.VelocityCci):F0} m/s, {GroundMetres(air.GroundFixedPointCci, target) / 1000.0:F1} km from target");
        Out.WriteLine($"drag costs {GroundMetres(vac.GroundFixedPointCci, air.GroundFixedPointCci) / 1000.0:F1} km of range");
    }

    /// <summary>
    /// Term 1. How far the impact moves per metre a second at cutoff, measured by perturbing the
    /// cutoff velocity along each of three axes and flying the predictor.
    /// </summary>
    [Fact]
    public void HowMuchOneMetrePerSecondAtCutoffIsWorth()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double3 v = arc.RequiredVelocityCci;

        double3 prograde = Vec.Unit(v);
        double3 radial = Vec.Unit(from);
        double3 cross = Vec.Unit(Vec.Cross(radial, prograde));

        (string name, double3 axis)[] axes =
            [("prograde", prograde), ("radial", radial), ("cross-track", cross)];

        double3 baseline = Land(from, v);
        Out.WriteLine($"baseline lands {GroundMetres(baseline, target) / 1000.0:F2} km from the target");

        double worst = 0.0;
        double sumOfSquares = 0.0;

        foreach ((string name, double3 axis) in axes)
        {
            const double delta = 0.5;
            double3 plus = Land(from, v + axis * delta);
            double3 minus = Land(from, v - axis * delta);

            double perMetre = GroundMetres(plus, minus) / (2.0 * delta);
            worst = Math.Max(worst, perMetre);
            sumOfSquares += perMetre * perMetre;

            Out.WriteLine($"  {name,-12}: {perMetre:F0} m per m/s  "
                          + $"-> {perMetre * FlownResidual:F0} m at the flown {FlownResidual} m/s");
        }

        // The residual's direction is not recorded, so the honest single number is the root mean
        // square over the three axes rather than the worst of them.
        double isotropic = Math.Sqrt(sumOfSquares / axes.Length);

        Out.WriteLine($"worst axis {worst * FlownResidual / 1000.0:F2} km, "
                      + $"root mean square {isotropic * FlownResidual / 1000.0:F2} km, "
                      + $"at the flown {FlownResidual} m/s");
    }

    /// <summary>Where a warhead released from this state comes down, as a place on the ground.</summary>
    private static double3 Land(double3 fromCci, double3 velocityCci)
        => DeorbitShot.Land(fromCci, velocityCci);

    /// <summary>
    /// Term 3. The predictor and the round flown from one identical cutoff state.
    ///
    /// <para>Any disagreement here is a miss the correction loop cannot see, because the loop's
    /// only observer is the predictor.</para>
    /// </summary>
    [Fact]
    public void ThePredictorAgainstTheRoundItPredicts()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double3 v = arc.RequiredVelocityCci;

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact predicted, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));

        foreach (double dt in new[] { 1.0 / 60.0, 0.05, 0.32 })
        {
            (double3 landed, double seconds) = FlyTheRound(from, v, dt);
            double apart = GroundMetres(predicted.GroundFixedPointCci, landed);

            Out.WriteLine($"a {dt * 1000:F0} ms frame lands {apart:F0} m from the prediction "
                          + $"({seconds - predicted.Seconds:+0.000;-0.000} s later)");
        }

        // And the predictor's own coarse step, which is what the computer actually uses.
        foreach (double step in new[] { 0.5, 1.0, 2.0, 4.0 })
        {
            Assert.True(ImpactPredictor.TryPredict(Earth, from, v, step, 20_000.0,
                                                   out ImpactPredictor.Impact at, null, null,
                                                   new ImpactPredictor.Drag(DensityAt, Warhead)));
            Out.WriteLine($"predictor at a {step:F1} s coarse step: "
                          + $"{GroundMetres(predicted.GroundFixedPointCci, at.GroundFixedPointCci):F0} m "
                          + "from the 2 s answer");
        }
    }

    /// <summary>
    /// The round as the game flies it, which is now what asking for nothing gives.
    ///
    /// <param name="beforeGravityPerSubStep">
    /// Fly the pre-2026-08-24 round instead, for a budget that wants to show what that change was
    /// worth. It also held the air's own motion, which the shipped round still does.
    /// </param>
    /// </summary>
    private static (double3 GroundFixed, double Seconds) FlyTheRound(double3 fromCci, double3 velocityCci,
                                                                     double dt,
                                                                     bool beforeGravityPerSubStep = false)
        => DeorbitShot.FlyTheRound(fromCci, velocityCci, dt,
                                   beforeGravityPerSubStep
                                       ? DeorbitShot.Refresh.BeforeGravityPerSubStep
                                       : DeorbitShot.Refresh.AsFlown);

    /// <summary>
    /// Term 3, at the step the world is actually held to: coarse through the vacuum coast, fine
    /// once there is air. That is what <c>WarpPolicy</c> asks for through
    /// <c>IProjectile.FaithfulStepSeconds</c>, and it is not the same as either constant step.
    /// </summary>
    [Fact]
    public void ThePredictorAgainstTheRoundAtTheStepTheWorldIsHeldTo()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 _);
        double3 v = arc.RequiredVelocityCci;

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact predicted, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));

        (double3 landed, double _) =
            DeorbitShot.FlyTheRoundAsWarped(from, v, BallisticScenarioWarp);

        Out.WriteLine($"held to the round's own faithful step: "
                      + $"{GroundMetres(predicted.GroundFixedPointCci, landed):F0} m from the prediction");
    }

    /// <summary>
    /// Term 4. What the crossing search leaves on the table, and what a metre of it is worth
    /// downrange on an arrival this shallow.
    /// </summary>
    [Fact]
    public void WhatTheGroundCrossingCosts()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 _);
        double3 v = arc.RequiredVelocityCci;

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));

        double depth = R - Vec.Len(hit.PointCci);
        double gamma = Vec.AngleBetween(hit.PointCci, hit.VelocityCci) - Math.PI / 2.0;
        double groundPerHeight = 1.0 / Math.Tan(gamma);

        Out.WriteLine($"the predictor stops {depth * 100:F1} cm below the surface "
                      + $"(tolerance {ImpactPredictor.CrossingToleranceMetres * 100:F0} cm)");
        Out.WriteLine($"arrival gamma {gamma * 180.0 / Math.PI:F2} deg, "
                      + $"{groundPerHeight:F1} m of ground per m of height");
        Out.WriteLine($"so the crossing is worth {depth * groundPerHeight:F2} m downrange");

        // And the round's own crossing, which is a linear walk-back inside one 5 ms sub-step
        // against a sphere sampled once a frame, not a bisection on depth.
        BallisticBody body = Earth;
        Slug round = new(from, v, null, 1, from, Vec.Zero)
        {
            Munition = Warhead,
            Ground = new DeorbitShot.Ball(),
            AirDensityAt = (pos, _) => DensityAt(pos),
        };

        for (double t = 0.0; t < 20_000.0 && round.State == RoundState.Flying; t += 1.0 / 60.0)
        {
            round.Update(1.0 / 60.0, null, body.GravityCci(round.PositionEcl),
                         body.GroundVelocityCci(round.PositionEcl), from, Warhead,
                         DensityAt(round.PositionEcl));
        }

        double roundDepth = R - Vec.Len(round.PositionEcl);
        Out.WriteLine($"the round stops {roundDepth * 100:F1} cm below it, "
                      + $"worth {roundDepth * groundPerHeight:F2} m downrange");
    }

    /// <summary>
    /// Term 2. Six tubes on a six-degree cone, one attitude, 2 m/s off each — what that alone
    /// spreads the group by on this trajectory.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.37)]
    public void WhatTheTubeCantsSpreadTheGroupBy(double rollTurns)
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double3 v = arc.RequiredVelocityCci;

        // The bus holds the line its burn ended on. On a near-orbital deorbit that is retrograde.
        double3 nose = -Vec.Unit(v);
        doubleQuat attitude = doubleQuat.CreateFromAxisAngle(nose, rollTurns * 2.0 * Math.PI)
                              * Vec.RotationFromTo(new double3(1, 0, 0), nose);

        Tube[] tubes = Arsenal.MirvBus.Tubes;
        double3[] landed = new double3[tubes.Length];

        for (int i = 0; i < tubes.Length; i++)
        {
            double3 axis = Vec.Unit(attitude * tubes[i].Direction);
            landed[i] = Land(from, v + axis * Warhead.LaunchSpeed);
        }

        double worst = 0.0;
        for (int a = 0; a < landed.Length; a++)
        {
            for (int b = a + 1; b < landed.Length; b++)
            {
                worst = Math.Max(worst, GroundMetres(landed[a], landed[b]));
            }
        }

        double meanMiss = 0.0;
        double closest = double.MaxValue, furthest = 0.0;
        foreach (double3 p in landed)
        {
            double m = GroundMetres(p, target);
            meanMiss += m / landed.Length;
            closest = Math.Min(closest, m);
            furthest = Math.Max(furthest, m);
        }

        Out.WriteLine($"roll {rollTurns:F2} turns: spread {worst:F0} m across the six, "
                      + $"misses {closest / 1000.0:F2}-{furthest / 1000.0:F2} km, mean {meanMiss / 1000.0:F2}");

        // The kick itself, common to all six.
        double3 noKick = Land(from, v);
        Out.WriteLine($"  the 2 m/s ejection alone moves the mean impact "
                      + $"{GroundMetres(noKick, landed[0]):F0}-{GroundMetres(noKick, landed[3]):F0} m");
    }

    /// <summary>
    /// Term 2's other half. Warheads released seconds apart fly different arcs, because the bus is
    /// falling and the ejection's leverage is being spent.
    /// </summary>
    [Fact]
    public void WhatASecondOfReleaseDelayCosts()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 _);
        double3 v = arc.RequiredVelocityCci;

        double3 nose = -Vec.Unit(v);
        double3 atCutoff = Land(from, v + nose * Warhead.LaunchSpeed);

        foreach (double delay in new[] { 0.1, 1.0, 3.0, 10.0, 30.0 })
        {
            Assert.True(Kepler.TryCoast(Mu, from, v, delay, out double3 r, out double3 vv));

            double3 later = Land(r, vv + nose * Warhead.LaunchSpeed);

            // Back to a place on the ground: the two flights are of different length.
            // Un-carried by the delay as well as by the flight, so both are places on the ground
            // measured from one epoch. Carrying it the other way is worth 465 m a second at the
            // equator and would report the planet's own turn as a miss.
            Out.WriteLine($"released {delay,5:F1} s after cutoff: "
                          + $"{GroundMetres(atCutoff, Earth.UncarryCci(later, delay)):F0} m "
                          + "from the t+0 impact");
        }
    }

    /// <summary>
    /// Term 5. What the frame step costs the round, and which half of the round's own model it
    /// comes out of: the gravity and the air's motion are frame-level inputs, held across every
    /// sub-step, while the predictor evaluates gravity at every RK4 stage.
    /// </summary>
    [Fact]
    public void WhereTheRoundsFrameStepErrorLives()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 _);
        double3 v = arc.RequiredVelocityCci;

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact predicted, null, null,
                                               new ImpactPredictor.Drag(DensityAt, Warhead)));

        foreach (double dt in new[] { 1.0 / 60.0, 0.05, 0.1, 8.0 / 60.0, 0.2, 0.32 })
        {
            // Swapped rather than renamed: re-reading is now what asking for nothing gives, so it
            // is the *held* round that has to be asked for by name.
            (double3 held, double heldSeconds) = FlyTheRound(from, v, dt, beforeGravityPerSubStep: true);
            (double3 fresh, double freshSeconds) = FlyTheRound(from, v, dt);

            Out.WriteLine($"{dt * 1000,4:F0} ms frame: "
                          + $"gravity held {GroundMetres(predicted.GroundFixedPointCci, held),6:F0} m, "
                          + $"re-read {GroundMetres(predicted.GroundFixedPointCci, fresh),6:F0} m "
                          + $"from the prediction  (the freeze is worth {GroundMetres(held, fresh):F0} m)");
        }
    }

    /// <summary>
    /// The whole point of the aim correction, and the thing the arrival latch pins: with the
    /// arrival committed, a cutoff that slips forces a different arc to arrive at the same instant.
    /// The vacuum arc still passes through the aim exactly; what changes is how much of it is spent
    /// in air, and that is what the drag prediction reports as a miss.
    /// </summary>
    [Fact]
    public void WhatACutoffSlippingUnderACommittedArrivalCosts()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double3 v0 = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

        double latched = arc.FlightSeconds;
        Out.WriteLine($"arrival latched at cutoff + {latched:F0} s");

        double3 atCutoff = Land(from, arc.RequiredVelocityCci);
        Out.WriteLine($"the committed arc lands {GroundMetres(atCutoff, target) / 1000.0:F2} km from the target");

        foreach (double slip in new[] { -4.0, -2.0, -1.0, 1.0, 2.0, 4.0, 8.0 })
        {
            // Where the vehicle would be if it cut off `slip` seconds later, coasting there on the
            // arc it is already flying. What the burn really does is somewhere between this and
            // the circular orbit it started from, so this is the shape rather than the magnitude.
            Assert.True(Kepler.TryCoast(Mu, from, arc.RequiredVelocityCci, slip,
                                        out double3 r, out double3 _));

            if (!BallisticArc.TrySolve(Earth, r, Earth.UncarryCci(target, 0.0), latched - slip,
                                       out BallisticArc.Solution s))
            {
                Out.WriteLine($"{slip,5:F1} s of slip: no arc");
                continue;
            }

            double3 landed = Land(r, s.RequiredVelocityCci);
            double miss = GroundMetres(Earth.UncarryCci(landed, slip), target);

            Out.WriteLine($"{slip,5:F1} s of slip: the drag-flown arc lands {miss / 1000.0:F2} km "
                          + $"from the target ({(miss - GroundMetres(atCutoff, target)) / 1000.0:+0.00;-0.00} km)");
        }
    }

    /// <summary>
    /// What a wrong cutoff <em>estimate</em> costs: the predicted cutoff point moved while the
    /// remaining flight time is not moved with it, which is what an error in
    /// <c>BurnoutGuidance</c>'s extrapolation looks like.
    ///
    /// <para>Not what the burn does — there the two move together and the arc still arrives at the
    /// latched instant, which is why the true miss across a burn is flat. This is the cost of the
    /// estimate being wrong, not of it changing.</para>
    /// </summary>
    [Fact]
    public void HowFarThePredictionMovesPerKilometreTheCutoffPointDoes()
    {
        BallisticArc.Solution arc = Shot(out double3 from, out double3 target);
        double latched = arc.FlightSeconds;

        double3 prograde = Vec.Unit(arc.RequiredVelocityCci);
        double3 radial = Vec.Unit(from);
        double3 cross = Vec.Unit(Vec.Cross(radial, prograde));

        double3 baseline = Land(from, arc.RequiredVelocityCci);
        double baseMiss = GroundMetres(baseline, target);

        foreach ((string name, double3 axis) in
                 new (string, double3)[] { ("along track", prograde), ("radial", radial), ("cross-track", cross) })
        {
            const double delta = 1_000.0;

            Assert.True(BallisticArc.TrySolve(Earth, from + axis * delta, target, latched,
                                              out BallisticArc.Solution plus));
            Assert.True(BallisticArc.TrySolve(Earth, from - axis * delta, target, latched,
                                              out BallisticArc.Solution minus));

            double a = GroundMetres(Land(from + axis * delta, plus.RequiredVelocityCci), target);
            double b = GroundMetres(Land(from - axis * delta, minus.RequiredVelocityCci), target);

            Out.WriteLine($"{name,-12}: {(a - b) / 2.0:F0} m of predicted miss per km of cutoff point "
                          + $"(baseline {baseMiss / 1000.0:F2} km)");
        }
    }

    /// <summary>
    /// The whole burn, stepped, so what the computer's own prediction says on every cycle can be
    /// read beside what a warhead off that arc would actually do.
    ///
    /// <para>Its own loop rather than <c>IcbmFlightRig</c>'s, because what is being measured is the
    /// program's <em>internal</em> state on every cycle: where it thinks the cutoff will be and
    /// what arc it has solved. The rig reports a flight; this reports the cycles inside one.</para>
    ///
    /// <para>Two scorings, and the difference between them is the whole finding. The prediction
    /// departs at <em>cutoff</em> while the burn is running, so its impact point is expressed in
    /// the planet's orientation at that future instant — and comparing it against where the target
    /// is <em>now</em> reads the planet's own turn over the remaining burn as a miss.</para>
    /// </summary>
    [Fact]
    public void WhatThePredictedMissSaysAcrossTheBurn()
    {
        Shot(out double3 from, out double3 target);

        double3 position = from;
        double3 velocity = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

        IcbmProgram program = new(new IcbmConfig { Armed = true });

        const double h = 1.0 / 60.0;
        const double dryKg = 3_000.0, thrustN = 300_000.0, veMps = 3_100.0;
        double propellantKg = 40_000.0;

        double elapsed = 0.0;
        double3 firstCutoff = Vec.Zero;
        bool haveFirst = false;
        int cycle = 0;

        while (elapsed < 600.0)
        {
            double mass = dryKg + propellantKg;
            IcbmState state = new(Earth, position, velocity, Earth.CarryCci(target, elapsed),
                                  HasAim: true,
                                  new BoosterPerformance(thrustN, thrustN / veMps, mass, propellantKg),
                                  0.0, PropellantAvailable: propellantKg > 0.0,
                                  ThrottleAchieved: 1.0);

            IcbmCommand command = program.Update(elapsed == 0.0 ? 0.0 : h, state);

            if (program.Phase is IcbmPhase.Coast or IcbmPhase.NoSolution or IcbmPhase.Idle) break;

            if (program.Arc is { } arc && program.Phase == IcbmPhase.ClosedLoop)
            {
                if (!haveFirst) { firstCutoff = program.CutoffPositionCci; haveFirst = true; }

                double toCutoff = double.IsFinite(command.SecondsToCutoff)
                                  ? Math.Max(0.0, command.SecondsToCutoff) : 0.0;

                double3 landed = Land(program.CutoffPositionCci, arc.RequiredVelocityCci);

                // What IcbmComputer.Predict reports: the impact in the planet's orientation at the
                // cutoff instant, against the target's position now.
                double asReported = GroundMetres(landed, Earth.CarryCci(target, elapsed));

                // The same two points brought to one epoch.
                double inOneEpoch = GroundMetres(Earth.UncarryCci(landed, toCutoff),
                                                 Earth.CarryCci(target, elapsed));

                if (cycle++ % 300 == 0 || toCutoff < 0.05)
                {
                    Out.WriteLine($"  t+{elapsed,6:F2} s: {program.VelocityToGain,7:F1} m/s to gain, "
                                  + $"cutoff in {toCutoff,5:F1} s and "
                                  + $"{Vec.Len(program.CutoffPositionCci - firstCutoff) / 1000.0,5:F2} km "
                                  + $"from the first :: as reported {asReported / 1000.0,6:F2} km, "
                                  + $"in one epoch {inOneEpoch / 1000.0,6:F2} km "
                                  + $"(the planet's turn is {465.0 * toCutoff / 1000.0,5:F2} km)");
                }
            }

            if (command.EngineOn && propellantKg > 0.0)
            {
                double throttle = Math.Clamp(command.Throttle, 0.0, 1.0);
                propellantKg = Math.Max(0.0, propellantKg - thrustN / veMps * throttle * h);
                velocity += Vec.Unit(command.ThrustDirectionCci) * (thrustN * throttle / mass) * h;
            }

            velocity += Earth.GravityCci(position) * h;
            position += velocity * h;
            elapsed += h;
        }

        Out.WriteLine($"cut off at t+{elapsed:F2} s, {program.VelocityToGain:F3} m/s still to gain");
    }

    /// <summary>
    /// The epoch term on its own, with no burn and no guidance: a prediction that departs
    /// <paramref name="secondsToCutoff"/> in the future reports its impact in the planet's
    /// orientation at that instant, and scoring it against where the target is now reads the turn
    /// in between as a miss.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(30.0)]
    [InlineData(60.0)]
    public void APredictionThatDepartsInTheFutureIsScoredAgainstANowThatHasNotTurnedYet(double secondsToCutoff)
    {
        Shot(out double3 from, out double3 target);

        // A perfect shot: the arc is solved to the aim carried to the cutoff instant, exactly as
        // BurnoutGuidance does it, and departs from where the vehicle will then be.
        double3 aimAtCutoff = Earth.CarryCci(target, secondsToCutoff);
        Assert.True(BallisticArc.TrySolve(Earth, from, aimAtCutoff, 487.0,
                                          out BallisticArc.Solution arc));

        Assert.True(ImpactPredictor.TryPredict(Earth, from, arc.RequiredVelocityCci, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact hit));

        double asReported = GroundMetres(hit.GroundFixedPointCci, target);
        double inOneEpoch = GroundMetres(Earth.UncarryCci(hit.GroundFixedPointCci, secondsToCutoff),
                                         target);

        Out.WriteLine($"cutoff {secondsToCutoff,5:F1} s away: reported {asReported / 1000.0,6:F2} km, "
                      + $"in one epoch {inOneEpoch / 1000.0:F3} km");
    }

    /// <summary>
    /// The whole loop — guidance, the aim correction wired as <c>Ksa/IcbmComputer.cs</c> wires it,
    /// and a warhead flown off the cutoff state — with one thing varied: whether what the
    /// correction observes has been brought to the same epoch as what it is scored against.
    ///
    /// <para>This is the experiment the budget turns on. Nothing else in the flight differs.</para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAimCorrectionObservesAcrossTwoPlanetOrientations(bool bringToOneEpoch)
    {
        Shot(out double3 from, out double3 target);

        double3 position = from;
        double3 velocity = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        AimCorrection correction = new();

        const double h = 1.0 / 60.0;
        const double predictEvery = 0.5;
        const double dryKg = 3_000.0, thrustN = 300_000.0, veMps = 3_100.0;
        double propellantKg = 40_000.0;

        double elapsed = 0.0;
        double sincePredict = double.PositiveInfinity;
        bool frozen = false;
        double frozenAt = double.NaN, frozenReported = double.NaN, frozenTrue = double.NaN;

        Out.WriteLine(bringToOneEpoch
                          ? "observed in one epoch:"
                          : "observed as the mod does it (impact at the cutoff epoch, target now):");

        while (elapsed < 600.0)
        {
            double3 aimNow = Earth.CarryCci(target, elapsed);
            double mass = dryKg + propellantKg;

            IcbmState state = new(Earth, position, velocity, correction.Apply(aimNow), HasAim: true,
                                  new BoosterPerformance(thrustN, thrustN / veMps, mass, propellantKg),
                                  0.0, PropellantAvailable: propellantKg > 0.0,
                                  ThrottleAchieved: 1.0, AimIsSteady: correction.IsSteady);

            IcbmCommand command = program.Update(elapsed == 0.0 ? 0.0 : h, state);

            if (double.IsFinite(program.CommittedArrivalFromNow) && !frozen) correction.Freeze();

            if (program.Phase is IcbmPhase.Coast or IcbmPhase.NoSolution or IcbmPhase.Idle) break;

            sincePredict += h;
            if (sincePredict >= predictEvery && program.Arc is { } arc
                && program.Phase == IcbmPhase.ClosedLoop)
            {
                sincePredict = 0.0;

                double toCutoff = double.IsFinite(command.SecondsToCutoff)
                                  ? Math.Max(0.0, command.SecondsToCutoff) : 0.0;

                double3 landed = Land(program.CutoffPositionCci, arc.RequiredVelocityCci);
                double3 inOneEpoch = Earth.UncarryCci(landed, toCutoff);

                double reported = GroundMetres(landed, aimNow);
                double truth = GroundMetres(inOneEpoch, aimNow);

                if (!frozen && double.IsFinite(program.CommittedArrivalFromNow))
                {
                    frozen = true;
                    frozenAt = toCutoff;
                    frozenReported = reported;
                    frozenTrue = truth;
                }

                if (!frozen) correction.Observe(bringToOneEpoch ? inOneEpoch : landed, aimNow);

                if (!frozen || toCutoff < 1.0 || (int)(elapsed / 10.0) != (int)((elapsed - predictEvery) / 10.0))
                Out.WriteLine($"  t+{elapsed,6:F2} s, cutoff in {toCutoff,5:F1} s: "
                              + $"bias {Vec.Len(correction.BiasCci) / 1000.0,6:F1} km, "
                              + $"reported {reported / 1000.0,6:F2} km, "
                              + $"true {truth / 1000.0,6:F2} km{(frozen ? "  [frozen]" : "")}");
            }

            if (command.EngineOn && propellantKg > 0.0)
            {
                double throttle = Math.Clamp(command.Throttle, 0.0, 1.0);
                propellantKg = Math.Max(0.0, propellantKg - thrustN / veMps * throttle * h);
                velocity += Vec.Unit(command.ThrustDirectionCci) * (thrustN * throttle / mass) * h;
            }

            velocity += Earth.GravityCci(position) * h;
            position += velocity * h;
            elapsed += h;
        }

        // And where a warhead off that cutoff state actually lands, flown rather than predicted.
        (double3 impact, double flight) = FlyTheRound(position, velocity, 1.0 / 60.0);
        double landedMiss = GroundMetres(Earth.UncarryCci(impact, elapsed), target);

        Out.WriteLine($"froze with the cutoff {frozenAt:F1} s away, reporting "
                      + $"{frozenReported / 1000.0:F2} km against a true {frozenTrue / 1000.0:F2} km");
        Out.WriteLine($"cut off at t+{elapsed:F2} s with {program.VelocityToGain:F3} m/s to gain; "
                      + $"the warhead lands {landedMiss / 1000.0:F2} km from the target "
                      + $"after {flight:F0} s");
    }

    /// <summary>
    /// <see cref="AimCorrection.BiasCci"/> is a free vector in the body's inertial frame while the
    /// target it is added to is a point on a turning planet, so a frozen bias goes stale — which
    /// only accrues over the window between the freeze and cutoff.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-26.5)]
    [InlineData(-60.0)]
    public void WhatAFrozenBiasLosesWhileThePlanetTurnsUnderIt(double latitudeDeg)
    {
        double lat = latitudeDeg * Math.PI / 180.0;
        double3 target = new(R * Math.Cos(lat), 0, R * Math.Sin(lat));

        // The size the loop actually settled on for this shot, headlessly.
        double3 bias = Vec.Unit(new double3(0, 1, 0)) * 65_000.0;

        foreach (double window in new[] { 10.0, 28.0, 51.0 })
        {
            double3 asFrozen = Aim(target, bias);
            double3 carried = Aim(target, Earth.CarryCci(bias, window));

            Out.WriteLine($"lat {latitudeDeg,6:F1}, {window,4:F0} s frozen: "
                          + $"{GroundMetres(asFrozen, carried):F0} m");
        }
    }

    /// <summary>The aim, kept on the target's own radius — what <see cref="AimCorrection.Apply"/> does.</summary>
    private static double3 Aim(double3 targetCci, double3 biasCci)
    {
        double3 moved = targetCci + biasCci;
        return moved * (Vec.Len(targetCci) / Vec.Len(moved));
    }
}
