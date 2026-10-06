using Brutal.Numerics;
using Xunit;

namespace KSArmory.Tests;

/// <summary>A hole's stream, flown parcel by parcel.</summary>
public class LeakStreamTests
{
    private static readonly double3 Exit = new(1, 2, 3);
    private static readonly double3 Down = new(0, 0, -9.81);
    private const double Frame = 1.0 / 60.0;

    private static LeakJet Flowing(double3 velocity, double3 acceleration, double seconds)
    {
        LeakJet jet = new();
        for (double t = 0.0; t < seconds; t += Frame) jet.Step(Frame, Exit, velocity, acceleration, flowing: true);
        return jet;
    }

    private static LeakStream.Point[] Draw(LeakJet jet, bool flowing = true)
    {
        var points = new LeakStream.Point[LeakStream.Points + 1];
        int n = jet.Draw(Exit, 0.02, flowing, points);
        return points[..n];
    }

    /// <summary>Held steady, each parcel is where a projectile that left that long ago would be.</summary>
    [Fact]
    public void HeldSteadyItIsTheArcAProjectileFollows()
    {
        double3 velocity = new(6, 0, 0);
        LeakStream.Point[] stream = Draw(Flowing(velocity, Down, 3.0));

        Assert.Equal(Exit, stream[0].Position);
        foreach (LeakStream.Point p in stream)
        {
            double3 expected = Exit + (velocity * p.OutSeconds) + (Down * (0.5 * p.OutSeconds * p.OutSeconds));
            Assert.True(Vec.Len(p.Position - expected) < 1.0e-6, $"{p.OutSeconds:F3} s out, {Vec.Len(p.Position - expected):F4} m off");
        }

        Assert.True(stream[^1].OutSeconds > LeakStream.FollowSeconds - LeakJet.EmitSeconds);
    }

    /// <summary>
    /// The throttle moving changes how the liquid already out accelerates, never where it is: a frame
    /// after the change every parcel is within a frame's travel of where it was. Redrawn as the arc of
    /// the moment instead, the stream's far end jumps by metres.
    /// </summary>
    [Fact]
    public void ChangingTheAccelerationBendsItRatherThanMovingIt()
    {
        double3 velocity = new(6, 0, 0);
        LeakJet jet = Flowing(velocity, Down, 3.0);
        LeakStream.Point[] before = Draw(jet);

        jet.Step(Frame, Exit, velocity, new double3(-25, 0, 0), flowing: true);
        LeakStream.Point[] after = Draw(jet);

        // Every parcel that was out is still one, a frame older and a frame's flight further on.
        for (int i = 1; i < before.Length - 1; i++)
        {
            LeakStream.Point was = before[i];
            LeakStream.Point now = Array.Find(after, p => Math.Abs(p.OutSeconds - (was.OutSeconds + Frame)) < 1.0e-9);
            Assert.True(Vec.Len(now.Position - was.Position) < 25.0 * Frame, $"{Vec.Len(now.Position - was.Position):F3} m");
        }
    }

    [Fact]
    public void AStoppedLeaksTailFallsAwayFromTheHole()
    {
        LeakJet jet = Flowing(new double3(6, 0, 0), Down, 3.0);
        for (int i = 0; i < 30; i++) jet.Step(Frame, Exit, Vec.Zero, Down, flowing: false);

        LeakStream.Point[] tail = Draw(jet, flowing: false);
        Assert.True(Vec.Len(tail[0].Position - Exit) > 1.0);

        for (int i = 0; i < 200; i++) jet.Step(Frame, Exit, Vec.Zero, Down, flowing: false);
        Assert.True(jet.Empty);
    }

    [Fact]
    public void APausedStreamHoldsStill()
    {
        LeakJet jet = Flowing(new double3(6, 0, 0), Down, 1.0);
        LeakStream.Point[] before = Draw(jet);

        jet.Step(0.0, Exit, new double3(6, 0, 0), Down, flowing: true);

        Assert.Equal(before, Draw(jet));
    }

    /// <summary>The same flow through every section, so a stream thins as gravity speeds it up.</summary>
    [Fact]
    public void ItThinsAsItSpeedsUp()
    {
        LeakStream.Point[] stream = Draw(Flowing(new double3(2, 0, 0), Down, 3.0));

        Assert.Equal(0.02, stream[0].Radius, 9);
        for (int i = 2; i < stream.Length; i++) Assert.True(stream[i].Radius < stream[i - 1].Radius);
    }

    [Fact]
    public void HowFarAlongItIsOnlyGrows()
    {
        LeakStream.Point[] stream = Draw(Flowing(new double3(3, 1, 2), Down, 3.0));

        for (int i = 1; i < stream.Length; i++) Assert.True(stream[i].AlongMetres > stream[i - 1].AlongMetres);
    }

    [Fact]
    public void AJetNecksToTheDischargeCoefficientsShareOfItsHole()
    {
        double exit = LeakStream.ExitRadius(0.03);

        Assert.Equal(TankLeak.DischargeCoefficient, exit * exit / (0.03 * 0.03), 9);
        Assert.Equal(LeakStream.WholeForDiameters * 2 * exit, LeakStream.WholeForMetres(exit), 9);
    }
}
