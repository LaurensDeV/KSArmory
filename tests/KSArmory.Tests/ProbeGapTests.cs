using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Why a warhead lands past the release probe that predicted it, taken apart into named terms.
///
/// <para>The probe is <see cref="ImpactPredictor"/> flown from the state the round actually left on,
/// so any gap here is a miss <see cref="AimCorrection"/> cannot remove — its only observer is that
/// same predictor. Flown 21 August with the guidance solved to 0.1 km at release, the warheads
/// landed 1.57-1.88 km out in groups 0.02-0.03 km wide: one bias, six rounds, and nothing upstream
/// left to blame.</para>
///
/// <para><b>Measurement only.</b> Nothing here asserts an improvement; every test either reports a
/// number or pins a difference that was measured, so the budget can be re-run after a change.
/// <c>docs/MIRV-NEXT.md</c> item 2 is the backlog entry these numbers are spent against.</para>
///
/// <para><b>Every figure is quoted on both surfaces</b>, the mean sphere and
/// <see cref="DeorbitShot.RoughGround"/>. The smooth planet is the one case where the round and the
/// prediction read the same surface however far apart they are, so it prices the flight models
/// against each other and nothing else — and <c>docs/MIRV-NEXT.md</c> item -1 records seven
/// headless improvements that scored well there and lost in flight.</para>
/// </summary>
public class ProbeGapTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;
    private static MunitionProfile Warhead => DeorbitShot.Warhead;
    private static double GroundMetres(double3 a, double3 b) => DeorbitShot.GroundMetres(a, b);

    /// <summary>
    /// The integration step a converged flight of the same round is taken at.
    ///
    /// <para>Twenty times the shipped 5 ms. <c>docs/KINETIC-FLOOR.md</c> measures the round's own
    /// error as first order and clean at 30.6 m per millisecond, so this is metres from converged
    /// and the extrapolation is a fit rather than a guess.</para>
    /// </summary>
    private const double ConvergedStep = 0.00025;

    /// <summary>
    /// The surfaces every number here is quoted on.
    ///
    /// <para><b>The third one is the realistic one.</b> <see cref="DeorbitShot.RoughGround"/>'s
    /// shortest term is 19 km across, so it carries height without carrying <em>features</em>;
    /// `ErodedGroundKsaSpectrum` puts KSA's own seven erosion octaves on it, down to 166 m. The
    /// predictor is indifferent to the difference — 0.13 m, `PredictorStepTests` — so any column
    /// that moves between relief and erosion is the round's, which is what
    /// <c>docs/ACCURACY-PLAN.md</c> 3ab left open.</para>
    /// </summary>
    private static IEnumerable<(string What, Func<double3, double>? Terrain)> Surfaces =>
    [
        ("mean sphere", null),
        ("with relief", DeorbitShot.RoughGround),
        ("with KSA erosion", DeorbitShot.ErodedGroundKsaSpectrum),
    ];

    /// <summary>The round's own ground test over whichever of those surfaces.</summary>
    private static IGroundTest GroundFor(Func<double3, double>? terrain)
        => terrain is null ? new DeorbitShot.Ball() : new DeorbitShot.Relief { Surface = terrain };

    /// <summary>Where the probe says the release state comes down.</summary>
    private static double3 Probe(double3 fromCci, double3 velocityCci, Func<double3, double>? terrain)
        => DeorbitShot.Land(fromCci, velocityCci, terrain);

    /// <summary>
    /// The release state the whole budget is flown from: the cheapest arc off the 200 km pickup,
    /// with the warhead's own ejection kick already on it.
    /// </summary>
    private static void ReleaseState(out double3 fromCci, out double3 velocityCci)
    {
        BallisticArc.Solution arc = DeorbitShot.Shot(out fromCci, out double3 _);

        // Along the departure, which is where a bus that has finished trimming is pointed.
        velocityCci = arc.RequiredVelocityCci
                      + Vec.Unit(arc.RequiredVelocityCci) * Warhead.LaunchSpeed;
    }

    /// <summary>
    /// How far past the reference a point lies <em>along the track</em>, signed.
    ///
    /// <para>A bare ground distance cannot tell an overshoot from an undershoot, and the sign is the
    /// whole finding in <see cref="TheTwoLargestTermsPushTheImpactOppositeWays"/>.</para>
    /// </summary>
    private static double Downrange(double3 referenceCci, double3 pointCci, double3 alongTrackCci)
    {
        double metres = GroundMetres(referenceCci, pointCci);
        return Vec.Dot(pointCci - referenceCci, alongTrackCci) >= 0.0 ? metres : -metres;
    }

    /// <summary>The direction the round is travelling over the ground when it arrives.</summary>
    private static double3 AlongTrack(double3 fromCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

        double3 up = Vec.Unit(hit.PointCci);
        return Vec.Unit(hit.VelocityCci - up * Vec.Dot(hit.VelocityCci, up));
    }

    private static double FlightSeconds(double3 fromCci, double3 velocityCci,
                                        Func<double3, double>? terrain)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, terrain, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));
        return hit.Seconds;
    }

    /// <summary>
    /// The gap itself, on both surfaces and at every step the world is actually held to.
    ///
    /// <para>The flown number this is spent against is 1.6 km, and the point of the two columns is
    /// that neither of them reaches it.</para>
    /// </summary>
    [Fact]
    public void HowFarTheRoundLandsFromItsOwnReleaseProbe()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);

        foreach ((string what, Func<double3, double>? terrain) in Surfaces)
        {
            double3 probe = Probe(from, v, terrain);
            Out.WriteLine($"{what}:");

            foreach (double dt in new[] { DeorbitShot.NominalFrame, Medium.FaithfulStepInAir, 0.13, 0.32 })
            {
                (double3 landed, double seconds) =
                    DeorbitShot.FlyTheRound(from, v, dt, default, GroundFor(terrain));

                Out.WriteLine($"  {dt * 1000,4:F0} ms frame: {Downrange(probe, landed, along),7:F0} m "
                              + $"downrange of the probe "
                              + $"({seconds - FlightSeconds(from, v, terrain):+0.000;-0.000} s)");
            }

            (double3 warped, double _) =
                DeorbitShot.FlyTheRoundAsWarped(from, v, DeorbitShot.ScenarioWarp, default,
                                                GroundFor(terrain));

            Out.WriteLine($"  as flown ({DeorbitShot.ScenarioWarp:F0}x coast, "
                          + $"{Medium.FaithfulStepInAir * 1000:F0} ms in air): "
                          + $"{Downrange(probe, warped, along),7:F0} m downrange of the probe");
        }
    }

    /// <summary>
    /// Whether the gap over erosion is a bias or a coin toss, which decides whether it is worth
    /// correcting at all.
    ///
    /// <para>The release is nudged by a few centimetres per second — far below anything guidance
    /// controls — and the gap re-measured. A term that stays put under that is a bias something
    /// could remove. One that swings by kilometres is the round and the probe stopping on
    /// <em>different features</em>, which is chaotic rather than wrong, and the only lever on it is
    /// making the two read the same surface at the same instant.</para>
    /// </summary>
    [Fact]
    public void WhetherTheGapOverErosionIsABiasOrACoinToss()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);

        foreach ((string what, Func<double3, double>? terrain) in Surfaces)
        {
            List<double> gaps = [];

            for (int i = -3; i <= 3; i++)
            {
                double3 nudged = v + along * (i * 0.02);

                (double3 landed, double _) = DeorbitShot.FlyTheRound(
                    from, nudged, Medium.FaithfulStepInAir, default, GroundFor(terrain));

                gaps.Add(Downrange(Probe(from, nudged, terrain), landed, along));
            }

            Out.WriteLine($"{what}: gap over +/-6 cm/s of release: "
                          + $"{gaps.Min():F0} to {gaps.Max():F0} m, "
                          + $"spread {gaps.Max() - gaps.Min():F0} m");
        }
    }

    /// <summary>
    /// Which of the two is reading the ground wrongly, asked directly.
    ///
    /// <para>Both stop on the same surface, so a disagreement means one of them stopped somewhere
    /// the terrain is not. This evaluates the surface <em>at each side's own landing point</em> and
    /// reports how far each stopped from it: the one whose stop radius does not match the ground
    /// beneath it is the one at fault.</para>
    /// </summary>
    [Fact]
    public void WhichSideStopsWhereTheGroundIsNot()
    {
        ReleaseState(out double3 from, out double3 v);

        foreach ((string what, Func<double3, double>? terrain) in Surfaces)
        {
            if (terrain is null) continue;

            (double3 landed, double _) = DeorbitShot.FlyTheRound(
                from, v, Medium.FaithfulStepInAir, default, GroundFor(terrain));

            double3 probe = Probe(from, v, terrain);

            double roundError = landed.Length() - terrain(landed);
            double probeError = probe.Length() - terrain(probe);

            Out.WriteLine($"{what}:");
            Out.WriteLine($"  round stopped {roundError,9:F1} m from the surface under it "
                          + $"(terrain there {terrain(landed) - DeorbitShot.R,8:F1} m)");
            Out.WriteLine($"  probe stopped {probeError,9:F1} m from the surface under it "
                          + $"(terrain there {terrain(probe) - DeorbitShot.R,8:F1} m)");
        }
    }

    /// <summary>
    /// The decomposition: one difference removed at a time from the round as flown, each measured
    /// as how far the impact moves.
    ///
    /// <para>Measured <b>one at a time against the same baseline</b> rather than cumulatively, so no
    /// term is credited with an interaction the ordering happened to give it. Whether they are
    /// separable is then a question the numbers answer rather than one the method assumes — and
    /// here they are not: see <see cref="TheTwoLargestTermsPushTheImpactOppositeWays"/>.</para>
    /// </summary>
    [Fact]
    public void WhichDifferenceBetweenTheRoundAndItsProbeIsWorthWhat()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);

        foreach ((string what, Func<double3, double>? terrain) in Surfaces)
        {
            double3 probe = Probe(from, v, terrain);

            double3 Fly(DeorbitShot.Refresh refresh)
                => DeorbitShot.FlyTheRoundAsWarped(from, v, DeorbitShot.ScenarioWarp, refresh,
                                                   GroundFor(terrain)).GroundFixed;

            double3 asFlown = Fly(DeorbitShot.Refresh.AsFlown);

            (string Term, double3 Landed)[] terms =
            [
                ("the ground held for a whole frame", Fly(new DeorbitShot.Refresh { Ground = true })),
                ("the air's motion held for a whole frame", Fly(new DeorbitShot.Refresh { AirMotion = true })),
                ("symplectic Euler at 5 ms", Fly(new DeorbitShot.Refresh { StepSeconds = ConvergedStep })),
            ];

            Out.WriteLine($"{what}: the round lands {Downrange(probe, asFlown, along):F0} m "
                          + "downrange of its probe");

            double sum = 0.0;
            foreach ((string term, double3 landed) in terms)
            {
                double moved = Downrange(asFlown, landed, along);
                sum += moved;
                Out.WriteLine($"  removing {term,-42}{moved,8:F0} m");
            }

            // All four at once, which is the round made as much like the predictor as this rig can
            // make it. What is left is the two schemes themselves plus the crossing rule.
            double3 converged = Fly(new DeorbitShot.Refresh
            {
                AirMotion = true,
                Ground = true,
                StepSeconds = ConvergedStep,
            });

            Out.WriteLine($"  removing {"all four together",-42}{Downrange(asFlown, converged, along),8:F0} m "
                          + $"(the four separately sum to {sum:F0} m)");
            Out.WriteLine($"  {"unaccounted for",-51}{Downrange(probe, converged, along),8:F0} m");
        }
    }

    /// <summary>
    /// The gap against the frame the <em>coast</em> is flown at, which is the one thing about it a
    /// player can change from outside.
    ///
    /// <para><see cref="Medium.FaithfulStepInAir"/> already pulls the entry back to 50 ms, so the
    /// coarse frame is spent entirely above the atmosphere — where it looks harmless and is not:
    /// gravity is a frame-level argument to <see cref="Slug"/> and a coarse coast integrates the
    /// whole fall on a stale one. <c>WarpPolicy</c> allows up to
    /// <c>MaxFaithfulStepSeconds / frameTime</c>, about 19x at 60 fps, and the scenario runner asks
    /// for 8.</para>
    /// </summary>
    [Fact]
    public void HowTheGapGrowsWithTheFrameTheCoastIsFlownAt()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);
        double3 probe = Probe(from, v, null);

        double Gap(double warp, DeorbitShot.Refresh refresh)
            => Downrange(probe,
                         DeorbitShot.FlyTheRoundAsWarped(from, v, warp, refresh).GroundFixed,
                         along);

        Out.WriteLine("coast   frame    as flown   gravity per sub-step   converged sub-step   both");

        foreach (double warp in new[] { 1.0, 2.0, 4.0, 8.0, 16.0, 19.0 })
        {
            double frame = Math.Min(warp * DeorbitShot.NominalFrame, Warhead.MaxFaithfulStepSeconds);

            Out.WriteLine($"{warp,4:F0}x{frame * 1000,8:F0} ms{Gap(warp, DeorbitShot.Refresh.AsFlown),12:F0} m"
                          + $"{Gap(warp, DeorbitShot.Refresh.BeforeGravityPerSubStep),19:F0} m"
                          + $"{Gap(warp, new DeorbitShot.Refresh { HoldGravity = true, StepSeconds = ConvergedStep }),19:F0} m"
                          + $"{Gap(warp, new DeorbitShot.Refresh { StepSeconds = ConvergedStep }),9:F0} m");
        }
    }

    /// <summary>
    /// Both sides of the one lever that reaches this without touching a flight model:
    /// <see cref="MunitionProfile.MaxFaithfulStepSeconds"/> on the warhead, which is what
    /// <c>WarpPolicy</c> holds the world down to while the salvo is in the air.
    ///
    /// <para>Priced rather than proposed. The win is metres of miss and the cost is minutes of the
    /// player's evening, and <c>Sim/WarpPolicy.cs</c> says in as many words that a ballistic weapon
    /// can take far longer steps than an interceptor — so this trades against a decision that was
    /// made deliberately, and the numbers are what it should be re-made on.</para>
    /// </summary>
    [Fact]
    public void WhatCappingTheCoastsFrameWouldWinAndCost()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);
        double3 probe = Probe(from, v, null);

        double coastSeconds = FlightSeconds(from, v, null);

        Out.WriteLine($"the scenario asks for {BallisticScenarioWarp:F0}x and the coast lasts "
                      + $"{coastSeconds:F0} s of simulated time");
        Out.WriteLine("cap      world held to   frame    gap    coast takes");

        foreach (double cap in new[] { Warhead.MaxFaithfulStepSeconds, 0.20, 0.10, 0.05 })
        {
            // What the policy would settle on, asked of the policy rather than re-derived: the
            // margin and the "already inside the limit" early return are both its business.
            WarpPolicy policy = new();
            WarpDecision decision = policy.Decide(BallisticScenarioWarp * DeorbitShot.NominalFrame,
                                                  BallisticScenarioWarp, roundsInFlight: true,
                                                  enabled: true, faithfulStep: cap);

            double speed = decision.Action == WarpAction.Slow ? decision.Speed : BallisticScenarioWarp;
            double frame = speed * DeorbitShot.NominalFrame;

            double gap = Downrange(probe,
                                   DeorbitShot.FlyTheRoundAsWarped(from, v, speed).GroundFixed,
                                   along);

            Out.WriteLine($"{cap * 1000,4:F0} ms{speed,12:F1}x{frame * 1000,9:F0} ms{gap,8:F0} m"
                          + $"{coastSeconds / speed,10:F0} s of wall clock");
        }
    }

    /// <summary>What <c>Ksa/BallisticScenario.cs</c> asks the world for once the salvo is away.</summary>
    private const double BallisticScenarioWarp = DeorbitShot.ScenarioWarp;

    /// <summary>
    /// <b>The cancelling pair is gone, and the sub-step is what is left.</b>
    ///
    /// <para><c>docs/MIRV-NEXT.md</c> item 2d records re-reading gravity per sub-step being priced
    /// headlessly as a large win and flying worse three times out of three. The explanation was that
    /// it took out one half of a cancelling pair — and the other half was the <em>pull centre</em>,
    /// not the sub-step. Both shipped together on 2026-08-24 and the pair took the mean miss from
    /// 0.44 km to 0.05 km.</para>
    ///
    /// <para>So the warning that came out of 2d does not reach the sub-step. Against the round the
    /// game actually flies, gravity's own marginal contribution is <b>zero</b> — it is already the
    /// baseline — and converging the sub-step is a lone term worth the whole of what is left.</para>
    ///
    /// <para>The first assertion is the one that matters: if it ever stops reading zero, either the
    /// shipped tree has lost the per-sub-step gravity or <see cref="DeorbitShot.Refresh.AsFlown"/>
    /// has gone stale against it again — and every other column of every budget taken with this rig
    /// is then priced against a round nothing flies.</para>
    /// </summary>
    [Fact]
    public void GravityIsAlreadyShippedAndTheSubStepIsTheWholeOfWhatIsLeft()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);

        double3 Fly(DeorbitShot.Refresh refresh)
            => DeorbitShot.FlyTheRoundAsWarped(from, v, DeorbitShot.ScenarioWarp, refresh).GroundFixed;

        double3 probe = Probe(from, v, null);

        double gap = Downrange(probe, Fly(DeorbitShot.Refresh.AsFlown), along);
        double before = Downrange(probe, Fly(DeorbitShot.Refresh.BeforeGravityPerSubStep), along);
        double left = Downrange(probe, Fly(DeorbitShot.Refresh.AsFlown with
        {
            StepSeconds = ConvergedStep,
        }), along);

        Out.WriteLine($"before the per-sub-step gravity  {before,8:F0} m from the probe");
        Out.WriteLine($"the shipped round               {gap,8:F0} m");
        Out.WriteLine($"...and with the sub-step converged {left,6:F0} m");

        // What says the rig still models the change that shipped in August, and the assertion that
        // catches AsFlown going stale against the tree again: the two configurations have to be
        // hundreds of metres apart, because that difference is the whole of what that flight won.
        Assert.True(Math.Abs(before - gap) > 100.0,
                    $"the rig no longer distinguishes the shipped round from the pre-2026-08-24 one; "
                    + $"{before:F0} m against {gap:F0} m");

        // And the sub-step is not half of a cancelling pair: on its own it leaves the round on its
        // own predictor, which is the opposite of what item 2d's warning would predict.
        //
        // An absolute bound rather than a fraction of the gap, so it states the claim -- a
        // converged round agrees with its predictor -- rather than the size of what is being
        // removed. A fraction also inverts on any arm that has already taken the sub-step, where
        // there is no gap left to remove a share of.
        Assert.True(Math.Abs(left) < 20.0,
                    $"a converged sub-step should leave the round on its predictor; {left:F0} m");
    }

    /// <summary>
    /// The one term that is identically zero on a smooth planet and is not zero on a real one:
    /// <see cref="Slug"/> holds one terrain sample for a whole frame while
    /// <see cref="ImpactPredictor"/> takes a fresh one every step.
    ///
    /// <para>Reported against the frame, because that is what it scales with — and what timewarp
    /// changes. <c>docs/KINETIC-FLOOR.md</c> section 2 has the closed form, <c>s.d/(tan y + s)</c>,
    /// which is also why this relief prices it at almost nothing: <c>s</c> is about 1%.</para>
    /// </summary>
    [Fact]
    public void WhatHoldingOneTerrainSampleForAWholeFrameCosts()
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);

        foreach ((string what, Func<double3, double>? terrain) in Surfaces)
        {
            Out.WriteLine($"{what}:");

            foreach (double dt in new[] { DeorbitShot.NominalFrame, Medium.FaithfulStepInAir, 0.13, 0.32 })
            {
                IGroundTest counter = GroundFor(terrain);

                (double3 held, double _) = DeorbitShot.FlyTheRound(from, v, dt, default, counter);
                (double3 fresh, double _) =
                    DeorbitShot.FlyTheRound(from, v, dt,
                                            new DeorbitShot.Refresh { HoldGravity = true, Ground = true },
                                            GroundFor(terrain));

                string cost = counter is DeorbitShot.Relief r ? $", {r.Sampled} lookups held" : "";
                Out.WriteLine($"  {dt * 1000,4:F0} ms frame: {Downrange(held, fresh, along),7:F0} m{cost}");
            }
        }
    }

    /// <summary>
    /// The gain the ground applies to whatever the flight models leave, measured against a ramp of
    /// stated gradient rather than against this rig's own gentle relief.
    ///
    /// <para>This is <b>the term the rig is blind to and flight is not</b>. A round stopping short
    /// of its prediction stops on ground that is itself sloping, so the residual is re-multiplied
    /// by <c>1/(1 - s/tan y)</c> — unbounded at this arrival past about a 12% slope
    /// (<c>docs/KINETIC-FLOOR.md</c> section 5). <see cref="DeorbitShot.RoughGround"/> presents
    /// about 1%, so every relief number above is a floor and not an estimate.</para>
    /// </summary>
    [Theory]
    [InlineData(0.00)]
    [InlineData(0.02)]
    [InlineData(0.05)]
    [InlineData(0.08)]
    [InlineData(0.10)]
    public void WhatTheGroundsOwnSlopeMultipliesTheGapBy(double gradient)
    {
        ReleaseState(out double3 from, out double3 v);
        double3 along = AlongTrack(from, v);

        Func<double3, double> ramp = Ramp(from, v, gradient);

        double3 probe = Probe(from, v, ramp);
        (double3 landed, double _) =
            DeorbitShot.FlyTheRoundAsWarped(from, v, DeorbitShot.ScenarioWarp, default,
                                            new DeorbitShot.Relief { Surface = ramp });

        double flat = Math.Abs(Downrange(Probe(from, v, null),
                                         DeorbitShot.FlyTheRoundAsWarped(from, v, DeorbitShot.ScenarioWarp)
                                                    .GroundFixed,
                                         along));
        double here = Math.Abs(Downrange(probe, landed, along));

        Out.WriteLine($"{gradient * 100,4:F0}% downhill: {here,7:F0} m past the probe "
                      + $"({(flat > 0.0 ? here / flat : double.NaN):F2}x the {flat:F0} m on the flat)");
    }

    /// <summary>
    /// How far the constant-gradient part of <see cref="Ramp"/> runs either side of the aim.
    ///
    /// <para>Five times the disagreement being measured, so the slope is linear across the whole of
    /// it — and no further, because an arrival at 7.1 degrees descends at 12.5% and terrain
    /// approaching that gradient over tens of kilometres cuts the arc short on the way in. That is
    /// real behaviour rather than an artefact, and it is a different measurement from this one.</para>
    /// </summary>
    private const double RampSpanMetres = 2_000.0;

    /// <summary>
    /// A constant gradient along the arrival track, as the surface both sides read.
    ///
    /// <para>Downhill, which is the direction that <em>amplifies</em>: a round landing long meets
    /// ground that has fallen away and runs on further still. Uphill is the same magnitude with the
    /// correction's sign reversed, and is the stable case.</para>
    /// </summary>
    private static Func<double3, double> Ramp(double3 fromCci, double3 velocityCci, double gradient)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

        double3 at = Vec.Unit(hit.GroundFixedPointCci);

        // The ground track, which is what a slope is measured along: the inertial velocity with the
        // planet's own motion at that point taken out, brought into the frame the aim sits in.
        double3 overGround = Earth.UncarryCci(hit.VelocityCci - Earth.GroundVelocityCci(hit.PointCci),
                                              hit.Seconds);
        double3 along = Vec.Unit(overGround - at * Vec.Dot(overGround, at));

        return bodyFixed =>
        {
            double s = DeorbitShot.R * Math.Asin(Math.Clamp(Vec.Dot(Vec.Unit(bodyFixed), along), -1.0, 1.0));
            return DeorbitShot.R - gradient * RampSpanMetres * Math.Tanh(s / RampSpanMetres);
        };
    }

    /// <summary>
    /// The two stopping rules, which are not the same rule: the predictor bisects until the answer
    /// is within <see cref="ImpactPredictor.CrossingToleranceMetres"/> <em>below</em> the surface,
    /// the round walks its last sub-step back to the surface linearly.
    ///
    /// <para>A tolerance only ever crossed one way is a bias rather than a spread, and on this
    /// arrival every metre of height is eight of ground.</para>
    /// </summary>
    [Fact]
    public void WhatTheTwoStoppingRulesLeaveBetweenThem()
    {
        ReleaseState(out double3 from, out double3 v);

        Assert.True(ImpactPredictor.TryPredict(Earth, from, v, 2.0, 20_000.0,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)));

        double gamma = Vec.AngleBetween(hit.PointCci, hit.VelocityCci) - Math.PI / 2.0;
        double perMetre = 1.0 / Math.Tan(gamma);
        double depth = DeorbitShot.R - Vec.Len(hit.PointCci);

        Out.WriteLine($"arrival {gamma * 180.0 / Math.PI:F2} deg, {perMetre:F1} m of ground per m of height");
        Out.WriteLine($"the predictor stops {depth * 100:F1} cm under, worth {depth * perMetre:F2} m downrange");

        Slug round = new(from, v, null, 1, from, Vec.Zero)
        {
            Munition = Warhead,
            Ground = new DeorbitShot.Ball(),
            AirDensityAt = (pos, _) => DeorbitShot.DensityAt(pos),
        };

        for (double t = 0.0; t < 20_000.0 && round.State == RoundState.Flying; t += Medium.FaithfulStepInAir)
        {
            round.Update(Medium.FaithfulStepInAir, null, Earth.GravityCci(round.PositionEcl),
                         Earth.GroundVelocityCci(round.PositionEcl), from, Warhead,
                         DeorbitShot.DensityAt(round.PositionEcl));
        }

        double roundDepth = DeorbitShot.R - Vec.Len(round.PositionEcl);
        Out.WriteLine($"the round stops {roundDepth * 100:F1} cm under, worth {roundDepth * perMetre:F2} m");
    }

    /// <summary>
    /// The surface the round stops on against the surface the prediction flies to, which over water
    /// are not the same thing: <c>Ksa/GroundTest.cs</c> clamps the height field to the waterline and
    /// <c>Ksa/IcbmComputer.cs</c>'s <c>TerrainRadiusAt</c> does not.
    ///
    /// <para>Zero on a shot that arrives over dry land, which is what this one does — so it is not
    /// what the 1.6 km is. <c>SurfaceAgreementTests</c> prices the case where it is not zero, at
    /// 35 km of ground on the mean depth of Earth's shipped cubemap.</para>
    /// </summary>
    [Fact]
    public void WhetherThePredictionIsFlyingToASeabedOnThisShot()
    {
        ReleaseState(out double3 from, out double3 v);

        double3 toSeabed = Probe(from, v, DeorbitShot.RoughGround);
        double3 toSea = Probe(from, v, DeorbitShot.RoughGroundAtSea);

        double stands = DeorbitShot.RoughGround(toSeabed) - DeorbitShot.R;

        Out.WriteLine($"the arrival is over ground standing {stands:+0;-0} m against the waterline");
        Out.WriteLine($"prediction to the seabed against prediction to the sea: "
                      + $"{GroundMetres(toSeabed, toSea):F0} m");
    }

    /// <summary>
    /// Buoyancy: zero on this shot and not zero in general.
    ///
    /// <para><see cref="Slug"/> takes gravity through <see cref="Medium.Buoyancy"/> and
    /// <see cref="ImpactPredictor"/> takes it raw, so a round that declares a
    /// <see cref="MunitionProfile.NeutralDensityRatio"/> is predicted by a model that does not know
    /// it floats. Nothing in <see cref="Arsenal"/> declares one — which is why it contributes
    /// nothing here — but <c>Sim/PackReader.cs</c> reads the field, so a weapon pack can.</para>
    /// </summary>
    [Fact]
    public void BuoyancyIsZeroForThisWarheadAndAGapForAnyRoundThatDeclaresIt()
    {
        double3 gravity = Earth.GravityCci(new double3(DeorbitShot.R, 0, 0));

        Assert.Equal(gravity, Medium.Buoyancy(gravity, Warhead, densityRatio: 1.0));

        // The same call for a round that does float, which is what the predictor would ignore.
        MunitionProfile floats = new()
        {
            Name = "FLOATS",
            DisplayName = "a round that displaces its own mass",
            NeutralDensityRatio = 2f,
        };

        double3 felt = Medium.Buoyancy(gravity, floats, densityRatio: 1.0);

        Out.WriteLine($"the Mk 21 feels all of gravity; a round at half its neutral density feels "
                      + $"{Vec.Len(felt) / Vec.Len(gravity):P0} of it, which the predictor does not model");

        Assert.NotEqual(gravity, felt);
    }

    /// <summary>
    /// That both sides genuinely read the surface they are handed rather than one of them silently
    /// falling back to the mean sphere.
    ///
    /// <para>The whole decomposition is worthless if they do not, and the failure is invisible: a
    /// predictor handed no terrain function answers with <see cref="BallisticBody.SurfaceRadius"/>,
    /// and a round handed no ground test never stops at all.</para>
    /// </summary>
    [Fact]
    public void BothSidesActuallyStopOnTheSurfaceTheyAreHanded()
    {
        ReleaseState(out double3 from, out double3 v);

        // A kilometre down everywhere, so the difference cannot be an accident of where this arc
        // happens to land — which over RoughGround is ground standing within metres of the sphere.
        double3 onSphere = Probe(from, v, null);
        double3 lowered = Probe(from, v, _ => DeorbitShot.R - 1_000.0);

        Assert.True(GroundMetres(onSphere, lowered) > 1_000.0,
                    $"the predictor is not reading the surface it was handed; "
                    + $"{GroundMetres(onSphere, lowered):F0} m");

        DeorbitShot.Relief ground = new();
        (double3 landed, double _) =
            DeorbitShot.FlyTheRoundAsWarped(from, v, DeorbitShot.ScenarioWarp, default, ground);

        Assert.True(ground.Sampled > 0, "the round never asked where the ground was");

        double stoppedAt = Vec.Len(landed);
        double terrainThere = DeorbitShot.RoughGround(landed);

        Out.WriteLine($"the round stopped {stoppedAt - DeorbitShot.R:+0.0;-0.0} m off the mean sphere, "
                      + $"where the relief stands {terrainThere - DeorbitShot.R:+0.0;-0.0} m");

        Assert.True(Math.Abs(stoppedAt - terrainThere) < 5.0,
                    $"the round stopped {Math.Abs(stoppedAt - terrainThere):F1} m off the relief");
    }
}
