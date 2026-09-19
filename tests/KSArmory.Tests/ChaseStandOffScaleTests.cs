using Xunit;

namespace KSArmory.Tests;

public class ChaseStandOffScaleTests
{
    [Fact]
    public void TheMissileTheStandOffWasFramedOnIsUnscaled()
        => Assert.Equal(1.0, ChaseView.StandOffScale(3.10), 1e-9);

    [Fact]
    public void AShellIsChasedFromItsOwnLengthsAway()
    {
        double shell = ChaseView.StandOffScale(0.42825);

        Assert.Equal(0.42825 / 3.10, shell, 1e-9);

        // 26 m behind a 3.1 m missile is 3.6 m behind a 0.43 m shell: the same angle across the picture.
        Assert.InRange(26.0 * shell, 3.5, 3.7);
    }

    [Fact]
    public void ItIsBoundedBothWaysAndIgnoresNonsense()
    {
        Assert.Equal(0.1, ChaseView.StandOffScale(0.01), 1e-9);
        Assert.Equal(2.0, ChaseView.StandOffScale(50.0), 1e-9);
        Assert.Equal(1.0, ChaseView.StandOffScale(double.NaN), 1e-9);
        Assert.Equal(1.0, ChaseView.StandOffScale(0.0), 1e-9);
    }
}
