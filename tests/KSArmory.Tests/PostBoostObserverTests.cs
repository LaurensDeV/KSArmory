using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// What actually moves the number the post-boost correction reads, apportioned between the two
/// things that can.
///
/// <para>The correction's only observer is a prediction flown from the bus's own state with the
/// ejection kick already added, and two things move that prediction without the shot having
/// changed: the <b>thrusters</b>, which move the vehicle it is flown from, and the <b>nose</b>,
/// which is the direction the kick is added along. Both look identical from the miss alone — a
/// number that walks while the aim is frozen.</para>
///
/// <para>Measurement, like <c>MirvBudgetTests</c>, on the same shot through
/// <see cref="DeorbitShot"/>. What it asserts is the <em>ordering</em> of the two terms, because
/// that is what decides where the gate goes: a gate on the smaller one is a gate that changes
/// nothing.</para>
/// </summary>
public class PostBoostObserverTests(ITestOutputHelper Out)
{
    private static BallisticBody Earth => DeorbitShot.Earth;

    private static MunitionProfile Warhead => DeorbitShot.Warhead;

    /// <summary>The band of release directions a separated bus is measured drifting through.</summary>
    private const double FlownLowDegrees = 95.0;

    /// <inheritdoc cref="FlownLowDegrees"/>
    private const double FlownHighDegrees = 119.0;

    /// <summary>What a stock 3 m decoupler does to a six-tonne bus, in metres per second.</summary>
    private const double SeparationShove = 1.1;

    private static double3 Land(double3 fromCci, double3 velocityCci)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 2.0,
                                               ImpactPredictor.DefaultMaxSeconds,
                                               out ImpactPredictor.Impact hit, null, null,
                                               new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)),
                    "the prediction never came down");
        return hit.GroundFixedPointCci;
    }

    /// <summary>
    /// The aim correction as <c>Ksa/IcbmComputer.cs</c> rides it during the burn, so the cutoff the
    /// sweeps run from is the one the guidance actually converges on rather than an ideal arc.
    /// </summary>
    private sealed class Loop : IcbmFlightRig.IAimLoop
    {
        private readonly AimCorrection _aim = new();
        private double _sincePredict = double.PositiveInfinity;

        public double BiasMetres => Vec.Len(_aim.BiasCci);

        public double3 Apply(double3 aimNowCci) => _aim.Apply(aimNowCci);

        public bool IsSteady => _aim.IsSteady;

        public void AfterUpdate(IcbmProgram program, in IcbmCommand command, double3 aimNowCci,
                                double step)
        {
            if (double.IsFinite(program.CommittedArrivalFromNow)) _aim.Freeze();

            _sincePredict += step;
            if (_sincePredict < 0.5) return;
            _sincePredict = 0.0;

            if (!program.IsBurning || program.Arc is not { } arc) return;

            double3 kick = Vec.Unit(command.ThrustDirectionCci) * Warhead.LaunchSpeed;

            if (!ImpactPredictor.TryPredict(Earth, program.CutoffPositionCci,
                                            arc.RequiredVelocityCci + kick, 2.0,
                                            ImpactPredictor.DefaultMaxSeconds,
                                            out ImpactPredictor.Impact hit, null, null,
                                            new ImpactPredictor.Drag(DeorbitShot.DensityAt, Warhead)))
            {
                return;
            }

            double departsIn = double.IsFinite(command.SecondsToCutoff)
                               ? Math.Max(0.0, command.SecondsToCutoff) : 0.0;

            _aim.Observe(Earth.UncarryCci(hit.GroundFixedPointCci, departsIn), aimNowCci);
        }
    }

    /// <summary>Where the guidance leaves the bus, with its aim already corrected.</summary>
    private readonly record struct Cutoff(double3 PositionCci, double3 VelocityCci, double3 NoseCci,
                                          double3 TargetCci, double BiasMetres);

    private static Cutoff AtCutoff()
    {
        IcbmFlightRig rig = new()
        {
            Body = Earth,
            PositionCci = new double3(DeorbitShot.R + 300_000.0, 0, 0),
            VelocityCci = new double3(0, Math.Sqrt(DeorbitShot.Mu / (DeorbitShot.R + 300_000.0)), 0),
            Stages =
            [
                new()
                {
                    DryMassKg = 3_000, PropellantKg = 40_000,
                    ThrustNewtons = 300_000, ExhaustVelocity = 3_100,
                },
            ],
            CommandLatencyFrames = 1,
            ThrottleRatePerSecond = 2.0,
            MinThrottle = 0.12,
            StepJitter = 0.5,
        };

        double3 aimAtEpoch = new(DeorbitShot.R * Math.Cos(DeorbitShot.RangeMetres / DeorbitShot.R),
                                 DeorbitShot.R * Math.Sin(DeorbitShot.RangeMetres / DeorbitShot.R), 0);

        Loop loop = new();
        rig.AimLoop = loop;

        IcbmProgram program = new(new IcbmConfig { Armed = true });
        IcbmFlightRig.Flight flight = rig.Fly(program, aimAtEpoch, DeorbitShot.NominalFrame, 6_000.0);

        Assert.True(flight.Reached, $"the burn never reached coast: {flight.Hold}");

        double3 nose = Vec.Unit(flight.CoastDirectionCci.Equals(Vec.Zero)
                                    ? flight.LastBurnDirectionCci
                                    : flight.CoastDirectionCci);

        return new Cutoff(flight.CutoffPositionCci, flight.CutoffVelocityCci, nose,
                          Earth.CarryCci(aimAtEpoch, flight.CutoffSeconds), loop.BiasMetres);
    }

    /// <summary>The predicted miss with the modelled kick turned <paramref name="degrees"/> off the nose.</summary>
    private static double MissWithKickTurned(in Cutoff at, double degrees, bool otherPlane)
    {
        double3 axis = otherPlane
                       ? Vec.Unit(Vec.Cross(at.NoseCci, Vec.AnyPerpendicular(at.NoseCci)))
                       : Vec.AnyPerpendicular(at.NoseCci);

        double a = degrees * Math.PI / 180.0;
        double3 kick = Vec.Unit(at.NoseCci * Math.Cos(a) + axis * Math.Sin(a)) * Warhead.LaunchSpeed;

        return DeorbitShot.GroundMetres(Land(at.PositionCci, at.VelocityCci + kick), at.TargetCci);
    }

    /// <summary>The predicted miss with the bus <paramref name="metresPerSecond"/> off its arc.</summary>
    private static double MissWithBusOffItsArc(in Cutoff at, double metresPerSecond)
    {
        // Radially, which is the worst of the three and the direction a decoupler on the mounting
        // joint mostly pushes along once the nose is anywhere but along the track.
        double3 error = Vec.Unit(at.PositionCci) * metresPerSecond;
        double3 kick = at.NoseCci * Warhead.LaunchSpeed;

        return DeorbitShot.GroundMetres(Land(at.PositionCci, at.VelocityCci + kick + error),
                                        at.TargetCci);
    }

    /// <summary>
    /// The whole apportionment. With the aim frozen the predicted miss can only move because the
    /// bus moved or because its nose turned, and the two are not the same size.
    ///
    /// <para>The nose is the one that matters: a separated bus has a 22.11 deg pointing band, free
    /// roll angle and no elected control part, and its salvo is thrown 95-119 deg off the
    /// platform's track across three otherwise identical runs. The trim, at a reading the settle
    /// gate admits, is already down at <see cref="BusTrim.SettledMetresPerSecond"/>.</para>
    /// </summary>
    [Fact]
    public void TheNoseMovesTheReadingFarFurtherThanTheTrimCan()
    {
        Cutoff at = AtCutoff();
        double onTheNose = MissWithKickTurned(at, 0.0, otherPlane: false);

        Out.WriteLine($"cutoff {Earth.AltitudeOf(at.PositionCci) / 1000.0:F0} km up at "
                      + $"{Vec.Len(at.VelocityCci):F0} m/s, aim biased {at.BiasMetres / 1000.0:F1} km, "
                      + $"kick {Warhead.LaunchSpeed} m/s");
        Out.WriteLine($"predicted miss with the kick on the nose: {onTheNose / 1000.0:F2} km");
        Out.WriteLine("");
        Out.WriteLine("the nose turned (km of predicted miss):");

        foreach (double deg in new[] { 11.06, 22.11, 45.0, FlownLowDegrees, FlownHighDegrees, 180.0 })
        {
            Out.WriteLine($"  {deg,6:F2} deg | {MissWithKickTurned(at, deg, false) / 1000.0,7:F2} "
                          + $"| {MissWithKickTurned(at, deg, true) / 1000.0,7:F2}");
        }

        Out.WriteLine("");
        Out.WriteLine("the bus off its arc, kick fixed on the nose (km of predicted miss):");

        foreach (double dv in new[] { BusTrim.SettledMetresPerSecond, 0.05, 0.2, SeparationShove })
        {
            Out.WriteLine($"  {dv,6:F2} m/s | {MissWithBusOffItsArc(at, dv) / 1000.0,7:F2}");
        }

        double noseSwing = Math.Min(
            Math.Abs(MissWithKickTurned(at, FlownLowDegrees, false) - onTheNose),
            Math.Abs(MissWithKickTurned(at, FlownLowDegrees, true) - onTheNose));

        // At a reading the gate admits. Anything larger and the trim is still firing, which the
        // sequencer has always refused to read across.
        double trimSwing =
            Math.Abs(MissWithBusOffItsArc(at, BusTrim.SettledMetresPerSecond) - onTheNose);

        Out.WriteLine("");
        Out.WriteLine($"nose, at the low end of the flown band: {noseSwing / 1000.0:F2} km");
        Out.WriteLine($"trim, at a reading the gate admits:     {trimSwing / 1000.0:F2} km");
        Out.WriteLine($"ratio: {noseSwing / trimSwing:F0}x");

        // Two kilometres, not the eight this read when the round left its tube at 2 m/s. The term
        // is exactly linear in the kick and the shipped one is a quarter of that, so the bar moved
        // with it. What is being asserted is the ratio below; this only keeps the test honest about
        // the term still being large in absolute terms.
        Assert.True(noseSwing > 2_000.0,
                    $"the nose only moved the reading {noseSwing / 1000.0:F2} km across the flown band");
        Assert.True(trimSwing < AimCorrection.ImprovedByMetres,
                    $"the trim leaves {trimSwing:F0} m at a settled reading, which is more than the "
                    + $"{AimCorrection.ImprovedByMetres:F0} m the correction judges a pass by");
        Assert.True(noseSwing > 30.0 * trimSwing,
                    $"only {noseSwing / trimSwing:F0}x between them — gating on the nose would not "
                    + "be the change that matters");
    }

    /// <summary>
    /// Quietening the round's ejection kick took the nose out of first place.
    ///
    /// <para>At 2 m/s off the tube the nose dominated everything, including a separation shove that
    /// nothing had taken out — which is why the observer gates on it. At the shipped quarter of that
    /// the two have crossed over, because the nose term is linear in the kick and the shove is not
    /// affected by it at all.</para>
    ///
    /// <para><b>It does not make the gate pointless.</b> A wholly un-nulled shove is a state no
    /// reading is ever taken in — the sequencer waits for the trim to finish first — so the nose is
    /// still the largest thing present when a reading is actually taken, which
    /// <see cref="TheNoseMovesTheReadingFarFurtherThanTheTrimCan"/> holds. What the crossover does
    /// change is which term is worth attacking next.</para>
    /// </summary>
    [Fact]
    public void TheQuieterKickTookTheNoseOutOfFirstPlace()
    {
        Cutoff at = AtCutoff();
        double onTheNose = MissWithKickTurned(at, 0.0, otherPlane: false);

        double worstTrim = Math.Abs(MissWithBusOffItsArc(at, SeparationShove) - onTheNose);
        double nose = Math.Abs(MissWithKickTurned(at, FlownLowDegrees, true) - onTheNose);

        Out.WriteLine($"a whole {SeparationShove} m/s shove un-nulled: {worstTrim / 1000.0:F2} km");
        Out.WriteLine($"the nose at {FlownLowDegrees:F0} deg:                {nose / 1000.0:F2} km");

        Assert.True(nose < worstTrim,
                    $"the nose is {nose / 1000.0:F2} km against {worstTrim / 1000.0:F2} km for a "
                    + "wholly un-nulled separation — at this kick the nose should no longer be the "
                    + "larger of the two");
    }

    /// <summary>
    /// What sizes <see cref="PostBoostAim.SteadyWithinDegrees"/>: how far the predicted impact
    /// moves per degree the modelled kick turns, near the nose.
    ///
    /// <para>The tolerance has to keep that under what the correction can resolve between passes —
    /// <see cref="AimCorrection.ImprovedByMetres"/> — or the gate admits readings that flip a
    /// better pass into a worse one.</para>
    /// </summary>
    [Fact]
    public void TheSettleToleranceKeepsAReadingInsideWhatAPassIsJudgedBy()
    {
        Cutoff at = AtCutoff();
        double3 onTheNose = Land(at.PositionCci, at.VelocityCci + at.NoseCci * Warhead.LaunchSpeed);

        double worst = 0.0;

        foreach (bool otherPlane in new[] { false, true })
        {
            double3 axis = otherPlane
                           ? Vec.Unit(Vec.Cross(at.NoseCci, Vec.AnyPerpendicular(at.NoseCci)))
                           : Vec.AnyPerpendicular(at.NoseCci);

            double a = PostBoostAim.SteadyWithinDegrees * Math.PI / 180.0;
            double3 kick = Vec.Unit(at.NoseCci * Math.Cos(a) + axis * Math.Sin(a))
                         * Warhead.LaunchSpeed;

            double moved = DeorbitShot.GroundMetres(
                Land(at.PositionCci, at.VelocityCci + kick), onTheNose);

            Out.WriteLine($"{PostBoostAim.SteadyWithinDegrees:F1} deg of nose moves the predicted "
                          + $"impact {moved:F0} m "
                          + $"({moved / PostBoostAim.SteadyWithinDegrees:F0} m per degree)");

            worst = Math.Max(worst, moved);
        }

        Assert.True(worst < AimCorrection.ImprovedByMetres,
                    $"a reading inside the settle band still moves {worst:F0} m, against the "
                    + $"{AimCorrection.ImprovedByMetres:F0} m a pass is judged by");
    }
}
