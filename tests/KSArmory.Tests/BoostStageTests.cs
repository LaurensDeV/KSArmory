using Xunit;

namespace KSArmory.Tests;

/// <summary>
/// Multi-stage boost. A booster and a sustainer are different accelerations for different
/// durations, and averaging them into one burn gets the burnout speed roughly right and the
/// trajectory wrong, which is the shape of error nothing downstream can recover from.
/// </summary>
public class BoostStageTests
{
    private static MunitionProfile Round(float first, float accel, params BoostStage[] rest) => new()
    {
        Name = "test", DisplayName = "test",
        BoostSeconds = first, BoostAccel = accel, Stages = rest,
    };

    [Fact]
    public void ASingleStageRoundIsUnchangedByTheStageList()
    {
        MunitionProfile m = Round(2.4f, 520f);

        Assert.Equal(2.4f, m.TotalBoostSeconds, 4);
        Assert.Equal(520f, m.BoostAccelAt(0.0), 4);
        Assert.Equal(520f, m.BoostAccelAt(2.4), 4);
        Assert.Equal(0f, m.BoostAccelAt(2.5), 4);
    }

    [Fact]
    public void EachStageBurnsInTurnAndThenTheRoundCoasts()
    {
        // A booster that pushes hard and briefly, then a sustainer that pushes gently and long.
        MunitionProfile m = Round(2.0f, 400f, new BoostStage(6.0f, 90f));

        Assert.Equal(8.0f, m.TotalBoostSeconds, 4);

        Assert.Equal(400f, m.BoostAccelAt(0.5), 4);
        Assert.Equal(400f, m.BoostAccelAt(2.0), 4);
        Assert.Equal(90f, m.BoostAccelAt(2.01), 4);
        Assert.Equal(90f, m.BoostAccelAt(8.0), 4);
        Assert.Equal(0f, m.BoostAccelAt(8.01), 4);
    }

    [Fact]
    public void AThirdStageBurnsAfterTheSecond()
    {
        MunitionProfile m = Round(1.0f, 300f, new BoostStage(2.0f, 200f), new BoostStage(3.0f, 100f));

        Assert.Equal(6.0f, m.TotalBoostSeconds, 4);
        Assert.Equal(300f, m.BoostAccelAt(1.0), 4);
        Assert.Equal(200f, m.BoostAccelAt(3.0), 4);
        Assert.Equal(100f, m.BoostAccelAt(6.0), 4);
        Assert.Equal(0f, m.BoostAccelAt(6.1), 4);
    }

    /// <summary>
    /// The speed a staged round reaches must be the sum of its stages, not the first one repeated
    /// or the last one applied throughout. This is what a single scalar pair cannot express.
    /// </summary>
    [Fact]
    public void StagedThrustIntegratesToTheSumOfItsStages()
    {
        MunitionProfile m = Round(2.0f, 400f, new BoostStage(6.0f, 90f));

        double v = 0.0, dt = 0.001;
        for (double t = 0.0; t < m.TotalBoostSeconds; t += dt) v += m.BoostAccelAt(t) * dt;

        // 400 x 2 + 90 x 6 = 1340 m/s.
        Assert.Equal(1340.0, v, 0);
    }

    [Fact]
    public void AnUnpoweredRoundNeverThrusts()
    {
        MunitionProfile shell = Round(0f, 0f);

        Assert.Equal(0f, shell.TotalBoostSeconds, 4);
        Assert.Equal(0f, shell.BoostAccelAt(0.0), 4);
        Assert.Equal(0f, shell.BoostAccelAt(5.0), 4);
    }
}
