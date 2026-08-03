using Brutal.Numerics;
using Xunit;

namespace AirDefence.Tests;

/// <summary>
/// Guards the crossing-target test in <see cref="InterceptorTests"/>. A hit test only proves
/// something if the same geometry misses when the guidance is weakened, otherwise the scenario
/// was winnable by flying straight and the test was never checking the lead at all.
/// </summary>
public class GuidanceDiscriminationTests
{
    private static readonly object TargetHandle = new();
    private static readonly double3 NoGravity = new(0, 0, 0);

    /// <summary>The same crossing engagement as the interception test, parameterised by N.</summary>
    private static (RoundState State, double Closest) FlyCrossingEngagement(float navConstant)
    {
        var munition = new MunitionProfile { Name = "test", DisplayName = "test", DragK = 0f, NavConstant = navConstant };

        var round = new Interceptor(
            new double3(0, 0, 0),
            new double3(munition.LaunchSpeed, 0, 0),
            TargetHandle,
            tube: 1,
            platformEcl: default);

        // Target sits dead ahead at launch and then runs hard across the line of fire, so a
        // round that does not lead arrives roughly a kilometre behind it.
        double3 targetStart = new(2500, 0, 0);
        double3 targetVel = new(0, 250, 0);

        const double dt = 1.0 / 60.0;
        double t = 0.0;
        double closest = double.MaxValue;

        while (round.State == RoundState.Flying && t < 30.0)
        {
            double3 targetPos = targetStart + targetVel * t;
            closest = Math.Min(closest, Vec.Len(targetPos - round.PositionEcl));
            round.Update(dt, new TargetState(targetPos, targetVel, 5.0), NoGravity, frameVelocityEcl: default, platformEcl: default, munition);
            t += dt;
        }

        return (round.State, closest);
    }

    [Fact]
    public void WithoutProportionalNavigation_TheCrossingTargetIsMissed()
    {
        var (state, closest) = FlyCrossingEngagement(navConstant: 0f);

        Assert.NotEqual(RoundState.Detonated, state);
        Assert.True(closest > 500.0,
            $"unguided round passed within {closest:F0} m - the crossing scenario is too easy to prove a lead");
    }

    [Fact]
    public void WithProportionalNavigation_TheSameEngagementHits()
    {
        var (state, _) = FlyCrossingEngagement(navConstant: 4f);

        Assert.Equal(RoundState.Detonated, state);
    }

    /// <summary>
    /// Under-gained navigation should do measurably worse than the tuned value. This pins the
    /// direction of the relationship, so a sign error in the LOS rate cannot pass unnoticed.
    /// </summary>
    [Fact]
    public void RaisingTheNavConstant_ImprovesTheMissDistance()
    {
        var weak = FlyCrossingEngagement(navConstant: 0.5f);
        var tuned = FlyCrossingEngagement(navConstant: 4f);

        Assert.True(tuned.Closest < weak.Closest,
            $"N=4 closed to {tuned.Closest:F1} m but N=0.5 closed to {weak.Closest:F1} m");
    }
}
