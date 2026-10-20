using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

public class BusTurnTests
{
    private static readonly double3 Target = new(1, 0, 0);

    private static double3 Off(double degrees)
    {
        double r = double.DegreesToRadians(degrees);
        return new double3(Math.Cos(r), Math.Sin(r), 0);
    }

    [Fact]
    public void ItSettlesOnlyOnceTheNoseHasHeldOnTheLineForTheSteadyTime()
    {
        BusTurn turn = new();
        turn.Begin(Target, 150.0);

        Assert.Equal(BusTurn.Stage.Turning, turn.Update(0.1, Off(30)));
        for (int i = 0; i < 19; i++) Assert.Equal(BusTurn.Stage.Turning, turn.Update(0.1, Off(1)));

        Assert.Equal(BusTurn.Stage.Settled, turn.Update(0.1, Off(1)));
        Assert.False(turn.Holds);
        Assert.Equal(0.2, turn.FirstOnLineSeconds, 6);
    }

    [Fact]
    public void LeavingTheLineStartsTheSteadyTimeAgain()
    {
        BusTurn turn = new();
        turn.Begin(Target, 150.0);

        for (int i = 0; i < 15; i++) turn.Update(0.1, Off(1));
        turn.Update(0.1, Off(3));
        for (int i = 0; i < 15; i++) Assert.Equal(BusTurn.Stage.Turning, turn.Update(0.1, Off(1)));
    }

    [Fact]
    public void TheCapKeepsTheAttitudeActuallyReached()
    {
        BusTurn turn = new();
        turn.Begin(Target, 0.95);

        for (int i = 0; i < 9; i++) Assert.Equal(BusTurn.Stage.Turning, turn.Update(0.1, Off(40)));

        Assert.Equal(BusTurn.Stage.Capped, turn.Update(0.1, Off(20)));
        Assert.Equal(20.0, double.RadiansToDegrees(Math.Acos(Vec.Dot(turn.ReachedCci, Target))), 6);
    }

    [Fact]
    public void AFrameThatWouldNotResolveNeitherSettlesNorResetsTheReading()
    {
        BusTurn turn = new();
        turn.Begin(Target, 150.0);

        turn.Update(0.1, Off(10));
        Assert.Equal(BusTurn.Stage.Turning, turn.Update(0.1, Vec.Zero));
        Assert.Equal(10.0, turn.OffDegrees, 6);
    }

    [Fact]
    public void TheRateIsTheNosesOwnTurnPerSecond()
    {
        BusTurn turn = new();
        turn.Begin(Target, 150.0);

        turn.Update(0.5, Off(60));
        turn.Update(0.5, Off(57));
        Assert.Equal(6.0, turn.RateDegPerSecond, 6);
        Assert.Equal(60.0, turn.StartDegrees, 6);
    }

    [Fact]
    public void NoTargetIsNoTurn()
    {
        BusTurn turn = new();
        turn.Begin(Vec.Zero, 150.0);
        Assert.False(turn.Holds);
    }
}
