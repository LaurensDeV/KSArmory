using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Aiming somewhere other than the target, so the round arrives at it.
///
/// <para>The transfer solver puts the arc through a point, in vacuum. The round stops where the
/// ground is. On a shallow arrival the arc covers roughly twelve kilometres of ground per kilometre
/// of height, so ground that is not where the solver assumed is worth tens of kilometres — and no
/// amount of flying the solution better closes it, because the solution is being flown perfectly.
/// </para>
/// </summary>
public class AimCorrectionTests
{
    private const double Mu = 3.986004418e14;
    private const double R = 6_371_000.0;

    private static BallisticBody Earth => new(Mu, R, new double3(0, 0, 1), 7.2921159e-5);

    /// <summary>
    /// Ground that rises toward the target, which is what an arrival over the Andes from the west
    /// actually is — and the shape that makes a shallow arc stop short of where it was aimed.
    /// </summary>
    private static Func<double3, double> RisingGround(double3 towardCci, double metresPerRadian)
        => point =>
        {
            double angle = Vec.AngleBetween(point, towardCci);
            return R + Math.Max(0.0, metresPerRadian * (0.5 - angle));
        };

    private static double3 FlyAndLand(double3 fromCci, double3 velocityCci, Func<double3, double> ground)
    {
        Assert.True(ImpactPredictor.TryPredict(Earth, fromCci, velocityCci, 1.0, 12_000.0,
                                               out ImpactPredictor.Impact hit, ground));
        return hit.GroundFixedPointCci;
    }

    [Fact]
    public void AShallowArrivalOverRisingGroundLandsShortOfAPerfectSolution()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 orbital = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        double3 target = new(R * Math.Cos(0.405), R * Math.Sin(0.405), 0);

        Assert.True(BallisticArc.TryCheapest(Earth, from, orbital, target, out BallisticArc.Solution s));

        double3 landed = FlyAndLand(from, s.RequiredVelocityCci, RisingGround(target, 12_000.0));
        double miss = R * Vec.AngleBetween(landed, target);

        Assert.True(miss > 20_000.0,
                    $"the uncorrected shot should land well short; it was {miss / 1000.0:F1} km off");
    }

    /// <summary>
    /// And correcting the aim by what the flown arc loses closes it, which is the whole claim.
    /// </summary>
    [Fact]
    public void CorrectingTheAimByWhatTheFlownArcLosesClosesIt()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 orbital = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        double3 target = new(R * Math.Cos(0.405), R * Math.Sin(0.405), 0);

        Func<double3, double> ground = RisingGround(target, 12_000.0);

        AimCorrection correction = new();
        double miss = double.NaN;

        // The loop the computer runs: solve to the corrected aim, fly it, fold the error back in.
        for (int i = 0; i < 12; i++)
        {
            double3 aim = correction.Apply(target);
            Assert.True(BallisticArc.TryCheapest(Earth, from, orbital, aim, out BallisticArc.Solution s));

            double3 landed = FlyAndLand(from, s.RequiredVelocityCci, ground);
            miss = R * Vec.AngleBetween(landed, target);

            correction.Observe(landed, target);
        }

        Assert.True(miss < 2_000.0, $"after twelve corrections it was still {miss / 1000.0:F1} km off");
        Assert.True(Vec.Len(correction.BiasCci) > 1_000.0, "it should have had to move the aim at all");
    }

    /// <summary>
    /// And it has to be fed the arc being flown <em>to</em>, not the state being flown
    /// <em>through</em>. Mid-burn the vehicle is nowhere near its cutoff conic, so a prediction
    /// from where it currently is measures a trajectory nobody intends to fly — and the correction
    /// that comes back is about that trajectory rather than about the shot.
    /// </summary>
    [Fact]
    public void PredictingFromTheStateBeingFlownThroughCorrectsTheWrongArc()
    {
        double3 from = new(R + 200_000.0, 0, 0);
        double3 orbital = new(0, Math.Sqrt(Mu / (R + 200_000.0)), 0);
        double3 target = new(R * Math.Cos(0.405), R * Math.Sin(0.405), 0);

        Func<double3, double> ground = RisingGround(target, 12_000.0);

        // Still under power: lower, slower, and a long way from the conic it will depart on.
        double3 midBurnFrom = new(R + 60_000.0, 0, 0);
        double3 midBurnVelocity = orbital * 0.7;

        AimCorrection correction = new();
        double miss = double.NaN;

        for (int i = 0; i < 12; i++)
        {
            double3 aim = correction.Apply(target);
            Assert.True(BallisticArc.TryCheapest(Earth, from, orbital, aim, out BallisticArc.Solution s));

            // What the shot will actually do, which is not what is being observed.
            miss = R * Vec.AngleBetween(FlyAndLand(from, s.RequiredVelocityCci, ground), target);

            correction.Observe(FlyAndLand(midBurnFrom, midBurnVelocity, ground), target);
        }

        Assert.True(miss > 20_000.0,
                    $"correcting off the mid-burn state should not close the shot; it reached "
                    + $"{miss / 1000.0:F1} km, which means the two states are not distinguishable here");
    }

    /// <summary>
    /// The correction is scored against the target, never against the aim it produced. Scoring it
    /// on its own output reports a perfect shot however far the rounds land.
    /// </summary>
    [Fact]
    public void ItIsScoredAgainstTheTargetRatherThanItsOwnAim()
    {
        double3 target = new(R, 0, 0);
        double3 landedShort = new(R * Math.Cos(-0.01), R * Math.Sin(-0.01), 0);

        AimCorrection correction = new();
        correction.Observe(landedShort, target);

        Assert.True(Vec.Len(correction.BiasCci) > 0.0, "an error against the target has to move it");

        // Landing exactly on the target leaves the bias where it is, however large.
        double3 held = correction.BiasCci;
        correction.Observe(target, target);
        Assert.Equal(held, correction.BiasCci);
    }

    [Fact]
    public void ItWillNotWalkTheAimAcrossAContinent()
    {
        double3 target = new(R, 0, 0);
        double3 wildlyOff = new(-R, 0, 0);

        AimCorrection correction = new();
        for (int i = 0; i < 50; i++) correction.Observe(wildlyOff, target);

        Assert.True(Vec.Len(correction.BiasCci) <= AimCorrection.MaxMetres + 1.0,
                    $"the bias ran to {Vec.Len(correction.BiasCci) / 1000.0:F0} km");
    }
}
