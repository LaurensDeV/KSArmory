using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSArmory.Tests;

/// <summary>
/// Whether the arrival floor, priced from the state it is latched in, is still one the burn's own
/// exit can fly. <c>docs/ACCURACY-PLAN.md</c> item 5e, and the answer is that it always is.
///
/// <para><b>The suspicion was that the floor is latched early and spent late</b> — priced from a
/// low, slow vehicle with the whole stack aboard, satisfied by an arc departing from burnout — and
/// that past some angle no such arc exists, which would explain why
/// <see cref="IcbmConfig.ArrivalPreference"/> at 0.8 is a settled loss.</para>
///
/// <para><b>It goes the other way.</b> The post-boost state reaches <em>steeper</em> arcs than the
/// latch state can afford, at every range measured: the existence wall at cutoff is 77.8 degrees
/// against 67.8 affordable at latch on a 2,736 km shot, 62.2 against 56.2 at 6,269, and 40.9
/// against 37.5 at 12,902. So re-checking the floor against the exit would <em>raise</em> it, and
/// the latched floor is never the binding constraint.</para>
///
/// <para>What actually ends the control is cost, and specifically the trim's: flown
/// 2026-09-01-2148, the debt still owed when the warheads left runs 2.63 / 2.56 / 2.60 m/s at
/// preferences 0, 0.5 and 0.65 and jumps to <b>4.19</b> at 0.8, where the healthy-mode median goes
/// 0.029 km to 0.098. It is not the divergent mode either — 3 of 24, Fisher p=0.234.</para>
/// </summary>
public class ArrivalFloorRestateTests(ITestOutputHelper Out)
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);
    private static double3 Downrange(double m) => new(R * Math.Cos(m / R), R * Math.Sin(m / R), 0);

    private static IcbmFlightRig Rig() => new()
    {
        Body = Earth,
        PositionCci = new double3(R + 400_000.0, 0, 0),
        VelocityCci = new double3(0, Math.Sqrt(Mu / (R + 400_000.0)), 0),
        Stages = [new() { DryMassKg = 3_000, PropellantKg = 40_000, ThrustNewtons = 300_000, ExhaustVelocity = 3_100 }],
        CommandLatencyFrames = 1,
        ThrottleRatePerSecond = 2.0,
        MinThrottle = 0.12,
        StepJitter = 0.5,
    };

    /// <summary>
    /// What the budget said at latch, against what the burn's exit can actually fly.
    ///
    /// <para>Measurement, not an assertion about a number — the point is the shape of the gap
    /// across range and preference.</para>
    /// </summary>
    [Theory]
    [InlineData(2_736_000.0)]
    [InlineData(6_269_000.0)]
    [InlineData(12_902_000.0)]
    public void TheFloorIsPricedFromOneStateAndFlownFromAnother(double shotMetres)
    {
        Out.WriteLine($"--- {shotMetres / 1000:F0} km ---");
        Out.WriteLine($"{"pref",6}{"afford",9}{"latched",9}{"wall at cutoff",20}{"arc there?",12}");

        foreach (double preference in new[] { 0.5, 0.65, 0.8 })
        {
            IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = preference });
            double3 aim = Downrange(shotMetres);
            IcbmFlightRig.Flight flight = Rig().Fly(program, aim, 0.02, 6_000.0);

            if (!flight.Reached)
            {
                Out.WriteLine($"{preference,6:F2}   did not reach cutoff ({flight.Hold})");
                continue;
            }

            // Where the arc stops EXISTING from the state the burn actually left, which is a
            // different question from what a further burn could afford -- by cutoff there is
            // nothing left to spend, so affordability answers zero for every angle and says
            // nothing.
            double wall = ExistenceWallDeg(flight.CutoffPositionCci, flight.CutoffVelocityCci, aim);

            bool arcExists = BallisticArc.TryCheapest(
                Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, aim,
                out _, 1.0, false, double.NaN, program.ArrivalFloorDeg);

            Out.WriteLine($"{preference,6:F2}{program.ArrivalFloorFromDeg,9:F1}"
                          + $"{program.ArrivalFloorDeg,9:F1}{wall,20:F1}"
                          + $"{(arcExists ? "yes" : "NO"),12}");
        }
    }

    /// <summary>
    /// The steepest floor an arc can still be found for from this state, ignoring cost entirely.
    ///
    /// <para>Bisected the same way <see cref="ArrivalBudget"/> bisects on affordability, and to the
    /// same resolution, so the two are read against each other without a units question.</para>
    /// </summary>
    private static double ExistenceWallDeg(double3 positionCci, double3 velocityCci, double3 aimCci)
    {
        bool Solvable(double floor) => BallisticArc.TryCheapest(
            Earth, positionCci, velocityCci, aimCci, out _, 1.0, false, double.NaN, floor);

        if (!Solvable(0.0)) return double.NaN;

        double lo = 0.0;
        double hi = ArrivalBudget.SteepestConsideredDeg;

        while (hi - lo > ArrivalBudget.ResolutionDeg)
        {
            double mid = 0.5 * (lo + hi);
            if (Solvable(mid)) lo = mid; else hi = mid;
        }

        return lo;
    }

    /// <summary>
    /// Every preference the control offers latches a floor the burn's exit can still fly.
    ///
    /// <para>The guard this leaves behind. It holds today at the top of the range as well as at the
    /// shipped 0.5, so if a change to the ascent or the budget ever does make the exit the binding
    /// constraint, this is what says so — rather than the shot quietly falling back to a short
    /// steep arc whose aim costs several times as much to move.</para>
    /// </summary>
    [Theory]
    [InlineData(2_736_000.0, 0.5)]
    [InlineData(2_736_000.0, 0.8)]
    [InlineData(6_269_000.0, 0.5)]
    [InlineData(6_269_000.0, 0.8)]
    [InlineData(12_902_000.0, 0.5)]
    public void EveryPreferenceLatchesAFloorTheExitCanFly(double shotMetres, double preference)
    {
        IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = preference });
        double3 aim = Downrange(shotMetres);
        IcbmFlightRig.Flight flight = Rig().Fly(program, aim, 0.02, 6_000.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold}");
        Assert.True(BallisticArc.TryCheapest(
                        Earth, flight.CutoffPositionCci, flight.CutoffVelocityCci, aim,
                        out _, 1.0, false, double.NaN, program.ArrivalFloorDeg),
                    $"the latched floor of {program.ArrivalFloorDeg:F1} deg has no arc from cutoff");
    }

    /// <summary>
    /// The exit reaches steeper than the latch could afford, which is 5e's premise inverted.
    ///
    /// <para>The one assertion worth keeping from the measurement: if this ever reverses, the floor
    /// really is being priced from the wrong state and 5e becomes live again.</para>
    /// </summary>
    [Theory]
    [InlineData(2_736_000.0)]
    [InlineData(6_269_000.0)]
    [InlineData(12_902_000.0)]
    public void TheExitReachesSteeperThanTheLatchCouldAfford(double shotMetres)
    {
        IcbmProgram program = new(new IcbmConfig { Armed = true, ArrivalPreference = 0.5 });
        double3 aim = Downrange(shotMetres);
        IcbmFlightRig.Flight flight = Rig().Fly(program, aim, 0.02, 6_000.0);

        Assert.True(flight.Reached, $"never reached cutoff: {flight.Hold}");

        double wall = ExistenceWallDeg(flight.CutoffPositionCci, flight.CutoffVelocityCci, aim);

        Assert.True(wall > program.ArrivalFloorFromDeg,
                    $"the exit walls at {wall:F1} deg, under the {program.ArrivalFloorFromDeg:F1} "
                    + "the latch state called affordable -- 5e is live again");
    }
}
