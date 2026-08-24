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
    /// <summary>
    /// One noisy reading must not buy a step larger than the miss it is removing.
    ///
    /// <para>The step is the error divided by how much the aim was last measured to be worth, so an
    /// estimate below one asks to move the aim further than the error itself — and two observations
    /// taken a moment apart can produce one from noise alone. Flown, before the bound: 28.6 km of
    /// bias to 192.9 in a single cycle, then the clamp, then 205 km of miss.</para>
    /// </summary>
    [Fact]
    public void ItNeverMovesTheAimFurtherThanTheMissItIsRemoving()
    {
        AimCorrection aim = new();
        double3 target = new(6_371_000.0, 0, 0);
        double3 along = new(0, 1, 0);

        // A plant that answers with noise rather than a response: the impact barely moves however
        // far the aim is pushed, which is what drives the estimate towards zero.
        for (int i = 0; i < 20; i++)
        {
            double3 before = aim.BiasCci;
            double wobble = (i % 2 == 0 ? 1.0 : -1.0) * 40.0;

            aim.Observe(target + along * (50_000.0 + wobble), target);

            double step = Vec.Len(aim.BiasCci - before);

            Assert.True(step <= 50_100.0,
                        $"cycle {i} moved the aim {step / 1000.0:F1} km to remove 50 km of miss");
        }
    }

    /// <summary>
    /// It converges whatever the aim is worth, which is the whole point of measuring rather than
    /// assuming.
    ///
    /// <para>The plant is not one this loop chooses. While the solver may pick its own flight time,
    /// moving the aim moves the impact by about as much again; once the guidance latches the
    /// arrival, the same aim change forces a different trajectory to arrive at the same instant and
    /// the impact moves several times further. A fixed fraction can only be right for one of those,
    /// and at 3,459 km the wrong one walked the bias to its 300 km limit with 209 km of miss.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(5.0)]
    [InlineData(0.5)]

    // Stiff enough that any fixed fraction large enough to converge the slack plants above will
    // oscillate here instead: at a quarter this is a loop gain of two, which never settles.
    [InlineData(8.0)]
    public void ItConvergesWhateverMovingTheAimIsWorth(double plant)
    {
        AimCorrection aim = new();
        double3 target = new(6_371_000.0, 0, 0);
        double3 along = new(0, 1, 0);

        for (int i = 0; i < 60; i++)
        {
            // The loop's own convention: the arc is aimed at target + bias and lands that plus
            // whatever the flight loses, so moving the aim by one moves the impact by the plant.
            double missAlong = 50_000.0 + plant * Vec.Dot(aim.BiasCci, along);
            aim.Observe(target + along * missAlong, target);
        }

        double left = Math.Abs(50_000.0 + plant * Vec.Dot(aim.BiasCci, along));

        Assert.True(left < 500.0,
                    $"a plant of {plant:F1} left {left / 1000.0:F1} km of miss after sixty cycles");
        Assert.False(aim.Settled, "it should have converged rather than given up");
    }

    /// <summary>
    /// A correction that is making the miss worse stops, and keeps the best aim it found.
    ///
    /// <para>It is a feedback loop against a plant that is not always the one its gain was chosen
    /// for. While the solver may pick its own flight time, moving the aim moves the impact by about
    /// as much again and a gain of a half converges; once the arrival is latched, the same aim
    /// change forces a different trajectory to arrive at the same instant, and on a shallow
    /// near-orbital shot the response is amplified past where that gain is stable.</para>
    ///
    /// <para>Flown at 3,459 km: 55.1 km of miss down to 43.7 at 77 km of bias, then 44.7, 47.9,
    /// 65.2, 126.1, and pinned at the 300 km limit with 209 km of miss — the loop walking away from
    /// its own best while the thing it was removing grew.</para>
    /// </summary>
    [Fact]
    public void ACorrectionThatStopsHelpingKeepsItsBestRatherThanRunningAway()
    {
        AimCorrection aim = new();
        double3 target = new(6_371_000.0, 0, 0);
        double3 along = new(0, 1, 0);

        // A plant that over-responds: the impact moves three times whatever the aim was moved, so
        // every correction overshoots and the next one is larger. Gain 0.5 diverges against it.
        const double PlantGain = 3.0;
        double bestSeen = double.PositiveInfinity;

        for (int i = 0; i < 40; i++)
        {
            double biasAlong = Vec.Dot(aim.BiasCci, along);
            double missAlong = 50_000.0 - PlantGain * biasAlong;

            bestSeen = Math.Min(bestSeen, Math.Abs(missAlong));
            aim.Observe(target + along * missAlong, target);
        }

        Assert.True(aim.Settled, "it should have noticed it was not improving");

        // And it kept the best it found rather than the last thing it tried.
        double left = Math.Abs(50_000.0 - PlantGain * Vec.Dot(aim.BiasCci, along));

        Assert.True(left <= bestSeen + AimCorrection.ImprovedBy(bestSeen),
                    $"kept an aim worth {left:F0} m against a best of {bestSeen:F0} m");

        // The point of the rule: it is nowhere near the clamp it would have run to.
        Assert.True(Vec.Len(aim.BiasCci) < AimCorrection.MaxMetres / 2.0,
                    $"bias ran to {Vec.Len(aim.BiasCci) / 1000.0:F0} km");
    }

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
