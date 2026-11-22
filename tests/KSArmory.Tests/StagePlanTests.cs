using Xunit;

namespace KSArmory.Tests;

public class StagePlanTests
{
    // IcbmFlightTests' two-stage stack with a 900 kN first stage, so its first burn is long.
    private static StagePhase Lower(bool solid = false)
        => new(900_000, 900_000 / 2_600.0, 63_200, 46_000 / (900_000 / 2_600.0), solid);

    private static StagePhase Upper => new(260_000, 260_000 / 3_000.0, 13_200, 12_000 / (260_000 / 3_000.0));

    private static StagePlan TwoStages(bool solidLower = false) => new([Lower(solidLower), Upper]);

    // What the program has without a plan: the running stage's figures over the whole stack's mass.
    private static BoosterPerformance RunningStageAlone
        => new(900_000, 900_000 / 2_600.0, 63_200, 46_000);

    [Fact]
    public void ABurnPastTheStagingTakesTheWholeLowerStageAndTheUpperStagesOwnLaw()
    {
        StagePlan plan = TwoStages();
        double lower = Lower().DeltaV;
        double wanted = lower + 1_000.0;

        double upperSeconds = Upper.StartMassKg / Upper.MassFlowKgPerSec * (1.0 - Math.Exp(-1_000.0 / 3_000.0));
        double expected = Lower().DurationSeconds + upperSeconds;

        Assert.Equal(expected, plan.SecondsToGain(wanted), 6);

        double single = RunningStageAlone.SecondsToGain(wanted);
        Assert.True(expected - single > 20.0, $"the single-stage form says {single:F1} s against the stages' {expected:F1} s");
    }

    [Fact]
    public void ABurnInsideTheRunningStageIsTheRunningStagesOwnAnswer()
    {
        Assert.Equal(RunningStageAlone.SecondsToGain(1_500.0), TwoStages().SecondsToGain(1_500.0), 6);
    }

    [Fact]
    public void ASolidReachedIsSpentWhole()
    {
        Assert.Equal(Lower().DurationSeconds, TwoStages(solidLower: true).SecondsToGain(500.0), 6);
    }

    [Fact]
    public void WhatIsLeftIsEachStagesRocketEquationAdded()
    {
        double expected = 2_600.0 * Math.Log(63_200.0 / 17_200.0) + 3_000.0 * Math.Log(13_200.0 / 1_200.0);
        Assert.Equal(expected, TwoStages().DeltaVRemaining, 6);
        Assert.Equal(Lower().DurationSeconds + Upper.DurationSeconds, TwoStages().BurnSecondsRemaining, 6);
    }

    [Fact]
    public void AskingForMoreThanTheStackHoldsStaysFiniteAndLongerThanEveryBurn()
    {
        StagePlan plan = TwoStages();
        double seconds = plan.SecondsToGain(plan.DeltaVRemaining + 500.0);
        Assert.True(double.IsFinite(seconds) && seconds > plan.BurnSecondsRemaining, $"{seconds:F1} s");
    }

    [Fact]
    public void TheDisplacementAcrossAStagingIsTheIntegralOfWhatEachStageGained()
    {
        StagePlan plan = TwoStages();
        double seconds = Lower().DurationSeconds + 40.0;

        const double h = 1e-3;
        double gained = 0.0, displacement = 0.0;
        for (double t = 0.0; t < seconds - h / 2; t += h)
        {
            StagePhase phase = t < Lower().DurationSeconds ? Lower() : Upper;
            double since = t < Lower().DurationSeconds ? t : t - Lower().DurationSeconds;
            double accel = phase.ThrustNewtons / (phase.StartMassKg - phase.MassFlowKgPerSec * (since + h / 2));
            displacement += (gained + 0.5 * accel * h) * h;
            gained += accel * h;
        }

        // A millisecond step straddles the staging once, which is worth under a metre of 330 km.
        Assert.Equal(displacement, plan.ThrustDisplacement(seconds), 1.0);
    }

    [Fact]
    public void TheRunningPhaseTakesTheLiveFiguresAndKeepsItsPropellant()
    {
        BoosterPerformance live = new(800_000, 800_000 / 2_500.0, 60_000, 43_000);
        StagePhase running = TwoStages().WithRunning(live).Phases[0];

        Assert.Equal(800_000, running.ThrustNewtons);
        Assert.Equal(60_000, running.StartMassKg);
        Assert.Equal(46_000, running.PropellantKg, 6);
        Assert.Equal(Upper, TwoStages().WithRunning(live).Phases[1]);
    }
}
