using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// What the stack can still do. Every number here decides when the engines stop, and the burn's
/// last second is the one that settles where the warheads land.
/// </summary>
public class BoosterPerformanceTests
{
    private static BoosterPerformance Stage
        => new(ThrustNewtons: 260_000, MassFlowKgPerSec: 86.667, TotalMassKg: 13_200, PropellantMassKg: 12_000);

    [Fact]
    public void ExhaustVelocityAndBurnTimeComeOffTheThrustAndTheFlow()
    {
        Assert.Equal(3000.0, Stage.ExhaustVelocity, 0);
        Assert.Equal(138.5, Stage.BurnSecondsRemaining, 1);
        Assert.Equal(19.7, Stage.AccelerationNow, 1);
    }

    [Fact]
    public void WhatIsLeftInTheTanksIsTsiolkovskyOverThem()
    {
        Assert.Equal(3000.0 * Math.Log(13_200.0 / 1_200.0), Stage.DeltaVRemaining, 1);
    }

    /// <summary>
    /// The stack gets lighter as it burns, and treating the acceleration as constant is what makes
    /// a cutoff late. This stage roughly triples its acceleration over the burn.
    /// </summary>
    [Fact]
    public void TheTimeToGainAVelocityAllowsForTheStackGettingLighter()
    {
        double naive = 2000.0 / Stage.AccelerationNow;
        double real = Stage.SecondsToGain(2000.0);

        Assert.True(real < naive, $"{real:F1} s should be less than the constant-mass {naive:F1} s");

        // Flying the rocket equation forward has to land back on the velocity asked for.
        double gained = -Stage.ExhaustVelocity * Math.Log(1.0 - real / Stage.Tau);
        Assert.Equal(2000.0, gained, 3);
    }

    [Fact]
    public void ThrustDisplacementIsTheDistanceCoveredBeyondCoasting()
    {
        double seconds = 60.0;
        double travelled = Stage.ThrustDisplacement(seconds);

        // Bounded below by constant initial thrust and above by constant final thrust, because the
        // acceleration rises monotonically across the burn.
        double atStart = 0.5 * Stage.AccelerationNow * seconds * seconds;
        double endMass = Stage.TotalMassKg - Stage.MassFlowKgPerSec * seconds;
        double atEnd = 0.5 * (Stage.ThrustNewtons / endMass) * seconds * seconds;

        Assert.True(travelled > atStart, $"{travelled:F0} m should exceed {atStart:F0} m");
        Assert.True(travelled < atEnd, $"{travelled:F0} m should be under {atEnd:F0} m");
    }

    /// <summary>
    /// A stack that cannot thrust must not report a finite time to gain anything. Read as a cutoff
    /// countdown, a zero there ends a flight that has not started and calls it complete.
    /// </summary>
    [Fact]
    public void AStackThatCannotThrustNeverFinishesTheBurn()
    {
        BoosterPerformance dead = new(0, 0, 5000, 0);

        Assert.False(dead.CanThrust);
        Assert.Equal(0.0, dead.DeltaVRemaining);
        Assert.Equal(0.0, dead.BurnSecondsRemaining);
        Assert.True(double.IsPositiveInfinity(dead.SecondsToGain(100.0)));
        Assert.Equal(0.0, dead.SecondsToGain(0.0));
    }
}
